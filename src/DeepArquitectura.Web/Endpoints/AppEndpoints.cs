using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Services;
using DeepArquitectura.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Endpoints;

/// <summary>
/// API de la app Android del capataz. Autenticación con token Bearer (sesión revocable desde el panel);
/// cada pedido verifica que la obra esté asignada a ese capataz.
/// </summary>
public static class AppEndpoints
{
    private const string ClaveCapataz = "capatazId";
    private static readonly System.Text.Json.JsonSerializerOptions JsonWeb = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void MapAppCapataz(this WebApplication app)
    {
        var api = app.MapGroup("/api/app").DisableAntiforgery();

        api.MapPost("/login", async (LoginRequest req, CapatazService svc, CancellationToken ct) =>
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
            var id = await http.RequestServices.GetRequiredService<CapatazService>().AutenticarAsync(Token(http), http.RequestAborted);
            if (id is null)
                return Results.Json(new ErrorApp("La sesión venció o fue cerrada. Ingresá de nuevo."), statusCode: StatusCodes.Status401Unauthorized);
            http.Items[ClaveCapataz] = id.Value;
            return await next(ctx);
        });

        conSesion.MapPost("/logout", async (HttpContext http, CapatazService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(Token(http)!, ct);
            return Results.NoContent();
        });

        conSesion.MapGet("/yo", async (HttpContext http, CapatazService svc, CancellationToken ct) =>
            await svc.PerfilAsync(Capataz(http), ct) is { } perfil ? Results.Ok(perfil) : Results.Unauthorized());

        // La obra con lo que el capataz tiene que atender (avances por validar, órdenes sin leer, tareas atrasadas).
        conSesion.MapGet("/obras/{obraId:int}", async (int obraId, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
            await campo.ObraAsync(Capataz(http), obraId, ct) is { } obra ? Results.Ok(obra) : NoAsignada());

        conSesion.MapPost("/obras/{obraId:int}/validaciones", async (int obraId, ValidacionesRequest req, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
            await campo.ValidarAsync(Capataz(http), obraId, req, ct) is { } error ? Error(error) : Results.NoContent());

        conSesion.MapPost("/obras/{obraId:int}/atrasos", async (int obraId, AtrasoRequest req, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
            await campo.InformarAtrasoAsync(Capataz(http), obraId, req, ct) is { } error ? Error(error) : Results.NoContent());

        conSesion.MapPost("/obras/{obraId:int}/sobrantes", async (int obraId, NuevoSobranteRequest req, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
        {
            var (id, error) = await campo.SobranteAsync(Capataz(http), obraId, req, ct);
            return id is int ok ? Results.Ok(new IdResponse(ok)) : Error(error!);
        });

        conSesion.MapPost("/obras/{obraId:int}/notas-pedido", async (int obraId, NotaPedidoRequest req, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
        {
            var (id, error) = await campo.NotaPedidoAsync(Capataz(http), obraId, req, ct);
            return id is int ok ? Results.Ok(new IdResponse(ok)) : Error(error!);
        });

        conSesion.MapPost("/obras/{obraId:int}/libro/leido", async (int obraId, LeidoRequest req, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
            await campo.MarcarLeidoAsync(Capataz(http), obraId, req, ct) is { } error ? Error(error) : Results.NoContent());

        // Gasto con sus comprobantes en un solo envío (multipart): o llega todo o nada, y reintentar no duplica.
        conSesion.MapPost("/obras/{obraId:int}/gastos", async (int obraId, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Error("Se esperaba un formulario multipart.");
            var form = await http.Request.ReadFormAsync(ct);
            NuevoGastoRequest? datos;
            try { datos = System.Text.Json.JsonSerializer.Deserialize<NuevoGastoRequest>(form[CamposGasto.Datos].ToString(), JsonWeb); }
            catch (System.Text.Json.JsonException) { datos = null; }
            if (datos is null) return Error("Faltan los datos del gasto.");
            var archivos = form.Files.GetFiles(CamposGasto.Archivo)
                .Select(f => new Adjuntos.Archivo(f.FileName, f.Length, f.OpenReadStream)).ToList();
            var (id, error) = await campo.GastoAsync(Capataz(http), obraId, datos, archivos, ct);
            return id is int ok ? Results.Ok(new IdResponse(ok)) : Error(error!);
        })
        .WithMetadata(new RequestSizeLimitAttribute(RutasApp.MaximoComprobantes * RutasApp.TamanoMaximoComprobante + 1024 * 1024))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = RutasApp.MaximoComprobantes * RutasApp.TamanoMaximoComprobante + 1024 * 1024 });

        // Foto de un avance de la cuadrilla, para verla antes de validar (solo de sus obras).
        conSesion.MapGet("/medios/{medioId:int}", async (int medioId, HttpContext http, CampoCapataz campo, CancellationToken ct) =>
        {
            if (await campo.AbrirFotoAsync(Capataz(http), medioId, ct) is not { } foto) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Stream(foto.Contenido, foto.ContentType);
        });

        conSesion.MapPost("/obras/{obraId:int}/registros", async (int obraId, NuevoRegistroRequest req, HttpContext http, CapatazService svc, CancellationToken ct) =>
        {
            var (id, error) = await svc.RegistrarAsync(Capataz(http), obraId, req, ct);
            return id is int ok ? Results.Ok(new NuevoRegistroResponse(ok)) : Error(error!);
        });

        conSesion.MapPut("/obras/{obraId:int}/etapas/{etapaId:int}", async (int obraId, int etapaId, CambiarEtapaRequest req, HttpContext http, CapatazService svc, CancellationToken ct) =>
        {
            var error = await svc.CambiarEtapaAsync(Capataz(http), obraId, etapaId, req.Estado, ct);
            return error is null ? Results.NoContent() : Error(error);
        });

        // Foto o video (multipart). Se acepta hasta 500 MB por archivo.
        conSesion.MapPost("/obras/{obraId:int}/medios", async (int obraId, HttpContext http, CapatazService svc, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Error("Se esperaba un formulario multipart.");
            var form = await http.Request.ReadFormAsync(ct);
            var archivo = form.Files.GetFile(CamposMedio.Archivo);
            if (archivo is null) return Error("Falta el archivo.");
            if (!Guid.TryParse(form[CamposMedio.RegistroRef], out var registroRef) || !Guid.TryParse(form[CamposMedio.MedioRef], out var medioRef))
                return Error("Faltan identificadores del envío.");

            var (id, error) = await svc.SubirMedioAsync(Capataz(http), obraId, registroRef, medioRef, archivo.FileName, archivo.Length, archivo.OpenReadStream, ct);
            return id is int ok ? Results.Ok(new { id = ok })
                // 409: la novedad todavía no llegó → la app reintenta más tarde.
                : error == "La novedad todavía no llegó." ? Results.Json(new ErrorApp(error), statusCode: StatusCodes.Status409Conflict)
                : Error(error!);
        })
        .WithMetadata(new RequestSizeLimitAttribute(RutasApp.TamanoMaximoMedio + 1024 * 1024))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = RutasApp.TamanoMaximoMedio + 1024 * 1024 });
    }

    /// <summary>Fotos y videos para el panel: solo con sesión de administrador, con rangos para poder adelantar videos.</summary>
    public static void MapMediosPanel(this WebApplication app)
    {
        app.MapGet("/admin/medios/{id:int}", async (int id, IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, HttpContext http, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var m = await db.Medios.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Ruta, x.ContentType }).SingleOrDefaultAsync(ct);
            if (m is null) return Results.NotFound();
            var stream = await almacen.AbrirAsync(m.Ruta, ct);
            if (stream is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Stream(stream, m.ContentType, enableRangeProcessing: true);
        })
        .RequireAuthorization();
    }

    private static string? Token(HttpContext http)
    {
        var h = http.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : null;
    }

    private static int Capataz(HttpContext http) => (int)http.Items[ClaveCapataz]!;

    private static IResult NoAsignada() =>
        Results.Json(new ErrorApp("Esta obra no está asignada a tu usuario."), statusCode: StatusCodes.Status403Forbidden);

    private static IResult Error(string mensaje) =>
        mensaje is "Obra no asignada." or "La obra no es tuya." ? NoAsignada() : Results.BadRequest(new ErrorApp(mensaje));
}
