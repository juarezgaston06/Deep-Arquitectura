using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeepArquitectura.Web.Endpoints;

/// <summary>
/// API de la app de los albañiles. Token Bearer propio (sesión revocable desde el panel); cada pedido verifica que la
/// obra esté asignada al albañil y sea de su capataz.
/// </summary>
public static class AlbanilEndpoints
{
    private const string ClaveAlbanil = "albanilId";

    public static void MapAppAlbanil(this WebApplication app)
    {
        var api = app.MapGroup("/api/albanil").DisableAntiforgery();

        api.MapPost("/login", async (LoginRequest req, AlbanilService svc, CancellationToken ct) =>
        {
            var r = await svc.LoginAsync(req.Usuario, req.Pin, req.Dispositivo, ct);
            return r is null
                ? Results.Json(new ErrorApp("Usuario o PIN incorrectos, o el usuario está desactivado."), statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(r);
        })
        .RequireRateLimiting("app-login");

        var conSesion = api.MapGroup("").AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var id = await http.RequestServices.GetRequiredService<AlbanilService>().AutenticarAsync(Token(http), http.RequestAborted);
            if (id is null)
                return Results.Json(new ErrorApp("La sesión venció o fue cerrada. Ingresá de nuevo."), statusCode: StatusCodes.Status401Unauthorized);
            http.Items[ClaveAlbanil] = id.Value;
            return await next(ctx);
        });

        conSesion.MapPost("/logout", async (HttpContext http, AlbanilService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(Token(http)!, ct);
            return Results.NoContent();
        });

        conSesion.MapGet("/yo", async (HttpContext http, AlbanilService svc, CancellationToken ct) =>
            await svc.PerfilAsync(Albanil(http), ct) is { } perfil ? Results.Ok(perfil) : Results.Unauthorized());

        conSesion.MapGet("/obras/{obraId:int}", async (int obraId, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            await svc.ObraAsync(Albanil(http), obraId, ct) is { } obra ? Results.Ok(obra) : NoAsignada());

        conSesion.MapPost("/obras/{obraId:int}/avances", async (int obraId, NuevoAvanceRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
        {
            var (r, error) = await svc.RegistrarAvanceAsync(Albanil(http), obraId, req, ct);
            return r is not null ? Results.Ok(r) : Error(error!);
        });

        conSesion.MapPost("/obras/{obraId:int}/jornada/entrada", async (int obraId, FichadaRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.FicharEntradaAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/jornada/salida", async (int obraId, FichadaRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.FicharSalidaAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/pedidos", async (int obraId, NuevoPedidoRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.PedirMaterialAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/problemas", async (int obraId, NuevoProblemaRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.ReportarProblemaAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/partes", async (int obraId, ParteRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.GuardarParteAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/recepciones", async (int obraId, NuevaRecepcionRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.RegistrarRecepcionAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/controles", async (int obraId, RespuestaControlRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.ResponderControlAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/notas-pedido", async (int obraId, NotaPedidoRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.NotaPedidoAsync(Albanil(http), obraId, req, ct)));

        conSesion.MapPost("/obras/{obraId:int}/libro/leido", async (int obraId, LeidoRequest req, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            Resultado(await svc.MarcarLeidoAsync(Albanil(http), obraId, req, ct)));

        // Última foto de un punto fijo, para encuadrar igual que la vez anterior.
        conSesion.MapGet("/obras/{obraId:int}/puntos/{puntoId:int}/ultima", async (int obraId, int puntoId, HttpContext http, AlbanilService svc, CancellationToken ct) =>
            await svc.UltimaFotoPuntoAsync(Albanil(http), obraId, puntoId, ct) is { } f
                ? Results.Stream(f.Contenido, f.ContentType)
                : Results.NotFound());

        // Foto o video (multipart) de un avance o de un problema. Hasta 500 MB por archivo.
        conSesion.MapPost("/obras/{obraId:int}/medios", async (int obraId, HttpContext http, AlbanilService svc, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Error("Se esperaba un formulario multipart.");
            var form = await http.Request.ReadFormAsync(ct);
            var archivo = form.Files.GetFile(CamposMedio.Archivo);
            if (archivo is null) return Error("Falta el archivo.");
            if (!Guid.TryParse(form[CamposMedio.RegistroRef], out var registroRef) || !Guid.TryParse(form[CamposMedio.MedioRef], out var medioRef))
                return Error("Faltan identificadores del envío.");
            return Resultado(await svc.SubirMedioAsync(Albanil(http), obraId, registroRef, medioRef, archivo.FileName, archivo.Length, archivo.OpenReadStream, ct));
        })
        .WithMetadata(new RequestSizeLimitAttribute(RutasApp.TamanoMaximoMedio + 1024 * 1024))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = RutasApp.TamanoMaximoMedio + 1024 * 1024 });
    }

    /// <summary>Fotos y videos del portal del cliente: solo los que el estudio dejó visibles en esa obra.</summary>
    public static void MapPortalCliente(this WebApplication app)
    {
        app.MapGet("/seguimiento/{token}/medios/{id:int}", async (string token, int id, PortalCliente portal, HttpContext http, CancellationToken ct) =>
        {
            if (await portal.MedioAsync(token, id, ct) is not { } m) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            return Results.Stream(m.Contenido, m.ContentType, enableRangeProcessing: true);
        });
    }

    private static string? Token(HttpContext http)
    {
        var h = http.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : null;
    }

    private static int Albanil(HttpContext http) => (int)http.Items[ClaveAlbanil]!;

    private static IResult Resultado((int? Id, string? Error) r) =>
        r.Id is int id ? Results.Ok(new IdResponse(id)) : Error(r.Error!);

    private static IResult NoAsignada() =>
        Results.Json(new ErrorApp("Esta obra no está asignada a tu usuario."), statusCode: StatusCodes.Status403Forbidden);

    private static IResult Error(string mensaje) => mensaje switch
    {
        AlbanilService.ErrorNoAsignada => NoAsignada(),
        // 409: lo que este envío referencia todavía no llegó → la app reintenta (y vuelve a mandar lo anterior).
        AlbanilService.ErrorNovedadPendiente or AlbanilService.ErrorEntradaPendiente =>
            Results.Json(new ErrorApp(mensaje), statusCode: StatusCodes.Status409Conflict),
        _ => Results.BadRequest(new ErrorApp(mensaje)),
    };
}
