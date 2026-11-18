using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DeepArquitectura.Tests;

/// <summary>App del cliente (/mi-obra): acceso con código + PIN, aislamiento por obra, mensajes, aprobaciones, documentos y pagos.</summary>
public sealed class ClienteAppTests : IDisposable
{
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly TestDb _db = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "deeparq-cliente-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly AppCliente _app;
    private readonly GestionCliente _gestion;
    private readonly int _obra, _otra;

    public ClienteAppTests()
    {
        var almacen = new AlmacenMediosLocal(_carpeta);
        var portal = new PortalCliente(_db, almacen, new AvanceObra(_db, _reloj));
        _app = new AppCliente(_db, almacen, portal, _cache, _reloj);
        _gestion = new GestionCliente(_db, almacen, _cache, _reloj);

        using var db = _db.CreateDbContext();
        var cliente = new Cliente { Nombre = "Laura" };
        var obra = new Obra { Codigo = "OB-1", Nombre = "Casa Laura", Cliente = cliente, MontoPresupuesto = 1_000_000, Estado = EstadoObra.EnCurso,
            Etapas = [new EtapaObra { Orden = 1, Nombre = "Fundaciones", Estado = EstadoEtapa.EnCurso }] };
        var otra = new Obra { Codigo = "OB-2", Nombre = "Otra", Cliente = cliente };
        db.AddRange(obra, otra);
        db.SaveChanges();
        (_obra, _otra) = (obra.Id, otra.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    [Theory]
    [InlineData("deep 7k3m q9tx", "DEEP-7K3M-Q9TX")]
    [InlineData("7K3MQ9TX", "DEEP-7K3M-Q9TX")]
    [InlineData("DEEP-7K3M-Q9TX", "DEEP-7K3M-Q9TX")]
    [InlineData("DEEP-7K3M-Q9T", null)]
    [InlineData("DEEP-0K3M-Q9TX", null)] // 0 no está en el alfabeto
    [InlineData("", null)]
    public void Normaliza_el_codigo(string texto, string? esperado) => Assert.Equal(esperado, AccesoClientes.Normalizar(texto));

    [Fact]
    public void Codigos_y_pin_generados_tienen_formato()
    {
        var c = AccesoClientes.NuevoCodigo();
        Assert.Equal(c, AccesoClientes.Normalizar(c));
        Assert.True(AccesoClientes.PinConFormato(AccesoClientes.NuevoPin()));
    }

    [Fact]
    public async Task Ingresa_con_codigo_y_pin_y_se_cierra_al_dar_pin_nuevo()
    {
        var (codigo, pin) = await _gestion.GenerarAccesoAsync(_obra);
        var r = await _app.IngresarAsync(codigo.ToLowerInvariant().Replace("-", " "), pin);
        Assert.Equal(AppCliente.ResultadoIngreso.Ok, r.Resultado);
        Assert.Equal(_obra, r.ObraId);
        Assert.True(await _app.SesionValidaAsync(_obra, r.Sello));

        var (codigo2, pin2) = await _gestion.GenerarAccesoAsync(_obra);
        Assert.Equal(codigo, codigo2);
        Assert.False(await _app.SesionValidaAsync(_obra, r.Sello)); // la sesión vieja ya no vale
        Assert.Equal(AppCliente.ResultadoIngreso.Incorrecto, (await _app.IngresarAsync(codigo, pin == pin2 ? "000000" : pin)).Resultado);
        Assert.Equal(AppCliente.ResultadoIngreso.Ok, (await _app.IngresarAsync(codigo, pin2)).Resultado);
    }

    [Fact]
    public async Task Desactivar_cierra_sesiones_y_no_deja_entrar()
    {
        var (codigo, pin) = await _gestion.GenerarAccesoAsync(_obra);
        var r = await _app.IngresarAsync(codigo, pin);
        await _gestion.DesactivarAccesoAsync(_obra);
        Assert.False(await _app.SesionValidaAsync(_obra, r.Sello));
        Assert.Equal(AppCliente.ResultadoIngreso.Incorrecto, (await _app.IngresarAsync(codigo, pin)).Resultado);
    }

    [Fact]
    public async Task Bloquea_el_codigo_tras_varios_pin_errados()
    {
        var (codigo, pin) = await _gestion.GenerarAccesoAsync(_obra);
        var malo = pin == "111111" ? "222222" : "111111";
        for (var i = 0; i < AccesoClientes.IntentosAntesDeBloquear - 1; i++)
            Assert.Equal(AppCliente.ResultadoIngreso.Incorrecto, (await _app.IngresarAsync(codigo, malo)).Resultado);
        Assert.Equal(AppCliente.ResultadoIngreso.Bloqueado, (await _app.IngresarAsync(codigo, malo)).Resultado);
        // Bloqueado: ni con el PIN correcto.
        Assert.Equal(AppCliente.ResultadoIngreso.Bloqueado, (await _app.IngresarAsync(codigo, pin)).Resultado);

        _reloj.Ahora += AccesoClientes.Bloqueo + TimeSpan.FromMinutes(1);
        Assert.Equal(AppCliente.ResultadoIngreso.Ok, (await _app.IngresarAsync(codigo, pin)).Resultado);
    }

    [Fact]
    public async Task Mensajes_no_se_duplican_y_se_marcan_leidos()
    {
        var r = Guid.NewGuid();
        Assert.Null(await _app.EnviarMensajeAsync(_obra, "Laura", "¿Cuándo empiezan el techo?", r));
        Assert.Null(await _app.EnviarMensajeAsync(_obra, "Laura", "¿Cuándo empiezan el techo?", r)); // doble toque
        Assert.NotNull(await _app.EnviarMensajeAsync(_obra, "Laura", "   ", Guid.NewGuid()));
        Assert.Equal((1, 0), await _gestion.NovedadesAsync());

        var vistos = await _gestion.MensajesAsync(_obra);
        Assert.Single(vistos);
        Assert.Equal(0, (await _gestion.NovedadesAsync()).Mensajes);

        Assert.Null(await _gestion.ResponderAsync(_obra, "La semana que viene."));
        Assert.Equal(1, (await _app.AvisosAsync(_obra)).Mensajes);
        Assert.Equal(2, (await _app.MensajesAsync(_obra)).Count);
        Assert.Equal(0, (await _app.AvisosAsync(_obra)).Mensajes);
        Assert.Empty(await _app.MensajesAsync(_otra)); // otra obra no ve nada
    }

    [Fact]
    public async Task Aprobacion_con_opciones_exige_elegir_y_no_se_puede_cambiar()
    {
        var (id, error) = await _gestion.PedirAprobacionAsync(_obra, new GestionCliente.DatosAprobacion
        {
            Titulo = "Cerámico del baño", Opciones = "Gris 60x60\nBlanco 30x60", Monto = 50_000,
        });
        Assert.Null(error);
        Assert.Equal(1, (await _app.AvisosAsync(_obra)).Aprobaciones);

        Assert.NotNull(await _app.ResponderAsync(_obra, id!.Value, true, "Rojo", null, "Laura", "1.2.3.4")); // no es opción
        Assert.NotNull(await _app.ResponderAsync(_obra, id.Value, true, "Gris 60x60", null, " ", null)); // sin nombre
        Assert.NotNull(await _app.ResponderAsync(_otra, id.Value, true, "Gris 60x60", null, "Laura", null)); // otra obra
        Assert.Null(await _app.ResponderAsync(_obra, id.Value, true, "Gris 60x60", null, "Laura", "1.2.3.4"));
        Assert.NotNull(await _app.ResponderAsync(_obra, id.Value, false, null, "Mejor no", "Laura", null)); // ya respondida
        Assert.NotNull(await _gestion.AnularAprobacionAsync(id.Value)); // respondida: queda como constancia

        var a = Assert.Single(await _gestion.AprobacionesAsync(_obra));
        Assert.Equal(EstadoAprobacion.Aprobada, a.Estado);
        Assert.Equal("Gris 60x60", a.OpcionElegida);
        Assert.Equal("1.2.3.4", a.RespondidoDesdeIp);

        // El adicional aprobado se suma al total en pagos.
        var p = (await _app.PagosAsync(_obra))!;
        Assert.Equal(50_000, p.Adicionales);
        Assert.Equal(1_050_000, p.Saldo);
    }

    [Fact]
    public async Task Rechazar_exige_comentario_y_anulada_no_se_ve()
    {
        var (id, _) = await _gestion.PedirAprobacionAsync(_obra, new GestionCliente.DatosAprobacion { Titulo = "Adicional de rejas" });
        Assert.NotNull(await _app.ResponderAsync(_obra, id!.Value, false, null, null, "Laura", null));
        Assert.Null(await _app.ResponderAsync(_obra, id.Value, false, null, "Muy caro", "Laura", null));
        Assert.Equal(EstadoAprobacion.Rechazada, Assert.Single(await _app.AprobacionesAsync(_obra)).Estado);

        var (otra, _) = await _gestion.PedirAprobacionAsync(_obra, new GestionCliente.DatosAprobacion { Titulo = "Otra" });
        Assert.Null(await _gestion.AnularAprobacionAsync(otra!.Value));
        Assert.Single(await _app.AprobacionesAsync(_obra));
        Assert.NotNull((await _gestion.PedirAprobacionAsync(_obra, new GestionCliente.DatosAprobacion { Titulo = "X", Opciones = "Única" })).Error);
    }

    [Fact]
    public async Task Documentos_solo_visibles_y_de_su_obra()
    {
        var (visible, e1) = await _gestion.SubirDocumentoAsync(_obra, "Plano de planta", CategoriaDocumento.Plano, true, "planta.pdf", 4, () => new MemoryStream([1, 2, 3, 4]));
        var (oculto, _) = await _gestion.SubirDocumentoAsync(_obra, "Interno", CategoriaDocumento.Otro, false, "interno.pdf", 4, () => new MemoryStream([1, 2, 3, 4]));
        Assert.Null(e1);
        Assert.NotNull((await _gestion.SubirDocumentoAsync(_obra, "Virus", CategoriaDocumento.Otro, true, "x.exe", 4, () => new MemoryStream([1]))).Error);

        Assert.Single(await _app.DocumentosAsync(_obra));
        var d = await _app.DocumentoAsync(_obra, visible!.Value);
        Assert.NotNull(d);
        await d.Value.Contenido.DisposeAsync();
        Assert.Equal("Plano de planta.pdf", d.Value.Nombre);
        Assert.Null(await _app.DocumentoAsync(_obra, oculto!.Value));
        Assert.Null(await _app.DocumentoAsync(_otra, visible.Value));

        // Adjunto a una aprobación: se ve aunque no esté en la lista de documentos.
        await _gestion.PedirAprobacionAsync(_obra, new GestionCliente.DatosAprobacion { Titulo = "Ver plano", DocumentoObraId = oculto.Value });
        var adj = await _app.DocumentoAsync(_obra, oculto.Value);
        Assert.NotNull(adj);
        await adj.Value.Contenido.DisposeAsync();

        await _gestion.BorrarDocumentoAsync(oculto.Value);
        Assert.Null(Assert.Single(await _app.AprobacionesAsync(_obra)).DocumentoObraId);
    }

    [Fact]
    public async Task Pagos_muestra_solo_certificados_emitidos()
    {
        await using (var db = _db.CreateDbContext())
        {
            db.Certificados.AddRange(
                new Certificado { ObraId = _obra, Numero = 1, Fecha = new(2026, 10, 1), PorcentajeAcumulado = 20, MontoObra = 1_000_000, Monto = 200_000, Estado = EstadoCertificado.Cobrado },
                new Certificado { ObraId = _obra, Numero = 2, Fecha = new(2026, 11, 1), PorcentajeAnterior = 20, PorcentajeAcumulado = 35, MontoObra = 1_000_000, Monto = 150_000, Estado = EstadoCertificado.Emitido },
                new Certificado { ObraId = _obra, Numero = 3, Fecha = new(2026, 11, 10), PorcentajeAnterior = 35, PorcentajeAcumulado = 40, MontoObra = 1_000_000, Monto = 50_000 });
            db.Pagos.Add(new Pago { ObraId = _obra, Fecha = new(2026, 10, 5), Monto = 200_000, Concepto = "Certificado 1" });
            await db.SaveChangesAsync();
        }
        var p = (await _app.PagosAsync(_obra))!;
        Assert.Equal([2, 1], p.Certificados.Select(c => c.Numero));
        Assert.Equal(150_000, p.APagar);
        Assert.Equal(200_000, p.Pagado);
        Assert.Equal(800_000, p.Saldo);
        Assert.Equal(15, p.Certificados[0].PorcentajePeriodo);
        Assert.Empty((await _app.PagosAsync(_otra))!.Certificados);
    }

    [Fact]
    public async Task Avance_cuenta_solo_lo_aprobado()
    {
        await using (var db = _db.CreateDbContext())
        {
            var etapa = await db.Etapas.SingleAsync(e => e.ObraId == _obra);
            db.Tareas.AddRange(
                new TareaObra { ObraId = _obra, EtapaObraId = etapa.Id, Titulo = "Zapatas", Unidad = "u", CantidadObjetivo = 10, CantidadHecha = 8, Porcentaje = 80, CantidadAprobada = 5, PorcentajeAprobado = 50 },
                new TareaObra { ObraId = _obra, EtapaObraId = etapa.Id, Titulo = "Viga", PorcentajeAprobado = 50, Porcentaje = 100 });
            await db.SaveChangesAsync();
        }
        var a = (await _app.AvanceAsync(_obra))!;
        var e = Assert.Single(a.Etapas);
        Assert.Equal([50, 50], e.Trabajos.Select(t => t.Porcentaje));
        Assert.Equal(50, e.Porcentaje);
    }
}
