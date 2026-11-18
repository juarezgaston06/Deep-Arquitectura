using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>Fotos y videos que mandan las apps de obra (capataz y albañiles): formatos admitidos y guardado.</summary>
public static class FormatosMedio
{
    // Extensiones admitidas → (tipo, content type). El content type del teléfono no siempre es confiable.
    private static readonly Dictionary<string, (TipoMedio Tipo, string ContentType)> Formatos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = (TipoMedio.Foto, "image/jpeg"), [".jpeg"] = (TipoMedio.Foto, "image/jpeg"),
        [".png"] = (TipoMedio.Foto, "image/png"), [".webp"] = (TipoMedio.Foto, "image/webp"),
        [".heic"] = (TipoMedio.Foto, "image/heic"), [".heif"] = (TipoMedio.Foto, "image/heif"),
        [".mp4"] = (TipoMedio.Video, "video/mp4"), [".mov"] = (TipoMedio.Video, "video/quicktime"),
        [".3gp"] = (TipoMedio.Video, "video/3gpp"), [".webm"] = (TipoMedio.Video, "video/webm"),
    };

    public const string ErrorFormato = "Formato no admitido. Fotos: jpg, png, webp, heic. Videos: mp4, mov, 3gp, webm.";
    public const string ErrorTamano = "El archivo está vacío o supera los 500 MB.";

    public static bool Admitido(string? nombreArchivo) =>
        !string.IsNullOrEmpty(nombreArchivo) && Formatos.ContainsKey(Path.GetExtension(nombreArchivo));

    public static bool TryObtener(string? nombreArchivo, out (TipoMedio Tipo, string ContentType) formato) =>
        Formatos.TryGetValue(Path.GetExtension(nombreArchivo ?? ""), out formato);

    /// <summary>
    /// Guarda el archivo en el almacenamiento y lo adjunta a la entrada de bitácora. Idempotente por
    /// <paramref name="medioRef"/> (si dos reintentos llegan juntos, gana el primero).
    /// </summary>
    public static async Task<int> GuardarAsync(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen,
        int registroId, int obraId, Guid medioRef, (TipoMedio Tipo, string ContentType) formato,
        string nombreArchivo, long tamano, Func<Stream> abrir, DateTime ahora, CancellationToken ct)
    {
        var ext = Path.GetExtension(nombreArchivo).ToLowerInvariant();
        var ruta = $"obra-{obraId}/{ahora:yyyy}/{ahora:MM}/{medioRef:N}{ext}";
        await using (var s = abrir())
            await almacen.GuardarAsync(ruta, s, formato.ContentType, ct);

        var nombre = Path.GetFileName(nombreArchivo);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var medio = new MedioRegistro
        {
            RegistroAvanceId = registroId,
            Tipo = formato.Tipo,
            Ruta = ruta,
            ContentType = formato.ContentType,
            Tamano = tamano,
            NombreOriginal = string.IsNullOrEmpty(nombre) ? null : nombre[..Math.Min(nombre.Length, 260)],
            Ref = medioRef,
        };
        db.Medios.Add(medio);
        try
        {
            await db.SaveChangesAsync(ct);
            return medio.Id;
        }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.Medios.Where(m => m.Ref == medioRef).Select(m => (int?)m.Id).SingleOrDefaultAsync(ct);
            return id ?? throw new InvalidOperationException("No se pudo guardar el archivo.");
        }
    }

    public static bool TamanoValido(long tamano) => tamano > 0 && tamano <= RutasApp.TamanoMaximoMedio;
}
