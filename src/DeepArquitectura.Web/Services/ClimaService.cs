using System.Text.Json;
using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

public class ClimaOptions
{
    public const string Seccion = "Clima";
    /// <summary>
    /// Consulta Open-Meteo (gratis, sin clave). Solo viajan las coordenadas de la obra; nada del cliente ni del personal.
    /// </summary>
    public bool Habilitado { get; set; } = true;
    public string Url { get; set; } = "https://api.open-meteo.com/v1/forecast";
    /// <summary>Hora local (Córdoba) de la actualización diaria.</summary>
    public int HoraLocal { get; set; } = 6;
}

/// <summary>Clima diario de cada obra con ubicación: los últimos 7 días y el pronóstico de 3, para explicar atrasos.</summary>
public class ClimaService(IDbContextFactory<AppDbContext> dbFactory, HttpClient http, Microsoft.Extensions.Options.IOptions<ClimaOptions> opciones,
    ILogger<ClimaService> log)
{
    /// <summary>Actualiza el clima de las obras activas con ubicación. Devuelve cuántas obras se actualizaron.</summary>
    public async Task<int> ActualizarAsync(CancellationToken ct = default)
    {
        if (!opciones.Value.Habilitado) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obras = await db.Obras
            .Where(o => o.Latitud != null && o.Longitud != null
                && (o.Estado == EstadoObra.EnCurso || o.Estado == EstadoObra.EnTramiteMunicipal || o.Estado == EstadoObra.Pausada))
            .Select(o => new { o.Id, o.Latitud, o.Longitud }).ToListAsync(ct);
        var n = 0;
        foreach (var o in obras)
        {
            try
            {
                await ActualizarObraAsync(db, o.Id, o.Latitud!.Value, o.Longitud!.Value, ct);
                n++;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "No se pudo traer el clima de la obra {Obra}", o.Id);
            }
        }
        return n;
    }

    public async Task ActualizarObraAsync(int obraId, CancellationToken ct = default)
    {
        if (!opciones.Value.Habilitado) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.Obras.Where(x => x.Id == obraId).Select(x => new { x.Latitud, x.Longitud }).SingleAsync(ct);
        if (o.Latitud is null || o.Longitud is null) return;
        await ActualizarObraAsync(db, obraId, o.Latitud.Value, o.Longitud.Value, ct);
    }

    private async Task ActualizarObraAsync(AppDbContext db, int obraId, double lat, double lon, CancellationToken ct)
    {
        var url = FormattableString.Invariant(
            $"{opciones.Value.Url}?latitude={lat:0.####}&longitude={lon:0.####}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max&timezone=America%2FArgentina%2FCordoba&past_days=7&forecast_days=3");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync(url, cts.Token);
        var dias = Interpretar(json);

        var fechas = dias.Select(d => d.Fecha).ToList();
        var existentes = await db.Climas.Where(c => c.ObraId == obraId && fechas.Contains(c.Fecha)).ToDictionaryAsync(c => c.Fecha, ct);
        foreach (var d in dias)
        {
            if (!existentes.TryGetValue(d.Fecha, out var c))
            {
                c = new ClimaDiario { ObraId = obraId, Fecha = d.Fecha };
                db.Climas.Add(c);
            }
            (c.TempMax, c.TempMin, c.LluviaMm, c.VientoMaxKmh, c.CodigoTiempo, c.ActualizadoEn) =
                (d.TempMax, d.TempMin, d.LluviaMm, d.VientoMaxKmh, d.CodigoTiempo, DateTime.UtcNow);
        }
        await db.SaveChangesAsync(ct);
    }

    public sealed record Dia(DateOnly Fecha, double? TempMax, double? TempMin, double? LluviaMm, double? VientoMaxKmh, int? CodigoTiempo);

    /// <summary>Respuesta «daily» de Open-Meteo → un registro por día.</summary>
    public static List<Dia> Interpretar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var daily = doc.RootElement.GetProperty("daily");
        var fechas = daily.GetProperty("time").EnumerateArray().Select(e => DateOnly.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        double? Num(string campo, int i) =>
            daily.TryGetProperty(campo, out var arr) && arr.GetArrayLength() > i && arr[i].ValueKind == JsonValueKind.Number ? arr[i].GetDouble() : null;
        return fechas.Select((f, i) => new Dia(f, Num("temperature_2m_max", i), Num("temperature_2m_min", i), Num("precipitation_sum", i),
            Num("wind_speed_10m_max", i), Num("weather_code", i) is double c ? (int)c : null)).ToList();
    }

    /// <summary>Hoy y los dos días siguientes, para la app.</summary>
    public static async Task<IReadOnlyList<ClimaDto>> ProximosAsync(AppDbContext db, int obraId, DateOnly hoy, CancellationToken ct) =>
        (await db.Climas.AsNoTracking().Where(c => c.ObraId == obraId && c.Fecha >= hoy && c.Fecha <= hoy.AddDays(2))
            .OrderBy(c => c.Fecha).ToListAsync(ct))
        .Select(ADto).ToList();

    public static ClimaDto ADto(ClimaDiario c) => new(c.Fecha, c.TempMax, c.TempMin, c.LluviaMm, c.VientoMaxKmh, Descripcion(c.CodigoTiempo));

    /// <summary>Código WMO → texto corto.</summary>
    public static string Descripcion(int? codigo) => codigo switch
    {
        0 => "Despejado",
        1 or 2 => "Algo nublado",
        3 => "Nublado",
        45 or 48 => "Niebla",
        >= 51 and <= 57 => "Llovizna",
        >= 61 and <= 67 => "Lluvia",
        >= 71 and <= 77 => "Nieve",
        >= 80 and <= 82 => "Chaparrones",
        >= 95 => "Tormenta",
        null => "Sin datos",
        _ => "Variable",
    };

    public static string Icono(int? codigo) => codigo switch
    {
        0 => "☀️",
        1 or 2 => "⛅",
        3 or 45 or 48 => "☁️",
        >= 51 and <= 67 or >= 80 and <= 82 => "🌧️",
        >= 95 => "⛈️",
        _ => "🌤️",
    };
}

/// <summary>
/// Actualiza el clima de las obras activas al arrancar y después una vez por día, a la hora local configurada
/// (<see cref="ClimaOptions.HoraLocal"/>). Una sola vez por día a propósito: cada corrida despierta la base Azure SQL
/// serverless (queda prendida al menos una hora) y con la oferta gratuita conviene que se pause el resto del tiempo.
/// </summary>
public sealed class ActualizacionClima(ClimaService clima, Microsoft.Extensions.Options.IOptions<ClimaOptions> opciones,
    ILogger<ActualizacionClima> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct); // no competir con el arranque (migraciones)
        while (!ct.IsCancellationRequested)
        {
            try { await clima.ActualizarAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogWarning(ex, "No se pudo actualizar el clima"); }
            await Task.Delay(HastaLaProxima(DateTime.UtcNow, opciones.Value.HoraLocal), ct);
        }
    }

    /// <summary>Cuánto falta para la próxima corrida (hora local de Córdoba).</summary>
    public static TimeSpan HastaLaProxima(DateTime ahoraUtc, int horaLocal)
    {
        var local = Textos.AHoraLocal(ahoraUtc);
        var proxima = local.Date.AddHours(Math.Clamp(horaLocal, 0, 23));
        if (proxima <= local) proxima = proxima.AddDays(1);
        return proxima - local;
    }
}
