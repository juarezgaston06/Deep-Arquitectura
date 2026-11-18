using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Visita de relevamiento: la ficha que se llena en el celular al ver el terreno o la casa por primera vez.</summary>
public class Relevamientos(IDbContextFactory<AppDbContext> dbFactory, Adjuntos adjuntos)
{
    public static readonly string[] Estados = ["Terreno baldío", "Bueno", "Regular", "Malo", "Para demoler"];

    public static readonly string[] HallazgosSugeridos =
    [
        "Humedad", "Fisuras / grietas", "Instalación eléctrica precaria", "Instalación de gas sin habilitar", "Sin planos aprobados",
        "Construcción sin declarar", "Medianera compartida", "Desnivel del terreno", "Árboles a extraer", "Napa alta / terreno bajo",
        "Acceso difícil para materiales", "Techo con filtraciones",
    ];

    public async Task<List<Relevamiento>> ListaAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Relevamientos.AsNoTracking().Include(r => r.Cliente).Include(r => r.Obra)
            .OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id).Take(200).ToListAsync(ct);
    }

    public async Task<Relevamiento?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Relevamientos.AsNoTracking().Include(r => r.Adjuntos).Include(r => r.Cliente).Include(r => r.Obra)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>Nuevo relevamiento con los datos que ya se conocen (de la consulta, el cliente o la obra).</summary>
    public async Task<Relevamiento> NuevoAsync(int? consultaId, int? clienteId, int? obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = new Relevamiento { Fecha = Textos.HoyLocal() };
        if (obraId is int o && await db.Obras.AsNoTracking().SingleOrDefaultAsync(x => x.Id == o, ct) is { } obra)
        {
            r.ObraId = obra.Id; r.ClienteId = obra.ClienteId; r.Direccion = obra.Direccion; r.Localidad = obra.Localidad;
            r.Latitud = obra.Latitud; r.Longitud = obra.Longitud;
        }
        if (clienteId is int c && r.ClienteId is null && await db.Clientes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c, ct) is { } cli)
        {
            r.ClienteId = cli.Id; r.Direccion ??= cli.Direccion; r.Localidad ??= cli.Localidad;
        }
        if (consultaId is int q && await db.Consultas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == q, ct) is { } cons)
        {
            r.ConsultaId = cons.Id; r.ClienteId ??= cons.ClienteId; r.Contacto = cons.Nombre; r.Pedido = cons.Mensaje;
        }
        return r;
    }

    public async Task<(int? Id, string? Error)> GuardarAsync(Relevamiento d, CancellationToken ct = default)
    {
        if (d.ClienteId is null && d.ObraId is null && string.IsNullOrWhiteSpace(d.Contacto)) return (null, "Indicá el cliente o el nombre del interesado.");
        if (d.FrenteM is < 0 || d.FondoM is < 0 || d.SuperficieTerrenoM2 is < 0 || d.SuperficieCubiertaM2 is < 0 || d.Plantas is < 0 or > 50
            || d.AntiguedadAnios is < 0 or > 300 || d.PresupuestoCliente is < 0)
            return (null, "Revisá las medidas: hay valores negativos o fuera de rango.");
        if (d.Latitud is < -90 or > 90 || d.Longitud is < -180 or > 180) return (null, "Ubicación inválida.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Relevamiento r;
        if (d.Id == 0) db.Relevamientos.Add(r = new Relevamiento());
        else if (await db.Relevamientos.SingleOrDefaultAsync(x => x.Id == d.Id, ct) is { } existente) r = existente;
        else return (null, "El relevamiento no existe.");

        if (d.ObraId is int obraId)
        {
            var clienteObra = await db.Obras.Where(o => o.Id == obraId).Select(o => (int?)o.ClienteId).SingleOrDefaultAsync(ct);
            if (clienteObra is null) return (null, "La obra no existe.");
            d.ClienteId = clienteObra;
        }
        r.ClienteId = d.ClienteId; r.ConsultaId = d.ConsultaId; r.ObraId = d.ObraId;
        r.Contacto = Texto(d.Contacto, 200);
        r.Fecha = d.Fecha == default ? Textos.HoyLocal() : d.Fecha;
        r.Direccion = Texto(d.Direccion, 300); r.Localidad = Texto(d.Localidad, 120);
        r.Latitud = d.Latitud; r.Longitud = d.Longitud;
        r.FrenteM = d.FrenteM; r.FondoM = d.FondoM;
        // Si no cargó la superficie pero sí frente y fondo, la calculamos.
        r.SuperficieTerrenoM2 = d.SuperficieTerrenoM2 ?? (d.FrenteM is > 0 && d.FondoM is > 0 ? Math.Round(d.FrenteM.Value * d.FondoM.Value, 2) : null);
        r.SuperficieCubiertaM2 = d.SuperficieCubiertaM2; r.Plantas = d.Plantas; r.AntiguedadAnios = d.AntiguedadAnios;
        r.EstadoGeneral = Texto(d.EstadoGeneral, 40);
        r.Agua = d.Agua; r.Luz = d.Luz; r.Gas = d.Gas; r.Cloaca = d.Cloaca;
        r.Hallazgos = Texto(d.Hallazgos, 1000);
        r.Pedido = Texto(d.Pedido, 4000); r.PresupuestoCliente = d.PresupuestoCliente; r.PlazoDeseado = Texto(d.PlazoDeseado, 200);
        r.Notas = Texto(d.Notas, 4000);
        await db.SaveChangesAsync(ct);

        // La obra toma la ubicación si no la tenía (sirve para fichadas y clima).
        if (r.ObraId is int o && r.Latitud is not null && r.Longitud is not null)
            await db.Obras.Where(x => x.Id == o && x.Latitud == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Latitud, r.Latitud).SetProperty(x => x.Longitud, r.Longitud), ct);
        return (r.Id, null);
    }

    public async Task<string?> AgregarFotosAsync(int relevamientoId, IReadOnlyCollection<Adjuntos.Archivo> archivos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Relevamientos.AsNoTracking().Where(x => x.Id == relevamientoId).Select(x => new { x.ObraId }).SingleOrDefaultAsync(ct);
        if (r is null) return "El relevamiento no existe.";
        return await adjuntos.AgregarAsync(r.ObraId, "relevamiento", archivos, a => a.RelevamientoId = relevamientoId, ct: ct);
    }

    public async Task BorrarAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Relevamientos.Include(x => x.Adjuntos).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return;
        var rutas = r.Adjuntos.Select(a => a.Ruta).ToList();
        db.Relevamientos.Remove(r);
        await db.SaveChangesAsync(ct);
        await adjuntos.BorrarArchivosAsync(rutas, ct);
    }

    public static IEnumerable<string> Hallazgos(Relevamiento r) =>
        (r.Hallazgos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string? Texto(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
