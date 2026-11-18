using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Circuito de datos de la obra desde la app: recepción de materiales, controles de etapa, libro de obra
/// (órdenes de servicio y notas de pedido) y fotos desde puntos fijos.
/// </summary>
public partial class AlbanilService
{
    // ================================================================== Recepción de materiales

    /// <summary>
    /// Material que llegó. Si viene de un pedido, lo marca entregado (o entrega parcial) y, si falta algo, deja un
    /// problema «Falta material» automático. Crea una entrada de bitácora con el mismo Ref para la foto del remito.
    /// </summary>
    public async Task<(int? Id, string? Error)> RegistrarRecepcionAsync(int albanilId, int obraId, NuevaRecepcionRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (r.Items is null || r.Items.Count == 0) return (null, "Cargá qué llegó.");
        if (r.Items.Count > 40) return (null, "Demasiados renglones.");
        if (r.Items.Any(i => string.IsNullOrWhiteSpace(i.Material) || i.Material.Trim().Length > 150 || string.IsNullOrWhiteSpace(i.Unidad) || i.Unidad.Trim().Length > 20))
            return (null, "Revisá el material y la unidad de cada renglón.");
        if (r.Items.Any(i => i.Cantidad is < 0 or > 1_000_000)) return (null, "Alguna cantidad no es válida.");
        if (r.Nota is { Length: > 1000 } || r.Proveedor is { Length: > 150 } || r.NumeroRemito is { Length: > 50 }) return (null, "Algún dato es demasiado largo.");

        var previa = await db.Recepciones.Where(x => x.Ref == r.Ref).Select(x => new { x.Id, x.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previa is not null) return previa.AlbanilId == albanilId ? (previa.Id, null) : (null, "Envío duplicado.");

        PedidoMaterial? pedido = null;
        if (r.PedidoId is int pedidoId)
        {
            pedido = await db.Pedidos.Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == pedidoId && p.ObraId == obraId, ct);
            if (pedido is null) return (null, "Ese pedido no es de esta obra.");
            if (r.Items.Any(i => i.PedidoItemId is int pi && pedido.Items.All(x => x.Id != pi))) return (null, "Algún renglón no es de ese pedido.");
        }

        var fecha = NormalizarFecha(r.FechaUtc);
        var items = r.Items.Where(i => i.Cantidad > 0).ToList();
        var resumen = items.Count == 0 ? "no llegó nada" : string.Join(", ", items.Select(i => $"{Textos.Numero(i.Cantidad)} {i.Unidad.Trim()} {i.Material.Trim()}"));
        var registro = new RegistroAvance
        {
            ObraId = obraId, AlbanilId = albanilId, CapatazId = capatazId, Ref = r.Ref, Fecha = fecha,
            Titulo = Recortar($"📦 Llegó material{(pedido is null ? "" : $" (pedido #{pedido.Id})")}: {resumen}", 200),
            Descripcion = Limpio(string.Join(" · ", new[] { r.Proveedor is null ? null : $"Proveedor: {r.Proveedor.Trim()}",
                r.NumeroRemito is null ? null : $"Remito {r.NumeroRemito.Trim()}", r.Nota?.Trim() }.Where(x => !string.IsNullOrEmpty(x))), 4000),
        };
        var recepcion = new RecepcionMaterial
        {
            Ref = r.Ref, ObraId = obraId, PedidoMaterialId = pedido?.Id, AlbanilId = albanilId, CapatazId = capatazId,
            Registro = registro, Fecha = fecha,
            Proveedor = Limpio(r.Proveedor, 150), NumeroRemito = Limpio(r.NumeroRemito, 50), Nota = Limpio(r.Nota, 1000),
            Items = r.Items.Select(i => new RecepcionItem
            {
                PedidoMaterialItemId = i.PedidoItemId, Material = i.Material.Trim(), Cantidad = Math.Round(i.Cantidad, 2), Unidad = i.Unidad.Trim(),
            }).ToList(),
        };
        db.Recepciones.Add(recepcion);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.Recepciones.Where(x => x.Ref == r.Ref && x.AlbanilId == albanilId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar la recepción.") : (id, null);
        }

        if (pedido is not null)
        {
            var recibido = await RecibidoPorItemAsync(db, pedido.Items.Select(i => i.Id).ToList(), ct);
            var faltantes = pedido.Items
                .Select(i => (Item: i, Falta: i.Cantidad - recibido.GetValueOrDefault(i.Id)))
                .Where(x => x.Falta > 0).ToList();
            pedido.Estado = faltantes.Count == 0 ? EstadoPedido.Entregado : EstadoPedido.EntregaParcial;
            await db.SaveChangesAsync(ct);
            if (faltantes.Count > 0)
                await CrearProblemaAsync(db, DeterministaRef(r.Ref, "faltante"), obraId, albanilId, capatazId, pedido.TareaObraId,
                    TipoProblema.FaltaMaterial, pedido.Urgente ? UrgenciaProblema.Alta : UrgenciaProblema.Media,
                    $"Del pedido #{pedido.Id} falta: {string.Join(", ", faltantes.Select(f => $"{Textos.Numero(f.Falta)} {f.Item.Unidad} {f.Item.Material}"))}.",
                    fecha, ct);
        }
        return (recepcion.Id, null);
    }

    /// <summary>Cuánto llegó de cada renglón de pedido (sumando todas las recepciones).</summary>
    internal static async Task<Dictionary<int, decimal>> RecibidoPorItemAsync(AppDbContext db, List<int> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var filas = await db.RecepcionItems.Where(i => i.PedidoMaterialItemId != null && itemIds.Contains(i.PedidoMaterialItemId.Value))
            .Select(i => new { Id = i.PedidoMaterialItemId!.Value, i.Cantidad }).ToListAsync(ct);
        return filas.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.Sum(f => f.Cantidad));
    }

    // ================================================================== Controles de etapa

    /// <summary>Controles de las etapas en curso, con su último resultado.</summary>
    private static async Task<IReadOnlyList<ControlDto>> ControlesAsync(AppDbContext db, int obraId, CancellationToken ct)
    {
        var controles = await db.Controles.AsNoTracking()
            .Where(c => c.Etapa.ObraId == obraId && c.Etapa.Estado == EstadoEtapa.EnCurso)
            .OrderBy(c => c.Etapa.Orden).ThenBy(c => c.Orden)
            .Select(c => new
            {
                c.Id, Etapa = c.Etapa.Nombre, c.Texto, c.RequiereFoto,
                Ultima = c.Respuestas.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id).Select(r => new { r.Resultado, r.Fecha, r.Nota }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return controles.Select(c => new ControlDto(c.Id, c.Etapa, c.Texto, c.RequiereFoto, c.Ultima?.Resultado.ToString(), c.Ultima?.Fecha, c.Ultima?.Nota)).ToList();
    }

    /// <summary>Resultado de un control. Las fotos se suben con el mismo Ref (queda una entrada en la bitácora).</summary>
    public async Task<(int? Id, string? Error)> ResponderControlAsync(int albanilId, int obraId, RespuestaControlRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (!Enum.TryParse<ResultadoControl>(r.Resultado, ignoreCase: true, out var resultado) || !Enum.IsDefined(resultado)) return (null, "Resultado inválido.");
        if (r.Nota is { Length: > 1000 }) return (null, "La nota es demasiado larga.");
        if (resultado == ResultadoControl.Observado && string.IsNullOrWhiteSpace(r.Nota)) return (null, "Contá qué está mal.");

        var previa = await db.RespuestasControl.Where(x => x.Ref == r.Ref).Select(x => new { x.Id, x.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previa is not null) return previa.AlbanilId == albanilId ? (previa.Id, null) : (null, "Envío duplicado.");
        var control = await db.Controles.Include(c => c.Etapa).SingleOrDefaultAsync(c => c.Id == r.ControlId && c.Etapa.ObraId == obraId, ct);
        if (control is null) return (null, "Ese control no es de esta obra.");

        var fecha = NormalizarFecha(r.FechaUtc);
        var registro = new RegistroAvance
        {
            ObraId = obraId, AlbanilId = albanilId, CapatazId = capatazId, Ref = r.Ref, Fecha = fecha, EtapaObraId = control.EtapaObraId,
            Titulo = Recortar($"{(resultado == ResultadoControl.Ok ? "✔ Control OK" : "✖ Control observado")}: {control.Texto}", 200),
            Descripcion = Limpio(r.Nota, 1000),
        };
        var respuesta = new RespuestaControl
        {
            Ref = r.Ref, ControlEtapaId = control.Id, AlbanilId = albanilId, CapatazId = capatazId, Registro = registro,
            Fecha = fecha, Resultado = resultado, Nota = Limpio(r.Nota, 1000),
        };
        db.RespuestasControl.Add(respuesta);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.RespuestasControl.Where(x => x.Ref == r.Ref && x.AlbanilId == albanilId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar el control.") : (id, null);
        }
        return (respuesta.Id, null);
    }

    // ================================================================== Libro de obra

    private static async Task<IReadOnlyList<OrdenDto>> OrdenesAsync(AppDbContext db, int obraId, int albanilId, CancellationToken ct) =>
        await db.Asientos.AsNoTracking()
            .Where(a => a.ObraId == obraId && a.Tipo == TipoAsiento.OrdenServicio)
            .OrderByDescending(a => a.Numero).Take(15)
            .Select(a => new OrdenDto(a.Id, a.Numero, a.Fecha, a.Texto, a.Lecturas.Any(l => l.AlbanilId == albanilId)))
            .ToListAsync(ct);

    /// <summary>Confirma la lectura de órdenes de servicio (idempotente).</summary>
    public async Task<(int? Id, string? Error)> MarcarLeidoAsync(int albanilId, int obraId, LeidoRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is null) return (null, ErrorNoAsignada);
        var ids = (r.Ordenes ?? []).Distinct().Take(50).ToList();
        var validas = await db.Asientos.Where(a => ids.Contains(a.Id) && a.ObraId == obraId && a.Tipo == TipoAsiento.OrdenServicio)
            .Select(a => a.Id).ToListAsync(ct);
        var yaLeidas = await db.Lecturas.Where(l => l.AlbanilId == albanilId && validas.Contains(l.AsientoLibroId)).Select(l => l.AsientoLibroId).ToListAsync(ct);
        foreach (var id in validas.Except(yaLeidas))
            db.Lecturas.Add(new LecturaAsiento { AsientoLibroId = id, AlbanilId = albanilId, LeidoEn = Ahora });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* dos confirmaciones simultáneas: alcanza con una */ }
        return (validas.Count, null);
    }

    /// <summary>Nota de pedido de la obra al estudio (queda numerada en el libro de obra).</summary>
    public async Task<(int? Id, string? Error)> NotaPedidoAsync(int albanilId, int obraId, NotaPedidoRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (string.IsNullOrWhiteSpace(r.Texto) || r.Texto.Trim().Length > 4000) return (null, "Escribí la nota (hasta 4000 caracteres).");
        var previa = await db.Asientos.Where(a => a.Ref == r.Ref).Select(a => new { a.Id, a.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previa is not null) return previa.AlbanilId == albanilId ? (previa.Id, null) : (null, "Envío duplicado.");

        var id = await LibroObra.AsentarAsync(db, obraId, TipoAsiento.NotaPedido, r.Texto.Trim(), NormalizarFecha(r.FechaUtc),
            capatazId: null, albanilId: albanilId, referencia: r.Ref, registroCapatazId: capatazId, ct: ct);
        return (id, null);
    }

    // ================================================================== Fotos desde puntos fijos

    private async Task<IReadOnlyList<PuntoFotoDto>> PuntosAsync(AppDbContext db, int obraId, CancellationToken ct)
    {
        var puntos = await db.PuntosFoto.AsNoTracking().Where(p => p.ObraId == obraId && p.Activo).OrderBy(p => p.Orden).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Nombre, p.Indicacion, p.FrecuenciaDias,
                Ultima = db.Registros.Where(r => r.PuntoFotoId == p.Id).OrderByDescending(r => r.Fecha).Select(r => (DateTime?)r.Fecha).FirstOrDefault(),
                ConFoto = db.Medios.Any(m => m.Registro.PuntoFotoId == p.Id && m.Tipo == TipoMedio.Foto),
            })
            .ToListAsync(ct);
        return puntos.Select(p => new PuntoFotoDto(p.Id, p.Nombre, p.Indicacion, p.Ultima,
            p.Ultima is null || Ahora - p.Ultima.Value >= TimeSpan.FromDays(p.FrecuenciaDias) - TimeSpan.FromHours(12), p.ConFoto)).ToList();
    }

    /// <summary>Última foto de un punto fijo (para que el albañil encuadre igual). Solo de obras que puede ver.</summary>
    public async Task<(Stream Contenido, string ContentType)?> UltimaFotoPuntoAsync(int albanilId, int obraId, int puntoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is null) return null;
        var m = await db.Medios.AsNoTracking()
            .Where(x => x.Tipo == TipoMedio.Foto && x.Registro.PuntoFotoId == puntoId && x.Registro.ObraId == obraId)
            .OrderByDescending(x => x.Registro.Fecha).ThenByDescending(x => x.Id)
            .Select(x => new { x.Ruta, x.ContentType }).FirstOrDefaultAsync(ct);
        if (m is null) return null;
        var s = await almacen.AbrirAsync(m.Ruta, ct);
        return s is null ? null : (s, m.ContentType);
    }
}
