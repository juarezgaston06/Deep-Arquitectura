using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Services;

/// <summary>Servidor configurado, token (en el almacenamiento seguro de Android) y perfil del capataz.</summary>
public sealed class Sesion
{
    /// <summary>
    /// Viene del build (propiedad ServidorApp del .csproj). En Debug: el emulador de Android viendo la app web
    /// que corre en la PC (dotnet run → http://localhost:5180 = 10.0.2.2:5180 desde el emulador).
    /// </summary>
    public static readonly string ServidorPorDefecto =
        typeof(Sesion).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ServidorApp")?.Value ?? ""; // el build exige ServidorApp fuera de Debug

    private string? _token;

    public string Servidor
    {
        get => Preferences.Default.Get("servidor", ServidorPorDefecto);
        set => Preferences.Default.Set("servidor", value.Trim().TrimEnd('/'));
    }

    public CapatazDto? Perfil { get; private set; }

    /// <summary>Última obra abierta: al volver a la app se entra directo a esa.</summary>
    public int? ObraActual
    {
        get => Preferences.Default.Get("obraActual", 0) is var id and > 0 ? id : null;
        set { if (value is int id) Preferences.Default.Set("obraActual", id); else Preferences.Default.Remove("obraActual"); }
    }

    public bool Iniciada => _token is not null;

    public string? Token => _token;

    /// <summary>Recupera la sesión guardada (al abrir la app).</summary>
    public async Task<bool> CargarAsync()
    {
        try { _token = await SecureStorage.Default.GetAsync("token"); }
        catch { _token = null; } // almacenamiento seguro dañado (p. ej. tras restaurar backup)
        var perfil = Preferences.Default.Get<string?>("perfil", null);
        if (perfil is not null)
            Perfil = JsonSerializer.Deserialize(perfil, AppJson.Default.CapatazDto);
        return _token is not null;
    }

    public async Task GuardarAsync(string token, CapatazDto perfil)
    {
        _token = token;
        await SecureStorage.Default.SetAsync("token", token);
        ActualizarPerfil(perfil);
    }

    public void ActualizarPerfil(CapatazDto perfil)
    {
        Perfil = perfil;
        Preferences.Default.Set("perfil", JsonSerializer.Serialize(perfil, AppJson.Default.CapatazDto));
    }

    public void Cerrar()
    {
        _token = null;
        Perfil = null;
        SecureStorage.Default.Remove("token");
        Preferences.Default.Remove("perfil");
    }
}
