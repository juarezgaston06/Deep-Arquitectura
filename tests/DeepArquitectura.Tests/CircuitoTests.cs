using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Agents;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Tests;

/// <summary>Circuito de datos de obra: validación, materiales, rendimientos, plan, certificados, controles, libro, portal…</summary>
public sealed class CircuitoTests : IDisposable
{
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private readonly TestDb _db = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "deeparq-medios-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero));
    private readonly AlbanilService _svc;
    private readonly CampoService _campo;
    private readonly ValidacionAvances _validacion;
    private readonly AvanceObra _avance;
    private readonly LibroObra _libro;
    private readonly ControlesObra _controles;
    private readonly PortalCliente _portal;
    private readonly int _obra, _otraObra, _etapa, _capataz, _otroCapataz;
    private readonly DateOnly _hoy = new(2026, 11, 11);
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    public CircuitoTests()
    {
        var almacen = new AlmacenMediosLocal(_carpeta);
        var estudio = new EstudioService(_db);
        _svc = new AlbanilService(_db, almacen, estudio, _reloj);
        _campo = new CampoService(_db, _reloj);
        _validacion = new ValidacionAvances(_db, _reloj);
        _avance = new AvanceObra(_db, _reloj);
        _libro = new LibroObra(_db, _reloj);
        _controles = new ControlesObra(_db);
        _portal = new PortalCliente(_db, almacen, _avance);

        using var db = _db.CreateDbContext();
        var cliente = new Cliente { Nombre = "Cliente" };
        var obra = new Obra
        {
            Codigo = "OB-2026-010", Nombre = "Casa Ruiz", Cliente = cliente, Estado = EstadoObra.EnCurso, MontoPresupuesto = 1_000_000,
            Latitud = -31.4201, Longitud = -64.1888, FechaInicio = _hoy.AddDays(-10),
            Etapas = [new EtapaObra { Orden = 1, Nombre = "Mampostería", Estado = EstadoEtapa.EnCurso }],
        };
        var otra = new Obra { Codigo = "OB-2026-011", Nombre = "Otra", Cliente = cliente };
        var capataz = new Capataz { Nombre = "Juan Capataz", Usuario = "jc", PinHash = "x" };
        var otroCapataz = new Capataz { Nombre = "Otro", Usuario = "otro", PinHash = "x" };
        db.AddRange(obra, otra, capataz, otroCapataz);
        db.SaveChanges();
        db.CapatazObras.AddRange(new CapatazObra { CapatazId = capataz.Id, ObraId = obra.Id }, new CapatazObra { CapatazId = otroCapataz.Id, ObraId = otra.Id });
        db.SaveChanges();
        (_obra, _otraObra, _etapa, _capataz, _otroCapataz) = (obra.Id, otra.Id, obra.Etapas[0].Id, capataz.Id, otroCapataz.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private async Task<int> Albanil(string usuario = "pedro", string? oficio = "Oficial albañil")
    {
        var (id, error) = await _svc.CrearAsync($"Albañil {usuario}", usuario, null, oficio, _capataz, "123456", [_obra]);
        Assert.Null(error);
        return id!.Value;
    }

    private async Task<int> Tarea(string titulo = "Muro PB", string? unidad = "m²", decimal? objetivo = 40, int? albanil = null,
        DateOnly? inicio = null, DateOnly? fin = null, string? rubro = null, decimal? peso = null, string? oficio = null) =>
        (await _campo.CrearTareaAsync(_obra, new CampoService.DatosTarea
        {
            Titulo = titulo, Unidad = unidad, CantidadObjetivo = unidad is null ? null : objetivo, AlbanilId = albanil, EtapaObraId = _etapa,
            FechaInicioPlan = inicio, FechaLimite = fin, Rubro = rubro, Peso = peso, Oficio = oficio,
        })).Id!.Value;

    private async Task<int> Avance(int albanil, int tarea, decimal? cantidad = null, int? porcentaje = null, bool terminada = false, DateTime? fecha = null)
    {
        var (r, error) = await _svc.RegistrarAvanceAsync(albanil, _obra, new NuevoAvanceRequest(Guid.NewGuid(), fecha ?? Ahora, tarea, cantidad, porcentaje, terminada, null));
        Assert.Null(error);
        return r!.Id;
    }

    private async Task<TareaObra> LeerTarea(int id)
    {
        await using var db = _db.CreateDbContext();
        return await db.Tareas.SingleAsync(t => t.Id == id);
    }

    // ------------------------------------------------------------ 1. Validación del capataz

    [Fact]
    public async Task Validacion_AprobarCorregirYRechazar()
    {
        var a = await Albanil();
        var t = await Tarea();
        var r1 = await Avance(a, t, 10);
        var r2 = await Avance(a, t, 8);

        var tarea = await LeerTarea(t);
        Assert.Equal((18m, 0m), (tarea.CantidadHecha, tarea.CantidadAprobada)); // informado, nada aprobado todavía
        Assert.Equal(2, (await _validacion.PendientesAsync(capatazId: _capataz)).Count);
        Assert.Empty(await _validacion.PendientesAsync(capatazId: _otroCapataz));

        Assert.Null(await _validacion.ValidarAsync(r1, EstadoValidacion.Corregido, cantidad: 6, capatazId: _capataz));
        Assert.NotNull(await _validacion.ValidarAsync(r2, EstadoValidacion.Rechazado, capatazId: _capataz)); // sin motivo
        Assert.Null(await _validacion.ValidarAsync(r2, EstadoValidacion.Rechazado, motivo: "Ese paño es del otro equipo", capatazId: _capataz));
        Assert.NotNull(await _validacion.ValidarAsync(r1, EstadoValidacion.Aprobado, capatazId: _otroCapataz)); // obra ajena

        tarea = await LeerTarea(t);
        Assert.Equal((6m, 6m, 15), (tarea.CantidadHecha, tarea.CantidadAprobada, tarea.PorcentajeAprobado));

        // Lo ve el albañil, con el motivo.
        var obra = await _svc.ObraAsync(a, _obra);
        var rechazado = obra!.MisAvances.Single(x => x.Id == r2);
        Assert.Equal(("Rechazado", "Ese paño es del otro equipo"), (rechazado.Validacion, rechazado.Motivo));

        // Cambiar de opinión: aprobar lo corregido vuelve a lo informado.
        Assert.Null(await _validacion.ValidarAsync(r1, EstadoValidacion.Aprobado, capatazId: _capataz));
        Assert.Equal(10m, (await LeerTarea(t)).CantidadAprobada);
    }

    [Fact]
    public async Task Validacion_RechazarTerminadaReabreLaTarea()
    {
        var a = await Albanil();
        var t = await Tarea(unidad: null);
        var r = await Avance(a, t, terminada: true);
        Assert.Equal(EstadoTarea.Completada, (await LeerTarea(t)).Estado);

        await _validacion.ValidarAsync(r, EstadoValidacion.Rechazado, motivo: "Falta terminar el remate");
        var tarea = await LeerTarea(t);
        Assert.Equal((EstadoTarea.EnCurso, 0), (tarea.Estado, tarea.Porcentaje));
    }

    private CampoCapataz CampoCapataz()
    {
        var almacen = new AlmacenMediosLocal(_carpeta);
        return new CampoCapataz(_db, new CapatazService(_db, almacen, new EstudioService(_db), _reloj), _validacion, _libro,
            new CostosObra(_db, new Adjuntos(_db, almacen), _reloj), almacen, _reloj);
    }

    [Fact]
    public async Task AppCapataz_VeLoPendienteYValidaDesdeLaApp()
    {
        var campo = CampoCapataz();
        var a = await Albanil();
        var t = await Tarea(fin: _hoy.AddDays(-2));
        var refAvance = Guid.NewGuid();
        var (r, _) = await _svc.RegistrarAvanceAsync(a, _obra, new NuevoAvanceRequest(refAvance, Ahora, t, 12, null, false, "Paño norte"));
        var (foto, errorFoto) = await _svc.SubirMedioAsync(a, _obra, refAvance, Guid.NewGuid(), "foto.jpg", 10, () => new MemoryStream(new byte[10]));
        Assert.Null(errorFoto);
        await _libro.EmitirOrdenAsync(_obra, "No hormigonar sin aviso");

        var obra = (await campo.ObraAsync(_capataz, _obra))!;
        var p = Assert.Single(obra.PorValidar!);
        Assert.Equal(("Albañil pedro", "Muro PB", 12m, "m²"), (p.Albanil, p.Tarea, p.Cantidad, p.Unidad));
        Assert.Equal(new[] { foto!.Value }, p.FotoIds);
        Assert.Single(obra.Ordenes!);
        var atrasada = Assert.Single(obra.Atrasadas!);
        Assert.Equal(t, atrasada.Id);
        Assert.Null(atrasada.Causa);
        Assert.Null(await campo.ObraAsync(_otroCapataz, _obra));

        // La foto la ve su capataz, no otro.
        var abierta = await campo.AbrirFotoAsync(_capataz, foto.Value);
        Assert.NotNull(abierta);
        await abierta.Value.Contenido.DisposeAsync();
        Assert.Null(await campo.AbrirFotoAsync(_otroCapataz, foto.Value));

        // Validar desde la cola del teléfono: repetir el envío deja lo mismo.
        var validar = new ValidacionesRequest([new ValidacionItem(r!.Id, ValoresCapataz.Aprobar)]);
        Assert.Null(await campo.ValidarAsync(_capataz, _obra, validar));
        Assert.Null(await campo.ValidarAsync(_capataz, _obra, validar));
        Assert.Equal(12m, (await LeerTarea(t)).CantidadAprobada);
        Assert.NotNull(await campo.ValidarAsync(_capataz, _obra, new([new ValidacionItem(r.Id, "quizás")])));
        Assert.NotNull(await campo.ValidarAsync(_otroCapataz, _otraObra, validar)); // no es de su obra
        Assert.NotNull(await campo.ValidarAsync(_capataz, _obra, new([new ValidacionItem(r.Id, ValoresCapataz.Rechazar)]))); // sin motivo

        // Causa del atraso y órdenes leídas.
        Assert.Null(await campo.InformarAtrasoAsync(_capataz, _obra, new AtrasoRequest(t, "Clima", "Llovió tres días")));
        Assert.NotNull(await campo.InformarAtrasoAsync(_capataz, _obra, new AtrasoRequest(t, "Inventada", null)));
        await campo.MarcarLeidoAsync(_capataz, _obra, new LeidoRequest(obra.Ordenes!.Select(o => o.Id).ToList()));
        obra = (await campo.ObraAsync(_capataz, _obra))!;
        Assert.Empty(obra.PorValidar!);
        Assert.Empty(obra.Ordenes!);
        Assert.Equal("Clima", Assert.Single(obra.Atrasadas!).Causa);
    }

    [Theory]
    [InlineData(8, 1)]    // 05:00 en Córdoba → falta 1 h para las 6
    [InlineData(9, 24)]   // 06:00 en punto → la de mañana
    [InlineData(10, 23)]  // 07:00 → mañana a las 6
    public void Clima_CorreUnaVezPorDiaALaHoraLocal(int horaUtc, int horasQueFaltan) =>
        Assert.Equal(TimeSpan.FromHours(horasQueFaltan),
            ActualizacionClima.HastaLaProxima(new DateTime(2026, 11, 18, horaUtc, 0, 0, DateTimeKind.Utc), 6));

    [Fact]
    public void AppCapataz_LasOpcionesDeLaAppSonLasDelServidor()
    {
        static void Iguales<T>((string Valor, string Etiqueta)[] lista) where T : struct, Enum =>
            Assert.Equal(Enum.GetNames<T>().Order(), lista.Select(x => x.Valor).Order());
        Iguales<CausaAtraso>(ValoresCapataz.CausasAtraso);
        Iguales<CategoriaGasto>(ValoresCapataz.CategoriasGasto);
        Iguales<DestinoSobrante>(ValoresCapataz.DestinosSobrante);
    }

    [Fact]
    public async Task AppCapataz_SobrantesYNotasNoSeDuplicanYSoloVanASusObras()
    {
        var campo = CampoCapataz();
        var s = new NuevoSobranteRequest(Guid.NewGuid(), Ahora, "Cemento", 5, "bolsas", "Deposito", null, null);
        var (s1, e1) = await campo.SobranteAsync(_capataz, _obra, s);
        var (s2, _) = await campo.SobranteAsync(_capataz, _obra, s);
        Assert.Null(e1);
        Assert.Equal(s1, s2);
        // A otra obra: solo a una de las suyas.
        Assert.NotNull((await campo.SobranteAsync(_capataz, _obra, s with { Ref = Guid.NewGuid(), Destino = "OtraObra", ObraDestinoId = _otraObra })).Error);
        Assert.NotNull((await campo.SobranteAsync(_otroCapataz, _obra, s with { Ref = Guid.NewGuid() })).Error);
        await using (var db = _db.CreateDbContext())
            Assert.Equal(1, await db.Sobrantes.CountAsync());

        var nota = new NotaPedidoRequest(Guid.NewGuid(), Ahora, "Pedimos definir la mesada");
        var (n1, _) = await campo.NotaPedidoAsync(_capataz, _obra, nota);
        Assert.Equal(n1, (await campo.NotaPedidoAsync(_capataz, _obra, nota)).Id);
        // Las fotos de la nota se cuelgan de su Ref, como las de cualquier novedad del capataz.
        var capataces = new CapatazService(_db, new AlmacenMediosLocal(_carpeta), new EstudioService(_db), _reloj);
        Assert.Null((await capataces.SubirMedioAsync(_capataz, _obra, nota.Ref, Guid.NewGuid(), "nota.jpg", 10, () => new MemoryStream(new byte[10]))).Error);
    }

    // ------------------------------------------------------------ 2. Recepción de materiales

    [Fact]
    public async Task Recepcion_CierraElPedidoYAvisaLoQueFalta()
    {
        var a = await Albanil();
        var (pedidoId, _) = await _svc.PedirMaterialAsync(a, _obra, new NuevoPedidoRequest(Guid.NewGuid(), Ahora, null, false, null,
            [new("Cemento", 10, "bolsas"), new("Arena", 2, "m³")]));
        var obra = await _svc.ObraAsync(a, _obra);
        var items = obra!.Pedidos.Single().Items!;

        var refRecepcion = Guid.NewGuid();
        var req = new NuevaRecepcionRequest(refRecepcion, Ahora, pedidoId, "Corralón Sur", "0001-123", null,
            [new(items[0].Id, "Cemento", 10, "bolsas"), new(items[1].Id, "Arena", 1, "m³")]);
        var (rec, error) = await _svc.RegistrarRecepcionAsync(a, _obra, req);
        Assert.Null(error);
        Assert.Equal(rec, (await _svc.RegistrarRecepcionAsync(a, _obra, req)).Id); // reintento

        await using (var db = _db.CreateDbContext())
        {
            Assert.Equal(EstadoPedido.EntregaParcial, (await db.Pedidos.SingleAsync()).Estado);
            var problema = await db.Problemas.SingleAsync();
            Assert.Equal(TipoProblema.FaltaMaterial, problema.Tipo);
            Assert.Contains("1 m³ Arena", problema.Descripcion);
        }
        Assert.Equal(1m, (await _svc.ObraAsync(a, _obra))!.Pedidos.Single().Items![1].Recibido);

        // Foto del remito colgada de la recepción.
        Assert.Null((await _svc.SubirMedioAsync(a, _obra, refRecepcion, Guid.NewGuid(), "remito.jpg", 1, () => new MemoryStream([1]))).Error);

        await _svc.RegistrarRecepcionAsync(a, _obra, new NuevaRecepcionRequest(Guid.NewGuid(), Ahora, pedidoId, null, null, null, [new(items[1].Id, "Arena", 1, "m³")]));
        await using var db2 = _db.CreateDbContext();
        Assert.Equal(EstadoPedido.Entregado, (await db2.Pedidos.SingleAsync()).Estado);
    }

    // ------------------------------------------------------------ 3. Rendimientos

    [Fact]
    public async Task Rendimientos_CantidadAprobadaSobreHorasDelParte()
    {
        var a = await Albanil();
        var t = await Tarea(rubro: "Mampostería de ladrillo hueco");
        var r = await Avance(a, t, 16);
        await _svc.GuardarParteAsync(a, _obra, new ParteRequest(_hoy, null, [new(t, 8, null)]));

        // Sin aprobar no cuenta.
        Assert.Empty((await _avance.RendimientosAsync()).PorRubro);

        await _validacion.ValidarAsync(r, EstadoValidacion.Aprobado);
        var res = await _avance.RendimientosAsync();
        var fila = Assert.Single(res.PorRubro);
        Assert.Equal(("Mampostería de ladrillo hueco", "m²", 2m, 16m), (fila.Rubro, fila.Unidad, fila.PorHora!.Value, fila.PorJornada!.Value));
        var persona = Assert.Single(res.PorPersona);
        Assert.Equal((a, 2m), (persona.AlbanilId, persona.PorHora!.Value));
    }

    // ------------------------------------------------------------ 4. Plan vs real

    [Fact]
    public async Task Curva_PlanContraRealYAtrasadas()
    {
        var a = await Albanil();
        var t = await Tarea(objetivo: 40, inicio: _hoy.AddDays(-10), fin: _hoy.AddDays(10));
        var atrasada = await Tarea("Encadenado", unidad: null, inicio: _hoy.AddDays(-10), fin: _hoy.AddDays(-1));
        await _validacion.ValidarAsync(await Avance(a, t, 20), EstadoValidacion.Aprobado);

        var curva = await _avance.CurvaAsync(_obra);
        Assert.NotNull(curva);
        // Dos tareas iguales: la primera va 50 % de plan y 50 % real; la atrasada 100 % de plan y 0 real.
        Assert.Equal(75m, curva!.PlanHoy);
        Assert.Equal(25m, curva.RealHoy);
        Assert.Equal(atrasada, Assert.Single(curva.Atrasadas).Id);
        Assert.Contains(curva.Puntos, p => p.Fecha == _hoy);
        Assert.True((await _svc.ObraAsync(a, _obra))!.Tareas.Single(x => x.Id == atrasada).Atrasada);
    }

    // ------------------------------------------------------------ 5. Certificados

    [Fact]
    public async Task Certificado_CobraSoloLoAprobadoNuevo()
    {
        var a = await Albanil();
        var t1 = await Tarea("Muros", unidad: null);
        await Tarea("Revoques", unidad: null);
        await _validacion.ValidarAsync(await Avance(a, t1, terminada: true), EstadoValidacion.Aprobado);

        var (vista, error) = await _avance.CalcularCertificadoAsync(_obra);
        Assert.Null(error);
        Assert.Equal((1, 50m, 500_000m), (vista!.Numero, vista.PorcentajeAcumulado, vista.Monto));

        var (cert, _) = await _avance.EmitirCertificadoAsync(_obra, null);
        Assert.NotNull((await _avance.EmitirCertificadoAsync(_obra, null)).Error); // nada nuevo
        var (segundo, _) = await _avance.CalcularCertificadoAsync(_obra);
        Assert.Equal((2, 50m, 0m), (segundo!.Numero, segundo.PorcentajeAnterior, segundo.Monto));

        Assert.Null(await _avance.MarcarCobradoAsync(cert!.Value, MedioPago.Transferencia));
        await using var db = _db.CreateDbContext();
        Assert.Equal(500_000m, (await db.Pagos.SingleAsync()).Monto);
        Assert.NotNull(await _avance.AnularCertificadoAsync(cert.Value)); // cobrado
    }

    // ------------------------------------------------------------ 6. Controles de etapa

    [Fact]
    public async Task Controles_PlantillaYRespuestaDesdeLaApp()
    {
        Assert.Equal("Mampostería", ControlesObra.Sugerida("Mampostería planta alta"));
        Assert.Null(await _controles.AgregarPlantillaAsync(_etapa, "Mampostería"));
        Assert.Null(await _controles.AgregarPlantillaAsync(_etapa, "Mampostería")); // no duplica

        var a = await Albanil();
        var controles = (await _svc.ObraAsync(a, _obra))!.Controles!;
        Assert.Equal(4, controles.Count);
        Assert.All(controles, c => Assert.Null(c.Resultado));

        var c0 = controles[0].Id;
        Assert.NotNull((await _svc.ResponderControlAsync(a, _obra, new RespuestaControlRequest(Guid.NewGuid(), Ahora, c0, "Observado", null))).Error);
        var refOk = Guid.NewGuid();
        Assert.Null((await _svc.ResponderControlAsync(a, _obra, new RespuestaControlRequest(refOk, Ahora, c0, "Ok", null))).Error);
        Assert.Null((await _svc.SubirMedioAsync(a, _obra, refOk, Guid.NewGuid(), "capa.jpg", 1, () => new MemoryStream([1]))).Error);

        Assert.Equal("Ok", (await _svc.ObraAsync(a, _obra))!.Controles![0].Resultado);
        Assert.NotNull(await _controles.BorrarAsync(c0)); // tiene respuesta
        Assert.Equal(ResultadoControl.Ok, (await _controles.DeObraAsync(_obra))[0].Resultado);
    }

    // ------------------------------------------------------------ 7. Libro de obra

    [Fact]
    public async Task Libro_OrdenesNumeradasConLecturaYNotasDePedido()
    {
        var a = await Albanil();
        var (o1, _) = await _libro.EmitirOrdenAsync(_obra, "Usar ladrillo hueco del 18 en medianera");
        await _libro.EmitirOrdenAsync(_obra, "No hormigonar sin aviso");
        var ordenes = (await _svc.ObraAsync(a, _obra))!.Ordenes!;
        Assert.Equal([2, 1], ordenes.Select(o => o.Numero));
        Assert.All(ordenes, o => Assert.False(o.Leida));

        await _svc.MarcarLeidoAsync(a, _obra, new LeidoRequest([o1!.Value, o1.Value]));
        Assert.True((await _svc.ObraAsync(a, _obra))!.Ordenes!.Single(o => o.Id == o1).Leida);

        var refNota = Guid.NewGuid();
        var (nota, error) = await _svc.NotaPedidoAsync(a, _obra, new NotaPedidoRequest(refNota, Ahora, "Falta definir altura de mesada"));
        Assert.Null(error);
        Assert.Equal(nota, (await _svc.NotaPedidoAsync(a, _obra, new NotaPedidoRequest(refNota, Ahora, "Falta definir altura de mesada"))).Id);

        // Capataz desde su app: el reintento del teléfono (mismo Ref) no duplica; en una obra ajena no se asienta.
        var refCapataz = Guid.NewGuid();
        var (n1, e1) = await _libro.NotaPedidoCapatazAsync(_capataz, _obra, refCapataz, "Pedimos plano de detalle de escalera", Ahora);
        var (n2, _) = await _libro.NotaPedidoCapatazAsync(_capataz, _obra, refCapataz, "Pedimos plano de detalle de escalera", Ahora);
        Assert.Null(e1);
        Assert.Equal(n1, n2);
        Assert.NotNull((await _libro.NotaPedidoCapatazAsync(_capataz, _otraObra, Guid.NewGuid(), "x")).Error);
        await using (var db = _db.CreateDbContext())
            Assert.Equal(2, (await db.Asientos.SingleAsync(x => x.Id == n1)).Numero);
        Assert.Equal(2, (await _libro.OrdenesSinLeerCapatazAsync(_capataz, _obra)).Count);
    }

    // ------------------------------------------------------------ 8. Ubicación y protección al fichar

    [Fact]
    public async Task Fichada_LejosDeLaObraYSinArnesEnAltura()
    {
        var a = await Albanil();
        // ~2 km al norte de la obra, en altura y sin arnés.
        await _svc.FicharEntradaAsync(a, _obra, new FichadaRequest(Guid.NewGuid(), Ahora, -31.4021, -64.1888, 10, true, ["Arnés"]));
        await using var db = _db.CreateDbContext();
        var j = await db.Jornadas.SingleAsync();
        Assert.InRange(j.EntradaDistanciaMetros!.Value, 1900, 2100);
        Assert.Equal("Arnés", j.EppFaltante);
        var p = await db.Problemas.SingleAsync();
        Assert.Equal((TipoProblema.Seguridad, UrgenciaProblema.Alta), (p.Tipo, p.Urgencia));
        Assert.InRange(Geo.DistanciaMetros(-31.4201, -64.1888, -31.4201, -64.1888)!.Value, 0, 1);
    }

    // ------------------------------------------------------------ 9 y 12. Puntos fijos y portal del cliente

    [Fact]
    public async Task PuntoFijoYPortal_SoloMuestranLoVisible()
    {
        var a = await Albanil();
        int punto;
        await using (var db = _db.CreateDbContext())
        {
            var p = new PuntoFoto { ObraId = _obra, Nombre = "Fachada", Indicacion = "Desde la vereda de enfrente" };
            db.PuntosFoto.Add(p);
            await db.SaveChangesAsync();
            punto = p.Id;
        }
        Assert.True((await _svc.ObraAsync(a, _obra))!.PuntosFoto!.Single().Pendiente);

        var refFija = Guid.NewGuid();
        await _svc.RegistrarAvanceAsync(a, _obra, new NuevoAvanceRequest(refFija, Ahora, null, null, null, false, null, punto));
        var (fija, _) = await _svc.SubirMedioAsync(a, _obra, refFija, Guid.NewGuid(), "fachada.jpg", 1, () => new MemoryStream([1]));
        var refInterna = Guid.NewGuid();
        await _svc.RegistrarAvanceAsync(a, _obra, new NuevoAvanceRequest(refInterna, Ahora, null, null, null, false, "interna"));
        var (interna, _) = await _svc.SubirMedioAsync(a, _obra, refInterna, Guid.NewGuid(), "x.jpg", 1, () => new MemoryStream([1]));

        Assert.False((await _svc.ObraAsync(a, _obra))!.PuntosFoto!.Single().Pendiente);
        var ultima = await _svc.UltimaFotoPuntoAsync(a, _obra, punto);
        Assert.NotNull(ultima);
        await ultima.Value.Contenido.DisposeAsync();

        Assert.Null(await _portal.VistaAsync("no-existe"));
        var token = await _portal.ActivarAsync(_obra);
        var vista = await _portal.VistaAsync(token);
        Assert.Equal("Casa Ruiz", vista!.Obra);
        Assert.Equal(fija, Assert.Single(Assert.Single(vista.Puntos).Fotos).MedioId);
        Assert.Empty(vista.Bitacora);
        var visible = await _portal.MedioAsync(token, fija!.Value);
        Assert.NotNull(visible);
        await visible.Value.Contenido.DisposeAsync();
        Assert.Null(await _portal.MedioAsync(token, interna!.Value)); // no visible
        await _portal.ApagarAsync(_obra);
        Assert.Null(await _portal.VistaAsync(token));
    }

    // ------------------------------------------------------------ 15. Oficios

    [Fact]
    public async Task TareaDeOficio_SoloLaVeEseOficio()
    {
        var albanil = await Albanil("pedro", "Oficial albañil");
        var electricista = await Albanil("luis", "Electricista");
        var t = await Tarea("Cableado PB", unidad: null, oficio: "Electricista");
        Assert.DoesNotContain((await _svc.ObraAsync(albanil, _obra))!.Tareas, x => x.Id == t);
        Assert.Contains((await _svc.ObraAsync(electricista, _obra))!.Tareas, x => x.Id == t);
    }

    // ------------------------------------------------------------ 10. Clima y 13. resumen diario

    [Fact]
    public void Clima_InterpretaOpenMeteo()
    {
        const string json = """
            {"daily":{"time":["2026-11-10","2026-11-11"],"weather_code":[61,0],"temperature_2m_max":[24.1,30.5],
             "temperature_2m_min":[15.0,17.2],"precipitation_sum":[12.4,0.0],"wind_speed_10m_max":[20.0,null]}}
            """;
        var dias = ClimaService.Interpretar(json);
        Assert.Equal(2, dias.Count);
        Assert.Equal((new DateOnly(2026, 11, 10), 12.4, 61), (dias[0].Fecha, dias[0].LluviaMm!.Value, dias[0].CodigoTiempo!.Value));
        Assert.Null(dias[1].VientoMaxKmh);
        Assert.True(new ClimaDiario { LluviaMm = 12.4 }.Desfavorable);
        Assert.Equal("Lluvia", ClimaService.Descripcion(61));
    }

    [Fact]
    public async Task Resumen_DestacaLoUrgente()
    {
        var a = await Albanil();
        await _svc.ReportarProblemaAsync(a, _obra, new NuevoProblemaRequest(Guid.NewGuid(), Ahora, null, "Accidente", "Alta", "Se cayó un andamio"));
        await _svc.FicharEntradaAsync(a, _obra, new FichadaRequest(Guid.NewGuid(), Ahora.AddHours(-3), -31.4201, -64.1888, 5, false, []));
        var resumen = new ResumenDiario(_db, _validacion);
        var obra = (await resumen.GenerarAsync(_hoy)).Single(o => o.Id == _obra);
        Assert.Single(obra.Presentes);
        Assert.Contains(obra.Alertas, x => x.Nivel == 2 && x.Texto.Contains("andamio"));
        Assert.False(resumen.ConIA);
    }
}
