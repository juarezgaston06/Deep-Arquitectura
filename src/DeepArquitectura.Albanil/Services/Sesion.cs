using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Services;

/// <summary>Servidor configurado, token (almacenamiento seguro de Android), perfil del albañil y obra elegida.</summary>
public sealed class Sesion
{
    /// <summary>Viene del build (propiedad ServidorApp del .csproj). En Debug: la PC vista desde el emulador.</summary>
    public static readonly string ServidorPorDefecto =
        typeof(Sesion).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ServidorApp")?.Value ?? "";

    private string? _token;

    public string Servidor
    {
        get => Preferences.Default.Get("servidor", ServidorPorDefecto);
        set => Preferences.Default.Set("servidor", value.Trim().TrimEnd('/'));
    }

    public AlbanilDto? Perfil { get; private set; }

    public string? Token => _token;

    /// <summary>Última obra abierta (al volver a la app se entra directo a esa).</summary>
    public int? ObraActual
    {
        get => Preferences.Default.Get("obraActual", 0) is var id and > 0 ? id : null;
        set { if (value is int id) Preferences.Default.Set("obraActual", id); else Preferences.Default.Remove("obraActual"); }
    }

    public async Task<bool> CargarAsync()
    {
        try { _token = await SecureStorage.Default.GetAsync("token"); }
        catch { _token = null; } // almacenamiento seguro dañado (p. ej. tras restaurar backup)
        var perfil = Preferences.Default.Get<string?>("perfil", null);
        if (perfil is not null)
        {
            try { Perfil = JsonSerializer.Deserialize(perfil, AppJson.Default.AlbanilDto); }
            catch (JsonException) { Perfil = null; }
        }
        return _token is not null && Perfil is not null;
    }

    public async Task GuardarAsync(string token, AlbanilDto perfil)
    {
        _token = token;
        await SecureStorage.Default.SetAsync("token", token);
        ActualizarPerfil(perfil);
    }

    public void ActualizarPerfil(AlbanilDto perfil)
    {
        Perfil = perfil;
        Preferences.Default.Set("perfil", JsonSerializer.Serialize(perfil, AppJson.Default.AlbanilDto));
        if (ObraActual is int id && perfil.Obras.All(o => o.Id != id)) ObraActual = null;
    }

    public void Cerrar()
    {
        _token = null;
        Perfil = null;
        ObraActual = null;
        SecureStorage.Default.Remove("token");
        Preferences.Default.Remove("perfil");
    }
}
