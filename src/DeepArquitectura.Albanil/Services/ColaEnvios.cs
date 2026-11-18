using System.Net;
using System.Text.Json;
using DeepArquitectura.Contracts;
using Microsoft.Extensions.Logging;

namespace DeepArquitectura.Albanil.Services;

public enum TipoEnvio { Avance, Entrada, Salida, Pedido, Problema, Parte, Recepcion, Control, NotaPedido, Leido }

/// <summary>Algo que el albañil cargó y todavía no llegó al estudio (con sus fotos y videos).</summary>
public sealed class Envio
{
    /// <summary>Id del envío: es el Ref del avance/problema/pedido/fichada en el servidor (reintentar no duplica).</summary>
    public Guid Ref { get; set; } = Guid.NewGuid();
    /// <summary>Quién lo cargó: si en el teléfono entra otro albañil, esto no se manda con su cuenta.</summary>
    public int AlbanilId { get; set; }
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = "";
    public TipoEnvio Tipo { get; set; }
    /// <summary>Texto para mostrar en la lista de pendientes (ej. «Avance: Mampostería PB»).</summary>
    public string Descripcion { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Cuerpo JSON del pedido, serializado al cargarlo.</summary>
    public string Cuerpo { get; set; } = "";
    public bool CuerpoEnviado { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
    public int Intentos { get; set; }
    /// <summary>Veces seguidas que el servidor dijo que lo que referencia este envío todavía no llegó (409).</summary>
    public int Conflictos { get; set; }
    public string? Error { get; set; }
    /// <summary>El estudio lo rechazó (obra quitada, dato inválido…): no se reintenta solo.</summary>
    public bool Rechazado { get; set; }
}

public sealed class Adjunto
{
    public Guid Ref { get; set; } = Guid.NewGuid();
    /// <summary>Copia dentro de la app: el envío no depende de la galería ni de archivos temporales.</summary>
    public string RutaLocal { get; set; } = "";
    public string Nombre { get; set; } = "";
    public bool EsVideo { get; set; }
    public long Tamano { get; set; }
    public bool Enviado { get; set; }
}

/// <summary>
/// Cola persistente: todo se guarda primero en el teléfono y se envía solo en segundo plano. En obra suele no haber
/// señal: se reintenta al volver la conexión y cada 30 s. Se envía en el orden en que se cargó (la salida va después
/// de su entrada); si algo espera a otro envío (409) se sigue con el resto.
/// </summary>
public sealed class ColaEnvios(ApiCliente api, Sesion sesion, ILogger<ColaEnvios> log)
{
    private static readonly string Carpeta = Path.Combine(FileSystem.AppDataDirectory, "pendientes");
    private static readonly string Archivo = Path.Combine(FileSystem.AppDataDirectory, "cola.json");
    private const int MaximoConflictos = 10;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _procesando = new(1, 1);
    private List<Envio>? _items;
    private CancellationTokenSource? _ciclo;

    /// <summary>Cambió algo (se agregó, se envió, falló): la pantalla actualiza el aviso de pendientes.</summary>
    public event EventHandler? Cambio;
    /// <summary>Llegó un envío al estudio (obraId): para refrescar la obra.</summary>
    public event EventHandler<int>? Enviado;
    public event EventHandler? SesionVencida;
    /// <summary>El estudio rechazó un envío (con el motivo).</summary>
    public event Action<string>? EnvioRechazado;

    public bool Enviando { get; private set; }

    public async Task<IReadOnlyList<Envio>> PendientesAsync()
    {
        await _lock.WaitAsync();
        try { return [.. (await CargarAsync()).Where(Mio)]; }
        finally { _lock.Release(); }
    }

    private bool Mio(Envio e) => sesion.Perfil is { } p && e.AlbanilId == p.Id;

    public void Iniciar()
    {
        if (_ciclo is not null) return;
        _ciclo = new CancellationTokenSource();
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet) Disparar();
        };
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do { await ProcesarAsync(); } while (await timer.WaitForNextTickAsync(_ciclo.Token).ConfigureAwait(false));
        });
    }

    public void Disparar() => _ = Task.Run(ProcesarAsync);

    /// <summary>Guarda el envío en el teléfono (copiando los archivos) y dispara la subida.</summary>
    public async Task AgregarAsync(Envio envio, IEnumerable<FileResult>? archivos = null)
    {
        envio.AlbanilId = sesion.Perfil?.Id ?? throw new InvalidOperationException("Sin sesión.");
        Directory.CreateDirectory(Carpeta);
        foreach (var f in archivos ?? [])
        {
            var adj = new Adjunto { Nombre = NombreSeguro(f.FileName), EsVideo = Medios.EsVideo(f) };
            adj.RutaLocal = Path.Combine(Carpeta, $"{adj.Ref:N}{Path.GetExtension(adj.Nombre)}");
            await using (var origen = await f.OpenReadAsync())
            await using (var destino = File.Create(adj.RutaLocal))
                await origen.CopyToAsync(destino);
            adj.Tamano = new FileInfo(adj.RutaLocal).Length;
            envio.Adjuntos.Add(adj);
        }

        await _lock.WaitAsync();
        try
        {
            (await CargarAsync()).Add(envio);
            await GuardarAsync();
        }
        finally { _lock.Release(); }
        Cambio?.Invoke(this, EventArgs.Empty);
        Disparar();
    }

    public async Task<int> ReintentarRechazadosAsync()
    {
        int n;
        await _lock.WaitAsync();
        try
        {
            var rechazados = (await CargarAsync()).Where(e => e.Rechazado && Mio(e)).ToList();
            foreach (var e in rechazados) { e.Rechazado = false; e.Error = null; e.Conflictos = 0; }
            n = rechazados.Count;
            if (n > 0) await GuardarAsync();
        }
        finally { _lock.Release(); }
        Cambio?.Invoke(this, EventArgs.Empty);
        Disparar();
        return n;
    }

    public async Task<int> DescartarRechazadosAsync()
    {
        int n;
        await _lock.WaitAsync();
        try
        {
            var items = await CargarAsync();
            var rechazados = items.Where(e => e.Rechazado && Mio(e)).ToList();
            foreach (var e in rechazados) { items.Remove(e); BorrarArchivos(e); }
            n = rechazados.Count;
            if (n > 0) await GuardarAsync();
        }
        finally { _lock.Release(); }
        Cambio?.Invoke(this, EventArgs.Empty);
        return n;
    }

    /// <summary>Al cerrar sesión: borra lo rechazado (lo pendiente queda para cuando vuelva a entrar).</summary>
    public Task LimpiarAlSalirAsync() => DescartarRechazadosAsync();

    private async Task ProcesarAsync()
    {
        if (!await _procesando.WaitAsync(0)) return; // ya hay un envío en curso
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            foreach (var envio in (await PendientesAsync()).Where(e => !e.Rechazado))
            {
                Enviando = true;
                Cambio?.Invoke(this, EventArgs.Empty);
                if (!await EnviarUnoAsync(envio)) break; // sin señal o sesión vencida: se reintenta más tarde
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error procesando la cola de envíos");
        }
        finally
        {
            Enviando = false;
            _procesando.Release();
            Cambio?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string Ruta(Envio e) => e.Tipo switch
    {
        TipoEnvio.Avance => RutasAlbanil.Avances(e.ObraId),
        TipoEnvio.Entrada => RutasAlbanil.Entrada(e.ObraId),
        TipoEnvio.Salida => RutasAlbanil.Salida(e.ObraId),
        TipoEnvio.Pedido => RutasAlbanil.Pedidos(e.ObraId),
        TipoEnvio.Problema => RutasAlbanil.Problemas(e.ObraId),
        TipoEnvio.Parte => RutasAlbanil.Partes(e.ObraId),
        TipoEnvio.Recepcion => RutasAlbanil.Recepciones(e.ObraId),
        TipoEnvio.Control => RutasAlbanil.Controles(e.ObraId),
        TipoEnvio.NotaPedido => RutasAlbanil.NotasPedido(e.ObraId),
        TipoEnvio.Leido => RutasAlbanil.Leido(e.ObraId),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>true = siguió de largo (enviado o rechazado); false = cortar y reintentar más tarde.</summary>
    private async Task<bool> EnviarUnoAsync(Envio e)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            if (!e.CuerpoEnviado)
            {
                await api.PostJsonAsync(Ruta(e), e.Cuerpo, cts.Token);
                await Modificar(e.Ref, x => { x.CuerpoEnviado = true; x.Conflictos = 0; });
            }

            foreach (var a in e.Adjuntos.Where(a => !a.Enviado))
            {
                // ~20 s por MB como peor caso con señal pobre, mínimo 2 minutos.
                using var ctsArchivo = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(120, a.Tamano / 1024 / 1024 * 20)));
                if (File.Exists(a.RutaLocal))
                    // Los archivos van colgados del avance o problema: su Ref es el del envío.
                    await api.SubirMedioAsync(e.ObraId, e.Ref, a.Ref, a.RutaLocal, a.Nombre, ctsArchivo.Token);
                await Modificar(e.Ref, x => x.Adjuntos.First(y => y.Ref == a.Ref).Enviado = true);
                TryDelete(a.RutaLocal);
            }

            await _lock.WaitAsync();
            try
            {
                (await CargarAsync()).RemoveAll(x => x.Ref == e.Ref);
                await GuardarAsync();
            }
            finally { _lock.Release(); }
            Enviado?.Invoke(this, e.ObraId);
            return true;
        }
        catch (SesionVencidaException)
        {
            SesionVencida?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (ErrorApiException ex) when (ex.Estado == HttpStatusCode.Conflict)
        {
            // Falta algo anterior en el servidor (la novedad de una foto, la entrada de una salida). Se vuelve a mandar
            // el cuerpo (idempotente) en el próximo intento y mientras tanto se sigue con lo demás.
            var rechazar = false;
            await Modificar(e.Ref, x =>
            {
                x.Intentos++;
                x.Conflictos++;
                if (x.Tipo is TipoEnvio.Avance or TipoEnvio.Problema or TipoEnvio.Recepcion or TipoEnvio.Control or TipoEnvio.NotaPedido) x.CuerpoEnviado = false;
                x.Error = ex.Message;
                if (x.Conflictos >= MaximoConflictos) { x.Rechazado = rechazar = true; x.Error = "No llegó al estudio lo anterior a este envío."; }
            });
            if (rechazar) EnvioRechazado?.Invoke($"{e.Descripcion}: no llegó al estudio.");
            return true;
        }
        catch (ErrorApiException ex)
        {
            log.LogWarning("Envío {Ref} rechazado: {Motivo}", e.Ref, ex.Message);
            await Modificar(e.Ref, x => { x.Rechazado = true; x.Error = ex.Message; });
            EnvioRechazado?.Invoke($"{e.Descripcion}: {ex.Message}");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or OperationCanceledException)
        {
            await Modificar(e.Ref, x => { x.Intentos++; x.Error = "Sin conexión con el estudio. Se reintenta solo."; });
            return false;
        }
    }

    private async Task Modificar(Guid envioRef, Action<Envio> cambio)
    {
        await _lock.WaitAsync();
        try
        {
            var e = (await CargarAsync()).FirstOrDefault(x => x.Ref == envioRef);
            if (e is null) return;
            cambio(e);
            await GuardarAsync();
        }
        finally { _lock.Release(); }
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    private async Task<List<Envio>> CargarAsync()
    {
        if (_items is not null) return _items;
        try
        {
            if (File.Exists(Archivo))
            {
                await using var f = File.OpenRead(Archivo);
                _items = await JsonSerializer.DeserializeAsync(f, AppJson.Default.ListEnvio) ?? [];
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "cola.json dañado; se empieza de cero");
        }
        return _items ??= [];
    }

    private async Task GuardarAsync()
    {
        var tmp = Archivo + ".tmp";
        await using (var f = File.Create(tmp))
            await JsonSerializer.SerializeAsync(f, _items!, AppJson.Default.ListEnvio);
        File.Move(tmp, Archivo, overwrite: true); // escritura atómica: un corte de batería no corrompe la cola
    }

    private static void BorrarArchivos(Envio e)
    {
        foreach (var a in e.Adjuntos) TryDelete(a.RutaLocal);
    }

    private static void TryDelete(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); } catch { /* se limpia en otro momento */ }
    }

    private static string NombreSeguro(string nombre)
    {
        var ext = Path.GetExtension(nombre);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        return $"{DateTime.Now:yyyyMMdd_HHmmss}{ext.ToLowerInvariant()}";
    }
}
