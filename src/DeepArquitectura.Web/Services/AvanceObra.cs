using System.Text.Json;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Números de la obra a partir de lo que entra desde el campo: rendimientos (cantidad aprobada / horas del parte),
/// curva de avance planificado vs. real, tareas atrasadas y certificados de avance para cobrar.
/// Todo usa lo <b>aprobado</b> por el capataz o el estudio, no lo que informa el albañil sin validar.
/// </summary>
public class AvanceObra(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateOnly Hoy => DateOnly.FromDateTime(Textos.AHoraLocal(_reloj.GetUtcNow().UtcDateTime));

    // ================================================================== Rendimientos

    public sealed record Rendimiento(string Rubro, string Unidad, int Obras, int Tareas, decimal Cantidad, decimal Horas)
    {
        public decimal? PorHora => Horas > 0 ? Math.Round(Cantidad / Horas, 2) : null;
        /// <summary>Por jornada de 8 horas (lo que se usa para presupuestar).</summary>
        public decimal? PorJornada => Horas > 0 ? Math.Round(Cantidad / Horas * 8, 1) : null;
    }

    public sealed record RendimientoPersona(int AlbanilId, string Albanil, string Rubro, string Unidad, decimal Cantidad, decimal Horas)
    {
        public decimal? PorHora => Horas > 0 ? Math.Round(Cantidad / Horas, 2) : null;
        public decimal? PorJornada => Horas > 0 ? Math.Round(Cantidad / Horas * 8, 1) : null;
    }

    public sealed record ResultadoRendimientos(List<Rendimiento> PorRubro, List<RendimientoPersona> PorPersona, int TareasSinHoras);

    /// <summary>
    /// Rendimientos de tareas con unidad: cantidad aprobada sobre horas cargadas en los partes. Se agrupan por rubro
    /// (o por el título si la tarea no tiene rubro) y unidad, para comparar entre obras.
    /// </summary>
    public async Task<ResultadoRendimientos> RendimientosAsync(int? obraId = null, DateOnly? desde = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tareas = await db.Tareas.AsNoTracking()
            .Where(t => t.Unidad != null && t.Estado != EstadoTarea.Cancelada && (obraId == null || t.ObraId == obraId))
            .Select(t => new { t.Id, t.ObraId, t.Titulo, t.Rubro, t.Unidad })
            .ToListAsync(ct);
        var ids = tareas.Select(t => t.Id).ToList();

        var horas = await db.ParteItems.AsNoTracking()
            .Where(i => i.TareaObraId != null && ids.Contains(i.TareaObraId.Value) && (desde == null || i.Parte.Fecha >= desde))
            .Select(i => new { Tarea = i.TareaObraId!.Value, i.Parte.AlbanilId, Nombre = i.Parte.Albanil.Nombre, i.Horas })
            .ToListAsync(ct);
        var desdeUtc = desde is null ? (DateTime?)null : desde.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(3);
        var cantidades = await db.Registros.AsNoTracking()
            .Where(r => r.TareaObraId != null && ids.Contains(r.TareaObraId.Value) && r.Cantidad != null
                && (r.Validacion == null || r.Validacion == EstadoValidacion.Aprobado || r.Validacion == EstadoValidacion.Corregido)
                && (desdeUtc == null || r.Fecha >= desdeUtc))
            .Select(r => new { Tarea = r.TareaObraId!.Value, r.AlbanilId, r.Cantidad })
            .ToListAsync(ct);

        string Rubro(int tareaId) { var t = tareas.First(x => x.Id == tareaId); return string.IsNullOrWhiteSpace(t.Rubro) ? t.Titulo.Trim() : t.Rubro.Trim(); }
        string Unidad(int tareaId) => tareas.First(x => x.Id == tareaId).Unidad!;

        var conHoras = horas.Select(h => h.Tarea).ToHashSet();
        var porRubro = tareas.Where(t => conHoras.Contains(t.Id))
            .GroupBy(t => (Rubro: Rubro(t.Id), Unidad: t.Unidad!), t => t)
            .Select(g => new Rendimiento(g.Key.Rubro, g.Key.Unidad, g.Select(t => t.ObraId).Distinct().Count(), g.Count(),
                cantidades.Where(c => g.Any(t => t.Id == c.Tarea)).Sum(c => c.Cantidad ?? 0),
                horas.Where(h => g.Any(t => t.Id == h.Tarea)).Sum(h => h.Horas)))
            .Where(r => r.Cantidad > 0)
            .OrderBy(r => r.Rubro)
            .ToList();

        var porPersona = horas
            .GroupBy(h => (h.AlbanilId, h.Nombre, Rubro: Rubro(h.Tarea), Unidad: Unidad(h.Tarea)))
            .Select(g =>
            {
                var tareasGrupo = g.Select(h => h.Tarea).ToHashSet();
                var cant = cantidades.Where(c => c.AlbanilId == g.Key.AlbanilId && tareasGrupo.Contains(c.Tarea)).Sum(c => c.Cantidad ?? 0);
                return new RendimientoPersona(g.Key.AlbanilId, g.Key.Nombre, g.Key.Rubro, g.Key.Unidad, cant, g.Sum(h => h.Horas));
            })
            .Where(r => r.Cantidad > 0)
            .OrderBy(r => r.Rubro).ThenByDescending(r => r.PorHora)
            .ToList();

        var sinHoras = tareas.Count(t => !conHoras.Contains(t.Id) && cantidades.Any(c => c.Tarea == t.Id));
        return new ResultadoRendimientos(porRubro, porPersona, sinHoras);
    }

    /// <summary>Rendimiento de una tarea (cantidad aprobada por hora), o null si no hay horas cargadas.</summary>
    public static decimal? RendimientoTarea(TareaObra t, decimal horas) =>
        t.Unidad is null || horas <= 0 || t.CantidadAprobada <= 0 ? null : Math.Round(t.CantidadAprobada / horas, 2);

    // ================================================================== Curva plan vs real

    public sealed record PuntoCurva(DateOnly Fecha, decimal Plan, decimal? Real);

    public sealed record Curva(List<PuntoCurva> Puntos, decimal PlanHoy, decimal RealHoy, int TareasConPlan, int TareasSinPlan, List<TareaObra> Atrasadas);

    /// <summary>
    /// Avance planificado (cada tarea avanza parejo entre su inicio y fin previstos) contra el real aprobado, semana a
    /// semana, ponderado por el peso de cada tarea. Solo entran las tareas con fecha de fin prevista.
    /// </summary>
    public async Task<Curva?> CurvaAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Obras.AsNoTracking().Where(o => o.Id == obraId).Select(o => new { o.FechaInicio, o.CreadoEn }).SingleOrDefaultAsync(ct);
        if (obra is null) return null;
        var todas = await db.Tareas.AsNoTracking().Include(t => t.Etapa).Include(t => t.Albanil)
            .Where(t => t.ObraId == obraId && t.Estado != EstadoTarea.Cancelada).ToListAsync(ct);
        var hoy = Hoy;
        var atrasadas = todas.Where(t => CalculoTareas.Atrasada(t, hoy)).OrderBy(t => t.FechaLimite).ToList();
        var conPlan = todas.Where(t => t.FechaLimite is not null).ToList();
        if (conPlan.Count == 0) return new Curva([], 0, 0, 0, todas.Count, atrasadas);

        var inicioObra = obra.FechaInicio ?? DateOnly.FromDateTime(Textos.AHoraLocal(obra.CreadoEn));
        DateOnly Inicio(TareaObra t) => t.FechaInicioPlan ?? (t.FechaLimite < inicioObra ? t.FechaLimite!.Value : inicioObra);
        var pesos = CalculoTareas.Pesos(conPlan);

        var ids = conPlan.Select(t => t.Id).ToList();
        var reportes = await db.Registros.AsNoTracking()
            .Where(r => r.TareaObraId != null && ids.Contains(r.TareaObraId.Value)
                && (r.Validacion == null || r.Validacion == EstadoValidacion.Aprobado || r.Validacion == EstadoValidacion.Corregido)
                && (r.Cantidad != null || r.PorcentajeTarea != null))
            .Select(r => new { Tarea = r.TareaObraId!.Value, r.Fecha, r.Id, r.Cantidad, r.PorcentajeTarea })
            .ToListAsync(ct);

        decimal Plan(TareaObra t, DateOnly d)
        {
            var (ini, fin) = (Inicio(t), t.FechaLimite!.Value);
            if (d >= fin) return 1;
            if (d < ini) return 0;
            var total = fin.DayNumber - ini.DayNumber;
            return total <= 0 ? 1 : (decimal)(d.DayNumber - ini.DayNumber) / total;
        }

        decimal Real(TareaObra t, DateOnly d)
        {
            var hasta = d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(3); // fin del día en Argentina
            var rs = reportes.Where(r => r.Tarea == t.Id && r.Fecha < hasta).ToList();
            if (rs.Any(r => r.PorcentajeTarea == 100)) return 1;
            if (rs.Count == 0)
                return t.Estado == EstadoTarea.Completada && t.CompletadaEn is { } c && c < hasta && !reportes.Any(r => r.Tarea == t.Id) ? 1 : 0;
            if (t.Unidad is not null)
                return t.CantidadObjetivo is > 0 ? Math.Min(1, rs.Sum(r => r.Cantidad ?? 0) / t.CantidadObjetivo.Value) : 0;
            var ultimo = rs.Where(r => r.PorcentajeTarea != null).OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id).FirstOrDefault();
            return (ultimo?.PorcentajeTarea ?? 0) / 100m;
        }

        decimal Suma(Func<TareaObra, decimal> f) => Math.Round(conPlan.Sum(t => pesos[t.Id] * f(t)) * 100, 1);

        var desde = conPlan.Min(Inicio);
        var hastaCurva = new[] { conPlan.Max(t => t.FechaLimite!.Value), hoy }.Max();
        var puntos = new List<PuntoCurva>();
        for (var d = desde; ; d = d.AddDays(7))
        {
            if (d > hastaCurva) d = hastaCurva;
            puntos.Add(new PuntoCurva(d, Suma(t => Plan(t, d)), d <= hoy ? Suma(t => Real(t, d)) : null));
            if (d == hastaCurva) break;
        }
        // Que siempre haya un punto exactamente en «hoy».
        if (hoy >= desde && hoy <= hastaCurva && puntos.All(p => p.Fecha != hoy))
        {
            puntos.Add(new PuntoCurva(hoy, Suma(t => Plan(t, hoy)), Suma(t => Real(t, hoy))));
            puntos = puntos.OrderBy(p => p.Fecha).ToList();
        }
        return new Curva(puntos, Suma(t => Plan(t, hoy)), Suma(t => Real(t, hoy)), conPlan.Count, todas.Count - conPlan.Count, atrasadas);
    }

    /// <summary>Tareas atrasadas de todas las obras activas (para el inicio del panel).</summary>
    public async Task<List<TareaObra>> AtrasadasAsync(CancellationToken ct = default)
    {
        var hoy = Hoy;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tareas.AsNoTracking().Include(t => t.Obra).Include(t => t.Albanil)
            .Where(t => (t.Estado == EstadoTarea.Pendiente || t.Estado == EstadoTarea.EnCurso) && t.FechaLimite != null && t.FechaLimite < hoy)
            .OrderBy(t => t.FechaLimite).Take(50).ToListAsync(ct);
    }

    // ================================================================== Certificados

    public sealed record RenglonCertificado(int TareaId, string Tarea, string? Etapa, decimal Peso, decimal AvanceAnterior, decimal AvanceActual)
    {
        /// <summary>Incidencia en la obra de lo hecho en este período (en %).</summary>
        public decimal IncidenciaPeriodo => Math.Round(Peso * (AvanceActual - AvanceAnterior), 3);
    }

    public sealed record VistaCertificado(int Numero, decimal MontoObra, decimal PorcentajeAnterior, decimal PorcentajeAcumulado, decimal Monto, List<RenglonCertificado> Renglones)
    {
        public decimal PorcentajePeriodo => PorcentajeAcumulado - PorcentajeAnterior;
    }

    /// <summary>
    /// Próximo certificado: % físico acumulado (Σ peso × avance aprobado) menos lo ya certificado, por el monto de la
    /// obra. Null + motivo si no se puede (sin monto o sin tareas).
    /// </summary>
    public async Task<(VistaCertificado? Vista, string? Error)> CalcularCertificadoAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var monto = await db.Obras.Where(o => o.Id == obraId).Select(o => o.MontoPresupuesto).SingleOrDefaultAsync(ct);
        if (monto is not > 0) return (null, "Cargá el monto de la obra (Presupuesto en «Datos de la obra») para certificar.");
        var tareas = await db.Tareas.AsNoTracking().Include(t => t.Etapa).Where(t => t.ObraId == obraId && t.Estado != EstadoTarea.Cancelada).ToListAsync(ct);
        if (tareas.Count == 0) return (null, "La obra no tiene tareas: el avance físico se mide por tareas.");

        var anterior = await db.Certificados.AsNoTracking().Where(c => c.ObraId == obraId).OrderByDescending(c => c.Numero).FirstOrDefaultAsync(ct);
        var previos = anterior is null ? [] : (JsonSerializer.Deserialize<List<RenglonCertificado>>(anterior.Detalle) ?? []).ToDictionary(r => r.TareaId, r => r.AvanceActual);
        var pesos = CalculoTareas.Pesos(tareas);
        var renglones = tareas.OrderBy(t => t.Etapa?.Orden ?? int.MaxValue).ThenBy(t => t.Id)
            .Select(t => new RenglonCertificado(t.Id, t.Titulo, t.Etapa?.Nombre, Math.Round(pesos[t.Id] * 100, 3),
                previos.GetValueOrDefault(t.Id), CalculoTareas.Fraccion(t)))
            .ToList();
        var acumulado = Math.Round(renglones.Sum(r => r.Peso * r.AvanceActual), 3);
        var porcAnterior = anterior?.PorcentajeAcumulado ?? 0;
        var importe = Math.Round((acumulado - porcAnterior) / 100 * monto.Value, 2);
        return (new VistaCertificado((anterior?.Numero ?? 0) + 1, monto.Value, porcAnterior, acumulado, importe, renglones), null);
    }

    public async Task<(int? Id, string? Error)> EmitirCertificadoAsync(int obraId, string? notas, CancellationToken ct = default)
    {
        var (vista, error) = await CalcularCertificadoAsync(obraId, ct);
        if (vista is null) return (null, error);
        if (vista.PorcentajePeriodo <= 0) return (null, "No hay avance aprobado nuevo desde el último certificado.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = new Certificado
        {
            ObraId = obraId, Numero = vista.Numero, Fecha = Hoy, PorcentajeAnterior = vista.PorcentajeAnterior,
            PorcentajeAcumulado = vista.PorcentajeAcumulado, MontoObra = vista.MontoObra, Monto = vista.Monto,
            Estado = EstadoCertificado.Emitido, Detalle = JsonSerializer.Serialize(vista.Renglones),
            Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim()[..Math.Min(notas.Trim().Length, 2000)],
        };
        db.Certificados.Add(c);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return (null, "Se emitió otro certificado al mismo tiempo. Volvé a intentar."); }
        return (c.Id, null);
    }

    /// <summary>Registra el cobro: crea el pago en la obra y deja el certificado cobrado.</summary>
    public async Task<string?> MarcarCobradoAsync(int certificadoId, MedioPago medio, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Certificados.SingleOrDefaultAsync(x => x.Id == certificadoId, ct);
        if (c is null) return "Certificado inexistente.";
        if (c.Estado == EstadoCertificado.Cobrado) return null;
        var pago = new Pago { ObraId = c.ObraId, Fecha = Hoy, Monto = c.Monto, Medio = medio, Concepto = $"Certificado N° {c.Numero}" };
        db.Pagos.Add(pago);
        c.Pago = pago;
        c.Estado = EstadoCertificado.Cobrado;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Solo se anula el último certificado y si no está cobrado.</summary>
    public async Task<string?> AnularCertificadoAsync(int certificadoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Certificados.SingleOrDefaultAsync(x => x.Id == certificadoId, ct);
        if (c is null) return "Certificado inexistente.";
        if (c.Estado == EstadoCertificado.Cobrado) return "Está cobrado: primero quitá el cobro de la obra.";
        if (await db.Certificados.AnyAsync(x => x.ObraId == c.ObraId && x.Numero > c.Numero, ct)) return "Solo se puede anular el último certificado.";
        db.Certificados.Remove(c);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public static List<RenglonCertificado> Detalle(Certificado c) => JsonSerializer.Deserialize<List<RenglonCertificado>>(c.Detalle) ?? [];
}
