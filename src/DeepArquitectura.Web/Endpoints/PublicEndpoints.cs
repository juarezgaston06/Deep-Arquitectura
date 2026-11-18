using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace DeepArquitectura.Web.Endpoints;

public static class PublicEndpoints
{
    public record ConsultaRequest(string? Nombre, string? Email, string? Telefono, string? Mensaje, string? Area, string? Sitio,
        EstudioService.OrigenVisita? Origen = null);

    public static void MapEndpointsEstudio(this WebApplication app)
    {
        // Formulario de contacto del sitio público. Lo llama main.js antes de abrir WhatsApp.
        app.MapPost("/api/consultas", async (ConsultaRequest req, EstudioService estudio, ILoggerFactory lf, CancellationToken ct) =>
        {
            // Honeypot: el campo "sitio" está oculto; si viene completo es un bot. Respondemos OK sin guardar.
            if (!string.IsNullOrWhiteSpace(req.Sitio)) return Results.Accepted();

            var errores = Validar(req);
            if (errores.Count > 0) return Results.ValidationProblem(errores);

            var area = Enum.TryParse<AreaConsulta>(req.Area, ignoreCase: true, out var a) ? a : AreaConsulta.General;
            var id = await estudio.RegistrarConsultaAsync(req.Nombre!, req.Email!, req.Telefono, req.Mensaje!, area, req.Origen, ct);
            lf.CreateLogger("Consultas").LogInformation("Nueva consulta #{Id} desde {Area}", id, area);
            return Results.Accepted();
        })
        .RequireRateLimiting("publico")
        .DisableAntiforgery();

        // Login. El antiforgery se valida a mano para que un token vencido (pestaña abierta mucho tiempo,
        // reinicio de la app) vuelva al formulario con un aviso en vez de mostrar un 400 en blanco.
        app.MapPost("/cuenta/login", async ([FromForm] string? usuario, [FromForm] string? password, [FromForm] string? returnUrl,
            HttpContext http, IAntiforgery antiforgery, AdminCredentials credenciales, ILoggerFactory lf) =>
        {
            var destino = DestinoSeguro(returnUrl);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.LocalRedirect(UrlLogin("sesion", destino));

            if (!await credenciales.ValidarAsync(usuario, password, http.RequestAborted))
            {
                lf.CreateLogger("Login").LogWarning("Login fallido desde {Ip}", http.Connection.RemoteIpAddress);
                return Results.LocalRedirect(UrlLogin("credenciales", destino));
            }

            await IniciarSesionAsync(http, credenciales);
            return Results.LocalRedirect(destino);
        })
        .DisableAntiforgery()
        .RequireRateLimiting("login");

        // Cambio de contraseña desde /admin/cuenta. Renueva la cookie de esta sesión; las demás quedan cerradas.
        app.MapPost("/cuenta/clave", async ([FromForm] string? actual, [FromForm] string? nueva, [FromForm] string? repetida,
            HttpContext http, IAntiforgery antiforgery, AdminCredentials credenciales) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.LocalRedirect("/admin/cuenta?error=sesion");

            var error = await credenciales.CambiarAsync(actual, nueva, repetida, http.RequestAborted);
            if (error is not null)
                return Results.LocalRedirect($"/admin/cuenta?error={error}");

            await IniciarSesionAsync(http, credenciales);
            return Results.LocalRedirect("/admin/cuenta?ok=1");
        })
        .DisableAntiforgery()
        .RequireAuthorization()
        .RequireRateLimiting("login");

        // Logout con token antiforgery: antes cualquier POST (incluso de otro sitio) cerraba la sesión.
        app.MapPost("/cuenta/logout", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.LocalRedirect("/admin");
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/admin/login?salio=1");
        })
        .DisableAntiforgery();
        app.MapGet("/healthz", () => Results.Ok("ok"));
    }

    /// <summary>Solo se vuelve a páginas del panel (evita redirecciones abiertas a otros sitios).</summary>
    internal static string DestinoSeguro(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && (returnUrl == "/admin" || returnUrl.StartsWith("/admin/", StringComparison.Ordinal))
            && !returnUrl.StartsWith("/admin/login", StringComparison.Ordinal) && !returnUrl.Contains('\\')
            ? returnUrl : "/admin";

    internal static string UrlLogin(string error, string destino) =>
        $"/admin/login?error={error}" + (destino == "/admin" ? "" : "&returnUrl=" + Uri.EscapeDataString(destino));

    private static async Task IniciarSesionAsync(HttpContext http, AdminCredentials credenciales)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, await credenciales.UsuarioAsync(http.RequestAborted)),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(AdminCredentials.ClaimSello, await credenciales.SelloAsync(http.RequestAborted) ?? ""),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }

    internal static Dictionary<string, string[]> Validar(ConsultaRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Length > 200) e["nombre"] = ["Ingresá tu nombre."];
        if (string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 200 || !new EmailAddressAttribute().IsValid(r.Email))
            e["email"] = ["Ingresá un correo válido."];
        if (string.IsNullOrWhiteSpace(r.Mensaje) || r.Mensaje.Length > 4000) e["mensaje"] = ["Contanos brevemente tu consulta."];
        if (r.Telefono is { Length: > 50 }) e["telefono"] = ["Teléfono demasiado largo."];
        return e;
    }
}
