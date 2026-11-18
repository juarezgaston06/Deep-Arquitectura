using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Endpoints;
using DeepArquitectura.Web.Presupuestos;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Tests;

/// <summary>Datos del proceso: origen de consultas, motivos de pérdida, relevamientos, costos reales, atrasos, trámites,
/// sobrantes, acta de entrega, garantía, encuestas, permiso de difusión y pagos informados.</summary>
public sealed class DatosProcesoTests : IDisposable
{
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly TestDb _db = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "deeparq-proceso-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero));
    private readonly Adjuntos _adjuntos;
    private readonly EstudioService _estudio;
    private readonly CostosObra _costos;
    private readonly TramitesObra _tramites;
    private readonly Postventa _postventa;
    private readonly Relevamientos _relevamientos;
    private readonly int _obra, _otra, _cliente, _etapa1, _etapa2, _capataz;

    public DatosProcesoTests()
    {
        _adjuntos = new Adjuntos(_db, new AlmacenMediosLocal(_carpeta));
        _estudio = new EstudioService(_db);
        _costos = new CostosObra(_db, _adjuntos, _reloj);
        _tramites = new TramitesObra(_db, _reloj);
        _postventa = new Postventa(_db, _adjuntos, _reloj);
        _relevamientos = new Relevamientos(_db, _adjuntos);

        using var db = _db.CreateDbContext();
        var cliente = new Cliente { Nombre = "Laura" };
        var obra = new Obra
        {
            Codigo = "OB-1", Nombre = "Casa Laura", Cliente = cliente, MontoPresupuesto = 1_000_000, Estado = EstadoObra.EnCurso, Servicio = TipoServicio.Gas,
            Etapas = [new EtapaObra { Orden = 1, Nombre = "Fundaciones" }, new EtapaObra { Orden = 2, Nombre = "Mampostería" }],
        };
        var otra = new Obra { Codigo = "OB-2", Nombre = "Otra", Cliente = cliente };
        var capataz = new Capataz { Nombre = "Raúl", Usuario = "raul", PinHash = "x" };
        db.AddRange(obra, otra, capataz);
        db.SaveChanges();
        db.CapatazObras.Add(new CapatazObra { CapatazId = capataz.Id, ObraId = obra.Id });
        db.SaveChanges();
        (_obra, _otra, _cliente, _capataz) = (obra.Id, otra.Id, cliente.Id, capataz.Id);
        (_etapa1, _etapa2) = (obra.Etapas[0].Id, obra.Etapas[1].Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private static Adjuntos.Archivo Foto(string nombre = "foto.jpg", int bytes = 10) =>
        new(nombre, bytes, () => new MemoryStream(new byte[bytes]));

    private async Task DarAccesoAsync()
    {
        await using var db = _db.CreateDbContext();
        db.AccesosCliente.Add(new AccesoCliente { ObraId = _obra, Codigo = "DEEP-AAAA-BBBB", PinHash = "x", Sello = "s" });
        await db.SaveChangesAsync();
    }

    // ================================================================== 1. Origen de la consulta

    [Theory]
    [InlineData("instagram", null, null, OrigenCliente.Instagram)]
    [InlineData(null, null, "l.instagram.com", OrigenCliente.Instagram)]
    [InlineData(null, null, "m.facebook.com", OrigenCliente.Facebook)]
    [InlineData("tiktok", "social", null, OrigenCliente.TikTok)]
    [InlineData(null, null, "www.google.com.ar", OrigenCliente.Buscador)]
    [InlineData("cartel", "qr", null, OrigenCliente.CartelObra)]
    [InlineData(null, null, null, OrigenCliente.Web)]
    public void Clasifica_el_canal(string? fuente, string? medio, string? referente, OrigenCliente esperado) =>
        Assert.Equal(esperado, EstudioService.ClasificarCanal(fuente, medio, referente));

    [Fact]
    public async Task La_consulta_guarda_origen_y_publicacion_y_el_cliente_hereda_el_canal()
    {
        int pub;
        await using (var db = _db.CreateDbContext())
        {
            var p = new Publicacion { Titulo = "Reel losa", Contenido = "x", Red = RedSocial.Instagram };
            db.Publicaciones.Add(p);
            await db.SaveChangesAsync();
            pub = p.Id;
        }
        var link = EstudioService.LinkConSeguimiento("https://deep.example/", RedSocial.Instagram, pub);
        Assert.Equal($"https://deep.example/?utm_source=instagram&utm_medium=social&utm_campaign=pub-{pub}", link);

        var id = await _estudio.RegistrarConsultaAsync("Ana", "ana@example.com", null, "Hola", AreaConsulta.General,
            new EstudioService.OrigenVisita("Instagram", "social", $"pub-{pub}", "https://l.instagram.com/x?y=1", "/gas"));
        await using (var db = _db.CreateDbContext())
        {
            var c = await db.Consultas.SingleAsync(x => x.Id == id);
            Assert.Equal(OrigenCliente.Instagram, c.Canal);
            Assert.Equal(pub, c.PublicacionId);
            Assert.Equal("l.instagram.com", c.Referente); // sin la ruta ni el query
            Assert.Equal("/gas", c.PaginaEntrada);
        }

        // Una campaña con id inexistente o un referente que no es URL no rompen nada.
        var id2 = await _estudio.RegistrarConsultaAsync("Beto", "beto@example.com", null, "Hola", AreaConsulta.General,
            new EstudioService.OrigenVisita(null, null, "pub-99999", "javascript:alert(1)", "https://otro.com"));
        await using (var db = _db.CreateDbContext())
        {
            var c = await db.Consultas.SingleAsync(x => x.Id == id2);
            Assert.Null(c.PublicacionId);
            Assert.Null(c.Referente);
            Assert.Null(c.PaginaEntrada);
        }

        var clienteId = await _estudio.ConvertirEnClienteAsync(id);
        await using (var db = _db.CreateDbContext())
        {
            Assert.Equal(OrigenCliente.Instagram, (await db.Clientes.SingleAsync(x => x.Id == clienteId)).Origen);
            Assert.NotNull((await db.Consultas.SingleAsync(x => x.Id == id)).ContactadaEn);
        }
    }

    // ================================================================== 2. Motivos de pérdida

    [Fact]
    public async Task Descartar_una_consulta_pide_motivo_y_el_tiempo_de_respuesta_queda_del_primer_contacto()
    {
        var id = await _estudio.RegistrarConsultaAsync("Ana", "ana@example.com", null, "Hola", AreaConsulta.General);
        Assert.NotNull(await _estudio.CambiarEstadoConsultaAsync(id, EstadoConsulta.Descartada));
        Assert.Null(await _estudio.CambiarEstadoConsultaAsync(id, EstadoConsulta.Contactada));
        DateTime primera;
        await using (var db = _db.CreateDbContext()) primera = (await db.Consultas.SingleAsync(x => x.Id == id)).ContactadaEn!.Value;
        Assert.Null(await _estudio.CambiarEstadoConsultaAsync(id, EstadoConsulta.Descartada, MotivoPerdida.Precio));
        await using (var db = _db.CreateDbContext())
        {
            var c = await db.Consultas.SingleAsync(x => x.Id == id);
            Assert.Equal(MotivoPerdida.Precio, c.MotivoDescarte);
            Assert.Equal(primera, c.ContactadaEn);
        }
    }

    [Fact]
    public async Task Un_presupuesto_rechazado_exige_motivo_y_guarda_cuando_se_decidio()
    {
        int id;
        await using (var db = _db.CreateDbContext())
        {
            var arancel = new ArancelVersion { Nombre = "A", Contenido = "{}", VigenteDesde = new(2026, 1, 1), VigenteHasta = new(2026, 12, 31) };
            var p = new Presupuesto { Numero = "PR-1", Titulo = "Planos", Estado = EstadoPresupuesto.Enviado, Fecha = new(2026, 11, 1), ArancelVersion = arancel, Parametros = "{}" };
            db.Presupuestos.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        }
        var svc = new PresupuestoService(_db, _estudio);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CambiarEstadoAsync(id, EstadoPresupuesto.Rechazado));
        await svc.CambiarEstadoAsync(id, EstadoPresupuesto.Rechazado, MotivoPerdida.EligioOtro);
        await using var db2 = _db.CreateDbContext();
        var r = await db2.Presupuestos.SingleAsync(x => x.Id == id);
        Assert.Equal(MotivoPerdida.EligioOtro, r.MotivoPerdida);
        Assert.NotNull(r.DecididoEn);
    }

    // ================================================================== 3. Relevamiento

    [Fact]
    public async Task El_relevamiento_calcula_la_superficie_y_le_pasa_la_ubicacion_a_la_obra()
    {
        Assert.NotNull((await _relevamientos.GuardarAsync(new Relevamiento { Fecha = new(2026, 11, 12) })).Error); // sin cliente ni contacto
        Assert.NotNull((await _relevamientos.GuardarAsync(new Relevamiento { Contacto = "X", FrenteM = -1 })).Error);

        var r = await _relevamientos.NuevoAsync(null, null, _obra);
        Assert.Equal(_cliente, r.ClienteId);
        r.FrenteM = 10; r.FondoM = 30; r.Latitud = -31.4; r.Longitud = -64.2; r.Hallazgos = "Humedad, Fisuras / grietas";
        var (id, error) = await _relevamientos.GuardarAsync(r);
        Assert.Null(error);
        Assert.Null(await _relevamientos.AgregarFotosAsync(id!.Value, [Foto(), Foto("b.png")]));
        Assert.NotNull(await _relevamientos.AgregarFotosAsync(id.Value, [Foto("virus.exe")]));

        var guardado = await _relevamientos.ObtenerAsync(id.Value);
        Assert.Equal(300m, guardado!.SuperficieTerrenoM2);
        Assert.Equal(2, guardado.Adjuntos.Count);
        Assert.Equal(["Humedad", "Fisuras / grietas"], Relevamientos.Hallazgos(guardado));
        await using var db = _db.CreateDbContext();
        Assert.Equal(-31.4, (await db.Obras.SingleAsync(o => o.Id == _obra)).Latitud);
    }

    // ================================================================== 4 + 5 + 6. Costos reales y rentabilidad

    [Fact]
    public async Task La_rentabilidad_suma_materiales_con_precio_gastos_y_horas_repartidas_por_etapa()
    {
        int item1, item2, recepcion;
        await using (var db = _db.CreateDbContext())
        {
            var albanil = new Albanil { Nombre = "Pedro", Usuario = "pedro", PinHash = "x", CapatazId = _capataz, ValorHora = 5000 };
            db.Albaniles.Add(albanil);
            var t1 = new TareaObra { ObraId = _obra, EtapaObraId = _etapa1, Titulo = "Zapatas" };
            var t2 = new TareaObra { ObraId = _obra, EtapaObraId = _etapa2, Titulo = "Muros" };
            db.Tareas.AddRange(t1, t2);
            await db.SaveChangesAsync();
            var pedido = new PedidoMaterial { Ref = Guid.NewGuid(), ObraId = _obra, AlbanilId = albanil.Id, TareaObraId = t2.Id, Fecha = DateTime.UtcNow };
            var rec = new RecepcionMaterial
            {
                Ref = Guid.NewGuid(), ObraId = _obra, Fecha = DateTime.UtcNow, Pedido = pedido,
                Items = [new RecepcionItem { Material = "Ladrillo", Cantidad = 1000, Unidad = "u" }, new RecepcionItem { Material = "Cemento", Cantidad = 10, Unidad = "bolsas" }],
            };
            db.Recepciones.Add(rec);
            // 8 h a $5.000 (valor guardado en la fichada, aunque después aumente) = $40.000, repartidas 6 h etapa 1 / 2 h etapa 2.
            var entrada = new DateTime(2026, 11, 10, 11, 0, 0, DateTimeKind.Utc);
            db.Jornadas.Add(new JornadaAlbanil { AlbanilId = albanil.Id, ObraId = _obra, Ref = Guid.NewGuid(), Entrada = entrada, Salida = entrada.AddHours(8), ValorHora = 5000 });
            db.Partes.Add(new ParteDiario
            {
                AlbanilId = albanil.Id, ObraId = _obra, Fecha = new DateOnly(2026, 11, 10), HorasTotales = 8,
                Items = [new ParteDiarioItem { TareaObraId = t1.Id, Horas = 6 }, new ParteDiarioItem { TareaObraId = t2.Id, Horas = 2 }],
            });
            // Jornada sin valor hora: cuenta como horas sin valor.
            var otro = new Albanil { Nombre = "Juan", Usuario = "juan", PinHash = "x", CapatazId = _capataz };
            db.Albaniles.Add(otro);
            await db.SaveChangesAsync();
            db.Jornadas.Add(new JornadaAlbanil { AlbanilId = otro.Id, ObraId = _obra, Ref = Guid.NewGuid(), Entrada = entrada, Salida = entrada.AddHours(4) });
            db.Aprobaciones.Add(new AprobacionCliente { ObraId = _obra, Titulo = "Adicional", Monto = 100_000, Estado = EstadoAprobacion.Aprobada });
            db.Pagos.Add(new Pago { ObraId = _obra, Fecha = new(2026, 11, 1), Monto = 300_000 });
            await db.SaveChangesAsync();
            (item1, item2, recepcion) = (rec.Items[0].Id, rec.Items[1].Id, rec.Id);
            await db.Albaniles.Where(a => a.Id == albanil.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ValorHora, 9000m));
        }

        Assert.NotNull(await _costos.GuardarPreciosAsync(recepcion, new Dictionary<int, decimal?> { [item1] = -5 }, null, null));
        Assert.Null(await _costos.GuardarPreciosAsync(recepcion, new Dictionary<int, decimal?> { [item1] = 200 }, "Corralón Sur", "A-0001"));

        Assert.NotNull((await _costos.RegistrarGastoAsync(_obra, new CostosObra.DatosGasto { Descripcion = "Volquete" })).Error); // sin monto
        Assert.NotNull((await _costos.RegistrarGastoAsync(_obra, new CostosObra.DatosGasto { Descripcion = "X", Monto = 1, EtapaObraId = 999 })).Error);
        Assert.Null((await _costos.RegistrarGastoAsync(_obra, new CostosObra.DatosGasto
            { Descripcion = "Volquete", Monto = 50_000, Categoria = CategoriaGasto.Flete, EtapaObraId = _etapa1 }, comprobantes: [Foto("c.pdf")])).Error);
        Assert.Null((await _costos.RegistrarSobranteAsync(_obra, new CostosObra.DatosSobrante
            { Material = "Ladrillo", Cantidad = 100, Unidad = "u", Destino = DestinoSobrante.DevueltoProveedor, ValorRecuperado = 20_000 })).Error);

        var r = await _costos.RentabilidadAsync(_obra);
        Assert.Equal(1_100_000m, r.Facturable);
        Assert.Equal(300_000m, r.Cobrado);
        Assert.Equal(200_000m, r.Materiales);          // 1000 ladrillos × $200; el cemento sin precio no suma
        Assert.Equal(1, r.RenglonesSinPrecio);
        Assert.Equal(40_000m, r.ManoDeObra);           // con el valor de la fichada, no el aumento a $9.000
        Assert.Equal(12m, r.HorasTrabajadas);
        Assert.Equal(4m, r.HorasSinValor);
        Assert.Equal(50_000m, r.Gastos[CategoriaGasto.Flete]);
        Assert.Equal(20_000m, r.Recuperado);
        Assert.Equal(270_000m, r.Costo);
        Assert.Equal(830_000m, r.Margen);
        Assert.True(r.Incompleta);

        var e1 = r.PorEtapa.Single(e => e.EtapaId == _etapa1);
        var e2 = r.PorEtapa.Single(e => e.EtapaId == _etapa2);
        Assert.Equal(30_000m, e1.ManoDeObra);
        Assert.Equal(50_000m, e1.Gastos);
        Assert.Equal(10_000m, e2.ManoDeObra);
        Assert.Equal(200_000m, e2.Materiales);         // el pedido era de la tarea «Muros»

        var precios = await _costos.PreciosAsync();
        Assert.Equal("Corralón Sur", Assert.Single(precios).Proveedor);
    }

    [Fact]
    public async Task El_gasto_del_capataz_no_se_duplica_al_reintentar_y_solo_vale_en_sus_obras()
    {
        var d = new CostosObra.DatosGasto { Ref = Guid.NewGuid(), Descripcion = "Flete arena", Monto = 25_000, Categoria = CategoriaGasto.Flete };
        var (a, e1) = await _costos.GastoCapatazAsync(_capataz, _obra, d, [Foto("comprobante.jpg")]);
        var (b, e2) = await _costos.GastoCapatazAsync(_capataz, _obra, d, [Foto("comprobante.jpg")]);
        Assert.Null(e1); Assert.Null(e2);
        Assert.Equal(a, b);
        // El reintento no vuelve a guardar el comprobante.
        Assert.Single(Assert.Single(await _costos.GastosAsync(_obra)).Adjuntos);
        Assert.NotNull((await _costos.GastoCapatazAsync(_capataz, _otra, new CostosObra.DatosGasto { Ref = Guid.NewGuid(), Descripcion = "x", Monto = 1 })).Error);
        Assert.NotNull((await _costos.GastoCapatazAsync(_capataz, _obra, new CostosObra.DatosGasto { Descripcion = "Sin ref", Monto = 1 })).Error);
    }

    [Fact]
    public async Task Valor_hora_invalido_se_rechaza() =>
        Assert.NotNull(await _costos.GuardarValorHoraAsync(1, -10));

    // ================================================================== 7. Causa de atrasos

    [Fact]
    public async Task La_causa_de_atraso_se_anota_solo_en_la_obra_y_se_sugiere_por_clima_o_falta_de_material()
    {
        var hoy = new DateOnly(2026, 11, 12);
        var t = new TareaObra { Id = 1, ObraId = _obra, Titulo = "Losa", FechaInicioPlan = hoy.AddDays(-10), FechaLimite = hoy.AddDays(-2) };
        var lluvia = Enumerable.Range(1, 3).Select(i => new ClimaDiario { ObraId = _obra, Fecha = hoy.AddDays(-i), LluviaMm = 12 }).ToList();
        Assert.Equal(CausaAtraso.Clima, CostosObra.SugerirCausa(t, lluvia, [], hoy)!.Causa);
        Assert.Equal(CausaAtraso.FaltaMaterial, CostosObra.SugerirCausa(t, lluvia,
            [new ProblemaObra { TareaObraId = 1, Tipo = TipoProblema.FaltaMaterial }], hoy)!.Causa);
        Assert.Null(CostosObra.SugerirCausa(t, [], [], hoy));

        int tareaId;
        await using (var db = _db.CreateDbContext())
        {
            var tarea = new TareaObra { ObraId = _obra, Titulo = "Losa", FechaLimite = hoy.AddDays(-3) };
            db.Tareas.Add(tarea);
            await db.SaveChangesAsync();
            tareaId = tarea.Id;
        }
        Assert.Equal(tareaId, Assert.Single(await _costos.AtrasadasDeObraAsync(_obra)).Id);
        Assert.NotNull(await _costos.InformarAtrasoAsync(tareaId, CausaAtraso.Clima, null, obraId: _otra));
        Assert.Null(await _costos.InformarAtrasoAsync(tareaId, CausaAtraso.CambioCliente, "Cambió las aberturas", obraId: _obra));
        await using var db2 = _db.CreateDbContext();
        var guardada = await db2.Tareas.SingleAsync(x => x.Id == tareaId);
        Assert.Equal(CausaAtraso.CambioCliente, guardada.CausaAtraso);
        Assert.NotNull(guardada.AtrasoInformadoEn);
    }

    // ================================================================== 8. Trámites

    [Fact]
    public async Task Los_tramites_guardan_su_historia_y_cuanto_tardaron()
    {
        Assert.Equal(3, await _tramites.AgregarSugeridosAsync(_obra)); // obra de gas → 3 de Ecogas
        Assert.Equal(0, await _tramites.AgregarSugeridosAsync(_obra)); // no duplica
        var t = (await _tramites.ListaAsync(_obra)).First();

        Assert.NotNull(await _tramites.MoverAsync(t.Id, new() { Fecha = new(2026, 11, 5), Estado = EstadoTramite.Observado })); // falta qué observaron
        Assert.NotNull(await _tramites.MoverAsync(t.Id, new() { Fecha = new(2026, 12, 5), Estado = EstadoTramite.Presentado })); // fecha futura
        Assert.Null(await _tramites.MoverAsync(t.Id, new() { Fecha = new(2026, 10, 1), Estado = EstadoTramite.Presentado, Expediente = "EX-123" }));
        Assert.Null(await _tramites.MoverAsync(t.Id, new() { Fecha = new(2026, 10, 20), Estado = EstadoTramite.Observado, Nota = "Falta ventilación", Vence = new(2026, 11, 15) }));
        Assert.Single(await _tramites.PorVencerAsync());
        Assert.Null(await _tramites.MoverAsync(t.Id, new() { Fecha = new(2026, 10, 31), Estado = EstadoTramite.Aprobado }));

        t = (await _tramites.ListaAsync(_obra)).First(x => x.Id == t.Id);
        Assert.Equal(30, TramitesObra.DiasHastaAprobacion(t));
        Assert.Equal("EX-123", t.Expediente);
        Assert.Null(t.Vence);
        Assert.Equal(4, t.Movimientos.Count); // creado + 3
        Assert.Empty(await _tramites.PorVencerAsync());

        await _tramites.CambiarVisibleAsync(t.Id, false);
        Assert.Equal(2, (await _tramites.ListaAsync(_obra, soloVisibles: true)).Count);
    }

    // ================================================================== 9. Sobrantes

    [Fact]
    public async Task El_desperdicio_se_mide_contra_lo_recibido()
    {
        await using (var db = _db.CreateDbContext())
        {
            db.Recepciones.Add(new RecepcionMaterial { Ref = Guid.NewGuid(), ObraId = _obra, Fecha = DateTime.UtcNow,
                Items = [new RecepcionItem { Material = "Cemento ", Cantidad = 40, Unidad = "bolsas" }] });
            await db.SaveChangesAsync();
        }
        Assert.NotNull((await _costos.RegistrarSobranteAsync(_obra, new() { Material = "cemento", Cantidad = 2, Unidad = "bolsas", Destino = DestinoSobrante.OtraObra })).Error);
        Assert.Null((await _costos.RegistrarSobranteAsync(_obra, new() { Material = "CEMENTO", Cantidad = 4, Unidad = "bolsas", Destino = DestinoSobrante.Desperdicio, ValorRecuperado = 999 })).Error);
        Assert.Null((await _costos.RegistrarSobranteAsync(_obra, new() { Material = "cemento", Cantidad = 3, Unidad = "bolsas", Destino = DestinoSobrante.OtraObra, ObraDestinoId = _otra })).Error);

        var d = Assert.Single(await _costos.DesperdicioAsync());
        Assert.Equal(40m, d.Recibido);
        Assert.Equal(4m, d.Desperdiciado);
        Assert.Equal(3m, d.Devuelto);
        Assert.Equal(10m, d.Porcentaje);
        Assert.Null((await _costos.SobrantesAsync(_obra)).Single(s => s.Destino == DestinoSobrante.Desperdicio).ValorRecuperado);
    }

    // ================================================================== 10. Acta de entrega

    [Fact]
    public async Task El_acta_se_firma_una_vez_y_con_pendientes_solo_con_reservas()
    {
        var (actaId, _) = await _postventa.CrearActaAsync(_obra, new(2026, 11, 10));
        Assert.Null(await _postventa.AgregarObservacionAsync(actaId!.Value, new() { Descripcion = "Sellar zócalo", Ubicacion = "Baño" }, [Foto()]));
        Assert.Null(await _postventa.ActaAsync(_obra, paraCliente: true)); // borrador: el cliente no la ve
        Assert.NotNull(await _postventa.FirmarActaAsync(_obra, "Laura", false, null, "1.2.3.4"));

        Assert.Null(await _postventa.PublicarActaAsync(actaId.Value));
        Assert.NotNull(await _postventa.FirmarActaAsync(_obra, "Laura", false, null, "1.2.3.4")); // pendiente sin reservas
        Assert.NotNull(await _postventa.FirmarActaAsync(_obra, "Laura", true, null, "1.2.3.4"));  // reservas sin comentario

        var obs = (await _postventa.ActaAsync(_obra))!.Observaciones.Single();
        Assert.Null(await _postventa.ResolverObservacionAsync(obs.Id, true, "Sellado", [Foto("despues.jpg")]));
        Assert.Null(await _postventa.FirmarActaAsync(_obra, "Laura Pérez", false, null, "1.2.3.4"));
        Assert.NotNull(await _postventa.FirmarActaAsync(_obra, "Laura Pérez", false, null, "1.2.3.4"));

        var acta = await _postventa.ActaAsync(_obra, paraCliente: true);
        Assert.Equal(EstadoActa.Firmada, acta!.Estado);
        Assert.Equal("Laura Pérez", acta.FirmadaPor);
        Assert.False(acta.ConReservas);
        Assert.Contains(acta.Observaciones.Single().Adjuntos, a => a.EsResolucion);
        Assert.Equal(new DateOnly(2027, 11, 12), Postventa.GarantiaHasta(acta));
        Assert.NotNull(await _postventa.BorrarObservacionAsync(obs.Id)); // firmada: queda como constancia
        // Al firmar se pide la encuesta de entrega.
        Assert.Contains(await _postventa.EncuestasAsync(_obra, soloPendientes: true), e => e.Momento == MomentoEncuesta.Entrega);
    }

    // ================================================================== 11. Reclamos de garantía

    [Fact]
    public async Task El_reclamo_no_se_duplica_y_el_cliente_solo_ve_sus_adjuntos()
    {
        var d = new Postventa.DatosReclamo { Ref = Guid.NewGuid(), Rubro = "Humedad / filtraciones", Descripcion = "Mancha en el dormitorio" };
        Assert.NotNull((await _postventa.CrearReclamoAsync(_obra, new() { Ref = Guid.NewGuid(), Rubro = "Inventado", Descripcion = "x" }, [])).Error);
        var (id, e1) = await _postventa.CrearReclamoAsync(_obra, d, [Foto()]);
        var (id2, e2) = await _postventa.CrearReclamoAsync(_obra, d, [Foto()]);
        Assert.Null(e1); Assert.Null(e2);
        Assert.Equal(id, id2);
        Assert.NotNull((await _postventa.CrearReclamoAsync(_otra, d, [])).Error); // mismo Ref desde otra obra

        var reclamo = Assert.Single(await _postventa.ReclamosAsync(_obra));
        var adjunto = Assert.Single(reclamo.Adjuntos);
        await using (var propio = (await _adjuntos.AbrirAsync(adjunto.Id, _obra))?.Contenido) Assert.NotNull(propio);
        Assert.Null(await _adjuntos.AbrirAsync(adjunto.Id, _otra));

        Assert.NotNull(await _postventa.RevisarReclamoAsync(id!.Value, new() { Estado = EstadoReclamo.Resuelto })); // sin causa
        Assert.NotNull(await _postventa.RevisarReclamoAsync(id.Value, new() { Estado = EstadoReclamo.Programado })); // sin fecha
        Assert.Null(await _postventa.RevisarReclamoAsync(id.Value, new()
            { Estado = EstadoReclamo.Resuelto, Causa = CausaReclamo.ManoDeObra, EtapaObraId = _etapa2, CostoReparacion = 30_000, Respuesta = "Listo" }));
        var revisado = Assert.Single(await _postventa.ReclamosAsync(_obra));
        Assert.NotNull(revisado.ResueltoEn);
        Assert.Equal(0, (await _postventa.NovedadesAsync()).ReclamosAbiertos);
    }

    [Fact]
    public async Task Un_adjunto_de_relevamiento_o_gasto_nunca_lo_ve_el_cliente()
    {
        var (gasto, _) = await _costos.RegistrarGastoAsync(_obra, new() { Descripcion = "Flete", Monto = 10 }, comprobantes: [Foto()]);
        var adj = Assert.Single((await _costos.GastosAsync(_obra)).Single().Adjuntos);
        Assert.Null(await _adjuntos.AbrirAsync(adj.Id, _obra));
        await using (var panel = (await _adjuntos.AbrirAsync(adj.Id))?.Contenido) Assert.NotNull(panel);
        Assert.NotNull(gasto);
    }

    // ================================================================== 12. Encuestas y permiso

    [Fact]
    public async Task Terminar_una_etapa_pide_encuesta_solo_si_el_cliente_tiene_la_app()
    {
        await _estudio.CambiarEstadoEtapaAsync(_etapa1, EstadoEtapa.Completada);
        Assert.Empty(await _postventa.EncuestasAsync(_obra));

        await DarAccesoAsync();
        await _estudio.CambiarEstadoEtapaAsync(_etapa2, EstadoEtapa.Completada);
        await _estudio.CambiarEstadoEtapaAsync(_etapa2, EstadoEtapa.Completada); // no duplica
        var e = Assert.Single(await _postventa.EncuestasAsync(_obra));
        Assert.Contains("Mampostería", e.Titulo);

        Assert.NotNull(await _postventa.ResponderEncuestaAsync(_obra, e.Id, 11, null));
        Assert.NotNull(await _postventa.ResponderEncuestaAsync(_otra, e.Id, 9, null));
        Assert.Null(await _postventa.ResponderEncuestaAsync(_obra, e.Id, 9, "Muy prolijos"));
        Assert.NotNull(await _postventa.ResponderEncuestaAsync(_obra, e.Id, 3, null)); // ya respondida
        Assert.Empty(await _postventa.EncuestasAsync(_obra, soloPendientes: true));
        Assert.Equal(1, (await _postventa.NovedadesAsync()).EncuestasNuevas);
    }

    [Fact]
    public void Nps_es_promotores_menos_detractores()
    {
        Assert.Null(Postventa.Nps([]));
        Assert.Equal(25m, Postventa.Nps([10, 9, 7, 3]));   // 50% − 25%
        Assert.Equal(-100m, Postventa.Nps([0, 6]));
    }

    [Fact]
    public async Task El_permiso_se_puede_cambiar_y_el_nombre_solo_va_con_testimonio()
    {
        Assert.NotNull(await _postventa.GuardarPermisoAsync(_obra, true, true, null, null, null));
        Assert.Null(await _postventa.GuardarPermisoAsync(_obra, true, true, null, "Laura", "1.1.1.1"));
        Assert.False((await _postventa.PermisoAsync(_obra))!.MostrarNombre);
        Assert.Null(await _postventa.GuardarPermisoAsync(_obra, false, true, "Excelente", "Laura", "1.1.1.1"));
        var p = await _postventa.PermisoAsync(_obra);
        Assert.False(p!.UsarFotos);
        Assert.True(p.MostrarNombre);

        var cm = new DeepArquitectura.Web.Agents.CmTools(_db);
        var t = Assert.Single(await cm.ListarTestimonios());
        Assert.Equal("Laura", t.Firma);
        Assert.Equal("no", (await cm.ListarObras()).Single(o => o.ObraId == _obra).PermisoFotos);
    }

    // ================================================================== 13. Pagos informados

    [Fact]
    public async Task El_pago_informado_se_confirma_una_vez_y_paga_el_certificado()
    {
        int cert;
        await using (var db = _db.CreateDbContext())
        {
            var c = new Certificado { ObraId = _obra, Numero = 1, Fecha = new(2026, 11, 1), Monto = 200_000, Estado = EstadoCertificado.Emitido };
            db.Certificados.Add(c);
            await db.SaveChangesAsync();
            cert = c.Id;
        }
        var d = new Postventa.DatosPagoInformado { Ref = Guid.NewGuid(), Fecha = new(2026, 11, 11), Monto = 200_000, Medio = MedioPago.Transferencia, CertificadoId = cert };
        Assert.NotNull((await _postventa.InformarPagoAsync(_obra, d, [])).Error); // transferencia sin comprobante
        Assert.NotNull((await _postventa.InformarPagoAsync(_obra, new() { Ref = Guid.NewGuid(), Fecha = new(2026, 12, 1), Monto = 1, Medio = MedioPago.Efectivo }, [])).Error); // fecha futura
        Assert.NotNull((await _postventa.InformarPagoAsync(_otra, new() { Ref = Guid.NewGuid(), Fecha = new(2026, 11, 11), Monto = 1, Medio = MedioPago.Efectivo, CertificadoId = cert }, [])).Error);
        var (id, error) = await _postventa.InformarPagoAsync(_obra, d, [Foto("comprobante.pdf")]);
        Assert.Null(error);
        Assert.Equal(id, (await _postventa.InformarPagoAsync(_obra, d, [Foto("comprobante.pdf")])).Id); // reintento

        Assert.NotNull(await _postventa.RechazarPagoAsync(id!.Value, null));
        Assert.Null(await _postventa.ConfirmarPagoAsync(id.Value));
        Assert.NotNull(await _postventa.ConfirmarPagoAsync(id.Value));
        Assert.NotNull(await _postventa.RechazarPagoAsync(id.Value, "no está"));

        await using var db2 = _db.CreateDbContext();
        var pago = await db2.Pagos.SingleAsync(p => p.ObraId == _obra);
        Assert.Equal(200_000m, pago.Monto);
        var c2 = await db2.Certificados.SingleAsync(x => x.Id == cert);
        Assert.Equal(EstadoCertificado.Cobrado, c2.Estado);
        Assert.Equal(pago.Id, c2.PagoId);
        Assert.Equal(pago.Id, (await db2.PagosInformados.SingleAsync()).PagoId);
    }

    [Theory]
    [InlineData("150000", 150000.0)]
    [InlineData("150.000", 150000.0)]
    [InlineData("$ 150.000,50", 150000.50)]
    [InlineData("150,000.50", 150000.50)]
    [InlineData("1500.5", 1500.5)]
    [InlineData("abc", null)]
    public void Lee_montos_escritos_de_distintas_formas(string texto, double? esperado) =>
        Assert.Equal(esperado is null ? null : (decimal)esperado.Value, ClienteProcesoEndpoints.Monto(texto));
}
