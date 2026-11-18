using System.Security.Cryptography;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Portal de seguimiento para el cliente: un link privado de solo lectura por obra (/seguimiento/{token}).
/// Muestra avance, etapas, curva, fotos fijas y las entradas de bitácora que el estudio marcó como visibles.
/// Nunca muestra nombres del personal, montos ni problemas internos.
/// </summary>
public class PortalCliente(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, AvanceObra avance)
{
    public async Task<string> ActivarAsync(int obraId, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Obras.Where(o => o.Id == obraId).ExecuteUpdateAsync(s => s.SetProperty(o => o.TokenPortal, token), ct);
        return token;
    }

    public async Task ApagarAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Obras.Where(o => o.Id == obraId).ExecuteUpdateAsync(s => s.SetProperty(o => o.TokenPortal, (string?)null), ct);
    }

    public sealed record FotoPortal(int MedioId, DateTime Fecha, bool EsVideo);
    public sealed record PuntoPortal(string Nombre, List<FotoPortal> Fotos);
    public sealed record EntradaPortal(DateTime Fecha, string Titulo, string? Descripcion, string? Etapa, List<FotoPortal> Medios);
    public sealed record EtapaPortal(string Nombre, EstadoEtapa Estado, DateOnly? Inicio, DateOnly? Fin);

    public sealed record Vista(string Obra, string? Localidad, EstadoObra Estado, int AvanceEtapas, decimal? AvanceFisico,
        DateOnly? EntregaEstimada, List<EtapaPortal> Etapas, AvanceObra.Curva? Curva, List<PuntoPortal> Puntos, List<EntradaPortal> Bitacora, DateTime? Actualizado);

    /// <summary>Null si el token no existe (o el portal se apagó).</summary>
    public async Task<Vista?> VistaAsync(string? token, CancellationToken ct = default)
    {
        if (!TokenValido(token)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.Obras.Where(x => x.TokenPortal == token).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        return id is int obraId ? await VistaObraAsync(obraId, ct) : null;
    }

    /// <summary>Lo mismo que ve el cliente, por obra (lo usa también la app del cliente, /mi-obra).</summary>
    public async Task<Vista?> VistaObraAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.Obras.AsNoTracking().Include(x => x.Etapas).SingleOrDefaultAsync(x => x.Id == obraId, ct);
        if (o is null) return null;

        var tareas = await db.Tareas.AsNoTracking().Where(t => t.ObraId == o.Id && t.Estado != EstadoTarea.Cancelada).ToListAsync(ct);
        decimal? fisico = null;
        if (tareas.Count > 0)
        {
            var pesos = CalculoTareas.Pesos(tareas);
            fisico = Math.Round(tareas.Sum(t => pesos[t.Id] * CalculoTareas.Fraccion(t)) * 100, 0);
        }

        var puntos = await db.PuntosFoto.AsNoTracking().Where(p => p.ObraId == o.Id && p.VisibleCliente).OrderBy(p => p.Orden).ThenBy(p => p.Id).ToListAsync(ct);
        var idsPuntos = puntos.Select(p => p.Id).ToList();
        var fotosPuntos = await db.Medios.AsNoTracking()
            .Where(m => m.Registro.ObraId == o.Id && m.Registro.PuntoFotoId != null && idsPuntos.Contains(m.Registro.PuntoFotoId.Value) && m.Tipo == TipoMedio.Foto)
            .Select(m => new { Punto = m.Registro.PuntoFotoId!.Value, m.Id, m.Registro.Fecha })
            .ToListAsync(ct);

        var entradas = (await db.Registros.AsNoTracking().Include(r => r.Etapa).Include(r => r.Medios)
                .Where(r => r.ObraId == o.Id && r.VisibleCliente && r.PuntoFotoId == null)
                .OrderByDescending(r => r.Fecha).Take(40)
                .AsSplitQuery().ToListAsync(ct))
            .Select(r => new
            {
                r.Fecha, r.Titulo, r.Descripcion, Etapa = r.Etapa?.Nombre,
                Medios = r.Medios.OrderBy(m => m.Id).Select(m => new FotoPortal(m.Id, r.Fecha, m.Tipo == TipoMedio.Video)).ToList(),
            })
            .ToList();

        var ultima = await db.Registros.Where(r => r.ObraId == o.Id).MaxAsync(r => (DateTime?)r.Fecha, ct);
        return new Vista(o.Nombre, o.Localidad, o.Estado, o.PorcentajeAvance, fisico, o.FechaEntregaEstimada,
            o.Etapas.Where(e => e.Estado != EstadoEtapa.Omitida).OrderBy(e => e.Orden).Select(e => new EtapaPortal(e.Nombre, e.Estado, e.FechaInicio, e.FechaFin)).ToList(),
            tareas.Count > 0 ? await avance.CurvaAsync(o.Id, ct) : null,
            puntos.Select(p => new PuntoPortal(p.Nombre, fotosPuntos.Where(f => f.Punto == p.Id).OrderBy(f => f.Fecha).Select(f => new FotoPortal(f.Id, f.Fecha, false)).ToList()))
                .Where(p => p.Fotos.Count > 0).ToList(),
            entradas.Select(e => new EntradaPortal(e.Fecha, e.Titulo, e.Descripcion, e.Etapa, e.Medios)).ToList(),
            ultima);
    }

    /// <summary>Archivo visible en el portal de esa obra (bitácora visible o foto fija visible); si no, null.</summary>
    public async Task<(Stream Contenido, string ContentType)?> MedioAsync(string? token, int medioId, CancellationToken ct = default)
    {
        if (!TokenValido(token)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obraId = await db.Obras.Where(x => x.TokenPortal == token).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        return obraId is int id ? await MedioObraAsync(id, medioId, ct) : null;
    }

    /// <summary>Archivo visible para el cliente de esa obra (bitácora visible o foto fija visible); si no, null.</summary>
    public async Task<(Stream Contenido, string ContentType)?> MedioObraAsync(int obraId, int medioId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.Medios.AsNoTracking()
            .Where(x => x.Id == medioId && x.Registro.ObraId == obraId
                && (x.Registro.VisibleCliente || (x.Registro.PuntoFoto != null && x.Registro.PuntoFoto.VisibleCliente)))
            .Select(x => new { x.Ruta, x.ContentType }).SingleOrDefaultAsync(ct);
        if (m is null) return null;
        var s = await almacen.AbrirAsync(m.Ruta, ct);
        return s is null ? null : (s, m.ContentType);
    }

    private static bool TokenValido(string? token) =>
        token is { Length: >= 40 and <= 64 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
