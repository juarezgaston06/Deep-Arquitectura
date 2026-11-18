using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Services;

/// <summary>
/// Lo que el teléfono recuerda entre aperturas: la última versión de cada obra (para trabajar sin señal) y lo que ya
/// se resolvió y se envió pero todavía no se ve reflejado en esa copia (para no mostrarlo de nuevo como pendiente).
/// </summary>
public sealed class EstadoLocal
{
    private static string Clave(string que, int obraId) => $"{que}-{obraId}";

    public ObraDetalleDto? ObraGuardada(int obraId)
    {
        var json = Preferences.Default.Get<string?>(Clave("obra", obraId), null);
        try { return json is null ? null : JsonSerializer.Deserialize(json, AppJson.Default.ObraDetalleDto); }
        catch { return null; }
    }

    /// <summary>Obra recién bajada del servidor: ya refleja lo enviado antes, así que se olvida esa lista.</summary>
    public void GuardarObra(ObraDetalleDto obra)
    {
        Preferences.Default.Set(Clave("obra", obra.Id), JsonSerializer.Serialize(obra, AppJson.Default.ObraDetalleDto));
        Preferences.Default.Remove(Clave("enviados", obra.Id));
    }

    public void RecordarEnviados(int obraId, IEnumerable<string> claves)
    {
        var nuevas = claves.ToList();
        if (nuevas.Count == 0) return;
        var actuales = Enviados(obraId);
        Preferences.Default.Set(Clave("enviados", obraId), string.Join('|', actuales.Union(nuevas).TakeLast(300)));
    }

    public HashSet<string> Enviados(int obraId) =>
        (Preferences.Default.Get<string?>(Clave("enviados", obraId), null) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
}

/// <summary>Claves de lo que un envío resuelve (ver <see cref="Envio.Claves"/>).</summary>
public static class ClavesEnvio
{
    public static string Validado(int registroId) => $"val:{registroId}";
    public static string Leida(int ordenId) => $"ord:{ordenId}";
    /// <summary>La causa elegida viaja en la clave: la pantalla la muestra mientras se envía.</summary>
    public static string Atraso(int tareaId, string causa) => $"atr:{tareaId}:{causa}";
    public static string Etapa(int etapaId, string estado) => $"eta:{etapaId}:{estado}";

    /// <summary>Causa de atraso anotada en el teléfono para esa tarea (la última), o null.</summary>
    public static string? CausaLocal(IEnumerable<string> claves, int tareaId) =>
        claves.LastOrDefault(c => c.StartsWith($"atr:{tareaId}:", StringComparison.Ordinal))?.Split(':')[2];

    public static string? EstadoEtapaLocal(IEnumerable<string> claves, int etapaId) =>
        claves.LastOrDefault(c => c.StartsWith($"eta:{etapaId}:", StringComparison.Ordinal))?.Split(':')[2];
}
