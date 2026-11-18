using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Trámites e inspecciones de la obra con su historia de estados (municipalidad, Ecogas, EPEC, Colegio…).</summary>
public class TramitesObra(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateOnly Hoy => DateOnly.FromDateTime(Textos.AHoraLocal(_reloj.GetUtcNow().UtcDateTime));

    /// <summary>Trámites típicos por servicio (se agregan con un botón y se pueden editar).</summary>
    public static IReadOnlyList<(string Nombre, string Organismo)> Sugeridos(TipoServicio s) => s switch
    {
        TipoServicio.PlanosMunicipales or TipoServicio.DisenoDeObra or TipoServicio.Relevamiento =>
        [
            ("Visado de planos", "Colegio de Arquitectos de Córdoba"),
            ("Permiso de edificación / aprobación de planos", "Municipalidad"),
            ("Inspección de obra", "Municipalidad"),
            ("Final de obra", "Municipalidad"),
        ],
        TipoServicio.Gas =>
        [
            ("Aprobación de planos de gas", "Ecogas"),
            ("Inspección parcial (cañerías a la vista)", "Ecogas"),
            ("Inspección final y habilitación", "Ecogas"),
        ],
        TipoServicio.Electricidad =>
        [
            ("Certificado de instalación apta", "ERSeP"),
            ("Pedido de conexión / medidor", "EPEC"),
        ],
        _ => [],
    };

    public async Task<List<TramiteObra>> ListaAsync(int obraId, bool soloVisibles = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tramites.AsNoTracking().Include(t => t.Movimientos.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id))
            .ThenInclude(m => m.Documento)
            .Where(t => t.ObraId == obraId && (!soloVisibles || t.VisibleCliente)).OrderBy(t => t.Orden).ThenBy(t => t.Id).ToListAsync(ct);
    }

    public async Task<(int? Id, string? Error)> AgregarAsync(int obraId, string? nombre, string? organismo, string? expediente = null, CancellationToken ct = default)
    {
        nombre = Relevamientos.Texto(nombre, 200);
        if (nombre is null) return (null, "Escribí el nombre del trámite.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        var orden = await db.Tramites.Where(t => t.ObraId == obraId).Select(t => (int?)t.Orden).MaxAsync(ct) ?? 0;
        var t = new TramiteObra
        {
            ObraId = obraId, Orden = orden + 1, Nombre = nombre, Organismo = Relevamientos.Texto(organismo, 150),
            Expediente = Relevamientos.Texto(expediente, 80),
        };
        t.Movimientos.Add(new MovimientoTramite { Fecha = Hoy, Estado = EstadoTramite.Preparando, Nota = "Trámite creado." });
        db.Tramites.Add(t);
        await db.SaveChangesAsync(ct);
        return (t.Id, null);
    }

    /// <summary>Agrega los sugeridos para el servicio de la obra que todavía no estén (mismo nombre).</summary>
    public async Task<int> AgregarSugeridosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Obras.AsNoTracking().SingleAsync(o => o.Id == obraId, ct);
        var existentes = await db.Tramites.Where(t => t.ObraId == obraId).Select(t => t.Nombre).ToListAsync(ct);
        var n = 0;
        foreach (var (nombre, organismo) in Sugeridos(obra.Servicio).Where(s => !existentes.Contains(s.Nombre)))
        {
            var exp = nombre.StartsWith("Permiso", StringComparison.Ordinal) ? obra.ExpedienteMunicipal : null;
            if ((await AgregarAsync(obraId, nombre, organismo, exp, ct)).Error is null) n++;
        }
        return n;
    }

    public sealed class DatosMovimiento
    {
        public DateOnly Fecha { get; set; }
        public EstadoTramite Estado { get; set; }
        public string? Nota { get; set; }
        public DateOnly? Vence { get; set; }
        public string? Expediente { get; set; }
        public int? DocumentoObraId { get; set; }
    }

    /// <summary>Nuevo estado o novedad del trámite. Completa las fechas de presentado y aprobado.</summary>
    public async Task<string?> MoverAsync(int tramiteId, DatosMovimiento d, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Tramites.SingleOrDefaultAsync(x => x.Id == tramiteId, ct);
        if (t is null) return "El trámite no existe.";
        var fecha = d.Fecha == default ? Hoy : d.Fecha;
        if (fecha > Hoy) return "La fecha no puede ser futura.";
        if (d.DocumentoObraId is int doc && !await db.Documentos.AnyAsync(x => x.Id == doc && x.ObraId == t.ObraId, ct))
            return "El documento no es de esta obra.";
        if (d.Estado == EstadoTramite.Observado && Relevamientos.Texto(d.Nota, 2000) is null) return "Anotá qué observaron.";

        db.MovimientosTramite.Add(new MovimientoTramite
        {
            TramiteObraId = t.Id, Fecha = fecha, Estado = d.Estado, Nota = Relevamientos.Texto(d.Nota, 2000), DocumentoObraId = d.DocumentoObraId,
        });
        t.Estado = d.Estado;
        if (d.Estado == EstadoTramite.Presentado) t.FechaPresentado ??= fecha;
        if (d.Estado == EstadoTramite.Aprobado) { t.FechaPresentado ??= fecha; t.FechaAprobado = fecha; }
        else t.FechaAprobado = null;
        t.Vence = d.Estado is EstadoTramite.Aprobado or EstadoTramite.Rechazado ? null : d.Vence;
        if (Relevamientos.Texto(d.Expediente, 80) is { } exp) t.Expediente = exp;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task CambiarVisibleAsync(int tramiteId, bool visible, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Tramites.Where(t => t.Id == tramiteId).ExecuteUpdateAsync(s => s.SetProperty(t => t.VisibleCliente, visible), ct);
    }

    public async Task BorrarAsync(int tramiteId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Tramites.Where(t => t.Id == tramiteId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Trámites abiertos que vencen en los próximos <paramref name="dias"/> días (o ya vencieron).</summary>
    public async Task<List<TramiteObra>> PorVencerAsync(int dias = 7, CancellationToken ct = default)
    {
        var limite = Hoy.AddDays(dias);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tramites.AsNoTracking().Include(t => t.Obra)
            .Where(t => t.Vence != null && t.Vence <= limite && t.Estado != EstadoTramite.Aprobado && t.Estado != EstadoTramite.Rechazado)
            .OrderBy(t => t.Vence).ToListAsync(ct);
    }

    /// <summary>Días entre presentado y aprobado (null si todavía no se aprobó).</summary>
    public static int? DiasHastaAprobacion(TramiteObra t) =>
        t.FechaPresentado is { } p && t.FechaAprobado is { } a ? a.DayNumber - p.DayNumber : null;
}
