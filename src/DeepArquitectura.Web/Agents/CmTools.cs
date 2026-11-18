using System.ComponentModel;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Agents;

/// <summary>
/// Herramientas que el agente CM puede invocar. Regla de privacidad: nunca exponen nombre,
/// teléfono, email, DNI ni dirección exacta de clientes — solo localidad, tipo de trabajo y avances.
/// </summary>
public class CmTools(IDbContextFactory<AppDbContext> dbFactory)
{
    public record ObraResumen(int ObraId, string Codigo, string Servicio, string Estado, string? Localidad, int PorcentajeAvance, string? EtapaActual,
        string PermisoFotos = "sin respuesta");
    public record Testimonio(int ObraId, string Servicio, string? Localidad, string Texto, string Firma, int? Puntaje);

    /// <summary>Obras cuyo cliente dijo explícitamente que NO se usen fotos (las demás quedan a criterio del estudio).</summary>
    private static Task<List<int>> ObrasSinPermisoAsync(AppDbContext db) =>
        db.Permisos.Where(p => !p.UsarFotos).Select(p => p.ObraId).ToListAsync();

    [Description("Testimonios reales que los clientes escribieron y autorizaron publicar desde su app. Firma = nombre (solo si lo autorizó) o 'Cliente de <localidad>'. Nunca inventes otros.")]
    public async Task<List<Testimonio>> ListarTestimonios()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Permisos.AsNoTracking().Where(p => p.Testimonio != null)
            .Select(p => new { p.ObraId, p.Obra.Servicio, p.Obra.Localidad, p.Testimonio, p.MostrarNombre, p.Nombre }).ToListAsync();
        var ids = t.Select(x => x.ObraId).ToList();
        var puntajes = await db.Encuestas.Where(e => ids.Contains(e.ObraId) && e.Momento == MomentoEncuesta.Entrega && e.Puntaje != null)
            .ToDictionaryAsync(e => e.ObraId, e => e.Puntaje);
        return t.Select(x => new Testimonio(x.ObraId, Textos.Etiqueta(x.Servicio), x.Localidad, x.Testimonio!,
            x.MostrarNombre && x.Nombre is not null ? x.Nombre : $"Cliente de {x.Localidad ?? "Córdoba"}", puntajes.GetValueOrDefault(x.ObraId))).ToList();
    }
    public record NovedadObra(int ObraId, string Fecha, string Titulo, string? Descripcion, string? Etapa);
    public record SerieFotosFijas(int ObraId, string Punto, int Fotos, string Primera, string Ultima, int DiasEntrePrimeraYUltima);

    [Description("Series de fotos tomadas siempre desde el mismo encuadre (puntos fijos) en cada obra: sirven para antes/después, time-lapse y reels de evolución. Sin datos personales.")]
    public async Task<List<SerieFotosFijas>> ListarFotosFijas(
        [Description("Opcional: limitar a una obra puntual.")] int? obraId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sinPermiso = await ObrasSinPermisoAsync(db);
        var fotos = await db.Medios.AsNoTracking()
            .Where(m => m.Tipo == TipoMedio.Foto && m.Registro.PuntoFotoId != null && (obraId == null || m.Registro.ObraId == obraId)
                && !sinPermiso.Contains(m.Registro.ObraId))
            .Select(m => new { m.Registro.ObraId, Punto = m.Registro.PuntoFoto!.Nombre, PuntoId = m.Registro.PuntoFotoId!.Value, m.Registro.Fecha })
            .ToListAsync();
        return fotos.GroupBy(f => (f.ObraId, f.PuntoId, f.Punto))
            .Where(g => g.Count() >= 2)
            .Select(g => new SerieFotosFijas(g.Key.ObraId, g.Key.Punto, g.Count(),
                g.Min(f => f.Fecha).ToString("yyyy-MM-dd"), g.Max(f => f.Fecha).ToString("yyyy-MM-dd"),
                (int)(g.Max(f => f.Fecha) - g.Min(f => f.Fecha)).TotalDays))
            .OrderByDescending(s => s.DiasEntrePrimeraYUltima).ToList();
    }

    public record PublicacionResumen(int Id, string Titulo, string Red, string Formato, string Pilar, string Estado, string? FechaProgramada);

    [Description("Lista las obras activas o finalizadas recientemente (sin datos personales de clientes), con avance y etapa actual. Útil para elegir de qué obra hacer contenido.")]
    public async Task<List<ObraResumen>> ListarObras(
        [Description("Si es true, incluye también obras finalizadas en los últimos 90 días.")] bool incluirFinalizadas = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var desde = DateTime.UtcNow.AddDays(-90);
        var obras = await db.Obras.AsNoTracking()
            .Include(o => o.Etapas)
            .Where(o => o.Estado == EstadoObra.EnCurso || o.Estado == EstadoObra.EnTramiteMunicipal
                || (incluirFinalizadas && o.Estado == EstadoObra.Finalizada && o.ActualizadoEn >= desde))
            .OrderByDescending(o => o.ActualizadoEn)
            .Take(30)
            .ToListAsync();

        var ids = obras.Select(o => o.Id).ToList();
        var permisos = await db.Permisos.AsNoTracking().Where(p => ids.Contains(p.ObraId)).ToDictionaryAsync(p => p.ObraId, p => p.UsarFotos);
        return obras.Select(o => new ObraResumen(
            o.Id, o.Codigo, Textos.Etiqueta(o.Servicio), Textos.Etiqueta(o.Estado), o.Localidad, o.PorcentajeAvance,
            o.Etapas.OrderBy(e => e.Orden).FirstOrDefault(e => e.Estado == EstadoEtapa.EnCurso)?.Nombre,
            permisos.TryGetValue(o.Id, out var si) ? (si ? "sí" : "no") : "sin respuesta")).ToList();
    }

    [Description("Devuelve las novedades de la bitácora de obra marcadas como aptas para redes, de los últimos N días. Es la materia prima para posts de 'Obra y proceso'.")]
    public async Task<List<NovedadObra>> ListarNovedadesParaRedes(
        [Description("Cantidad de días hacia atrás (1 a 120).")] int dias = 30,
        [Description("Opcional: limitar a una obra puntual.")] int? obraId = null)
    {
        dias = Math.Clamp(dias, 1, 120);
        await using var db = await dbFactory.CreateDbContextAsync();
        var desde = DateTime.UtcNow.AddDays(-dias);
        var sinPermiso = await ObrasSinPermisoAsync(db);
        var q = db.Registros.AsNoTracking().Include(r => r.Etapa)
            .Where(r => r.UsableEnRedes && r.Fecha >= desde && !sinPermiso.Contains(r.ObraId));
        if (obraId is int id) q = q.Where(r => r.ObraId == id);

        var registros = await q.OrderByDescending(r => r.Fecha).Take(40).ToListAsync();
        return registros.Select(r => new NovedadObra(r.ObraId, Textos.Fecha(r.Fecha), r.Titulo, r.Descripcion, r.Etapa?.Nombre)).ToList();
    }

    [Description("Lista las publicaciones ya creadas (borradores, aprobadas y publicadas) para no repetir temas.")]
    public async Task<List<PublicacionResumen>> ListarPublicacionesRecientes(
        [Description("Cantidad máxima a devolver (1 a 50).")] int cantidad = 20)
    {
        cantidad = Math.Clamp(cantidad, 1, 50);
        await using var db = await dbFactory.CreateDbContextAsync();
        var pubs = await db.Publicaciones.AsNoTracking()
            .Where(p => p.Estado != EstadoPublicacion.Descartada)
            .OrderByDescending(p => p.CreadoEn).Take(cantidad).ToListAsync();
        return pubs.Select(p => new PublicacionResumen(p.Id, p.Titulo, p.Red.ToString(), p.Formato.ToString(),
            p.Pilar.ToString(), p.Estado.ToString(), p.FechaProgramada?.ToString("yyyy-MM-dd"))).ToList();
    }

    [Description("Guarda una pieza de contenido como BORRADOR para que el profesional la revise y apruebe. Llamala una vez por pieza. Nunca publica nada en redes.")]
    public async Task<string> GuardarBorrador(
        [Description("Título interno corto.")] string titulo,
        [Description("Red principal: Instagram, TikTok, Facebook o Multired.")] RedSocial red,
        [Description("Formato: Reel, Carrusel, Post, Historia o VideoTikTok.")] FormatoPublicacion formato,
        [Description("Pilar: Educativo, Obra, Seguridad, DetrasDeEscena o PruebaSocial.")] PilarContenido pilar,
        [Description("Pieza completa en Markdown: gancho, guion o slides, qué filmar, captions por red, hashtags y CTA.")] string contenido,
        [Description("Fecha sugerida de publicación en formato yyyy-MM-dd (opcional).")] string? fechaProgramada = null,
        [Description("Id de la obra en la que se basa, si corresponde.")] int? obraId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (obraId is int id && !await db.Obras.AnyAsync(o => o.Id == id)) obraId = null;

        var pub = new Publicacion
        {
            Titulo = titulo.Length > 200 ? titulo[..200] : titulo,
            Red = red,
            Formato = formato,
            Pilar = pilar,
            Contenido = contenido,
            FechaProgramada = DateOnly.TryParse(fechaProgramada, out var f) ? f : null,
            ObraId = obraId,
            GeneradaPorIA = true,
        };
        db.Publicaciones.Add(pub);
        await db.SaveChangesAsync();
        return $"Borrador #{pub.Id} guardado.";
    }
}
