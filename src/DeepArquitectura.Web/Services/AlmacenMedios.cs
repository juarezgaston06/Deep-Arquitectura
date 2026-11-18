using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DeepArquitectura.Web.Services;

public class MediosOptions
{
    public const string Seccion = "Medios";
    /// <summary>
    /// Producción: dirección del Blob Storage (https://&lt;cuenta&gt;.blob.core.windows.net). La app entra con su identidad
    /// administrada (sin clave guardada en ningún lado).
    /// </summary>
    public string? BlobUrl { get; set; }
    /// <summary>Alternativa con clave (cadena de conexión). Si están las dos, gana <see cref="BlobUrl"/>.</summary>
    public string? BlobConnectionString { get; set; }
    public string Contenedor { get; set; } = "medios-obra";
    /// <summary>Carpeta para desarrollo local. Relativa a la raíz de la app si no es absoluta.</summary>
    public string CarpetaLocal { get; set; } = "App_Data/medios";
}

/// <summary>Dónde se guardan las fotos y videos de obra. Nunca se sirven en forma pública: pasan por el panel.</summary>
public interface IAlmacenMedios
{
    Task GuardarAsync(string ruta, Stream contenido, string contentType, CancellationToken ct = default);
    /// <summary>Stream con posibilidad de seek (para reproducir videos por rangos), o null si no existe.</summary>
    Task<Stream?> AbrirAsync(string ruta, CancellationToken ct = default);
    Task BorrarAsync(string ruta, CancellationToken ct = default);
}

public sealed class AlmacenMediosLocal(string carpeta) : IAlmacenMedios
{
    private readonly string _base = Path.GetFullPath(carpeta);

    public async Task GuardarAsync(string ruta, Stream contenido, string contentType, CancellationToken ct = default)
    {
        var destino = Resolver(ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var temporal = destino + ".subiendo";
        await using (var fs = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await contenido.CopyToAsync(fs, ct);
        File.Move(temporal, destino, overwrite: true);
    }

    public Task<Stream?> AbrirAsync(string ruta, CancellationToken ct = default)
    {
        var archivo = Resolver(ruta);
        return Task.FromResult<Stream?>(File.Exists(archivo)
            ? new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null);
    }

    public Task BorrarAsync(string ruta, CancellationToken ct = default)
    {
        var archivo = Resolver(ruta);
        if (File.Exists(archivo)) File.Delete(archivo);
        return Task.CompletedTask;
    }

    /// <summary>Evita que una ruta con ".." escape de la carpeta base.</summary>
    private string Resolver(string ruta)
    {
        var completa = Path.GetFullPath(Path.Combine(_base, ruta));
        if (!completa.StartsWith(_base + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Ruta de medio inválida.", nameof(ruta));
        return completa;
    }
}

public sealed class AlmacenMediosBlob(BlobContainerClient contenedor) : IAlmacenMedios
{
    private bool _creado;

    public async Task GuardarAsync(string ruta, Stream contenido, string contentType, CancellationToken ct = default)
    {
        if (!_creado) { await contenedor.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct); _creado = true; }
        await contenedor.GetBlobClient(ruta).UploadAsync(contenido,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
    }

    public async Task<Stream?> AbrirAsync(string ruta, CancellationToken ct = default)
    {
        var blob = contenedor.GetBlobClient(ruta);
        if (!await blob.ExistsAsync(ct)) return null;
        return await blob.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), ct);
    }

    public Task BorrarAsync(string ruta, CancellationToken ct = default) =>
        contenedor.GetBlobClient(ruta).DeleteIfExistsAsync(cancellationToken: ct);
}
