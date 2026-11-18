namespace DeepArquitectura.Albanil.Services;

public sealed record Punto(double Latitud, double Longitud, double? Precision);

/// <summary>Ubicación del teléfono al fichar. Si no hay permiso o GPS, se ficha igual (sin ubicación).</summary>
public static class Ubicacion
{
    public static async Task<Punto?> ObtenerAsync()
    {
        try
        {
            var estado = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (estado != PermissionStatus.Granted) estado = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (estado != PermissionStatus.Granted) return null;

            // La última conocida sale al instante; si es vieja o imprecisa se pide una nueva (máx. 12 s).
            var ultima = await Geolocation.Default.GetLastKnownLocationAsync();
            if (ultima is not null && DateTimeOffset.UtcNow - ultima.Timestamp < TimeSpan.FromMinutes(2) && ultima.Accuracy is < 100)
                return new Punto(ultima.Latitude, ultima.Longitude, ultima.Accuracy);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var actual = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(12)), cts.Token);
            var l = actual ?? ultima;
            return l is null ? null : new Punto(l.Latitude, l.Longitude, l.Accuracy);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException or OperationCanceledException)
        {
            return null; // GPS apagado o sin permiso: la fichada vale igual
        }
    }
}
