using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Services;

public sealed record JornadaLocal(Guid Ref, DateTime EntradaUtc);

/// <summary>
/// Lo que la app recuerda sin señal: la última copia de cada obra (para mostrar las tareas en el sótano) y la jornada
/// abierta (para saber si hay que fichar entrada o salida aunque la entrada todavía no haya llegado al estudio).
/// </summary>
public sealed class EstadoLocal(Sesion sesion)
{
    private string Clave(string que, int obraId) => $"{que}:{sesion.Perfil?.Id}:{obraId}";

    public ObraAlbanilDto? ObraGuardada(int obraId)
    {
        var json = Preferences.Default.Get<string?>(Clave("obra", obraId), null);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize(json, AppJson.Default.ObraAlbanilDto); }
        catch (JsonException) { return null; }
    }

    public void GuardarObra(ObraAlbanilDto obra) =>
        Preferences.Default.Set(Clave("obra", obra.Id), JsonSerializer.Serialize(obra, AppJson.Default.ObraAlbanilDto));

    /// <summary>Órdenes que ya confirmó en este teléfono (aunque el envío esté esperando señal).</summary>
    public bool Leida(int obraId, int ordenId) =>
        (Preferences.Default.Get<string?>(Clave("leidas", obraId), null) ?? "").Split(',').Contains(ordenId.ToString());

    public void MarcarLeidas(int obraId, IEnumerable<int> ordenes)
    {
        var actuales = (Preferences.Default.Get<string?>(Clave("leidas", obraId), null) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        Preferences.Default.Set(Clave("leidas", obraId), string.Join(',', actuales.Union(ordenes.Select(o => o.ToString())).TakeLast(200)));
    }

    /// <summary>Controles y fotos fijas ya cargados en este teléfono (se ven hechos antes de que lleguen al estudio).</summary>
    public void MarcarHecho(string que, int obraId, int id) =>
        Preferences.Default.Set(Clave($"{que}-{id}", obraId), DateTime.UtcNow.Ticks);

    public DateTime? Hecho(string que, int obraId, int id) =>
        Preferences.Default.Get(Clave($"{que}-{id}", obraId), 0L) is var t and > 0 ? new DateTime(t, DateTimeKind.Utc) : null;

    public JornadaLocal? Jornada(int obraId)
    {
        var v = Preferences.Default.Get<string?>(Clave("jornada", obraId), null);
        if (v?.Split('|') is not [var r, var t] || !Guid.TryParse(r, out var g) || !long.TryParse(t, out var ticks)) return null;
        var j = new JornadaLocal(g, new DateTime(ticks, DateTimeKind.Utc));
        // Una entrada de hace más de 20 h es un olvido: no se ofrece cerrarla como si fuera de hoy.
        return DateTime.UtcNow - j.EntradaUtc > TimeSpan.FromHours(20) ? null : j;
    }

    public void AbrirJornada(int obraId, JornadaLocal j) =>
        Preferences.Default.Set(Clave("jornada", obraId), $"{j.Ref}|{j.EntradaUtc.Ticks}");

    public void CerrarJornada(int obraId) => Preferences.Default.Remove(Clave("jornada", obraId));

    /// <summary>
    /// Une lo que dice el servidor con lo que sabe el teléfono: si el servidor tiene una jornada abierta que no está
    /// en el teléfono (reinstaló la app, cambió de teléfono) se adopta, salvo que su salida esté esperando señal.
    /// </summary>
    public void Reconciliar(ObraAlbanilDto obra, IEnumerable<Envio> pendientes)
    {
        if (obra.JornadaAbierta is not { } s || Jornada(obra.Id) is not null) return;
        var salidaPendiente = pendientes.Any(e => e.Tipo == TipoEnvio.Salida && e.ObraId == obra.Id && e.Cuerpo.Contains(s.Ref.ToString(), StringComparison.OrdinalIgnoreCase));
        if (!salidaPendiente) AbrirJornada(obra.Id, new JornadaLocal(s.Ref, s.EntradaUtc));
    }
}
