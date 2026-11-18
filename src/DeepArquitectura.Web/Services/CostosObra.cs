using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Costos reales de la obra: precios de lo recibido, gastos, mano de obra (fichadas × valor hora), sobrantes,
/// causas de atraso y la rentabilidad que sale de todo eso.
/// </summary>
public class CostosObra(IDbContextFactory<AppDbContext> dbFactory, Adjuntos adjuntos, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;
    private DateOnly Hoy => DateOnly.FromDateTime(Textos.AHoraLocal(Ahora));

    // ================================================================== 4. Precios en la recepción

    public async Task<List<RecepcionMaterial>> RecepcionesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Recepciones.AsNoTracking().Include(r => r.Items).Include(r => r.Albanil).Include(r => r.Capataz)
            .Where(r => r.ObraId == obraId).OrderByDescending(r => r.Fecha).ToListAsync(ct);
    }

    /// <summary>Precio unitario por renglón (de la factura). Un precio vacío lo deja sin cargar.</summary>
    public async Task<string?> GuardarPreciosAsync(int recepcionId, IReadOnlyDictionary<int, decimal?> precios, string? proveedor,
        string? factura, CancellationToken ct = default)
    {
        if (precios.Values.Any(p => p is < 0 or > 1_000_000_000)) return "Hay un precio inválido.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Recepciones.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == recepcionId, ct);
        if (r is null) return "La recepción no existe.";
        foreach (var i in r.Items)
            if (precios.TryGetValue(i.Id, out var p)) i.PrecioUnitario = p;
        r.Proveedor = Relevamientos.Texto(proveedor, 150) ?? r.Proveedor;
        r.NumeroFactura = Relevamientos.Texto(factura, 60);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> AgregarFacturaAsync(int recepcionId, IReadOnlyCollection<Adjuntos.Archivo> archivos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Recepciones.Where(r => r.Id == recepcionId).Select(r => (int?)r.ObraId).SingleOrDefaultAsync(ct);
        if (obra is null) return "La recepción no existe.";
        return await adjuntos.AgregarAsync(obra, "facturas", archivos, a => a.RecepcionMaterialId = recepcionId, ct: ct);
    }

    public sealed record PrecioMaterial(string Material, string Unidad, string Proveedor, decimal PrecioPromedio, decimal PrecioMinimo,
        decimal PrecioMaximo, decimal Cantidad, int Compras, DateTime Ultima);

    /// <summary>Comparación de precios por material y proveedor (todas las obras, últimos <paramref name="dias"/> días).</summary>
    public async Task<List<PrecioMaterial>> PreciosAsync(int dias = 365, CancellationToken ct = default)
    {
        var desde = Ahora.AddDays(-dias);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var filas = await db.RecepcionItems.AsNoTracking()
            .Where(i => i.PrecioUnitario != null && i.Recepcion.Fecha >= desde)
            .Select(i => new { i.Material, i.Unidad, Proveedor = i.Recepcion.Proveedor, Precio = i.PrecioUnitario!.Value, i.Cantidad, i.Recepcion.Fecha })
            .ToListAsync(ct);
        return filas
            .GroupBy(f => (Material: NormalizarMaterial(f.Material), Unidad: f.Unidad.Trim().ToLowerInvariant(), Proveedor: f.Proveedor?.Trim() ?? "Sin proveedor"))
            .Select(g =>
            {
                var cant = g.Sum(x => x.Cantidad);
                var prom = cant > 0 ? g.Sum(x => x.Precio * x.Cantidad) / cant : g.Average(x => x.Precio);
                return new PrecioMaterial(g.First().Material.Trim(), g.First().Unidad, g.Key.Proveedor, Math.Round(prom, 2),
                    g.Min(x => x.Precio), g.Max(x => x.Precio), cant, g.Count(), g.Max(x => x.Fecha));
            })
            .OrderBy(p => p.Material, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.PrecioPromedio).ToList();
    }

    /// <summary>Para agrupar «Cemento», «cemento » y «CEMENTO».</summary>
    public static string NormalizarMaterial(string m) => string.Join(' ', m.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // ================================================================== 5. Gastos de obra

    public sealed class DatosGasto
    {
        public DateOnly Fecha { get; set; }
        public CategoriaGasto Categoria { get; set; } = CategoriaGasto.Subcontrato;
        public string? Descripcion { get; set; }
        public string? Proveedor { get; set; }
        public string? NumeroComprobante { get; set; }
        public decimal? Monto { get; set; }
        public int? EtapaObraId { get; set; }
        /// <summary>Id del envío de la app del capataz (no duplica si reintenta).</summary>
        public Guid? Ref { get; set; }
    }

    public async Task<List<GastoObra>> GastosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Gastos.AsNoTracking().Include(g => g.Adjuntos).Include(g => g.Etapa).Include(g => g.Capataz)
            .Where(g => g.ObraId == obraId).OrderByDescending(g => g.Fecha).ThenByDescending(g => g.Id).ToListAsync(ct);
    }

    public async Task<(int? Id, string? Error)> RegistrarGastoAsync(int obraId, DatosGasto d, int? capatazId = null,
        IReadOnlyCollection<Adjuntos.Archivo>? comprobantes = null, CancellationToken ct = default)
    {
        var descripcion = Relevamientos.Texto(d.Descripcion, 300);
        if (descripcion is null) return (null, "Escribí qué se pagó.");
        if (d.Monto is not > 0 || d.Monto > 10_000_000_000) return (null, "Indicá el monto.");
        if (comprobantes is { Count: > 0 } && Adjuntos.Validar(comprobantes) is { } errorArchivo) return (null, errorArchivo);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        if (d.EtapaObraId is int e && !await db.Etapas.AnyAsync(x => x.Id == e && x.ObraId == obraId, ct)) return (null, "La etapa no es de esta obra.");

        var g = new GastoObra
        {
            ObraId = obraId, Fecha = d.Fecha == default ? Hoy : d.Fecha, Categoria = d.Categoria, Descripcion = descripcion,
            Proveedor = Relevamientos.Texto(d.Proveedor, 150), NumeroComprobante = Relevamientos.Texto(d.NumeroComprobante, 60),
            Monto = Math.Round(d.Monto.Value, 2), EtapaObraId = d.EtapaObraId, CapatazId = capatazId, Ref = d.Ref,
        };
        db.Gastos.Add(g);
        if (comprobantes is { Count: > 0 }) await adjuntos.GuardarAsync(db, obraId, "gastos", comprobantes, a => a.Gasto = g, ct: ct);
        await db.SaveChangesAsync(ct);
        return (g.Id, null);
    }

    public async Task<string?> AgregarComprobanteAsync(int gastoId, IReadOnlyCollection<Adjuntos.Archivo> archivos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Gastos.Where(g => g.Id == gastoId).Select(g => (int?)g.ObraId).SingleOrDefaultAsync(ct);
        return obra is null ? "El gasto no existe." : await adjuntos.AgregarAsync(obra, "gastos", archivos, a => a.GastoObraId = gastoId, ct: ct);
    }

    public async Task BorrarGastoAsync(int gastoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var g = await db.Gastos.Include(x => x.Adjuntos).SingleOrDefaultAsync(x => x.Id == gastoId, ct);
        if (g is null) return;
        var rutas = g.Adjuntos.Select(a => a.Ruta).ToList();
        db.Adjuntos.RemoveRange(g.Adjuntos);
        db.Gastos.Remove(g);
        await db.SaveChangesAsync(ct);
        await adjuntos.BorrarArchivosAsync(rutas, ct);
    }

    /// <summary>
    /// Gasto que carga el capataz desde su app, con sus comprobantes. Idempotente por <see cref="DatosGasto.Ref"/>:
    /// si el teléfono reintenta el mismo envío, devuelve el gasto que ya está (sin volver a guardar los archivos).
    /// </summary>
    public async Task<(int? Id, string? Error)> GastoCapatazAsync(int capatazId, int obraId, DatosGasto d,
        IReadOnlyCollection<Adjuntos.Archivo>? comprobantes = null, CancellationToken ct = default)
    {
        if (d.Ref is not Guid referencia || referencia == Guid.Empty) return (null, "Falta el identificador del envío.");
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (!await db.CapatazObras.AnyAsync(x => x.CapatazId == capatazId && x.ObraId == obraId, ct)) return (null, "La obra no es tuya.");
            var previo = await db.Gastos.Where(g => g.Ref == referencia).Select(g => new { g.Id, g.CapatazId, g.ObraId }).SingleOrDefaultAsync(ct);
            if (previo is not null) return previo.CapatazId == capatazId && previo.ObraId == obraId ? (previo.Id, null) : (null, "Envío duplicado.");
        }
        try
        {
            return await RegistrarGastoAsync(obraId, d, capatazId, comprobantes, ct);
        }
        catch (DbUpdateException)
        {
            // Dos reintentos simultáneos con el mismo Ref: gana el primero.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.Gastos.Where(g => g.Ref == referencia && g.CapatazId == capatazId).Select(g => (int?)g.Id).SingleOrDefaultAsync(ct) is int id
                ? (id, null) : throw new InvalidOperationException("No se pudo guardar el gasto.");
        }
    }

    // ================================================================== 6. Valor hora del personal

    public async Task<string?> GuardarValorHoraAsync(int albanilId, decimal? valorHora, CancellationToken ct = default)
    {
        if (valorHora is < 0 or > 10_000_000) return "Valor hora inválido.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.Albaniles.Where(a => a.Id == albanilId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ValorHora, valorHora is > 0 ? Math.Round(valorHora.Value, 2) : null), ct);
        return n == 1 ? null : "No existe.";
    }

    // ================================================================== 7. Causa de los atrasos

    public async Task<string?> InformarAtrasoAsync(int tareaId, CausaAtraso causa, string? detalle, int? obraId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Tareas.SingleOrDefaultAsync(x => x.Id == tareaId && (obraId == null || x.ObraId == obraId), ct);
        if (t is null) return "La tarea no existe en esta obra.";
        t.CausaAtraso = causa;
        t.DetalleAtraso = Relevamientos.Texto(detalle, 500);
        t.AtrasoInformadoEn = Ahora;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<List<TareaObra>> AtrasadasDeObraAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tareas = await db.Tareas.AsNoTracking()
            .Where(t => t.ObraId == obraId && t.FechaLimite != null && (t.Estado == EstadoTarea.Pendiente || t.Estado == EstadoTarea.EnCurso))
            .ToListAsync(ct);
        return tareas.Where(t => CalculoTareas.Atrasada(t, Hoy)).OrderBy(t => t.FechaLimite).ToList();
    }

    public async Task QuitarCausaAtrasoAsync(int tareaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Tareas.Where(t => t.Id == tareaId).ExecuteUpdateAsync(s => s.SetProperty(t => t.CausaAtraso, (CausaAtraso?)null)
            .SetProperty(t => t.DetalleAtraso, (string?)null).SetProperty(t => t.AtrasoInformadoEn, (DateTime?)null), ct);
    }

    public sealed record Sugerencia(CausaAtraso Causa, string Motivo);

    /// <summary>
    /// Causa probable de un atraso con lo que ya sabe el sistema: días de lluvia o viento en el período de la tarea,
    /// o problemas de falta de material avisados sobre la tarea. Null si no hay pistas.
    /// </summary>
    public static Sugerencia? SugerirCausa(TareaObra t, IEnumerable<ClimaDiario> clima, IEnumerable<ProblemaObra> problemas, DateOnly hoy)
    {
        var faltas = problemas.Count(p => p.TareaObraId == t.Id && p.Tipo == TipoProblema.FaltaMaterial);
        if (faltas > 0) return new(CausaAtraso.FaltaMaterial, $"{faltas} aviso(s) de falta de material en la tarea");
        var desde = t.FechaInicioPlan ?? t.FechaLimite?.AddDays(-14) ?? hoy.AddDays(-14);
        var malos = clima.Count(c => c.ObraId == t.ObraId && c.Fecha >= desde && c.Fecha <= hoy && c.Desfavorable);
        if (malos >= 2) return new(CausaAtraso.Clima, $"{malos} días de lluvia o viento fuerte desde el inicio previsto");
        return null;
    }

    public async Task<Dictionary<int, Sugerencia>> SugerenciasAsync(IReadOnlyCollection<TareaObra> tareas, CancellationToken ct = default)
    {
        if (tareas.Count == 0) return [];
        var obras = tareas.Select(t => t.ObraId).Distinct().ToList();
        var ids = tareas.Select(t => t.Id).ToList();
        var desde = Hoy.AddDays(-120);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var clima = await db.Climas.AsNoTracking().Where(c => obras.Contains(c.ObraId) && c.Fecha >= desde).ToListAsync(ct);
        var problemas = await db.Problemas.AsNoTracking().Where(p => p.TareaObraId != null && ids.Contains(p.TareaObraId.Value)).ToListAsync(ct);
        var r = new Dictionary<int, Sugerencia>();
        foreach (var t in tareas)
            if (t.CausaAtraso is null && SugerirCausa(t, clima, problemas, Hoy) is { } s) r[t.Id] = s;
        return r;
    }

    // ================================================================== 9. Sobrantes y devoluciones

    public sealed class DatosSobrante
    {
        public DateOnly Fecha { get; set; }
        public string? Material { get; set; }
        public decimal? Cantidad { get; set; }
        public string? Unidad { get; set; }
        public DestinoSobrante Destino { get; set; } = DestinoSobrante.Deposito;
        public int? ObraDestinoId { get; set; }
        public decimal? ValorRecuperado { get; set; }
        public int? EtapaObraId { get; set; }
        public string? Nota { get; set; }
        /// <summary>Id del envío de la app del capataz (no duplica si reintenta).</summary>
        public Guid? Ref { get; set; }
    }

    public async Task<List<MaterialSobrante>> SobrantesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sobrantes.AsNoTracking().Include(s => s.ObraDestino).Include(s => s.Etapa)
            .Where(s => s.ObraId == obraId).OrderByDescending(s => s.Fecha).ThenByDescending(s => s.Id).ToListAsync(ct);
    }

    public async Task<(int? Id, string? Error)> RegistrarSobranteAsync(int obraId, DatosSobrante d, int? capatazId = null, CancellationToken ct = default)
    {
        var material = Relevamientos.Texto(d.Material, 150);
        var unidad = Relevamientos.Texto(d.Unidad, 20);
        if (material is null) return (null, "¿Qué material sobró?");
        if (d.Cantidad is not > 0 || d.Cantidad > 1_000_000) return (null, "Indicá la cantidad.");
        if (unidad is null) return (null, "Indicá la unidad (bolsas, m³, u…).");
        if (d.ValorRecuperado is < 0) return (null, "El valor recuperado no puede ser negativo.");
        if (d.Destino == DestinoSobrante.OtraObra && (d.ObraDestinoId is null || d.ObraDestinoId == obraId)) return (null, "Elegí a qué obra se llevó.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        if (d.ObraDestinoId is int od && d.Destino == DestinoSobrante.OtraObra && !await db.Obras.AnyAsync(o => o.Id == od, ct))
            return (null, "La obra de destino no existe.");
        if (d.EtapaObraId is int e && !await db.Etapas.AnyAsync(x => x.Id == e && x.ObraId == obraId, ct)) return (null, "La etapa no es de esta obra.");
        var s = new MaterialSobrante
        {
            ObraId = obraId, Fecha = d.Fecha == default ? Hoy : d.Fecha, Material = material, Cantidad = d.Cantidad.Value, Unidad = unidad,
            Destino = d.Destino, ObraDestinoId = d.Destino == DestinoSobrante.OtraObra ? d.ObraDestinoId : null,
            ValorRecuperado = d.Destino == DestinoSobrante.Desperdicio ? null : d.ValorRecuperado, EtapaObraId = d.EtapaObraId,
            Nota = Relevamientos.Texto(d.Nota, 500), CapatazId = capatazId, Ref = d.Ref,
        };
        db.Sobrantes.Add(s);
        await db.SaveChangesAsync(ct);
        return (s.Id, null);
    }

    public async Task BorrarSobranteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Sobrantes.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }

    public sealed record Desperdicio(string Material, string Unidad, decimal Recibido, decimal Desperdiciado, decimal Devuelto, decimal Porcentaje, int Obras);

    /// <summary>Por material: cuánto se recibió, cuánto se tiró y cuánto se devolvió o reutilizó (todas las obras).</summary>
    public async Task<List<Desperdicio>> DesperdicioAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recibido = (await db.RecepcionItems.AsNoTracking().Select(i => new { i.Material, i.Unidad, i.Cantidad, i.Recepcion.ObraId }).ToListAsync(ct))
            .GroupBy(i => (NormalizarMaterial(i.Material), i.Unidad.Trim().ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => (Cantidad: g.Sum(x => x.Cantidad), Obras: g.Select(x => x.ObraId).Distinct().Count(), Nombre: g.First().Material.Trim()));
        var sobrantes = (await db.Sobrantes.AsNoTracking().ToListAsync(ct))
            .GroupBy(s => (NormalizarMaterial(s.Material), s.Unidad.Trim().ToLowerInvariant()));
        var lista = new List<Desperdicio>();
        foreach (var g in sobrantes)
        {
            var rec = recibido.GetValueOrDefault(g.Key);
            var tirado = g.Where(s => s.Destino == DestinoSobrante.Desperdicio).Sum(s => s.Cantidad);
            var devuelto = g.Where(s => s.Destino != DestinoSobrante.Desperdicio).Sum(s => s.Cantidad);
            lista.Add(new(rec.Nombre ?? g.First().Material, g.Key.Item2, rec.Cantidad, tirado, devuelto,
                rec.Cantidad > 0 ? Math.Round(tirado * 100 / rec.Cantidad, 1) : 0, Math.Max(rec.Obras, g.Select(s => s.ObraId).Distinct().Count())));
        }
        return lista.OrderByDescending(d => d.Porcentaje).ToList();
    }

    // ================================================================== Rentabilidad

    public sealed record CostoEtapa(int? EtapaId, string Etapa, decimal Materiales, decimal ManoDeObra, decimal Gastos)
    {
        public decimal Total => Materiales + ManoDeObra + Gastos;
    }

    public sealed record Rentabilidad(
        decimal MontoObra, decimal Adicionales, decimal Cobrado,
        decimal Materiales, int RenglonesSinPrecio, decimal ManoDeObra, decimal HorasTrabajadas, decimal HorasSinValor,
        Dictionary<CategoriaGasto, decimal> Gastos, decimal Recuperado, List<CostoEtapa> PorEtapa)
    {
        public decimal TotalGastos => Gastos.Values.Sum();
        public decimal Facturable => MontoObra + Adicionales;
        public decimal Costo => Materiales + ManoDeObra + TotalGastos - Recuperado;
        public decimal Margen => Facturable - Costo;
        public decimal? MargenPorcentaje => Facturable > 0 ? Math.Round(Margen * 100 / Facturable, 1) : null;
        /// <summary>Faltan datos para que el número sea confiable.</summary>
        public bool Incompleta => RenglonesSinPrecio > 0 || HorasSinValor > 0;
    }

    public async Task<Rentabilidad> RentabilidadAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Obras.AsNoTracking().SingleAsync(o => o.Id == obraId, ct);
        var etapas = await db.Etapas.AsNoTracking().Where(e => e.ObraId == obraId).OrderBy(e => e.Orden).ToListAsync(ct);
        // Sumas en memoria: son pocas filas por obra y SQLite (tests) no suma decimales en la base.
        var adicionales = (await db.Aprobaciones.Where(a => a.ObraId == obraId && a.Estado == EstadoAprobacion.Aprobada && a.Monto != null)
            .Select(a => a.Monto!.Value).ToListAsync(ct)).Sum();
        var cobrado = (await db.Pagos.Where(p => p.ObraId == obraId).Select(p => p.Monto).ToListAsync(ct)).Sum();

        var etapaTarea = await db.Tareas.AsNoTracking().Where(t => t.ObraId == obraId).ToDictionaryAsync(t => t.Id, t => t.EtapaObraId, ct);
        var porEtapa = new Dictionary<int, (decimal Mat, decimal Mo, decimal Gas)>();
        void Sumar(int? etapa, decimal mat = 0, decimal mo = 0, decimal gas = 0)
        {
            var k = etapa ?? 0;
            var v = porEtapa.GetValueOrDefault(k);
            porEtapa[k] = (v.Mat + mat, v.Mo + mo, v.Gas + gas);
        }

        // Materiales: renglones recibidos con precio. La etapa sale del pedido → tarea.
        var items = await db.RecepcionItems.AsNoTracking().Where(i => i.Recepcion.ObraId == obraId)
            .Select(i => new { i.Cantidad, i.PrecioUnitario, TareaId = i.Recepcion.Pedido != null ? i.Recepcion.Pedido.TareaObraId : null })
            .ToListAsync(ct);
        decimal materiales = 0;
        foreach (var i in items.Where(i => i.PrecioUnitario is not null))
        {
            var monto = Math.Round(i.Cantidad * i.PrecioUnitario!.Value, 2);
            materiales += monto;
            Sumar(i.TareaId is int t ? etapaTarea.GetValueOrDefault(t) : null, mat: monto);
        }

        // Gastos
        var gastos = await db.Gastos.AsNoTracking().Where(g => g.ObraId == obraId).ToListAsync(ct);
        foreach (var g in gastos) Sumar(g.EtapaObraId, gas: g.Monto);

        // Mano de obra: horas fichadas × valor hora, repartidas entre etapas según el parte diario del mismo día.
        var (manoObra, horas, horasSinValor) = await ManoDeObraAsync(db, obraId, etapaTarea, (e, m) => Sumar(e, mo: m), ct);

        var recuperado = (await db.Sobrantes.Where(s => s.ObraId == obraId && s.ValorRecuperado != null)
            .Select(s => s.ValorRecuperado!.Value).ToListAsync(ct)).Sum();

        var lista = etapas.Where(e => porEtapa.ContainsKey(e.Id))
            .Select(e => { var v = porEtapa[e.Id]; return new CostoEtapa(e.Id, e.Nombre, v.Mat, v.Mo, v.Gas); }).ToList();
        if (porEtapa.TryGetValue(0, out var sin) && sin.Mat + sin.Mo + sin.Gas > 0) lista.Add(new CostoEtapa(null, "Sin etapa", sin.Mat, sin.Mo, sin.Gas));

        return new Rentabilidad(obra.MontoPresupuesto ?? 0, adicionales, cobrado, materiales, items.Count(i => i.PrecioUnitario is null),
            manoObra, horas, horasSinValor, gastos.GroupBy(g => g.Categoria).ToDictionary(g => g.Key, g => g.Sum(x => x.Monto)), recuperado, lista);
    }

    private static async Task<(decimal Costo, decimal Horas, decimal HorasSinValor)> ManoDeObraAsync(AppDbContext db, int obraId,
        Dictionary<int, int?> etapaTarea, Action<int?, decimal> sumar, CancellationToken ct)
    {
        var jornadas = await db.Jornadas.AsNoTracking().Where(j => j.ObraId == obraId && j.Salida != null)
            .Select(j => new { j.AlbanilId, j.Entrada, j.Salida, j.ValorHora, Actual = j.Albanil.ValorHora }).ToListAsync(ct);
        var partes = await db.Partes.AsNoTracking().Where(p => p.ObraId == obraId).Include(p => p.Items).ToListAsync(ct);
        var parteDe = partes.GroupBy(p => (p.AlbanilId, p.Fecha)).ToDictionary(g => g.Key, g => g.First());

        decimal costo = 0, horas = 0, sinValor = 0;
        foreach (var j in jornadas)
        {
            var h = (decimal)Math.Round((j.Salida!.Value - j.Entrada).TotalHours, 2);
            if (h <= 0) continue;
            horas += h;
            if ((j.ValorHora ?? j.Actual) is not decimal valor) { sinValor += h; continue; }
            var monto = Math.Round(h * valor, 2);
            costo += monto;

            var dia = DateOnly.FromDateTime(Textos.AHoraLocal(j.Entrada));
            var items = parteDe.GetValueOrDefault((j.AlbanilId, dia))?.Items.Where(i => i.Horas > 0).ToList();
            var totalParte = items?.Sum(i => i.Horas) ?? 0;
            if (items is null || totalParte <= 0) { sumar(null, monto); continue; }
            foreach (var porEtapa in items.GroupBy(i => i.TareaObraId is int t ? etapaTarea.GetValueOrDefault(t) : null))
                sumar(porEtapa.Key, Math.Round(monto * porEtapa.Sum(i => i.Horas) / totalParte, 2));
        }
        return (costo, horas, sinValor);
    }
}
