using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Fotos y PDF adjuntos (relevamientos, gastos, facturas, entrega, reclamos, pagos informados).</summary>
public class Adjuntos(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen)
{
    public const long TamanoMaximo = 15 * 1024 * 1024;
    public const int MaximoPorEnvio = 5;
    public const string Acepta = ".jpg,.jpeg,.png,.webp,.heic,.pdf";
    public const string ErrorFormato = "Subí fotos (jpg, png, webp, heic) o PDF de hasta 15 MB.";

    private static readonly Dictionary<string, string> Formatos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".heic"] = "image/heic", [".pdf"] = "application/pdf",
    };

    public static bool TryFormato(string? nombre, out string contentType) =>
        Formatos.TryGetValue(Path.GetExtension(nombre ?? ""), out contentType!);

    public sealed record Archivo(string Nombre, long Tamano, Func<Stream> Abrir);

    /// <summary>Error si alguno no sirve (antes de guardar nada).</summary>
    public static string? Validar(IReadOnlyCollection<Archivo> archivos)
    {
        if (archivos.Count > MaximoPorEnvio) return $"Máximo {MaximoPorEnvio} archivos por vez.";
        foreach (var a in archivos)
            if (!TryFormato(a.Nombre, out _) || a.Tamano <= 0 || a.Tamano > TamanoMaximo) return ErrorFormato;
        return null;
    }

    /// <summary>Guarda los archivos en el almacenamiento y los agrega al contexto (el que llama hace SaveChanges).</summary>
    public async Task<List<Adjunto>> GuardarAsync(AppDbContext db, int? obraId, string carpeta, IEnumerable<Archivo> archivos,
        Action<Adjunto> asignar, bool esResolucion = false, CancellationToken ct = default)
    {
        var lista = new List<Adjunto>();
        foreach (var a in archivos)
        {
            TryFormato(a.Nombre, out var contentType);
            var ruta = $"{(obraId is int o ? $"obra-{o}" : "sin-obra")}/{carpeta}/{DateTime.UtcNow:yyyy}/{Guid.NewGuid():N}{Path.GetExtension(a.Nombre).ToLowerInvariant()}";
            await using (var s = a.Abrir())
                await almacen.GuardarAsync(ruta, s, contentType, ct);
            var nombre = Path.GetFileName(a.Nombre);
            var adj = new Adjunto
            {
                ObraId = obraId, Ruta = ruta, ContentType = contentType, Tamano = a.Tamano, EsResolucion = esResolucion,
                NombreOriginal = nombre.Length > 260 ? nombre[..260] : nombre,
            };
            asignar(adj);
            db.Adjuntos.Add(adj);
            lista.Add(adj);
        }
        return lista;
    }

    /// <summary>Agrega adjuntos a algo que ya existe (desde el panel).</summary>
    public async Task<string?> AgregarAsync(int? obraId, string carpeta, IReadOnlyCollection<Archivo> archivos, Action<Adjunto> asignar,
        bool esResolucion = false, CancellationToken ct = default)
    {
        if (Validar(archivos) is { } error) return error;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await GuardarAsync(db, obraId, carpeta, archivos, asignar, esResolucion, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task BorrarAsync(int adjuntoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Adjuntos.SingleOrDefaultAsync(x => x.Id == adjuntoId, ct);
        if (a is null) return;
        db.Adjuntos.Remove(a);
        await db.SaveChangesAsync(ct);
        await almacen.BorrarAsync(a.Ruta, ct);
    }

    /// <summary>Borra los archivos del almacenamiento de adjuntos ya quitados de la base.</summary>
    public async Task BorrarArchivosAsync(IEnumerable<string> rutas, CancellationToken ct = default)
    {
        foreach (var r in rutas) await almacen.BorrarAsync(r, ct);
    }

    /// <summary>Desde el panel: cualquier adjunto.</summary>
    public Task<(Stream Contenido, string ContentType, string Nombre)?> AbrirAsync(int adjuntoId, CancellationToken ct = default) =>
        AbrirAsync(adjuntoId, null, ct);

    /// <summary>
    /// Para la app del cliente (<paramref name="obraCliente"/>): solo adjuntos de su obra que él puede ver
    /// (sus reclamos y pagos informados, y las observaciones de un acta publicada).
    /// </summary>
    public async Task<(Stream Contenido, string ContentType, string Nombre)?> AbrirAsync(int adjuntoId, int? obraCliente, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Adjuntos.AsNoTracking().Where(a => a.Id == adjuntoId);
        if (obraCliente is int obra)
            q = q.Where(a => a.ObraId == obra && (a.ReclamoGarantiaId != null || a.PagoInformadoId != null
                || (a.ObservacionEntregaId != null && a.Observacion!.Acta.Estado != EstadoActa.Borrador)));
        var adj = await q.SingleOrDefaultAsync(ct);
        if (adj is null) return null;
        var s = await almacen.AbrirAsync(adj.Ruta, ct);
        return s is null ? null : (s, adj.ContentType, adj.NombreOriginal ?? $"adjunto-{adj.Id}{Path.GetExtension(adj.Ruta)}");
    }

    public static bool EsImagen(Adjunto a) => a.ContentType.StartsWith("image/", StringComparison.Ordinal);
}
