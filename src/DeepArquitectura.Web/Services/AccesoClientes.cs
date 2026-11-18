using System.Security.Cryptography;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace DeepArquitectura.Web.Services;

/// <summary>Código de obra y PIN de la app del cliente: generación, normalización y hash.</summary>
public static class AccesoClientes
{
    /// <summary>Sin 0/O, 1/I/L: se dictan por teléfono y se tipean en el celular sin confundirse.</summary>
    private const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const string Prefijo = "DEEP";
    public const int LargoPin = 6;
    public const int IntentosAntesDeBloquear = 8;
    public static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<string> Hasher = new();
    /// <summary>Para gastar lo mismo cuando el código no existe (no revela cuáles existen).</summary>
    private static readonly string FalsoHash = Hasher.HashPassword("x", Guid.NewGuid().ToString());

    /// <summary>Ej. DEEP-7K3M-Q9TX (8 caracteres al azar, ~40 bits: no se puede adivinar).</summary>
    public static string NuevoCodigo()
    {
        Span<char> c = stackalloc char[8];
        for (var i = 0; i < c.Length; i++) c[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return $"{Prefijo}-{c[..4]}-{c[4..]}";
    }

    public static string NuevoPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>
    /// Acepta lo que tipea el cliente ("deep 7k3m q9tx", "7K3MQ9TX", con o sin guiones) y lo deja como se guardó.
    /// Null si no tiene forma de código.
    /// </summary>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 40) return null;
        var s = new string(texto.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (s.StartsWith(Prefijo, StringComparison.Ordinal) && s.Length == Prefijo.Length + 8) s = s[Prefijo.Length..];
        if (s.Length != 8 || s.Any(ch => !Alfabeto.Contains(ch))) return null;
        return $"{Prefijo}-{s[..4]}-{s[4..]}";
    }

    public static bool PinConFormato(string? pin) => pin is { Length: LargoPin } && pin.All(char.IsAsciiDigit);

    public static string HashPin(string codigo, string pin) => Hasher.HashPassword(codigo, pin);

    public static bool VerificarPin(AccesoCliente? acceso, string pin)
    {
        if (acceso is null)
        {
            Hasher.VerifyHashedPassword("x", FalsoHash, pin);
            return false;
        }
        return Hasher.VerifyHashedPassword(acceso.Codigo, acceso.PinHash, pin) != PasswordVerificationResult.Failed;
    }

    public static string NuevoSello() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // ---- Documentos que el estudio sube para el cliente ----
    public const long TamanoMaximoDocumento = 50 * 1024 * 1024;

    private static readonly Dictionary<string, string> FormatosDocumento = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public const string ErrorFormatoDocumento = "Formato no admitido. Subí PDF, imagen (jpg, png, webp), Word (docx) o Excel (xlsx).";
    public const string AceptaDocumentos = ".pdf,.jpg,.jpeg,.png,.webp,.docx,.xlsx";

    public static bool TryFormatoDocumento(string? nombre, out string contentType) =>
        FormatosDocumento.TryGetValue(Path.GetExtension(nombre ?? ""), out contentType!);

    /// <summary>PDF e imágenes se abren en el navegador; Word y Excel se descargan.</summary>
    public static bool SeVeEnNavegador(string contentType) => contentType == "application/pdf" || contentType.StartsWith("image/", StringComparison.Ordinal);
}
