using System.Globalization;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace DeepArquitectura.Web.Endpoints;

/// <summary>
/// Aviso de una sola vez para la próxima pantalla de la app del cliente (Post/Redirect/Get). Va en una cookie corta
/// en vez de la URL: así nadie puede armar un link que muestre un texto cualquiera dentro de la app.
/// </summary>
public static class AvisoCliente
{
    private const string Cookie = "deeparq.aviso";

    public static void Poner(HttpContext http, string texto, bool ok) =>
        http.Response.Cookies.Append(Cookie, (ok ? "1|" : "0|") + Uri.EscapeDataString(texto.Length > 300 ? texto[..300] : texto),
            new CookieOptions { Path = SesionCliente.Ruta, HttpOnly = true, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(2), Secure = http.Request.IsHttps });

    /// <summary>Lee y borra el aviso (null si no hay).</summary>
    public static (string Texto, bool Ok)? Tomar(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(Cookie, out var v) || v.Length < 2) return null;
        if (!http.Response.HasStarted) http.Response.Cookies.Delete(Cookie, new CookieOptions { Path = SesionCliente.Ruta });
        return (Uri.UnescapeDataString(v[2..]), v[0] == '1');
    }
}

/// <summary>Lo que manda el cliente desde su app: reclamos, pagos informados, firma del acta, encuestas y permiso de difusión.</summary>
public static class ClienteProcesoEndpoints
{
    public static void MapDatosProcesoCliente(this WebApplication app)
    {
        app.MapPost("/mi-obra/reclamos", (HttpContext http, IAntiforgery af, Postventa svc) =>
            Procesar(http, af, "/mi-obra/garantia", async (obra, f) =>
            {
                var (_, error) = await svc.CrearReclamoAsync(obra, new Postventa.DatosReclamo
                {
                    Ref = Guid.TryParse(f["ref"], out var r) ? r : Guid.Empty, Rubro = f["rubro"], Ubicacion = f["ubicacion"],
                    Descripcion = f["descripcion"], Urgente = f["urgente"] == "si", Nombre = f["nombre"],
                }, Archivos(f), http.RequestAborted);
                return (error, "Recibimos tu reclamo. Te respondemos por acá.");
            }))
            .DisableAntiforgery();

        app.MapPost("/mi-obra/pagos/informar", (HttpContext http, IAntiforgery af, Postventa svc) =>
            Procesar(http, af, "/mi-obra/pagos", async (obra, f) =>
            {
                var (_, error) = await svc.InformarPagoAsync(obra, new Postventa.DatosPagoInformado
                {
                    Ref = Guid.TryParse(f["ref"], out var r) ? r : Guid.Empty,
                    Fecha = DateOnly.TryParseExact(f["fecha"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default,
                    Monto = Monto(f["monto"]),
                    Medio = Enum.TryParse<MedioPago>(f["medio"], out var m) ? m : MedioPago.Transferencia,
                    CertificadoId = int.TryParse(f["certificado"], out var c) ? c : null,
                    Nota = f["nota"], Nombre = f["nombre"],
                }, Archivos(f), http.RequestAborted);
                return (error, "Gracias: lo confirmamos apenas veamos la acreditación.");
            }))
            .DisableAntiforgery();

        app.MapPost("/mi-obra/entrega/firmar", (HttpContext http, IAntiforgery af, Postventa svc) =>
            Procesar(http, af, "/mi-obra/entrega", async (obra, f) =>
                (await svc.FirmarActaAsync(obra, f["nombre"], f["reservas"] == "si", f["comentario"], http.Connection.RemoteIpAddress?.ToString(),
                    http.RequestAborted), "Acta firmada. ¡Gracias por confiar en nosotros!")))
            .DisableAntiforgery();

        app.MapPost("/mi-obra/encuestas/{id:int}", (int id, HttpContext http, IAntiforgery af, Postventa svc) =>
            Procesar(http, af, "/mi-obra/", async (obra, f) =>
                (await svc.ResponderEncuestaAsync(obra, id, int.TryParse(f["puntaje"], out var p) ? p : null, f["comentario"], http.RequestAborted),
                    "¡Gracias por tu respuesta!")))
            .DisableAntiforgery();

        app.MapPost("/mi-obra/permiso", (HttpContext http, IAntiforgery af, Postventa svc) =>
            Procesar(http, af, "/mi-obra/compartir", async (obra, f) =>
                (await svc.GuardarPermisoAsync(obra, f["fotos"] == "si", f["mostrar-nombre"] == "si", f["testimonio"], f["nombre"],
                    http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted), "Guardado. Lo podés cambiar cuando quieras.")))
            .DisableAntiforgery();

        app.MapGet("/mi-obra/adjuntos/{id:int}", async (int id, HttpContext http, Adjuntos svc, CancellationToken ct) =>
        {
            if (await SesionCliente.ObraAsync(http) is not int obra) return Results.Unauthorized();
            return await svc.AbrirAsync(id, obra, ct) is { } a ? Archivo(http, a) : Results.NotFound();
        });

        app.MapGet("/admin/adjuntos/{id:int}", async (int id, HttpContext http, Adjuntos svc, CancellationToken ct) =>
            await svc.AbrirAsync(id, ct) is { } a ? Archivo(http, a) : Results.NotFound())
        .RequireAuthorization();
    }

    /// <summary>Antiforgery a mano, sesión del cliente, la acción, y vuelta a la pantalla con el aviso.</summary>
    private static async Task<IResult> Procesar(HttpContext http, IAntiforgery af, string volver,
        Func<int, IFormCollection, Task<(string? Error, string Ok)>> accion)
    {
        if (await SesionCliente.ObraAsync(http) is not int obra) return Results.LocalRedirect(SesionCliente.Entrar);
        if (!http.Request.HasFormContentType || !await af.IsRequestValidAsync(http))
        {
            AvisoCliente.Poner(http, "La pantalla estuvo abierta mucho tiempo. Volvé a intentar.", false);
            return Results.LocalRedirect(volver);
        }
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var (error, ok) = await accion(obra, form);
        AvisoCliente.Poner(http, error ?? ok, error is null);
        return Results.LocalRedirect(volver);
    }

    private static List<Adjuntos.Archivo> Archivos(IFormCollection f) =>
        f.Files.Where(x => x.Length > 0).Select(x => new Adjuntos.Archivo(x.FileName, x.Length, x.OpenReadStream)).ToList();

    /// <summary>Acepta «150000», «150.000», «150000,50» y «150,000.50».</summary>
    internal static decimal? Monto(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace("$", "").Replace(" ", "");
        if (s.Contains(',') && s.Contains('.'))
            s = s.LastIndexOf(',') > s.LastIndexOf('.') ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (s.Contains(',')) s = s.Replace(',', '.');
        else if (s.Count(ch => ch == '.') > 1 || (s.Contains('.') && s.Length - s.LastIndexOf('.') == 4)) s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static IResult Archivo(HttpContext http, (Stream Contenido, string ContentType, string Nombre) a)
    {
        http.Response.Headers.CacheControl = "private, max-age=3600";
        var disp = new ContentDispositionHeaderValue(AccesoClientes.SeVeEnNavegador(a.ContentType) ? "inline" : "attachment");
        disp.SetHttpFileName(a.Nombre);
        http.Response.Headers.ContentDisposition = disp.ToString();
        // Un adjunto subido por el cliente nunca se interpreta como página.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(a.Contenido, a.ContentType, enableRangeProcessing: true);
    }
}
