namespace DeepArquitectura.Web.Services;

/// <summary>
/// Al arrancar y una vez por día borra las sesiones vencidas de los teléfonos de capataces y albañiles. Corre a la misma
/// hora que el clima (unos minutos después): así la base serverless se despierta una sola vez por día por tareas propias.
/// </summary>
public sealed class LimpiezaSesiones(CapatazService capataces, AlbanilService albaniles,
    Microsoft.Extensions.Options.IOptions<ClimaOptions> opciones, ILogger<LimpiezaSesiones> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var borradas = await capataces.LimpiarSesionesVencidasAsync(ct);
                if (borradas > 0) log.LogInformation("Sesiones vencidas de capataces borradas: {Cantidad}", borradas);
                borradas = await albaniles.LimpiarSesionesVencidasAsync(ct);
                if (borradas > 0) log.LogInformation("Sesiones vencidas de albañiles borradas: {Cantidad}", borradas);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // La base serverless puede estar pausada o no disponible: se reintenta mañana.
                log.LogWarning(ex, "No se pudieron limpiar las sesiones vencidas");
            }
            await Task.Delay(ActualizacionClima.HastaLaProxima(DateTime.UtcNow, opciones.Value.HoraLocal) + TimeSpan.FromMinutes(5), ct);
        }
    }
}
