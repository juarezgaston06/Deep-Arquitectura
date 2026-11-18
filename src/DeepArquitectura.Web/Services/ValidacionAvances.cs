using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// El capataz (desde su app) o el estudio (panel) confirma lo que informan los albañiles: aprobar, corregir la cantidad o
/// el porcentaje, o rechazar con motivo. Lo aprobado es lo que cuenta para rendimientos y certificados; lo rechazado
/// deja de sumar también en lo que ve el albañil.
/// </summary>
public class ValidacionAvances(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    public sealed record Pendiente(int RegistroId, int ObraId, string ObraCodigo, string ObraNombre, int? AlbanilId, string Albanil,
        int TareaId, string Tarea, string? Unidad, decimal? Cantidad, int? Porcentaje, bool Terminada, DateTime Fecha, string? Nota, int Fotos, int Videos,
        decimal? CantidadObjetivo, decimal CantidadAprobadaTarea);

    /// <summary>Avances por validar. Con <paramref name="capatazId"/>: solo de sus obras.</summary>
    public async Task<List<Pendiente>> PendientesAsync(int? obraId = null, int? capatazId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Registros.AsNoTracking()
            .Where(r => r.Validacion == EstadoValidacion.Pendiente && r.TareaObraId != null
                && (obraId == null || r.ObraId == obraId)
                && (capatazId == null || db.CapatazObras.Any(c => c.CapatazId == capatazId && c.ObraId == r.ObraId)))
            .OrderBy(r => r.Fecha)
            .Select(r => new Pendiente(r.Id, r.ObraId, r.Obra.Codigo, r.Obra.Nombre, r.AlbanilId, r.Albanil != null ? r.Albanil.Nombre : "—",
                r.TareaObraId!.Value, r.Tarea!.Titulo, r.Tarea.Unidad, r.Cantidad, r.PorcentajeTarea, r.PorcentajeTarea == 100, r.Fecha, r.Descripcion,
                r.Medios.Count(m => m.Tipo == TipoMedio.Foto), r.Medios.Count(m => m.Tipo == TipoMedio.Video),
                r.Tarea.CantidadObjetivo, r.Tarea.CantidadAprobada))
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<int> CantidadPendientesAsync(int? obraId = null, int? capatazId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Registros.CountAsync(r => r.Validacion == EstadoValidacion.Pendiente
            && (obraId == null || r.ObraId == obraId)
            && (capatazId == null || db.CapatazObras.Any(c => c.CapatazId == capatazId && c.ObraId == r.ObraId)), ct);
    }

    /// <summary>
    /// Valida un avance. <paramref name="capatazId"/> null = el estudio desde el panel. Corregir pide la cantidad (o %)
    /// correcta; rechazar pide motivo (lo ve el albañil). Se puede volver a validar (cambiar de opinión).
    /// </summary>
    public async Task<string?> ValidarAsync(int registroId, EstadoValidacion decision, decimal? cantidad = null, int? porcentaje = null,
        string? motivo = null, int? capatazId = null, CancellationToken ct = default)
    {
        if (decision == EstadoValidacion.Pendiente) return "Elegí aprobar, corregir o rechazar.";
        motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()[..Math.Min(motivo.Trim().Length, 500)];
        if (decision == EstadoValidacion.Rechazado && motivo is null) return "Para rechazar decí el motivo (lo ve el albañil).";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Registros.Include(x => x.Tarea).SingleOrDefaultAsync(x => x.Id == registroId && x.Validacion != null, ct);
        if (r?.Tarea is null) return "Ese avance no existe o no requiere validación.";
        if (capatazId is int cap && !await db.CapatazObras.AnyAsync(c => c.CapatazId == cap && c.ObraId == r.ObraId, ct))
            return "Esa obra no está asignada a este capataz.";

        var terminada = (r.PorcentajeInformado ?? r.PorcentajeTarea) == 100;
        // Volver a lo informado antes de aplicar la nueva decisión.
        if (r.CantidadInformada is not null) { r.Cantidad = r.CantidadInformada; r.CantidadInformada = null; }
        if (r.PorcentajeInformado is not null) { r.PorcentajeTarea = r.PorcentajeInformado; r.PorcentajeInformado = null; }

        if (decision == EstadoValidacion.Corregido)
        {
            if (r.Tarea.Unidad is not null)
            {
                if (cantidad is not >= 0 || cantidad > 1_000_000) return "Indicá la cantidad correcta.";
                r.CantidadInformada = r.Cantidad ?? 0;
                r.Cantidad = Math.Round(cantidad.Value, 2);
            }
            else
            {
                if (porcentaje is not (>= 0 and <= 100)) return "Indicá el porcentaje correcto (0 a 100).";
                r.PorcentajeInformado = r.PorcentajeTarea ?? 0;
                r.PorcentajeTarea = porcentaje;
            }
        }

        r.Validacion = decision;
        r.ValidadoPorCapatazId = capatazId;
        r.ValidadoEn = Ahora;
        r.MotivoValidacion = motivo;

        // Un «terminé la tarea» rechazado la reabre; aprobado de nuevo la vuelve a cerrar.
        var t = r.Tarea;
        var vigenteTerminada = terminada && decision != EstadoValidacion.Rechazado && r.PorcentajeTarea == 100;
        if (terminada && !vigenteTerminada && t.Estado == EstadoTarea.Completada) { t.Estado = EstadoTarea.EnCurso; t.CompletadaEn = null; }
        if (vigenteTerminada && t.Estado != EstadoTarea.Completada) { t.Estado = EstadoTarea.Completada; t.CompletadaEn = r.Fecha; }
        await db.SaveChangesAsync(ct);
        await CalculoTareas.RecalcularAsync(db, t, ct);
        return null;
    }

    /// <summary>Aprueba todo lo pendiente de un capataz (o de una obra). Devuelve cuántos.</summary>
    public async Task<int> AprobarTodosAsync(int? capatazId, int? obraId, CancellationToken ct = default)
    {
        var pendientes = await PendientesAsync(obraId, capatazId, ct);
        foreach (var p in pendientes) await ValidarAsync(p.RegistroId, EstadoValidacion.Aprobado, capatazId: capatazId, ct: ct);
        return pendientes.Count;
    }
}
