using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Libro de obra digital: órdenes de servicio (estudio → obra) y notas de pedido (obra → estudio), numeradas por obra
/// y tipo. Las órdenes llevan registro de quién las leyó (capataz y cuadrilla). No reemplaza al libro rubricado si la
/// obra lo exige: deja todo ordenado, con fecha y autor.
/// </summary>
public class LibroObra(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Asienta con el próximo número. Si el teléfono manda un <paramref name="referencia"/>, se crea también una entrada
    /// de bitácora con ese Ref (para las fotos). Dos asientos simultáneos no repiten número (índice único + reintento).
    /// </summary>
    public static async Task<int> AsentarAsync(AppDbContext db, int obraId, TipoAsiento tipo, string texto, DateTime fecha,
        int? capatazId, int? albanilId, Guid? referencia = null, int? registroCapatazId = null, int? respondeA = null, CancellationToken ct = default)
    {
        for (var intento = 0; ; intento++)
        {
            var numero = (await db.Asientos.Where(a => a.ObraId == obraId && a.Tipo == tipo).MaxAsync(a => (int?)a.Numero, ct) ?? 0) + 1;
            var asiento = new AsientoLibro
            {
                ObraId = obraId, Tipo = tipo, Numero = numero, Fecha = fecha, Texto = texto,
                CapatazId = capatazId, AlbanilId = albanilId, Ref = referencia, RespondeAId = respondeA,
            };
            if (referencia is Guid r)
                asiento.Registro = new RegistroAvance
                {
                    ObraId = obraId, AlbanilId = albanilId, CapatazId = registroCapatazId ?? capatazId, Ref = r, Fecha = fecha,
                    Titulo = Recortar($"📖 Nota de pedido N° {numero}: {texto}", 200), Descripcion = texto.Length > 190 ? texto : null,
                };
            db.Asientos.Add(asiento);
            try
            {
                await db.SaveChangesAsync(ct);
                return asiento.Id;
            }
            catch (DbUpdateException) when (intento < 3)
            {
                db.Entry(asiento).State = EntityState.Detached;
                if (asiento.Registro is not null) db.Entry(asiento.Registro).State = EntityState.Detached;
                // Si el choque fue por el Ref (reintento del teléfono), devolver el que ya está.
                if (referencia is Guid g && await db.Asientos.Where(a => a.Ref == g).Select(a => (int?)a.Id).SingleOrDefaultAsync(ct) is int previo)
                    return previo;
            }
        }
    }

    // ------------------------------------------------------------------ Panel

    /// <summary>Orden de servicio del estudio a la obra (opcionalmente en respuesta a una nota de pedido).</summary>
    public async Task<(int? Id, string? Error)> EmitirOrdenAsync(int obraId, string texto, int? respondeA = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length > 4000) return (null, "Escribí la orden (hasta 4000 caracteres).");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (respondeA is int nota && !await db.Asientos.AnyAsync(a => a.Id == nota && a.ObraId == obraId && a.Tipo == TipoAsiento.NotaPedido, ct))
            return (null, "Esa nota de pedido no es de esta obra.");
        return (await AsentarAsync(db, obraId, TipoAsiento.OrdenServicio, texto.Trim(), Ahora, null, null, respondeA: respondeA, ct: ct), null);
    }

    // ------------------------------------------------------------------ Capataz (app)

    /// <summary>Órdenes de la obra que el capataz todavía no confirmó haber escuchado.</summary>
    public async Task<List<AsientoLibro>> OrdenesSinLeerCapatazAsync(int capatazId, int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Asientos.AsNoTracking()
            .Where(a => a.ObraId == obraId && a.Tipo == TipoAsiento.OrdenServicio && !a.Lecturas.Any(l => l.CapatazId == capatazId))
            .OrderBy(a => a.Numero).ToListAsync(ct);
    }

    public async Task MarcarLeidasCapatazAsync(int capatazId, IEnumerable<int> asientos, CancellationToken ct = default)
    {
        var ids = asientos.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ya = await db.Lecturas.Where(l => l.CapatazId == capatazId && ids.Contains(l.AsientoLibroId)).Select(l => l.AsientoLibroId).ToListAsync(ct);
        foreach (var id in ids.Except(ya)) db.Lecturas.Add(new LecturaAsiento { AsientoLibroId = id, CapatazId = capatazId, LeidoEn = Ahora });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* simultáneas: alcanza con una */ }
    }

    /// <summary>
    /// Nota de pedido del capataz desde su app. Idempotente por <paramref name="referencia"/> (el teléfono reintenta sin
    /// señal); con ese mismo Ref se suben después las fotos, colgadas de la entrada de bitácora que crea el asiento.
    /// </summary>
    public async Task<(int? Id, string? Error)> NotaPedidoCapatazAsync(int capatazId, int obraId, Guid referencia, string? texto,
        DateTime? fecha = null, CancellationToken ct = default)
    {
        if (referencia == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length > 4000) return (null, "Escribí la nota (hasta 4000 caracteres).");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.CapatazObras.AnyAsync(x => x.CapatazId == capatazId && x.ObraId == obraId, ct)) return (null, "Obra no asignada.");
        var previa = await db.Asientos.Where(a => a.Ref == referencia).Select(a => new { a.Id, a.CapatazId, a.ObraId }).SingleOrDefaultAsync(ct);
        if (previa is not null) return previa.CapatazId == capatazId && previa.ObraId == obraId ? (previa.Id, null) : (null, "Envío duplicado.");
        if (await db.Registros.AnyAsync(r => r.Ref == referencia, ct)) return (null, "Envío duplicado.");

        // Fecha del teléfono (cuándo la escribió), sin aceptar futuras ni muy viejas.
        var f = fecha is DateTime x ? (x.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : x.ToUniversalTime()) : Ahora;
        if (f > Ahora.AddMinutes(5) || f < Ahora.AddDays(-60)) f = Ahora;
        return (await AsentarAsync(db, obraId, TipoAsiento.NotaPedido, texto.Trim(), f, capatazId, null, referencia, ct: ct), null);
    }

    /// <summary>Confirma órdenes leídas: solo las órdenes de servicio de esa obra (lo demás se ignora).</summary>
    public async Task MarcarLeidasCapatazAsync(int capatazId, int obraId, IEnumerable<int> asientos, CancellationToken ct = default)
    {
        var ids = asientos.Distinct().Take(200).ToList();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            ids = await db.Asientos.Where(a => ids.Contains(a.Id) && a.ObraId == obraId && a.Tipo == TipoAsiento.OrdenServicio).Select(a => a.Id).ToListAsync(ct);
        await MarcarLeidasCapatazAsync(capatazId, ids, ct);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
