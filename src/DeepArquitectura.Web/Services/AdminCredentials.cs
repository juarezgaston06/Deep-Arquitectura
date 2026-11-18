using System.Security.Cryptography;
using System.Text;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeepArquitectura.Web.Services;

public class AdminOptions
{
    public const string Seccion = "Admin";
    public string Usuario { get; set; } = "";
    /// <summary>Hash generado con: dotnet run -- hash-password</summary>
    public string PasswordHash { get; set; } = "";
}

/// <summary>Motivos por los que se rechaza un cambio de contraseña (viajan como código en la URL, no como texto).</summary>
public enum ErrorCambioClave
{
    SinAdministrador,
    ActualIncorrecta,
    MuyCorta,
    MuyLarga,
    NoCoinciden,
    IgualALaActual,
}

/// <summary>
/// MVP con un único usuario administrador (el profesional del estudio). La contraseña se guarda solo como
/// hash PBKDF2 (PasswordHasher de ASP.NET Core Identity). La inicial viene de la configuración (Admin:*);
/// cuando se cambia desde el panel queda en la tabla CredencialesAdmin, que pasa a tener prioridad.
/// </summary>
public class AdminCredentials(IOptions<AdminOptions> options, IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AdminCredentials>? logger = null)
{
    public const string ClaimSello = "deeparq:sello";
    public const int LargoMinimo = 8;
    public const int LargoMaximo = 200;

    private static readonly PasswordHasher<string> Hasher = new();
    private readonly ILogger _log = logger ?? NullLogger<AdminCredentials>.Instance;
    // Serializa la lectura de la base con los cambios: sin esto, una lectura lenta iniciada antes de un cambio
    // podía pisar la caché con la contraseña vieja.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Vigente? _cache;

    private sealed record Vigente(string Usuario, string PasswordHash, string Sello);

    public async Task<bool> ConfiguradoAsync(CancellationToken ct = default) => await ObtenerAsync(ct) is not null;

    public async Task<string> UsuarioAsync(CancellationToken ct = default) => (await ObtenerAsync(ct))?.Usuario ?? "";

    /// <summary>Sello de la contraseña vigente; viaja en la cookie para cerrar sesiones viejas al cambiarla.</summary>
    public async Task<string?> SelloAsync(CancellationToken ct = default) => (await ObtenerAsync(ct))?.Sello;

    public async Task<bool> ValidarAsync(string? usuario, string? password, CancellationToken ct = default)
    {
        var v = await ObtenerAsync(ct);
        if (v is null || string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(password) || password.Length > LargoMaximo) return false;

        var usuarioOk = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(usuario.Trim().ToLowerInvariant()),
            Encoding.UTF8.GetBytes(v.Usuario.Trim().ToLowerInvariant()));

        PasswordVerificationResult resultado;
        try
        {
            resultado = Hasher.VerifyHashedPassword(v.Usuario, v.PasswordHash, password);
        }
        catch (FormatException)
        {
            // Un Admin:PasswordHash mal copiado antes tiraba un error 500 en el login.
            _log.LogError("Admin:PasswordHash no es un hash válido. Generalo con: dotnet run -- hash-password");
            return false;
        }
        return usuarioOk && resultado != PasswordVerificationResult.Failed;
    }

    /// <summary>Cambia la contraseña. Devuelve null si salió bien o el motivo del rechazo.</summary>
    public async Task<ErrorCambioClave?> CambiarAsync(string? actual, string? nueva, string? repetida, CancellationToken ct = default)
    {
        var v = await ObtenerAsync(ct);
        if (v is null) return ErrorCambioClave.SinAdministrador;
        if (!await ValidarAsync(v.Usuario, actual, ct)) return ErrorCambioClave.ActualIncorrecta;
        if (string.IsNullOrEmpty(nueva) || nueva.Length < LargoMinimo) return ErrorCambioClave.MuyCorta;
        if (nueva.Length > LargoMaximo) return ErrorCambioClave.MuyLarga;
        if (nueva != repetida) return ErrorCambioClave.NoCoinciden;
        if (nueva == actual) return ErrorCambioClave.IgualALaActual;

        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var fila = await db.Credenciales.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
            if (fila is null) db.Credenciales.Add(fila = new CredencialAdmin { Usuario = v.Usuario });
            fila.PasswordHash = Hashear(nueva);
            fila.Sello = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            fila.ActualizadoEn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _cache = new Vigente(fila.Usuario, fila.PasswordHash, fila.Sello);
        }
        finally { _lock.Release(); }
        _log.LogInformation("Contraseña del administrador cambiada desde el panel.");
        return null;
    }

    /// <summary>Borra la contraseña cambiada desde el panel y vuelve a la de la configuración (recuperación).</summary>
    public async Task<bool> RestablecerAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var borradas = await db.Credenciales.ExecuteDeleteAsync(ct);
            _cache = null;
            return borradas > 0;
        }
        finally { _lock.Release(); }
    }

    public static string Mensaje(ErrorCambioClave error) => error switch
    {
        ErrorCambioClave.SinAdministrador => "No hay un administrador configurado.",
        ErrorCambioClave.ActualIncorrecta => "La contraseña actual no es correcta.",
        ErrorCambioClave.MuyCorta => $"La nueva contraseña debe tener al menos {LargoMinimo} caracteres.",
        ErrorCambioClave.MuyLarga => $"La nueva contraseña no puede superar los {LargoMaximo} caracteres.",
        ErrorCambioClave.NoCoinciden => "Las dos contraseñas nuevas no coinciden.",
        ErrorCambioClave.IgualALaActual => "La nueva contraseña tiene que ser distinta de la actual.",
        _ => "No se pudo cambiar la contraseña.",
    };

    public static string Hashear(string password) => Hasher.HashPassword("admin", password);

    private async Task<Vigente?> ObtenerAsync(CancellationToken ct)
    {
        if (_cache is { } c) return c;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } c2) return c2;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var fila = await db.Credenciales.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            _cache = fila is not null
                ? new(fila.Usuario, fila.PasswordHash, fila.Sello)
                : string.IsNullOrWhiteSpace(options.Value.Usuario) || string.IsNullOrWhiteSpace(options.Value.PasswordHash)
                    ? null
                    // Sello derivado del hash: si se cambia la clave por configuración, también caen las sesiones viejas.
                    : new(options.Value.Usuario.Trim(), options.Value.PasswordHash.Trim(),
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.PasswordHash.Trim())))[..32]);
            return _cache;
        }
        finally { _lock.Release(); }
    }
}
