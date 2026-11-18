using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Avance de una tarea a partir de sus reportes (entradas de bitácora con TareaObraId):
/// <list type="bullet">
/// <item>Informado (lo que ve el albañil): todo lo no rechazado.</item>
/// <item>Aprobado (rendimientos y certificados): solo lo que aprobó o corrigió el capataz o el estudio.</item>
/// </list>
/// Con unidad se suman las cantidades (un reintento o un envío atrasado no descuadra nada); sin unidad vale el
/// porcentaje del reporte más reciente según la fecha del teléfono. Un reporte con PorcentajeTarea = 100 marca
/// «terminada» (en tareas con unidad también).
/// </summary>
public static class CalculoTareas
{
    public static readonly EstadoValidacion[] Aprobados = [EstadoValidacion.Aprobado, EstadoValidacion.Corregido];

    private sealed record Reporte(int Id, DateTime Fecha, decimal? Cantidad, int? PorcentajeTarea, EstadoValidacion? Validacion);

    /// <summary>Recalcula y guarda. No cambia el estado de la tarea (eso lo decide quien llama).</summary>
    public static async Task RecalcularAsync(AppDbContext db, TareaObra tarea, CancellationToken ct)
    {
        // Se trae en memoria: SQLite (tests) no agrega decimales y son pocos registros por tarea.
        // Validacion null (avances anteriores a la validación, o del capataz/estudio) cuenta como aprobado.
        var reportes = await db.Registros
            .Where(x => x.TareaObraId == tarea.Id && (x.Validacion == null || x.Validacion != EstadoValidacion.Rechazado)
                && (x.Cantidad != null || x.PorcentajeTarea != null))
            .Select(x => new Reporte(x.Id, x.Fecha, x.Cantidad, x.PorcentajeTarea, x.Validacion))
            .ToListAsync(ct);
        var aprobados = reportes.Where(r => r.Validacion is null || Aprobados.Contains(r.Validacion.Value)).ToList();

        tarea.CantidadHecha = reportes.Sum(r => r.Cantidad ?? 0);
        tarea.CantidadAprobada = aprobados.Sum(r => r.Cantidad ?? 0);

        int Porcentaje(List<Reporte> lista, decimal cantidad)
        {
            if (lista.Any(r => r.PorcentajeTarea == 100)) return 100;
            if (tarea.Unidad is not null)
                return tarea.CantidadObjetivo is > 0 ? (int)Math.Min(100, Math.Round(cantidad * 100 / tarea.CantidadObjetivo.Value)) : 0;
            var ultimo = lista.Where(r => r.PorcentajeTarea != null).OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id).FirstOrDefault();
            return ultimo?.PorcentajeTarea ?? 0;
        }

        tarea.Porcentaje = Porcentaje(reportes, tarea.CantidadHecha);
        tarea.PorcentajeAprobado = Porcentaje(aprobados, tarea.CantidadAprobada);
        // Si el estudio la dio por terminada desde el panel, cuenta completa.
        if (tarea.Estado == EstadoTarea.Completada && !reportes.Any(r => r.PorcentajeTarea == 100))
            tarea.Porcentaje = tarea.PorcentajeAprobado = 100;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Avance aprobado de la tarea, de 0 a 1 (para curva y certificados).</summary>
    public static decimal Fraccion(TareaObra t) =>
        t.Estado == EstadoTarea.Cancelada ? 0 : Math.Clamp(t.PorcentajeAprobado, 0, 100) / 100m;

    /// <summary>Peso de cada tarea en la obra: el cargado o, si no hay ninguno, todas iguales.</summary>
    public static Dictionary<int, decimal> Pesos(IReadOnlyCollection<TareaObra> tareas)
    {
        var validas = tareas.Where(t => t.Estado != EstadoTarea.Cancelada).ToList();
        var conPeso = validas.Where(t => t.Peso is > 0).ToList();
        if (validas.Count == 0) return [];
        if (conPeso.Count == 0) return validas.ToDictionary(t => t.Id, _ => 1m / validas.Count);
        // Las que no tienen peso toman el promedio de las que sí.
        var promedio = conPeso.Average(t => t.Peso!.Value);
        var total = validas.Sum(t => t.Peso is > 0 ? t.Peso.Value : promedio);
        return validas.ToDictionary(t => t.Id, t => (t.Peso is > 0 ? t.Peso.Value : promedio) / total);
    }

    public static bool Atrasada(TareaObra t, DateOnly hoy) =>
        t.Estado is EstadoTarea.Pendiente or EstadoTarea.EnCurso && t.FechaLimite is { } fin && fin < hoy;
}
