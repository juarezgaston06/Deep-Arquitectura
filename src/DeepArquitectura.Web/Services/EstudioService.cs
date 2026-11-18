using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Reglas de negocio que no son CRUD trivial: alta de consultas, conversión a cliente y alta de obras.</summary>
public class EstudioService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Primer contacto con el sitio que guarda main.js (utm del link y sitio de origen).</summary>
    public sealed record OrigenVisita(string? Fuente, string? Medio, string? Campania, string? Referente, string? Pagina);

    public async Task<int> RegistrarConsultaAsync(string nombre, string email, string? telefono, string mensaje, AreaConsulta area,
        OrigenVisita? origen = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consulta = new Consulta
        {
            Nombre = Recortar(nombre, 200),
            Email = Recortar(email, 200),
            Telefono = string.IsNullOrWhiteSpace(telefono) ? null : Recortar(telefono, 50),
            Mensaje = Recortar(mensaje, 4000),
            Area = area,
        };
        if (origen is not null)
        {
            consulta.UtmFuente = Opcional(origen.Fuente, 100)?.ToLowerInvariant();
            consulta.UtmMedio = Opcional(origen.Medio, 100)?.ToLowerInvariant();
            consulta.UtmCampania = Opcional(origen.Campania, 150);
            consulta.Referente = Host(origen.Referente);
            consulta.PaginaEntrada = Ruta(origen.Pagina);
            consulta.Canal = ClasificarCanal(consulta.UtmFuente, consulta.UtmMedio, consulta.Referente);
            if (IdPublicacion(consulta.UtmCampania) is int pub && await db.Publicaciones.AnyAsync(p => p.Id == pub, ct))
                consulta.PublicacionId = pub;
        }
        db.Consultas.Add(consulta);
        await db.SaveChangesAsync(ct);
        return consulta.Id;
    }

    /// <summary>Canal a partir de utm_source / utm_medium o, si no hay, del sitio de origen.</summary>
    public static OrigenCliente ClasificarCanal(string? fuente, string? medio, string? referente)
    {
        var f = $"{fuente} {referente}".ToLowerInvariant();
        if (f.Contains("instagram")) return OrigenCliente.Instagram;
        if (f.Contains("facebook") || f.Contains("fb.") || f.Contains("fbclid")) return OrigenCliente.Facebook;
        if (f.Contains("tiktok")) return OrigenCliente.TikTok;
        if (f.Contains("whatsapp") || f.Contains("wa.me")) return OrigenCliente.WhatsApp;
        if (f.Contains("cartel") || f.Contains("qr") || f.Contains("volante") || medio is "qr" or "cartel") return OrigenCliente.CartelObra;
        if (f.Contains("referido") || medio == "referido") return OrigenCliente.Referido;
        if (f.Contains("google") || f.Contains("bing") || f.Contains("duckduckgo") || f.Contains("yahoo") || medio is "cpc" or "organic")
            return OrigenCliente.Buscador;
        return OrigenCliente.Web;
    }

    /// <summary>utm_campaign = pub-{id}: la generó el panel de Redes para esa publicación.</summary>
    public static int? IdPublicacion(string? campania) =>
        campania is not null && campania.StartsWith("pub-", StringComparison.OrdinalIgnoreCase) && int.TryParse(campania[4..], out var id) && id > 0 ? id : null;

    /// <summary>Link de una publicación con los utm que después identifican las consultas que trajo.</summary>
    public static string LinkConSeguimiento(string sitio, RedSocial red, int publicacionId)
    {
        var fuente = red switch { RedSocial.Instagram => "instagram", RedSocial.TikTok => "tiktok", RedSocial.Facebook => "facebook", _ => "redes" };
        return $"{sitio.TrimEnd('/')}/?utm_source={fuente}&utm_medium=social&utm_campaign=pub-{publicacionId}";
    }

    private static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? Opcional(u.Host.ToLowerInvariant(), 200) : null;

    private static string? Ruta(string? pagina)
    {
        var p = Opcional(pagina, 300);
        return p is not null && p.StartsWith('/') ? p : null;
    }

    private static string? Opcional(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : Recortar(s, max);

    /// <summary>Cambia el estado de una consulta: guarda cuándo se contactó por primera vez y el motivo si se descarta.</summary>
    public async Task<string?> CambiarEstadoConsultaAsync(int consultaId, EstadoConsulta estado, MotivoPerdida? motivo = null, CancellationToken ct = default)
    {
        if (estado == EstadoConsulta.Descartada && motivo is null) return "Elegí por qué se descarta.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Consultas.SingleOrDefaultAsync(x => x.Id == consultaId, ct);
        if (c is null) return "La consulta no existe.";
        c.Estado = estado;
        if (estado is EstadoConsulta.Contactada or EstadoConsulta.Convertida) c.ContactadaEn ??= DateTime.UtcNow;
        c.MotivoDescarte = estado == EstadoConsulta.Descartada ? motivo : null;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task CambiarCanalAsync(int consultaId, OrigenCliente canal, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Consultas.Where(c => c.Id == consultaId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Canal, canal), ct);
        await db.Clientes.Where(c => c.Consultas.Any(q => q.Id == consultaId) && c.Origen == OrigenCliente.Web)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Origen, canal), ct);
    }

    /// <summary>
    /// Pasa una consulta a cliente. Si ya existe un cliente con el mismo email, la vincula a ese
    /// en vez de duplicarlo. Devuelve el Id del cliente.
    /// </summary>
    public async Task<int> ConvertirEnClienteAsync(int consultaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consulta = await db.Consultas.SingleAsync(c => c.Id == consultaId, ct);
        if (consulta.ClienteId is int yaVinculado) return yaVinculado;

        var email = consulta.Email.Trim();
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (cliente is null)
        {
            cliente = new Cliente
            {
                Nombre = consulta.Nombre,
                Email = email,
                Telefono = consulta.Telefono,
                Origen = consulta.Canal,
                Notas = $"Primera consulta ({Textos.Fecha(consulta.CreadoEn)}): {consulta.Mensaje}",
            };
            db.Clientes.Add(cliente);
        }

        consulta.Cliente = cliente;
        consulta.Estado = EstadoConsulta.Convertida;
        consulta.ContactadaEn ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return cliente.Id;
    }

    public async Task<Obra> CrearObraAsync(Obra obra, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        obra.Codigo = await SiguienteCodigoAsync(db, Textos.HoyLocal().Year, ct);
        if (obra.Etapas.Count == 0) obra.Etapas = PlantillasEtapas.Crear(obra.Servicio);
        db.Obras.Add(obra);
        await db.SaveChangesAsync(ct);
        return obra;
    }

    internal static async Task<string> SiguienteCodigoAsync(AppDbContext db, int anio, CancellationToken ct)
    {
        var prefijo = $"OB-{anio}-";
        var codigos = await db.Obras.Where(o => o.Codigo.StartsWith(prefijo)).Select(o => o.Codigo).ToListAsync(ct);
        var max = codigos.Select(c => int.TryParse(c[prefijo.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefijo}{max + 1:000}";
    }

    /// <summary>Cambia el estado de una etapa y completa fechas; si todas cerraron, la obra no se finaliza sola (lo decide el profesional).</summary>
    public async Task CambiarEstadoEtapaAsync(int etapaId, EstadoEtapa nuevo, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var etapa = await db.Etapas.Include(e => e.Obra).SingleAsync(e => e.Id == etapaId, ct);
        var hoy = Textos.HoyLocal();
        etapa.Estado = nuevo;
        if (nuevo == EstadoEtapa.EnCurso) etapa.FechaInicio ??= hoy;
        if (nuevo == EstadoEtapa.Completada) { etapa.FechaInicio ??= hoy; etapa.FechaFin = hoy; }
        if (nuevo == EstadoEtapa.Pendiente) etapa.FechaFin = null;

        // Arrancar una etapa pone la obra "en curso" si estaba solo presupuestada.
        if ((nuevo is EstadoEtapa.EnCurso or EstadoEtapa.Completada) && etapa.Obra.Estado == EstadoObra.Presupuestada)
        {
            etapa.Obra.Estado = EstadoObra.EnCurso;
            etapa.Obra.FechaInicio ??= hoy;
        }
        await db.SaveChangesAsync(ct);

        // Etapa terminada: encuesta corta en la app del cliente (si tiene acceso).
        if (nuevo == EstadoEtapa.Completada)
            await Postventa.AsegurarEncuestaAsync(db, etapa.ObraId, MomentoEncuesta.FinDeEtapa, etapa.Id, $"¿Cómo fue la etapa «{etapa.Nombre}»?", ct);
    }

    private static string Recortar(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
