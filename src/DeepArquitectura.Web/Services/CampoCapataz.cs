using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Lo que el capataz hace desde su app (táctil, sin IA): validar lo que informa la cuadrilla, anotar por qué se atrasa
/// una tarea, cargar gastos con su comprobante y sobrantes, notas de pedido y confirmar órdenes del libro de obra.
/// Todo viene de la cola offline del teléfono: cada operación es idempotente (Ref, o volver a aplicar deja lo mismo).
/// Siempre verifica que la obra esté asignada a ese capataz.
/// </summary>
public class CampoCapataz(IDbContextFactory<AppDbContext> dbFactory, CapatazService capataces, ValidacionAvances validacion,
    LibroObra libro, CostosObra costos, IAlmacenMedios almacen, TimeProvider? reloj = null)
{
    public const int MaximoValidacionesPorEnvio = 200;

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateOnly Hoy => DateOnly.FromDateTime(Textos.AHoraLocal(_reloj.GetUtcNow().UtcDateTime));

    /// <summary>La obra con lo que el capataz tiene que atender: avances por validar, órdenes sin leer y tareas atrasadas.</summary>
    public async Task<ObraDetalleDto?> ObraAsync(int capatazId, int obraId, CancellationToken ct = default)
    {
        var obra = await capataces.ObraAsync(capatazId, obraId, ct);
        if (obra is null) return null;

        var pendientes = await validacion.PendientesAsync(obraId, capatazId, ct);
        var ids = pendientes.Select(p => p.RegistroId).ToList();
        Dictionary<int, List<int>> fotos;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            fotos = (await db.Medios.AsNoTracking()
                    .Where(m => ids.Contains(m.RegistroAvanceId) && m.Tipo == TipoMedio.Foto)
                    .OrderBy(m => m.Id).Select(m => new { m.RegistroAvanceId, m.Id }).ToListAsync(ct))
                .GroupBy(m => m.RegistroAvanceId).ToDictionary(g => g.Key, g => g.Select(m => m.Id).Take(10).ToList());

        var porValidar = pendientes.Select(p => new AvancePorValidarDto(p.RegistroId, p.Albanil, p.TareaId, p.Tarea, p.Unidad,
            p.Cantidad, p.Porcentaje, p.Terminada, p.Fecha, p.Nota, p.Fotos, p.Videos, p.CantidadObjetivo, p.CantidadAprobadaTarea,
            fotos.GetValueOrDefault(p.RegistroId) ?? [])).ToList();

        var ordenes = (await libro.OrdenesSinLeerCapatazAsync(capatazId, obraId, ct))
            .Select(a => new OrdenDto(a.Id, a.Numero, a.Fecha, a.Texto, false)).ToList();

        var atrasadas = await costos.AtrasadasDeObraAsync(obraId, ct);
        var sugerencias = await costos.SugerenciasAsync(atrasadas, ct);
        Dictionary<int, string> etapas;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var etapaIds = atrasadas.Where(t => t.EtapaObraId != null).Select(t => t.EtapaObraId!.Value).Distinct().ToList();
            etapas = await db.Etapas.AsNoTracking().Where(e => etapaIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);
        }
        var listaAtrasadas = atrasadas.Select(t =>
        {
            var s = sugerencias.GetValueOrDefault(t.Id);
            return new TareaAtrasadaDto(t.Id, t.Titulo, t.EtapaObraId is int e ? etapas.GetValueOrDefault(e) : null, t.FechaLimite,
                t.PorcentajeAprobado, t.CausaAtraso?.ToString(), t.DetalleAtraso, s?.Causa.ToString(), s?.Motivo);
        }).ToList();

        return obra with { PorValidar = porValidar, Ordenes = ordenes, Atrasadas = listaAtrasadas };
    }

    /// <summary>
    /// Aplica una o varias decisiones. Sigue con las demás si una falla; devuelve el primer error (las que se aplicaron,
    /// quedan: volver a mandarlas deja lo mismo).
    /// </summary>
    public async Task<string?> ValidarAsync(int capatazId, int obraId, ValidacionesRequest r, CancellationToken ct = default)
    {
        if (r.Items is not { Count: > 0 }) return "No hay nada para validar.";
        if (r.Items.Count > MaximoValidacionesPorEnvio) return "Demasiados avances juntos.";
        if (!await capataces.PuedeVerObraAsync(capatazId, obraId, ct)) return "Obra no asignada.";

        List<int> deLaObra;
        var ids = r.Items.Select(i => i.RegistroId).Distinct().ToList();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            deLaObra = await db.Registros.Where(x => ids.Contains(x.Id) && x.ObraId == obraId).Select(x => x.Id).ToListAsync(ct);

        string? primerError = null;
        foreach (var item in r.Items)
        {
            EstadoValidacion? decision = item.Decision?.Trim().ToLowerInvariant() switch
            {
                ValoresCapataz.Aprobar => EstadoValidacion.Aprobado,
                ValoresCapataz.Corregir => EstadoValidacion.Corregido,
                ValoresCapataz.Rechazar => EstadoValidacion.Rechazado,
                _ => null,
            };
            var error = decision is null ? "Decisión inválida."
                : !deLaObra.Contains(item.RegistroId) ? "Ese avance no es de esta obra."
                : await validacion.ValidarAsync(item.RegistroId, decision.Value, item.Cantidad, item.Porcentaje, item.Motivo, capatazId, ct);
            primerError ??= error;
        }
        return primerError;
    }

    public async Task<string?> InformarAtrasoAsync(int capatazId, int obraId, AtrasoRequest r, CancellationToken ct = default)
    {
        if (!await capataces.PuedeVerObraAsync(capatazId, obraId, ct)) return "Obra no asignada.";
        if (!Enum.TryParse<CausaAtraso>(r.Causa, ignoreCase: true, out var causa) || !Enum.IsDefined(causa)) return "Elegí la causa del atraso.";
        return await costos.InformarAtrasoAsync(r.TareaId, causa, r.Detalle, obraId, ct);
    }

    public async Task<(int? Id, string? Error)> GastoAsync(int capatazId, int obraId, NuevoGastoRequest r,
        IReadOnlyCollection<Adjuntos.Archivo> comprobantes, CancellationToken ct = default)
    {
        if (!Enum.TryParse<CategoriaGasto>(r.Categoria, ignoreCase: true, out var categoria) || !Enum.IsDefined(categoria))
            categoria = CategoriaGasto.Otro;
        var d = new CostosObra.DatosGasto
        {
            Ref = r.Ref, Fecha = FechaLocal(r.FechaUtc), Categoria = categoria, Descripcion = r.Descripcion, Proveedor = r.Proveedor,
            NumeroComprobante = r.NumeroComprobante, Monto = r.Monto, EtapaObraId = r.EtapaId,
        };
        return await costos.GastoCapatazAsync(capatazId, obraId, d, comprobantes, ct);
    }

    public async Task<(int? Id, string? Error)> SobranteAsync(int capatazId, int obraId, NuevoSobranteRequest r, CancellationToken ct = default)
    {
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (!await capataces.PuedeVerObraAsync(capatazId, obraId, ct)) return (null, "Obra no asignada.");
        if (!Enum.TryParse<DestinoSobrante>(r.Destino, ignoreCase: true, out var destino) || !Enum.IsDefined(destino)) return (null, "Elegí qué se hizo con el material.");
        // A otra obra solo puede llevarlo a una de las suyas.
        if (destino == DestinoSobrante.OtraObra && (r.ObraDestinoId is not int od || !await capataces.PuedeVerObraAsync(capatazId, od, ct)))
            return (null, "Elegí a cuál de tus obras se llevó.");

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var previo = await db.Sobrantes.Where(s => s.Ref == r.Ref).Select(s => new { s.Id, s.CapatazId, s.ObraId }).SingleOrDefaultAsync(ct);
            if (previo is not null) return previo.CapatazId == capatazId && previo.ObraId == obraId ? (previo.Id, null) : (null, "Envío duplicado.");
        }
        var d = new CostosObra.DatosSobrante
        {
            Ref = r.Ref, Fecha = FechaLocal(r.FechaUtc), Material = r.Material, Cantidad = r.Cantidad, Unidad = r.Unidad,
            Destino = destino, ObraDestinoId = destino == DestinoSobrante.OtraObra ? r.ObraDestinoId : null, Nota = r.Nota,
        };
        try
        {
            return await costos.RegistrarSobranteAsync(obraId, d, capatazId, ct);
        }
        catch (DbUpdateException)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.Sobrantes.Where(s => s.Ref == r.Ref && s.CapatazId == capatazId).Select(s => (int?)s.Id).SingleOrDefaultAsync(ct) is int id
                ? (id, null) : throw new InvalidOperationException("No se pudo guardar el sobrante.");
        }
    }

    public Task<(int? Id, string? Error)> NotaPedidoAsync(int capatazId, int obraId, NotaPedidoRequest r, CancellationToken ct = default) =>
        libro.NotaPedidoCapatazAsync(capatazId, obraId, r.Ref, r.Texto, r.FechaUtc, ct);

    public async Task<string?> MarcarLeidoAsync(int capatazId, int obraId, LeidoRequest r, CancellationToken ct = default)
    {
        if (!await capataces.PuedeVerObraAsync(capatazId, obraId, ct)) return "Obra no asignada.";
        await libro.MarcarLeidasCapatazAsync(capatazId, obraId, r.Ordenes ?? [], ct);
        return null;
    }

    /// <summary>Foto de un avance de la cuadrilla, solo si es de una obra del capataz.</summary>
    public async Task<(Stream Contenido, string ContentType)?> AbrirFotoAsync(int capatazId, int medioId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.Medios.AsNoTracking()
            .Where(x => x.Id == medioId && x.Tipo == TipoMedio.Foto
                && db.CapatazObras.Any(c => c.CapatazId == capatazId && c.ObraId == x.Registro.ObraId))
            .Select(x => new { x.Ruta, x.ContentType }).SingleOrDefaultAsync(ct);
        if (m is null) return null;
        var s = await almacen.AbrirAsync(m.Ruta, ct);
        return s is null ? null : (s, m.ContentType);
    }

    /// <summary>Día del gasto o sobrante según el teléfono (hora de Córdoba), sin aceptar fechas futuras ni muy viejas.</summary>
    private DateOnly FechaLocal(DateTime fechaUtc)
    {
        var utc = fechaUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc) : fechaUtc.ToUniversalTime();
        var dia = DateOnly.FromDateTime(Textos.AHoraLocal(utc));
        return dia > Hoy || dia < Hoy.AddDays(-60) ? Hoy : dia;
    }
}
