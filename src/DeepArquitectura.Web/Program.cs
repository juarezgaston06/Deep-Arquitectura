using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DeepArquitectura.Web.Agents;
using DeepArquitectura.Web.Components;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Endpoints;
using DeepArquitectura.Web.Presupuestos;
using DeepArquitectura.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// Utilidad: `dotnet run -- hash-password "mi-clave"` imprime el hash para Admin__PasswordHash.
// Sin argumento la pide por teclado, oculta, para que no quede en el historial de la consola.
if (args is ["hash-password", ..])
{
    var clave = args.Length > 1 ? args[1] : LeerOculto("Contraseña: ");
    if (string.IsNullOrEmpty(clave)) { Console.Error.WriteLine("Contraseña vacía."); return; }
    Console.WriteLine(AdminCredentials.Hashear(clave));
    return;
}

static string LeerOculto(string prompt)
{
    Console.Error.Write(prompt);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new System.Text.StringBuilder();
    while (Console.ReadKey(intercept: true) is var k && k.Key != ConsoleKey.Enter)
    {
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    Console.Error.WriteLine();
    return sb.ToString();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Monitoreo (Application Insights): solo si Azure dejó la cadena de conexión en la configuración ---
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// --- Datos ---
var connectionString = builder.Configuration.GetConnectionString("DeepArquitectura")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:DeepArquitectura");
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5))); // Azure SQL serverless puede estar pausada

builder.Services.AddSingleton<EstudioService>();
builder.Services.AddSingleton<PresupuestoService>();

// --- App del capataz: fotos/videos en Azure Blob (producción) o en disco (desarrollo) ---
builder.Services.Configure<MediosOptions>(builder.Configuration.GetSection(MediosOptions.Seccion));
builder.Services.AddSingleton<IAlmacenMedios>(sp =>
{
    var o = builder.Configuration.GetSection(MediosOptions.Seccion).Get<MediosOptions>() ?? new MediosOptions();
    if (!string.IsNullOrWhiteSpace(o.BlobUrl))
        return new AlmacenMediosBlob(new Azure.Storage.Blobs.BlobContainerClient(
            new Uri($"{o.BlobUrl.TrimEnd('/')}/{o.Contenedor}"), new Azure.Identity.DefaultAzureCredential()));
    if (!string.IsNullOrWhiteSpace(o.BlobConnectionString))
        return new AlmacenMediosBlob(new Azure.Storage.Blobs.BlobContainerClient(o.BlobConnectionString, o.Contenedor));
    var carpeta = Path.IsPathRooted(o.CarpetaLocal) ? o.CarpetaLocal : Path.Combine(builder.Environment.ContentRootPath, o.CarpetaLocal);
    return new AlmacenMediosLocal(carpeta);
});
builder.Services.AddSingleton<CapatazService>();
// --- App de los albañiles (cuadrilla de cada capataz) y su gestión desde el panel ---
builder.Services.AddSingleton<AlbanilService>();
builder.Services.AddSingleton<CampoService>();
builder.Services.AddHostedService<LimpiezaSesiones>();

// --- Circuito de datos de obra: validación, controles, libro de obra, avance/certificados, portal, clima, resumen ---
builder.Services.AddSingleton<ValidacionAvances>();
builder.Services.AddSingleton<ControlesObra>();
builder.Services.AddSingleton<LibroObra>();
builder.Services.AddSingleton<AvanceObra>();
builder.Services.AddSingleton<PortalCliente>();
builder.Services.Configure<ClimaOptions>(builder.Configuration.GetSection(ClimaOptions.Seccion));
builder.Services.AddHttpClient<ClimaService>();
builder.Services.AddHostedService<ActualizacionClima>();

// --- App del cliente (/mi-obra): web instalable con código de obra + PIN ---
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<AppCliente>();
builder.Services.AddSingleton<GestionCliente>();

// --- Datos del proceso: relevamientos, costos reales, trámites, entrega, garantía, encuestas, pagos informados ---
builder.Services.AddSingleton<Adjuntos>();
builder.Services.AddSingleton<Relevamientos>();
builder.Services.AddSingleton<CostosObra>();
builder.Services.AddSingleton<TramitesObra>();
builder.Services.AddSingleton<Postventa>();
builder.Services.AddSingleton<Indicadores>();
// App del capataz (táctil): validar, atrasos, gastos, sobrantes, libro de obra.
builder.Services.AddSingleton<CampoCapataz>();

// --- Auth: un único administrador con cookie ---
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Seccion));
builder.Services.AddSingleton<AdminCredentials>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/admin/login";
        o.AccessDeniedPath = "/admin/login";
        o.Cookie.Name = "deeparq.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // En producción la cookie solo viaja por HTTPS; en desarrollo se permite el perfil http.
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        // Si la contraseña cambió después de iniciar sesión, la cookie vieja deja de valer.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var creds = ctx.HttpContext.RequestServices.GetRequiredService<AdminCredentials>();
            var sello = await creds.SelloAsync(ctx.HttpContext.RequestAborted);
            if (sello is null || ctx.Principal?.FindFirst(AdminCredentials.ClaimSello)?.Value != sello)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    // Segunda cookie, solo para /mi-obra: un cliente nunca queda con sesión del panel ni al revés.
    .AddCookie(SesionCliente.Esquema, o => SesionCliente.Configurar(o, builder.Environment.IsDevelopment()));
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
// Las pantallas interactivas (SignalR) no vuelven a leer la cookie: revalidamos cada minuto
// para que un cambio de contraseña también cierre los paneles abiertos en otros dispositivos.
builder.Services.AddScoped<AuthenticationStateProvider, RevalidarSesionAdmin>();

// --- Agente Community Manager ---
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.Seccion));
builder.Services.AddSingleton<CmTools>();
builder.Services.AddSingleton<CommunityManagerAgent>();
// Resumen diario: por reglas siempre; con Azure OpenAI además lo redacta.
builder.Services.AddSingleton(sp => new ResumenDiario(
    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>(),
    sp.GetRequiredService<ValidacionAvances>(),
    ClienteChat.Crear(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAIOptions>>().Value)));

// --- Anti-spam / fuerza bruta ---
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // En los formularios del panel, en vez de una página 429 en blanco volvemos al formulario con el aviso.
    o.OnRejected = (ctx, _) =>
    {
        var path = ctx.HttpContext.Request.Path;
        var destino = path == "/cuenta/login" ? "/admin/login?error=limite"
            : path == "/cuenta/clave" ? "/admin/cuenta?error=limite"
            : path == "/mi-obra/ingresar" ? SesionCliente.Entrar + "?error=limite" : null;
        if (destino is not null) ctx.HttpContext.Response.Redirect(destino);
        return ValueTask.CompletedTask;
    };
    o.AddPolicy("publico", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
    o.AddPolicy("app-login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
    o.AddPolicy("login-cliente", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
});

// App Service termina TLS en el front-end: confiar en X-Forwarded-* para IP real y esquema https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

if (app.Configuration.GetValue("Database:MigrarAlIniciar", true))
{
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
// Recuperación: `dotnet run -- reset-password` borra la contraseña cambiada desde el panel
// y vuelve a valer la de la configuración (Admin:PasswordHash).
if (args is ["reset-password"])
{
    var restablecida = await app.Services.GetRequiredService<AdminCredentials>().RestablecerAsync();
    Console.WriteLine(restablecida
        ? "Listo: vuelve a valer la contraseña de Admin:PasswordHash."
        : "No había contraseña cambiada desde el panel; ya vale la de Admin:PasswordHash.");
    return;
}

// Primera escala arancelaria (PDF 1er semestre 2026) si la base todavía no tiene ninguna.
await app.Services.GetRequiredService<PresupuestoService>().AsegurarArancelInicialAsync();

// Página 404 propia para navegación (no para la API).
app.UseWhen(ctx => HttpMethods.IsGet(ctx.Request.Method) && !ctx.Request.Path.StartsWithSegments("/api"),
    b => b.UseStatusCodePagesWithReExecute("/404"));

// En desarrollo, el emulador de Android llega por http://10.0.2.2:5180 (no confía en el certificado local):
// la API de la app no se redirige a https. En producción todo va por HTTPS.
app.UseWhen(ctx => !(app.Environment.IsDevelopment()
        && (ctx.Request.Path.StartsWithSegments("/api/app") || ctx.Request.Path.StartsWithSegments("/api/albanil"))),
    b => b.UseHttpsRedirection());
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// URLs viejas del sitio estático (Vite) → rutas nuevas, para no perder links compartidos ni SEO.
app.MapGet("/index.html", () => Results.Redirect("/", permanent: true));
app.MapGet("/electricidad/index.html", () => Results.Redirect("/electricidad", permanent: true));
app.MapGet("/gas/index.html", () => Results.Redirect("/gas", permanent: true));
app.MapGet("/logo.png", () => Results.Redirect("/img/logo.png", permanent: true));

app.MapStaticAssets();
app.MapEndpointsEstudio();
app.MapAppCapataz();
app.MapAppAlbanil();
app.MapPortalCliente();
app.MapAppCliente();
app.MapDatosProcesoCliente();
app.MapMediosPanel();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
