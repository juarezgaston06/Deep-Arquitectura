using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Tests;

public sealed class AlbanilTests : IDisposable
{
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly TestDb _db = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "deeparq-medios-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero));
    private readonly AlbanilService _svc;
    private readonly CampoService _campo;
    private readonly CapatazService _capataces;
    private readonly int _obraA, _obraB, _obraC, _etapaA, _capatazId, _otroCapatazId;
    private DateTime Ahora => _reloj.Ahora.UtcDateTime;

    public AlbanilTests()
    {
        var almacen = new AlmacenMediosLocal(_carpeta);
        var estudio = new EstudioService(_db);
        _svc = new AlbanilService(_db, almacen, estudio, _reloj);
        _campo = new CampoService(_db, _reloj);
        _capataces = new CapatazService(_db, almacen, estudio, _reloj);
        using var db = _db.CreateDbContext();
        var cliente = new Cliente { Nombre = "Cliente" };
        var a = new Obra { Codigo = "OB-2026-001", Nombre = "Casa Pérez", Cliente = cliente, Etapas = [new EtapaObra { Orden = 1, Nombre = "Mampostería" }] };
        var b = new Obra { Codigo = "OB-2026-002", Nombre = "Local Gómez", Cliente = cliente };
        var c = new Obra { Codigo = "OB-2026-003", Nombre = "Quincho Ruiz", Cliente = cliente };
        var capataz = new Capataz { Nombre = "Juan Capataz", Usuario = "jcapataz", PinHash = "x", Telefono = "351111" };
        var otro = new Capataz { Nombre = "Otro Capataz", Usuario = "otro", PinHash = "x" };
        db.AddRange(a, b, c, capataz, otro);
        db.SaveChanges();
        // A y B son del capataz; C es de otro capataz.
        db.CapatazObras.AddRange(
            new CapatazObra { CapatazId = capataz.Id, ObraId = a.Id },
            new CapatazObra { CapatazId = capataz.Id, ObraId = b.Id },
            new CapatazObra { CapatazId = otro.Id, ObraId = c.Id });
        db.SaveChanges();
        (_obraA, _obraB, _obraC, _etapaA, _capatazId, _otroCapatazId) = (a.Id, b.Id, c.Id, a.Etapas[0].Id, capataz.Id, otro.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private async Task<int> AlbanilConSesion(string usuario = "pedro", string pin = "123456")
    {
        var (id, error) = await _svc.CrearAsync("Pedro Albañil", usuario, "351222", "Oficial", _capatazId, pin, [_obraA]);
        Assert.Null(error);
        var login = await _svc.LoginAsync(usuario.ToUpperInvariant(), pin, "Moto G");
        Assert.NotNull(login);
        Assert.Equal(id, await _svc.AutenticarAsync(login!.Token));
        return id!.Value;
    }

    private async Task<int> TareaAsync(string titulo, string? unidad = "m²", decimal? objetivo = 40, int? albanilId = null) =>
        (await _campo.CrearTareaAsync(_obraA, new CampoService.DatosTarea
        {
            Titulo = titulo, Unidad = unidad, CantidadObjetivo = unidad is null ? null : objetivo, EtapaObraId = _etapaA, AlbanilId = albanilId,
        })).Id!.Value;

    // ------------------------------------------------------------ cuentas y acceso

    [Fact]
    public async Task Login_DevuelveCapatazYSoloObrasAsignadasDeSuCapataz()
    {
        var id = await AlbanilConSesion();
        var perfil = await _svc.PerfilAsync(id);

        Assert.Equal("Juan Capataz", perfil!.Capataz);
        Assert.Equal("351111", perfil.CapatazTelefono);
        Assert.Equal(["OB-2026-001"], perfil.Obras.Select(o => o.Codigo));
        Assert.NotNull(await _svc.ObraAsync(id, _obraA));
        Assert.Null(await _svc.ObraAsync(id, _obraB)); // del capataz, pero no asignada al albañil
    }

    [Fact]
    public async Task Crear_RechazaObrasDeOtroCapataz()
    {
        var (id, error) = await _svc.CrearAsync("Pedro", "pedro", null, null, _capatazId, "123456", [_obraC]);
        Assert.Null(id);
        Assert.Contains("su capataz", error);
    }

    [Fact]
    public async Task Asignar_SoloObrasDelCapataz()
    {
        var id = await AlbanilConSesion();
        Assert.NotNull(await _svc.AsignarAsync(id, _obraC, true));
        Assert.Null(await _svc.AsignarAsync(id, _obraB, true));
        Assert.NotNull(await _svc.ObraAsync(id, _obraB));
    }

    [Fact]
    public async Task SiElCapatazPierdeLaObra_LaCuadrillaDejaDeVerla()
    {
        var id = await AlbanilConSesion();
        await _capataces.AsignarAsync(_capatazId, _obraA, false);

        Assert.Null(await _svc.ObraAsync(id, _obraA));
        var (r, error) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, null, null, null, false, "x"));
        Assert.Null(r);
        Assert.Equal(AlbanilService.ErrorNoAsignada, error);
    }

    [Fact]
    public async Task CambiarCapataz_QuitaLasObrasQueNoSonDelNuevo()
    {
        var id = await AlbanilConSesion();
        await _svc.CambiarCapatazAsync(id, _otroCapatazId);

        await using var db = _db.CreateDbContext();
        Assert.False(await db.AlbanilObras.AnyAsync(x => x.AlbanilId == id));
        Assert.Equal("Otro Capataz", (await _svc.PerfilAsync(id))!.Capataz);
    }

    [Fact]
    public async Task Desactivar_CortaLaSesion()
    {
        var login = await CrearYLoguear();
        await _svc.CambiarActivoAsync(login.Albanil.Id, false);
        Assert.Null(await _svc.AutenticarAsync(login.Token));
        Assert.Null(await _svc.LoginAsync("pedro", "123456", null));
    }

    [Fact]
    public async Task TokenDeCapataz_NoSirveEnLaAppDeAlbaniles()
    {
        await _capataces.CrearAsync("Cap", "cap1", null, "123456", [_obraA]);
        var login = await _capataces.LoginAsync("cap1", "123456", null);
        Assert.Null(await _svc.AutenticarAsync(login!.Token));
    }

    private async Task<LoginAlbanilResponse> CrearYLoguear()
    {
        await _svc.CrearAsync("Pedro", "pedro", null, null, _capatazId, "123456", [_obraA]);
        return (await _svc.LoginAsync("pedro", "123456", null))!;
    }

    // ------------------------------------------------------------ tareas y avances

    [Fact]
    public async Task Avance_SumaCantidadRecalculaPorcentajeYQuedaEnBitacoraConCapataz()
    {
        var id = await AlbanilConSesion();
        var tarea = await TareaAsync("Mampostería PB");

        var (r1, e1) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, tarea, 8, null, false, "Paño norte"));
        Assert.Null(e1);
        Assert.Equal(8, r1!.Tarea!.CantidadHecha);
        Assert.Equal(20, r1.Tarea.Porcentaje);
        Assert.Equal("EnCurso", r1.Tarea.Estado);

        var (r2, _) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, tarea, 12, null, false, null));
        Assert.Equal(20, r2!.Tarea!.CantidadHecha);
        Assert.Equal(50, r2.Tarea.Porcentaje);

        await using var db = _db.CreateDbContext();
        var reg = await db.Registros.SingleAsync(x => x.Id == r1.Id);
        Assert.Equal((id, _capatazId, _etapaA), (reg.AlbanilId!.Value, reg.CapatazId!.Value, reg.EtapaObraId!.Value));
        Assert.Contains("+8 m²", reg.Titulo);
        // Arrancar la tarea pone en curso su etapa.
        Assert.Equal(EstadoEtapa.EnCurso, (await db.Etapas.SingleAsync(e => e.Id == _etapaA)).Estado);
    }

    [Fact]
    public async Task Avance_ReintentoConMismoRefNoDuplicaNiSumaDosVeces()
    {
        var id = await AlbanilConSesion();
        var tarea = await TareaAsync("Contrapiso");
        var req = new NuevoAvanceRequest(Guid.NewGuid(), Ahora, tarea, 10, null, false, null);

        var (a, _) = await _svc.RegistrarAvanceAsync(id, _obraA, req);
        var (b, _) = await _svc.RegistrarAvanceAsync(id, _obraA, req);

        Assert.Equal(a!.Id, b!.Id);
        Assert.Equal(10, b.Tarea!.CantidadHecha);
    }

    [Fact]
    public async Task Avance_EnPorcentaje_TomaElMasRecienteAunqueLlegueTarde()
    {
        var id = await AlbanilConSesion();
        var tarea = await TareaAsync("Revoque baño", unidad: null);

        await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, tarea, null, 60, false, null));
        // Un avance viejo que estaba sin señal llega después: no pisa el 60 %.
        var (r, _) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora.AddHours(-3), tarea, null, 30, false, null));
        Assert.Equal(60, r!.Tarea!.Porcentaje);

        var (fin, _) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, tarea, null, null, true, null));
        Assert.Equal(("Completada", 100), (fin!.Tarea!.Estado, fin.Tarea.Porcentaje));
    }

    [Fact]
    public async Task Avance_NoPuedeInformarTareaDeOtroCompanero()
    {
        var id = await AlbanilConSesion();
        var (otro, _) = await _svc.CrearAsync("Luis", "luis", null, null, _capatazId, "123456", [_obraA]);
        var ajena = await TareaAsync("Encadenado", albanilId: otro);
        var propia = await TareaAsync("Columnas", albanilId: id);
        var libre = await TareaAsync("Limpieza", unidad: null);

        var (r, error) = await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(Guid.NewGuid(), Ahora, ajena, 1, null, false, null));
        Assert.Null(r);
        Assert.Contains("compañero", error);

        var obra = await _svc.ObraAsync(id, _obraA);
        Assert.Equal([propia, libre], obra!.Tareas.Select(t => t.Id).Order());
        Assert.True(obra.Tareas.Single(t => t.Id == propia).Mia);
        Assert.False(obra.Tareas.Single(t => t.Id == libre).Mia);
    }

    [Fact]
    public async Task TareaCancelada_DesapareceDeLaApp()
    {
        var id = await AlbanilConSesion();
        var tarea = await TareaAsync("Revoque");
        await _campo.CambiarEstadoTareaAsync(tarea, EstadoTarea.Cancelada);
        Assert.Empty((await _svc.ObraAsync(id, _obraA))!.Tareas);
    }

    [Fact]
    public async Task CrearTarea_Valida()
    {
        var (_, sinCantidad) = await _campo.CrearTareaAsync(_obraA, new CampoService.DatosTarea { Titulo = "X", Unidad = "m²" });
        Assert.Contains("cantidad", sinCantidad);
        var (_, ajeno) = await _campo.CrearTareaAsync(_obraA, new CampoService.DatosTarea { Titulo = "X", AlbanilId = 999 });
        Assert.Contains("no está asignado", ajeno);
    }

    // ------------------------------------------------------------ medios

    [Fact]
    public async Task Medio_SeAdjuntaAlAvanceYNoLoAceptaLaAppDelCapataz()
    {
        var id = await AlbanilConSesion();
        var avance = Guid.NewGuid();
        await _svc.RegistrarAvanceAsync(id, _obraA, new NuevoAvanceRequest(avance, Ahora, null, null, null, false, "Fotos del día"));

        var bytes = new byte[] { 1, 2, 3 };
        var (medio, error) = await _svc.SubirMedioAsync(id, _obraA, avance, Guid.NewGuid(), "foto.jpg", bytes.Length, () => new MemoryStream(bytes));
        Assert.Null(error);
        Assert.NotNull(medio);

        // El avance es del albañil: un capataz no le puede colgar archivos aunque tenga el mismo Ref.
        var (_, errCap) = await _capataces.SubirMedioAsync(_capatazId, _obraA, avance, Guid.NewGuid(), "foto.jpg", 3, () => new MemoryStream(bytes));
        Assert.Equal("La novedad todavía no llegó.", errCap);

        var (_, pendiente) = await _svc.SubirMedioAsync(id, _obraA, Guid.NewGuid(), Guid.NewGuid(), "v.mp4", 3, () => new MemoryStream(bytes));
        Assert.Equal(AlbanilService.ErrorNovedadPendiente, pendiente);

        var (_, formato) = await _svc.SubirMedioAsync(id, _obraA, avance, Guid.NewGuid(), "x.exe", 3, () => new MemoryStream(bytes));
        Assert.Contains("Formato", formato);
    }

    // ------------------------------------------------------------ jornada

    [Fact]
    public async Task Jornada_EntradaYSalidaConUbicacion()
    {
        var id = await AlbanilConSesion();
        var entrada = Guid.NewGuid();
        var (j, error) = await _svc.FicharEntradaAsync(id, _obraA, new FichadaRequest(entrada, Ahora.AddHours(-8), -31.42, -64.18, 12));
        Assert.Null(error);
        Assert.Equal(entrada, (await _svc.ObraAsync(id, _obraA))!.JornadaAbierta!.Ref);

        // Reintento: misma jornada.
        Assert.Equal(j, (await _svc.FicharEntradaAsync(id, _obraA, new FichadaRequest(entrada, Ahora.AddHours(-8), -31.42, -64.18, 12))).Id);

        Assert.Null((await _svc.FicharSalidaAsync(id, _obraA, new FichadaRequest(entrada, Ahora, -31.42, -64.18, 8))).Error);
        Assert.Null((await _svc.ObraAsync(id, _obraA))!.JornadaAbierta);

        await using var db = _db.CreateDbContext();
        var jornada = await db.Jornadas.SingleAsync();
        Assert.Equal(8, jornada.Horas);
        Assert.Equal(_capatazId, jornada.CapatazId);
    }

    [Fact]
    public async Task Jornada_SalidaSinEntrada_Es409YUbicacionInvalidaSeRechaza()
    {
        var id = await AlbanilConSesion();
        Assert.Equal(AlbanilService.ErrorEntradaPendiente, (await _svc.FicharSalidaAsync(id, _obraA, new FichadaRequest(Guid.NewGuid(), Ahora, null, null, null))).Error);
        Assert.Equal("Ubicación inválida.", (await _svc.FicharEntradaAsync(id, _obraA, new FichadaRequest(Guid.NewGuid(), Ahora, 120, 0, 5))).Error);
    }

    // ------------------------------------------------------------ pedidos, problemas, partes

    [Fact]
    public async Task Pedido_SeGuardaConItemsYElEstudioLoResponde()
    {
        var id = await AlbanilConSesion();
        var req = new NuevoPedidoRequest(Guid.NewGuid(), Ahora, null, true, "Para el lunes",
            [new PedidoItemRequest("Cemento", 10, "bolsas"), new PedidoItemRequest("Arena", 2, "m³")]);
        var (pedido, error) = await _svc.PedirMaterialAsync(id, _obraA, req);
        Assert.Null(error);
        Assert.Equal(pedido, (await _svc.PedirMaterialAsync(id, _obraA, req)).Id); // reintento

        await _campo.CambiarEstadoPedidoAsync(pedido!.Value, EstadoPedido.Aprobado, "Llega el jueves");
        var p = Assert.Single((await _svc.ObraAsync(id, _obraA))!.Pedidos);
        Assert.Equal(("Aprobado", "Llega el jueves", true), (p.Estado, p.Respuesta, p.Urgente));
        Assert.Equal("10 bolsas Cemento, 2 m³ Arena", p.Detalle);

        Assert.NotNull((await _svc.PedirMaterialAsync(id, _obraA, req with { Ref = Guid.NewGuid(), Items = [] })).Error);
        Assert.NotNull((await _svc.PedirMaterialAsync(id, _obraA, req with { Ref = Guid.NewGuid(), Items = [new("Cal", 0, "bolsas")] })).Error);
    }

    [Fact]
    public async Task Problema_CreaEntradaEnBitacoraParaLasFotos()
    {
        var id = await AlbanilConSesion();
        var refProblema = Guid.NewGuid();
        var (problema, error) = await _svc.ReportarProblemaAsync(id, _obraA,
            new NuevoProblemaRequest(refProblema, Ahora, null, "ErrorPlano", "Alta", "La medida del vano no coincide"));
        Assert.Null(error);

        var bytes = new byte[] { 1 };
        Assert.Null((await _svc.SubirMedioAsync(id, _obraA, refProblema, Guid.NewGuid(), "plano.jpg", 1, () => new MemoryStream(bytes))).Error);

        await using var db = _db.CreateDbContext();
        var p = await db.Problemas.Include(x => x.Registro).ThenInclude(r => r!.Medios).SingleAsync(x => x.Id == problema);
        Assert.Equal((UrgenciaProblema.Alta, EstadoProblema.Abierto, _capatazId), (p.Urgencia, p.Estado, p.CapatazId!.Value));
        Assert.StartsWith("⚠ Problema (Error o duda en plano)", p.Registro!.Titulo);
        Assert.Single(p.Registro.Medios);

        Assert.NotNull((await _svc.ReportarProblemaAsync(id, _obraA, new NuevoProblemaRequest(Guid.NewGuid(), Ahora, null, "Inventado", "Alta", "x"))).Error);
    }

    [Fact]
    public async Task Parte_ReemplazaElDelMismoDiaYValidaHoras()
    {
        var id = await AlbanilConSesion();
        var tarea = await TareaAsync("Mampostería");
        var hoy = DateOnly.FromDateTime(Textos.AHoraLocal(Ahora));

        Assert.Null((await _svc.GuardarParteAsync(id, _obraA, new ParteRequest(hoy, "Levanté paño norte", [new(tarea, 6, null), new(null, 2, "Limpieza")]))).Error);
        Assert.Null((await _svc.GuardarParteAsync(id, _obraA, new ParteRequest(hoy, "Corregido", [new(tarea, 8, null)]))).Error);

        var parte = (await _svc.ObraAsync(id, _obraA))!.ParteDeHoy!;
        Assert.Equal(("Corregido", 8m), (parte.Resumen, parte.HorasTotales));
        Assert.Single(parte.Items);

        Assert.NotNull((await _svc.GuardarParteAsync(id, _obraA, new ParteRequest(hoy, null, [new(tarea, 20, null), new(null, 10, null)]))).Error);
        Assert.NotNull((await _svc.GuardarParteAsync(id, _obraA, new ParteRequest(hoy.AddDays(-30), "viejo", []))).Error);
        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.Partes.CountAsync());
    }
}
