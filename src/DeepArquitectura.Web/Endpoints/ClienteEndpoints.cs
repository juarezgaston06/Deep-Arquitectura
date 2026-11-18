using System.Security.Claims;
using DeepArquitectura.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DeepArquitectura.Web.Endpoints;

/// <summary>
/// Sesión de la app del cliente (/mi-obra): cookie propia, separada de la del panel, que solo viaja a /mi-obra.
/// Lleva la obra y el sello del acceso; si el estudio da un PIN nuevo o desactiva el código, deja de valer.
/// </summary>
public static class SesionCliente
{
    public const string Esquema = "Cliente";
    public const string ClaimObra = "deeparq:obra";
    public const string ClaimSello = "deeparq:sello";
    public const string Ruta = "/mi-obra";
    public const string Entrar = "/mi-obra/entrar";

    public static void Configurar(CookieAuthenticationOptions o, bool desarrollo)
    {
        o.Cookie.Name = "deeparq.cliente";
        o.Cookie.Path = Ruta;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        o.Cookie.SecurePolicy = desarrollo ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        // El cliente la instala como app y entra de vez en cuando: no le pedimos el PIN cada dos semanas.
        o.ExpireTimeSpan = TimeSpan.FromDays(180);
        o.SlidingExpiration = true;
        o.LoginPath = Entrar;
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var app = ctx.HttpContext.RequestServices.GetRequiredService<AppCliente>();
            if (ObraDe(ctx.Principal) is not int obra
                || !await app.SesionValidaAsync(obra, ctx.Principal?.FindFirst(ClaimSello)?.Value, ctx.HttpContext.RequestAborted))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(Esquema);
            }
        };
    }

    private static int? ObraDe(ClaimsPrincipal? p) =>
        p?.Identity is { IsAuthenticated: true, AuthenticationType: Esquema } && int.TryParse(p.FindFirst(ClaimObra)?.Value, out var id) ? id : null;

    /// <summary>Obra de la sesión del cliente, o null (no confunde la sesión del panel con la del cliente).</summary>
    public static async Task<int?> ObraAsync(HttpContext http)
    {
        if (http.Items.TryGetValue(ClaimObra, out var v)) return (int?)v;
        var r = await http.AuthenticateAsync(Esquema);
        var obra = r.Succeeded ? ObraDe(r.Principal) : null;
        http.Items[ClaimObra] = obra;
        return obra;
    }
}

public static class ClienteEndpoints
{
    public static void MapAppCliente(this WebApplication app)
    {
        // Ingreso con código de obra + PIN. Antiforgery a mano (como el login del panel): un formulario vencido vuelve con aviso.
        app.MapPost("/mi-obra/ingresar", async ([FromForm] string? codigo, [FromForm] string? pin, HttpContext http,
            IAntiforgery antiforgery, AppCliente svc, ILoggerFactory lf) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Volver("sesion", codigo);
            var r = await svc.IngresarAsync(codigo, pin, http.RequestAborted);
            switch (r.Resultado)
            {
                case AppCliente.ResultadoIngreso.Bloqueado:
                    lf.CreateLogger("LoginCliente").LogWarning("Código de cliente bloqueado por intentos, desde {Ip}", http.Connection.RemoteIpAddress);
                    return Volver("bloqueado", codigo);
                case AppCliente.ResultadoIngreso.Incorrecto:
                    lf.CreateLogger("LoginCliente").LogInformation("Ingreso de cliente fallido desde {Ip}", http.Connection.RemoteIpAddress);
                    return Volver("datos", codigo);
            }
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, r.Codigo!),
                    new Claim(SesionCliente.ClaimObra, r.ObraId.ToString()),
                    new Claim(SesionCliente.ClaimSello, r.Sello!),
                ], SesionCliente.Esquema);
            await http.SignInAsync(SesionCliente.Esquema, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.LocalRedirect("/mi-obra/");
        })
        .DisableAntiforgery()
        .RequireRateLimiting("login-cliente");

        app.MapPost("/mi-obra/salir", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Results.LocalRedirect("/mi-obra/");
            await http.SignOutAsync(SesionCliente.Esquema);
            return Results.LocalRedirect(SesionCliente.Entrar + "?salio=1");
        })
        .DisableAntiforgery();

        app.MapGet("/mi-obra/medios/{id:int}", async (int id, HttpContext http, PortalCliente portal, CancellationToken ct) =>
        {
            if (await SesionCliente.ObraAsync(http) is not int obra) return Results.Unauthorized();
            if (await portal.MedioObraAsync(obra, id, ct) is not { } m) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=604800";
            return Results.Stream(m.Contenido, m.ContentType, enableRangeProcessing: true);
        });

        app.MapGet("/mi-obra/documentos/{id:int}", async (int id, HttpContext http, AppCliente svc, CancellationToken ct) =>
        {
            if (await SesionCliente.ObraAsync(http) is not int obra) return Results.Unauthorized();
            if (await svc.DocumentoAsync(obra, id, ct) is not { } d) return Results.NotFound();
            return Archivo(http, d.Contenido, d.ContentType, d.Nombre);
        });

        // El panel ve cualquier documento de cualquier obra (también los no visibles para el cliente).
        app.MapGet("/admin/documentos/{id:int}", async (int id, HttpContext http, GestionCliente svc, CancellationToken ct) =>
            await svc.AbrirDocumentoAsync(id, ct) is { } d ? Archivo(http, d.Contenido, d.ContentType, d.Nombre) : Results.NotFound())
        .RequireAuthorization();
    }

    private static IResult Volver(string error, string? codigo)
    {
        var normal = AccesoClientes.Normalizar(codigo);
        return Results.LocalRedirect($"{SesionCliente.Entrar}?error={error}" + (normal is null ? "" : "&codigo=" + Uri.EscapeDataString(normal)));
    }

    /// <summary>PDF e imágenes se abren en el navegador (visor del teléfono); el resto se descarga.</summary>
    private static IResult Archivo(HttpContext http, Stream contenido, string contentType, string nombre)
    {
        http.Response.Headers.CacheControl = "private, max-age=3600";
        var disposicion = new ContentDispositionHeaderValue(AccesoClientes.SeVeEnNavegador(contentType) ? "inline" : "attachment");
        disposicion.SetHttpFileName(nombre);
        http.Response.Headers.ContentDisposition = disposicion.ToString();
        return Results.Stream(contenido, contentType, enableRangeProcessing: true);
    }
}
