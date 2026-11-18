namespace DeepArquitectura.Capataz.Services;

/// <summary>Cámara y galería del teléfono. Las fotos salen a 2560 px / calidad 80 (~1 MB), como en la app de la cuadrilla.</summary>
public static class Medios
{
    private static readonly MediaPickerOptions OpcionesFoto = new()
    {
        MaximumWidth = 2560,
        MaximumHeight = 2560,
        CompressionQuality = 80,
        RotateImage = true,
        PreserveMetaData = true,
    };

    public static bool EsVideo(FileResult f) =>
        f.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
        || Path.GetExtension(f.FileName).ToLowerInvariant() is ".mp4" or ".mov" or ".3gp" or ".webm";

    public static async Task<FileResult?> SacarFotoAsync()
    {
        if (!await PermisoCamaraAsync()) return null;
        return await MediaPicker.Default.CapturePhotoAsync(OpcionesFoto);
    }

    public static async Task<FileResult?> GrabarVideoAsync()
    {
        if (!await PermisoCamaraAsync()) return null;
        if (await Permissions.CheckStatusAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            await Permissions.RequestAsync<Permissions.Microphone>(); // sin micrófono el video sale mudo, pero sale
        return await MediaPicker.Default.CaptureVideoAsync(new MediaPickerOptions { Title = "Video de avance" });
    }

    /// <summary>Fotos o videos que ya estaban en la galería.</summary>
    public static async Task<IReadOnlyList<FileResult>> ElegirDeGaleriaAsync(bool videos)
    {
        var lista = videos
            ? await MediaPicker.Default.PickVideosAsync(new MediaPickerOptions { SelectionLimit = 5 })
            : await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                SelectionLimit = 10, MaximumWidth = 2560, MaximumHeight = 2560, CompressionQuality = 80, RotateImage = true,
            });
        return lista?.Where(f => f is not null).ToList() ?? [];
    }

    private static async Task<bool> PermisoCamaraAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported) return false;
        var estado = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (estado != PermissionStatus.Granted) estado = await Permissions.RequestAsync<Permissions.Camera>();
        return estado == PermissionStatus.Granted;
    }
}
