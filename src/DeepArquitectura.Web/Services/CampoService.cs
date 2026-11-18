using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Lo que el estudio hace desde el panel con la cuadrilla: tareas, respuesta a pedidos de material y a problemas.</summary>
public class CampoService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    public sealed class DatosTarea
    {
        public string Titulo { get; set; } = "";
        public string? Descripcion { get; set; }
        public int? EtapaObraId { get; set; }
        public int? AlbanilId { get; set; }
        /// <summary>Vacío = se mide en %.</summary>
        public string? Unidad { get; set; }
        public decimal? CantidadObjetivo { get; set; }
        /// <summary>Plan: inicio y fin previstos.</summary>
        public DateOnly? FechaInicioPlan { get; set; }
        public DateOnly? FechaLimite { get; set; }
        /// <summary>Sin albañil: solo la ve este oficio.</summary>
        public string? Oficio { get; set; }
        public string? Rubro { get; set; }
        public decimal? Peso { get; set; }
    }

    /// <summary>Unidades que se ofrecen en el panel (se puede escribir otra).</summary>
    public static readonly string[] UnidadesTarea = ["m²", "m³", "ml", "u", "kg"];

    /// <summary>Rubros sugeridos (agrupan rendimientos entre obras; se puede escribir otro).</summary>
    public static readonly string[] Rubros =
    [
        "Excavación", "Hormigón armado", "Mampostería de ladrillo común", "Mampostería de ladrillo hueco", "Mampostería de bloque",
        "Revoque grueso", "Revoque fino", "Contrapiso", "Carpeta", "Colocación de cerámicos", "Colocación de porcelanato",
        "Durlock", "Pintura", "Instalación sanitaria", "Instalación eléctrica", "Instalación de gas", "Cubierta / techo", "Impermeabilización",
    ];

    public async Task<(int? Id, string? Error)> CrearTareaAsync(int obraId, DatosTarea d, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await ValidarAsync(db, obraId, d, ct) is { } error) return (null, error);
        var t = new TareaObra { ObraId = obraId };
        Copiar(d, t);
        db.Tareas.Add(t);
        await db.SaveChangesAsync(ct);
        return (t.Id, null);
    }

    public async Task<string?> ActualizarTareaAsync(int tareaId, DatosTarea d, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Tareas.SingleOrDefaultAsync(x => x.Id == tareaId, ct);
        if (t is null) return "Tarea inexistente.";
        if (await ValidarAsync(db, t.ObraId, d, ct) is { } error) return error;
        var cambioUnidad = !string.Equals(t.Unidad, Limpio(d.Unidad, 20), StringComparison.Ordinal);
        Copiar(d, t);
        await db.SaveChangesAsync(ct);
        if (cambioUnidad || t.Unidad is not null) await CalculoTareas.RecalcularAsync(db, t, ct);
        return null;
    }

    /// <summary>El estudio puede cerrar, reabrir o cancelar una tarea (cancelada desaparece de la app).</summary>
    public async Task CambiarEstadoTareaAsync(int tareaId, EstadoTarea estado, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Tareas.SingleAsync(x => x.Id == tareaId, ct);
        t.Estado = estado;
        if (estado == EstadoTarea.Completada) t.CompletadaEn ??= Ahora;
        else t.CompletadaEn = null;
        await db.SaveChangesAsync(ct);
        await CalculoTareas.RecalcularAsync(db, t, ct);
    }

    public async Task CambiarEstadoPedidoAsync(int pedidoId, EstadoPedido estado, string? respuesta, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Pedidos.SingleAsync(x => x.Id == pedidoId, ct);
        p.Estado = estado;
        if (respuesta is not null) p.Respuesta = Limpio(respuesta, 1000);
        await db.SaveChangesAsync(ct);
    }

    public async Task CambiarEstadoProblemaAsync(int problemaId, EstadoProblema estado, string? respuesta, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Problemas.SingleAsync(x => x.Id == problemaId, ct);
        p.Estado = estado;
        p.ResueltoEn = estado == EstadoProblema.Resuelto ? p.ResueltoEn ?? Ahora : null;
        if (respuesta is not null) p.Respuesta = Limpio(respuesta, 1000);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> ValidarAsync(AppDbContext db, int obraId, DatosTarea d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Titulo) || d.Titulo.Trim().Length > 200) return "Escribí la tarea (hasta 200 caracteres).";
        if (d.Descripcion is { Length: > 2000 }) return "La descripción es demasiado larga.";
        var unidad = Limpio(d.Unidad, 20);
        if (unidad is not null && d.CantidadObjetivo is not > 0) return "Indicá la cantidad a hacer (ej. 40 m²) o dejá la unidad vacía para medir en %.";
        if (unidad is null && d.CantidadObjetivo is not null) return "Para una cantidad objetivo indicá la unidad.";
        if (d.EtapaObraId is int etapaId && !await db.Etapas.AnyAsync(e => e.Id == etapaId && e.ObraId == obraId, ct))
            return "La etapa no es de esta obra.";
        if (d.AlbanilId is int albanilId && !await db.AlbanilObras.AnyAsync(x => x.AlbanilId == albanilId && x.ObraId == obraId, ct))
            return "Ese albañil no está asignado a la obra.";
        if (d.FechaInicioPlan is { } ini && d.FechaLimite is { } fin && ini > fin) return "El inicio previsto no puede ser después del fin.";
        if (d.Peso is <= 0 or > 1_000_000) return "El peso tiene que ser mayor a cero (o dejalo vacío para que todas pesen igual).";
        if (d.Rubro is { Length: > 80 } || d.Oficio is { Length: > 60 }) return "Rubro u oficio demasiado largo.";
        return null;
    }

    private static void Copiar(DatosTarea d, TareaObra t)
    {
        t.Titulo = d.Titulo.Trim();
        t.Descripcion = Limpio(d.Descripcion, 2000);
        t.EtapaObraId = d.EtapaObraId;
        t.AlbanilId = d.AlbanilId;
        t.Unidad = Limpio(d.Unidad, 20);
        t.CantidadObjetivo = t.Unidad is null ? null : d.CantidadObjetivo;
        t.FechaLimite = d.FechaLimite;
        t.FechaInicioPlan = d.FechaInicioPlan;
        t.Oficio = t.AlbanilId is null ? Limpio(d.Oficio, 60) : null;
        t.Rubro = Limpio(d.Rubro, 80);
        t.Peso = d.Peso is > 0 ? d.Peso : null;
    }

    private static string? Limpio(string? texto, int max)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
