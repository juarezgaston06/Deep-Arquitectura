using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Lo que se aprende de los datos que se cargan en todo el proceso (página «Indicadores» del panel).</summary>
public class Indicadores(IDbContextFactory<AppDbContext> dbFactory, CostosObra costos)
{
    // ---------------- Comercial ----------------

    public sealed record FilaCanal(OrigenCliente Canal, int Consultas, int Clientes, int ObrasGanadas, double? HorasRespuestaMediana);

    /// <summary>Por canal: consultas, cuántas pasaron a cliente y cuántas terminaron en obra (desde <paramref name="desde"/>).</summary>
    public async Task<List<FilaCanal>> CanalesAsync(DateTime desde, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consultas = await db.Consultas.AsNoTracking().Where(c => c.CreadoEn >= desde)
            .Select(c => new { c.Canal, c.ClienteId, c.CreadoEn, c.ContactadaEn }).ToListAsync(ct);
        var clientes = consultas.Where(c => c.ClienteId != null).Select(c => c.ClienteId!.Value).Distinct().ToList();
        var conObra = (await db.Obras.Where(o => clientes.Contains(o.ClienteId) && o.Estado != EstadoObra.Cancelada)
            .Select(o => o.ClienteId).Distinct().ToListAsync(ct)).ToHashSet();
        return consultas.GroupBy(c => c.Canal)
            .Select(g => new FilaCanal(g.Key, g.Count(), g.Where(c => c.ClienteId != null).Select(c => c.ClienteId).Distinct().Count(),
                g.Where(c => c.ClienteId is int id && conObra.Contains(id)).Select(c => c.ClienteId).Distinct().Count(),
                Mediana(g.Where(c => c.ContactadaEn != null).Select(c => (c.ContactadaEn!.Value - c.CreadoEn).TotalHours))))
            .OrderByDescending(f => f.Consultas).ToList();
    }

    public sealed record FilaPublicacion(int Id, string Titulo, RedSocial Red, int Consultas, int Clientes);

    public async Task<List<FilaPublicacion>> PublicacionesQueTraenAsync(DateTime desde, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Consultas.AsNoTracking().Where(c => c.PublicacionId != null && c.CreadoEn >= desde)
                .Select(c => new { c.PublicacionId, c.Publicacion!.Titulo, c.Publicacion.Red, c.ClienteId }).ToListAsync(ct))
            .GroupBy(c => c.PublicacionId!.Value)
            .Select(g => new FilaPublicacion(g.Key, g.First().Titulo, g.First().Red, g.Count(), g.Count(c => c.ClienteId != null)))
            .OrderByDescending(f => f.Consultas).Take(15).ToList();
    }

    public sealed record Embudo(int Enviados, int Aceptados, int Rechazados, int Abiertos, Dictionary<MotivoPerdida, int> Motivos,
        double? DiasDecisionMediana, Dictionary<MotivoPerdida, int> DescartesConsulta)
    {
        public decimal? TasaCierre => Aceptados + Rechazados > 0 ? Math.Round(Aceptados * 100m / (Aceptados + Rechazados), 0) : null;
    }

    public async Task<Embudo> PresupuestosAsync(DateOnly desde, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Presupuestos.AsNoTracking().Where(x => x.Fecha >= desde && x.Estado != EstadoPresupuesto.Borrador)
            .Select(x => new { x.Estado, x.MotivoPerdida, x.CreadoEn, x.DecididoEn }).ToListAsync(ct);
        var desdeUtc = desde.ToDateTime(TimeOnly.MinValue);
        var descartes = await db.Consultas.AsNoTracking().Where(c => c.CreadoEn >= desdeUtc && c.MotivoDescarte != null)
            .GroupBy(c => c.MotivoDescarte!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return new Embudo(p.Count, p.Count(x => x.Estado == EstadoPresupuesto.Aceptado), p.Count(x => x.Estado == EstadoPresupuesto.Rechazado),
            p.Count(x => x.Estado == EstadoPresupuesto.Enviado),
            p.Where(x => x.MotivoPerdida != null).GroupBy(x => x.MotivoPerdida!.Value).ToDictionary(g => g.Key, g => g.Count()),
            Mediana(p.Where(x => x.DecididoEn != null).Select(x => (x.DecididoEn!.Value - x.CreadoEn).TotalDays)), descartes);
    }

    // ---------------- Obras ----------------

    public sealed record FilaObra(int Id, string Codigo, string Nombre, EstadoObra Estado, CostosObra.Rentabilidad R);

    public async Task<List<FilaObra>> RentabilidadObrasAsync(CancellationToken ct = default)
    {
        List<(int Id, string Codigo, string Nombre, EstadoObra Estado)> obras;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            obras = (await db.Obras.AsNoTracking().Where(o => o.Estado != EstadoObra.Cancelada && o.Estado != EstadoObra.Presupuestada)
                    .OrderByDescending(o => o.CreadoEn).Take(40).Select(o => new { o.Id, o.Codigo, o.Nombre, o.Estado }).ToListAsync(ct))
                .Select(o => (o.Id, o.Codigo, o.Nombre, o.Estado)).ToList();
        var filas = new List<FilaObra>();
        foreach (var o in obras) filas.Add(new FilaObra(o.Id, o.Codigo, o.Nombre, o.Estado, await costos.RentabilidadAsync(o.Id, ct)));
        return filas;
    }

    public sealed record FilaCausa(CausaAtraso Causa, int Tareas, int DiasAtraso, int Obras);

    /// <summary>Qué atrasa las obras: tareas con causa anotada y días de atraso (fin real o hoy − fin previsto).</summary>
    public async Task<List<FilaCausa>> CausasAtrasoAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hoy = Textos.HoyLocal();
        var t = await db.Tareas.AsNoTracking().Where(x => x.CausaAtraso != null)
            .Select(x => new { x.CausaAtraso, x.ObraId, x.FechaLimite, x.CompletadaEn }).ToListAsync(ct);
        return t.GroupBy(x => x.CausaAtraso!.Value)
            .Select(g => new FilaCausa(g.Key, g.Count(), g.Sum(x =>
            {
                if (x.FechaLimite is not { } fin) return 0;
                var real = x.CompletadaEn is DateTime c ? DateOnly.FromDateTime(Textos.AHoraLocal(c)) : hoy;
                return Math.Max(0, real.DayNumber - fin.DayNumber);
            }), g.Select(x => x.ObraId).Distinct().Count()))
            .OrderByDescending(f => f.DiasAtraso).ThenByDescending(f => f.Tareas).ToList();
    }

    public sealed record FilaTramite(string Organismo, string Tramite, int Cantidad, int Aprobados, double? DiasMediana, int Observaciones);

    public async Task<List<FilaTramite>> TramitesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Tramites.AsNoTracking().Include(x => x.Movimientos).ToListAsync(ct);
        return t.GroupBy(x => (Org: x.Organismo ?? "Sin organismo", x.Nombre))
            .Select(g => new FilaTramite(g.Key.Org, g.Key.Nombre, g.Count(), g.Count(x => x.Estado == EstadoTramite.Aprobado),
                Mediana(g.Select(TramitesObra.DiasHastaAprobacion).Where(d => d is not null).Select(d => (double)d!.Value)),
                g.Sum(x => x.Movimientos.Count(m => m.Estado == EstadoTramite.Observado))))
            .OrderBy(f => f.Organismo).ThenBy(f => f.Tramite).ToList();
    }

    public Task<List<CostosObra.Desperdicio>> DesperdicioAsync(CancellationToken ct = default) => costos.DesperdicioAsync(ct);
    public Task<List<CostosObra.PrecioMaterial>> PreciosAsync(CancellationToken ct = default) => costos.PreciosAsync(365, ct);

    // ---------------- Postventa ----------------

    public sealed record FilaReclamo(string Rubro, CausaReclamo? Causa, int Cantidad, decimal Costo, double? DiasResolucionMediana);

    public async Task<List<FilaReclamo>> ReclamosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Reclamos.AsNoTracking().Select(x => new { x.Rubro, x.Causa, x.CostoReparacion, x.Fecha, x.ResueltoEn }).ToListAsync(ct);
        return r.GroupBy(x => (x.Rubro, x.Causa))
            .Select(g => new FilaReclamo(g.Key.Rubro, g.Key.Causa, g.Count(), g.Sum(x => x.CostoReparacion ?? 0),
                Mediana(g.Where(x => x.ResueltoEn != null).Select(x => (x.ResueltoEn!.Value - x.Fecha).TotalDays))))
            .OrderByDescending(f => f.Cantidad).ToList();
    }

    public sealed record Satisfaccion(decimal? Nps, int Respuestas, int Pendientes, decimal? PromedioEntrega, List<(string Etapa, decimal Promedio, int N)> PorEtapa,
        List<(string Obra, int Puntaje, string Comentario)> ComentariosBajos);

    public async Task<Satisfaccion> SatisfaccionAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.Encuestas.AsNoTracking().Select(x => new { x.Momento, Etapa = x.Etapa != null ? x.Etapa.Nombre : null, x.Puntaje, x.Comentario, Obra = x.Obra.Codigo })
            .ToListAsync(ct);
        var resp = e.Where(x => x.Puntaje is not null).ToList();
        var entrega = resp.Where(x => x.Momento == MomentoEncuesta.Entrega).Select(x => (decimal)x.Puntaje!.Value).ToList();
        return new Satisfaccion(Postventa.Nps(resp.Select(x => x.Puntaje!.Value)), resp.Count, e.Count - resp.Count,
            entrega.Count > 0 ? Math.Round(entrega.Average(), 1) : null,
            resp.Where(x => x.Momento == MomentoEncuesta.FinDeEtapa && x.Etapa is not null).GroupBy(x => x.Etapa!)
                .Select(g => (g.Key, Math.Round((decimal)g.Average(x => x.Puntaje!.Value), 1), g.Count())).OrderBy(x => x.Item2).ToList(),
            resp.Where(x => x.Puntaje <= 6 && x.Comentario is not null).Select(x => (x.Obra, x.Puntaje!.Value, x.Comentario!)).Take(10).ToList());
    }

    internal static double? Mediana(IEnumerable<double> valores)
    {
        var l = valores.OrderBy(v => v).ToList();
        if (l.Count == 0) return null;
        return l.Count % 2 == 1 ? Math.Round(l[l.Count / 2], 1) : Math.Round((l[l.Count / 2 - 1] + l[l.Count / 2]) / 2, 1);
    }
}
