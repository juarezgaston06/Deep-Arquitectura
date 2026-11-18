using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Services;

/// <summary>El servidor rechazó el pedido con un motivo para mostrar (400/403/409).</summary>
public sealed class ErrorApiException(string mensaje, HttpStatusCode estado) : Exception(mensaje)
{
    public HttpStatusCode Estado { get; } = estado;
}

/// <summary>El token venció o el estudio cerró la sesión / desactivó al capataz.</summary>
public sealed class SesionVencidaException(string mensaje) : Exception(mensaje);

public sealed class ApiCliente(Sesion sesion)
{
    // Un video grande por datos móviles puede tardar: sin límite global, se corta con CancellationToken.
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<LoginResponse> LoginAsync(string usuario, string pin, CancellationToken ct = default)
    {
        var dispositivo = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
        using var r = await EnviarAsync(HttpMethod.Post, RutasApp.Login,
            JsonContent.Create(new LoginRequest(usuario, pin, dispositivo), AppJson.Default.LoginRequest), autenticado: false, ct);
        return (await r.Content.ReadFromJsonAsync(AppJson.Default.LoginResponse, ct))!;
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var _ = await EnviarAsync(HttpMethod.Post, RutasApp.Logout, null, autenticado: true, cts.Token);
        }
        catch { /* sin señal: la sesión se borra igual en el teléfono */ }
    }

    public async Task<CapatazDto> YoAsync(CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, RutasApp.Yo, null, autenticado: true, ct);
        return (await r.Content.ReadFromJsonAsync(AppJson.Default.CapatazDto, ct))!;
    }

    public async Task<ObraDetalleDto> ObraAsync(int obraId, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, RutasApp.Obra(obraId), null, autenticado: true, ct);
        return (await r.Content.ReadFromJsonAsync(AppJson.Default.ObraDetalleDto, ct))!;
    }

    /// <summary>Foto de un avance de la cuadrilla (para verla antes de validar).</summary>
    public async Task<byte[]> FotoAsync(int medioId, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, RutasApp.Foto(medioId), null, autenticado: true, ct);
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Envío de la cola: el cuerpo ya viene serializado.</summary>
    public async Task EnviarJsonAsync(HttpMethod metodo, string ruta, string json, CancellationToken ct = default)
    {
        using var _ = await EnviarAsync(metodo, ruta, new StringContent(json, Encoding.UTF8, "application/json"), autenticado: true, ct);
    }

    /// <summary>Gasto con sus comprobantes en un solo pedido.</summary>
    public async Task PostMultipartAsync(string ruta, string json, IEnumerable<(string Ruta, string Nombre)> archivos, CancellationToken ct = default)
    {
        var abiertos = new List<FileStream>();
        try
        {
            using var form = new MultipartFormDataContent { { new StringContent(json, Encoding.UTF8), CamposGasto.Datos } };
            foreach (var (rutaLocal, nombre) in archivos)
            {
                var f = File.OpenRead(rutaLocal);
                abiertos.Add(f);
                var contenido = new StreamContent(f, 81920);
                contenido.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(contenido, CamposGasto.Archivo, nombre);
            }
            using var _ = await EnviarAsync(HttpMethod.Post, ruta, form, autenticado: true, ct);
        }
        finally
        {
            foreach (var f in abiertos) await f.DisposeAsync();
        }
    }

    public async Task SubirMedioAsync(int obraId, Guid registroRef, Guid medioRef, string ruta, string nombre, CancellationToken ct = default)
    {
        await using var archivo = File.OpenRead(ruta);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(registroRef.ToString()), CamposMedio.RegistroRef },
            { new StringContent(medioRef.ToString()), CamposMedio.MedioRef },
        };
        var contenido = new StreamContent(archivo, 81920);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(contenido, CamposMedio.Archivo, nombre);
        using var _ = await EnviarAsync(HttpMethod.Post, RutasApp.Medios(obraId), form, autenticado: true, ct);
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string ruta, HttpContent? cuerpo, bool autenticado, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, sesion.Servidor + ruta) { Content = cuerpo };
        if (autenticado)
        {
            if (sesion.Token is null) throw new SesionVencidaException("Ingresá de nuevo.");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sesion.Token);
        }

        HttpResponseMessage r;
        try { r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception ex) when (ex is not (HttpRequestException or OperationCanceledException))
        {
            // Sin señal, Android tira WebException / Java.Net.* en vez de HttpRequestException: se trata igual (se reintenta).
            throw new HttpRequestException("Sin conexión con el estudio.", ex);
        }
        if (r.IsSuccessStatusCode) return r;

        string mensaje;
        try { mensaje = (await r.Content.ReadFromJsonAsync(AppJson.Default.ErrorApp, ct))?.Mensaje ?? r.ReasonPhrase ?? "Error"; }
        catch { mensaje = r.StatusCode == HttpStatusCode.TooManyRequests ? "Demasiados intentos. Esperá unos minutos." : $"Error del servidor ({(int)r.StatusCode})."; }
        var estado = r.StatusCode;
        r.Dispose();

        if (estado == HttpStatusCode.Unauthorized && autenticado) throw new SesionVencidaException(mensaje);
        // 5xx / 408 / 429: problema pasajero → se reintenta como si no hubiera señal.
        if ((int)estado >= 500 || estado is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException(mensaje, null, estado);
        throw new ErrorApiException(mensaje, estado);
    }
}
