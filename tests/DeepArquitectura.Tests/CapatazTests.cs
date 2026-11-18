using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Tests;

public sealed class CapatazTests : IDisposable
{
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly TestDb _db = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "deeparq-medios-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new(new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero));
    private readonly CapatazService _svc;
    private readonly int _obraA, _obraB, _etapaA;

    public CapatazTests()
    {
        _svc = new CapatazService(_db, new AlmacenMediosLocal(_carpeta), new EstudioService(_db), _reloj);
        using var db = _db.CreateDbContext();
        var cliente = new Cliente { Nombre = "Cliente" };
        var a = new Obra { Codigo = "OB-2026-001", Nombre = "Casa Pérez", Cliente = cliente, Etapas = [new EtapaObra { Orden = 1, Nombre = "Platea" }] };
        var b = new Obra { Codigo = "OB-2026-002", Nombre = "Local Gómez", Cliente = cliente };
        db.Obras.AddRange(a, b);
        db.SaveChanges();
        (_obraA, _obraB, _etapaA) = (a.Id, b.Id, a.Etapas[0].Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private async Task<(int Id, string Token)> CapatazConSesion(string usuario = "jperez", string pin = "123456")
    {
        var (id, error) = await _svc.CrearAsync("Juan Pérez", usuario, "351", pin, [_obraA]);
        Assert.Null(error);
        var login = await _svc.LoginAsync(usuario.ToUpperInvariant(), pin, "Pixel 8");
        Assert.NotNull(login);
        return (id!.Value, login!.Token);
    }

    [Fact]
    public async Task Login_DevuelveSoloLasObrasAsignadas()
    {
        var (_, token) = await CapatazConSesion();
        var id = await _svc.AutenticarAsync(token);
        var perfil = await _svc.PerfilAsync(id!.Value);

        Assert.Equal(["OB-2026-001"], perfil!.Obras.Select(o => o.Codigo));
        Assert.NotNull(await _svc.ObraAsync(id.Value, _obraA));
        Assert.Null(await _svc.ObraAsync(id.Value, _obraB)); // no asignada
    }

    [Theory]
    [InlineData("jperez", "999999")]
    [InlineData("noexiste", "123456")]
    [InlineData("", "")]
    public async Task Login_Rechaza(string usuario, string pin)
    {
        await CapatazConSesion();
        Assert.Null(await _svc.LoginAsync(usuario, pin, null));
    }

    [Theory]
    [InlineData("Ju", "123456", "al menos 3")]
    [InlineData("juan perez", "123456", "letras, números")]
    [InlineData("juan", "123", "PIN")]
    public async Task Crear_Valida(string usuario, string pin, string motivo)
    {
        var (id, error) = await _svc.CrearAsync("Juan", usuario, null, pin, []);
        Assert.Null(id);
        Assert.Contains(motivo, error);
    }

    [Fact]
    public async Task Crear_UsuarioRepetido()
    {
        await CapatazConSesion();
        var (_, error) = await _svc.CrearAsync("Otro", "JPerez", null, "654321", []);
        Assert.Contains("Ya existe", error);
    }

    [Fact]
    public async Task Sesion_SeCortaAlDesactivarCambiarPinOVencer()
    {
        var (id, token) = await CapatazConSesion();
        await _svc.CambiarActivoAsync(id, false);
        Assert.Null(await _svc.AutenticarAsync(token));
        Assert.Null(await _svc.LoginAsync("jperez", "123456", null)); // desactivado no entra

        await _svc.CambiarActivoAsync(id, true);
        token = (await _svc.LoginAsync("jperez", "123456", null))!.Token;
        Assert.Null(await _svc.CambiarPinAsync(id, "777777"));
        Assert.Null(await _svc.AutenticarAsync(token));

        token = (await _svc.LoginAsync("jperez", "777777", null))!.Token;
        _reloj.Ahora += CapatazService.DuracionSesion + TimeSpan.FromDays(1);
        Assert.Null(await _svc.AutenticarAsync(token));
    }

    [Fact]
    public async Task LimpiarSesiones_BorraSoloLasVencidas()
    {
        var (_, vieja) = await CapatazConSesion();
        _reloj.Ahora += CapatazService.DuracionSesion + TimeSpan.FromDays(1);
        var nueva = (await _svc.LoginAsync("jperez", "123456", null))!.Token;

        Assert.Equal(1, await _svc.LimpiarSesionesVencidasAsync());
        Assert.Null(await _svc.AutenticarAsync(vieja));
        Assert.NotNull(await _svc.AutenticarAsync(nueva));
        Assert.Equal(0, await _svc.LimpiarSesionesVencidasAsync());
    }

    [Fact]
    public async Task Sesion_UsoDeslizanteNoVenceSiSeUsa()
    {
        var (_, token) = await CapatazConSesion();
        for (var i = 0; i < 4; i++)
        {
            _reloj.Ahora += TimeSpan.FromDays(60);
            Assert.NotNull(await _svc.AutenticarAsync(token));
        }
    }

    [Fact]
    public async Task QuitarObra_CortaElAccesoEnElMomento()
    {
        var (id, _) = await CapatazConSesion();
        await _svc.AsignarAsync(id, _obraA, false);
        var (rid, error) = await _svc.RegistrarAsync(id, _obraA, new NuevoRegistroRequest(Guid.NewGuid(), DateTime.UtcNow, "Hormigonado", null, null));
        Assert.Null(rid);
        Assert.Equal("Obra no asignada.", error);
    }

    [Fact]
    public async Task Registrar_EsIdempotentePorRef()
    {
        var (id, _) = await CapatazConSesion();
        var req = new NuevoRegistroRequest(Guid.NewGuid(), _reloj.GetUtcNow().UtcDateTime.AddHours(-2), "Llenado de platea", "Se hormigonó", _etapaA);

        var (a, _) = await _svc.RegistrarAsync(id, _obraA, req);
        var (b, _) = await _svc.RegistrarAsync(id, _obraA, req); // reintento del teléfono

        Assert.Equal(a, b);
        await using var db = _db.CreateDbContext();
        var r = await db.Registros.SingleAsync();
        Assert.Equal(id, r.CapatazId);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddHours(-2), r.Fecha); // respeta la hora del teléfono
    }

    [Fact]
    public async Task Registrar_FechaFuturaSeReemplazaPorAhora_YEtapaDeOtraObraNo()
    {
        var (id, _) = await CapatazConSesion();
        await _svc.RegistrarAsync(id, _obraA, new NuevoRegistroRequest(Guid.NewGuid(), _reloj.GetUtcNow().UtcDateTime.AddDays(3), "x", null, null));
        await using (var db = _db.CreateDbContext())
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, (await db.Registros.SingleAsync()).Fecha);

        await _svc.AsignarAsync(id, _obraB, true);
        var (rid, error) = await _svc.RegistrarAsync(id, _obraB, new NuevoRegistroRequest(Guid.NewGuid(), DateTime.UtcNow, "x", null, _etapaA));
        Assert.Null(rid);
        Assert.Contains("etapa", error);
    }

    [Fact]
    public async Task SubirMedio_GuardaArchivoUnaSolaVez()
    {
        var (id, _) = await CapatazConSesion();
        var registroRef = Guid.NewGuid();
        var medioRef = Guid.NewGuid();
        byte[] bytes = [0xFF, 0xD8, 0xFF, 1, 2, 3];
        Stream Abrir() => new MemoryStream(bytes);

        // La foto llega antes que la novedad (cola desordenada) → 409 y la app reintenta.
        var (antes, error) = await _svc.SubirMedioAsync(id, _obraA, registroRef, medioRef, "IMG_1.jpg", bytes.Length, Abrir);
        Assert.Null(antes);
        Assert.Equal("La novedad todavía no llegó.", error);

        await _svc.RegistrarAsync(id, _obraA, new NuevoRegistroRequest(registroRef, DateTime.UtcNow, "Foto desde obra", null, null));
        var (m1, _) = await _svc.SubirMedioAsync(id, _obraA, registroRef, medioRef, "IMG_1.jpg", bytes.Length, Abrir);
        var (m2, _) = await _svc.SubirMedioAsync(id, _obraA, registroRef, medioRef, "IMG_1.jpg", bytes.Length, Abrir);
        Assert.Equal(m1, m2);

        await using var db = _db.CreateDbContext();
        var medio = await db.Medios.SingleAsync();
        Assert.Equal(TipoMedio.Foto, medio.Tipo);
        Assert.Equal("image/jpeg", medio.ContentType);
        Assert.StartsWith($"obra-{_obraA}/2026/11/", medio.Ruta);
        await using var guardado = await new AlmacenMediosLocal(_carpeta).AbrirAsync(medio.Ruta);
        using var ms = new MemoryStream();
        await guardado!.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Theory]
    [InlineData("virus.exe", "Formato no admitido")]
    [InlineData("video.mp4", "500 MB")]
    public async Task SubirMedio_RechazaFormatoOTamano(string nombre, string motivo)
    {
        var (id, _) = await CapatazConSesion();
        var registroRef = Guid.NewGuid();
        await _svc.RegistrarAsync(id, _obraA, new NuevoRegistroRequest(registroRef, DateTime.UtcNow, "x", null, null));
        var (m, error) = await _svc.SubirMedioAsync(id, _obraA, registroRef, Guid.NewGuid(), nombre,
            nombre.EndsWith(".mp4") ? RutasApp.TamanoMaximoMedio + 1 : 10, () => new MemoryStream(new byte[10]));
        Assert.Null(m);
        Assert.Contains(motivo, error);
    }

    [Fact]
    public Task AlmacenLocal_NoPermiteSalirDeLaCarpeta() =>
        Assert.ThrowsAsync<ArgumentException>(() => new AlmacenMediosLocal(_carpeta).GuardarAsync("../fuera.jpg", new MemoryStream(), "image/jpeg"));

    [Fact]
    public async Task CambiarEtapa_DesdeLaApp()
    {
        var (id, _) = await CapatazConSesion();
        Assert.Null(await _svc.CambiarEtapaAsync(id, _obraA, _etapaA, "Completada"));
        Assert.Equal("Estado de etapa inválido.", await _svc.CambiarEtapaAsync(id, _obraA, _etapaA, "Omitida"));
        Assert.Equal("Obra no asignada.", await _svc.CambiarEtapaAsync(id, _obraB, _etapaA, "EnCurso"));

        var obra = await _svc.ObraAsync(id, _obraA);
        Assert.Equal("Completada", obra!.Etapas.Single().Estado);
        Assert.Equal(100, obra.PorcentajeAvance);
    }
}
