using DeepArquitectura.Web.Agents;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Endpoints;
using DeepArquitectura.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeepArquitectura.Tests;

/// <summary>Fábrica de contextos sobre una única conexión SQLite en memoria (vive lo que vive el test).</summary>
public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);
    public void Dispose() => _conn.Dispose();
}

public class EstudioTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly EstudioService _svc;

    public EstudioTests() => _svc = new EstudioService(_db);
    public void Dispose() => _db.Dispose();

    private async Task<int> CrearClienteAsync(string nombre = "Ana Cliente")
    {
        await using var db = _db.CreateDbContext();
        var c = new Cliente { Nombre = nombre, Email = "ana@example.com", Direccion = "Calle Falsa 123", Telefono = "351000000" };
        db.Clientes.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task RegistrarConsulta_GuardaNuevaConFecha()
    {
        var id = await _svc.RegistrarConsultaAsync("  Juan  ", "juan@example.com", null, "Necesito planos", AreaConsulta.Gas);

        await using var db = _db.CreateDbContext();
        var c = await db.Consultas.SingleAsync(x => x.Id == id);
        Assert.Equal("Juan", c.Nombre);
        Assert.Equal(EstadoConsulta.Nueva, c.Estado);
        Assert.Equal(AreaConsulta.Gas, c.Area);
        Assert.True(c.CreadoEn > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ConvertirEnCliente_CreaClienteYMarcaConvertida()
    {
        var consultaId = await _svc.RegistrarConsultaAsync("Juan", "juan@example.com", null, "Hola", AreaConsulta.General);

        var clienteId = await _svc.ConvertirEnClienteAsync(consultaId);

        await using var db = _db.CreateDbContext();
        var cliente = await db.Clientes.Include(c => c.Consultas).SingleAsync(c => c.Id == clienteId);
        Assert.Equal("Juan", cliente.Nombre);
        Assert.Equal(OrigenCliente.Web, cliente.Origen);
        Assert.Single(cliente.Consultas);
        Assert.Equal(EstadoConsulta.Convertida, cliente.Consultas[0].Estado);
    }

    [Fact]
    public async Task ConvertirEnCliente_ReusaClienteConMismoEmail_YEsIdempotente()
    {
        var existente = await CrearClienteAsync();
        var consultaId = await _svc.RegistrarConsultaAsync("Ana", "ana@example.com", null, "Otra obra", AreaConsulta.Electricidad);

        var id1 = await _svc.ConvertirEnClienteAsync(consultaId);
        var id2 = await _svc.ConvertirEnClienteAsync(consultaId);

        Assert.Equal(existente, id1);
        Assert.Equal(existente, id2);
        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.Clientes.CountAsync());
    }

    [Fact]
    public async Task CrearObra_GeneraCodigoCorrelativoYEtapasDePlantilla()
    {
        var clienteId = await CrearClienteAsync();

        var o1 = await _svc.CrearObraAsync(new Obra { ClienteId = clienteId, Nombre = "Planos casa", Servicio = TipoServicio.PlanosMunicipales });
        var o2 = await _svc.CrearObraAsync(new Obra { ClienteId = clienteId, Nombre = "Gas casa", Servicio = TipoServicio.Gas });

        var anio = Textos.HoyLocal().Year;
        Assert.Equal($"OB-{anio}-001", o1.Codigo);
        Assert.Equal($"OB-{anio}-002", o2.Codigo);
        Assert.Equal(PlantillasEtapas.Para(TipoServicio.PlanosMunicipales).Count, o1.Etapas.Count);
        Assert.Equal(Enumerable.Range(1, o2.Etapas.Count), o2.Etapas.Select(e => e.Orden));
    }

    [Fact]
    public async Task CambiarEstadoEtapa_CompletaFechasYPasaObraAEnCurso()
    {
        var clienteId = await CrearClienteAsync();
        var obra = await _svc.CrearObraAsync(new Obra { ClienteId = clienteId, Nombre = "Tablero", Servicio = TipoServicio.Electricidad });
        var etapaId = obra.Etapas.OrderBy(e => e.Orden).First().Id;

        await _svc.CambiarEstadoEtapaAsync(etapaId, EstadoEtapa.Completada);

        await using var db = _db.CreateDbContext();
        var o = await db.Obras.Include(x => x.Etapas).SingleAsync(x => x.Id == obra.Id);
        var etapa = o.Etapas.Single(e => e.Id == etapaId);
        Assert.Equal(EstadoObra.EnCurso, o.Estado);
        Assert.NotNull(o.FechaInicio);
        Assert.NotNull(etapa.FechaFin);
        Assert.True(o.PorcentajeAvance > 0);
    }

    [Theory]
    [InlineData(new[] { EstadoEtapa.Completada, EstadoEtapa.Pendiente }, 50)]
    [InlineData(new[] { EstadoEtapa.Completada, EstadoEtapa.Omitida }, 100)]
    [InlineData(new[] { EstadoEtapa.Omitida }, 100)]
    [InlineData(new EstadoEtapa[0], 0)]
    [InlineData(new[] { EstadoEtapa.Completada, EstadoEtapa.EnCurso, EstadoEtapa.Pendiente }, 33)]
    public void CalcularAvance_IgnoraOmitidas(EstadoEtapa[] estados, int esperado)
    {
        var etapas = estados.Select(e => new EtapaObra { Estado = e }).ToList();
        Assert.Equal(esperado, Obra.CalcularAvance(etapas));
    }

    [Fact]
    public async Task CmTools_NoExponeDatosPersonalesDelCliente()
    {
        var clienteId = await CrearClienteAsync("Nombre Secreto");
        var obra = await _svc.CrearObraAsync(new Obra
        {
            ClienteId = clienteId, Nombre = "Obra de Nombre Secreto", Servicio = TipoServicio.Relevamiento,
            Estado = EstadoObra.EnCurso, Localidad = "Unquillo", Direccion = "Calle Falsa 123",
        });
        await using (var db = _db.CreateDbContext())
        {
            db.Registros.Add(new RegistroAvance { ObraId = obra.Id, Fecha = DateTime.UtcNow, Titulo = "Medición terminada", UsableEnRedes = true });
            db.Registros.Add(new RegistroAvance { ObraId = obra.Id, Fecha = DateTime.UtcNow, Titulo = "Interno", UsableEnRedes = false });
            await db.SaveChangesAsync();
        }
        var tools = new CmTools(_db);

        var obras = await tools.ListarObras();
        var novedades = await tools.ListarNovedadesParaRedes();

        var json = System.Text.Json.JsonSerializer.Serialize(new { obras, novedades });
        Assert.DoesNotContain("Secreto", json);
        Assert.DoesNotContain("Calle Falsa", json);
        Assert.DoesNotContain("ana@example.com", json);
        Assert.Contains("Unquillo", json);
        Assert.Single(novedades);
    }

    [Fact]
    public async Task CmTools_GuardarBorrador_IgnoraObraInexistente()
    {
        var tools = new CmTools(_db);

        var r = await tools.GuardarBorrador("Tip gas", RedSocial.Instagram, FormatoPublicacion.Reel, PilarContenido.Seguridad, "contenido", "2026-11-20", obraId: 999);

        await using var db = _db.CreateDbContext();
        var p = await db.Publicaciones.SingleAsync();
        Assert.Contains($"#{p.Id}", r);
        Assert.Null(p.ObraId);
        Assert.True(p.GeneradaPorIA);
        Assert.Equal(EstadoPublicacion.Borrador, p.Estado);
        Assert.Equal(new DateOnly(2026, 11, 20), p.FechaProgramada);
    }

    [Fact]
    public void ValidarConsulta_RechazaDatosInvalidos()
    {
        var ok = PublicEndpoints.Validar(new("Ana", "ana@example.com", null, "Hola", null, null));
        var mal = PublicEndpoints.Validar(new("", "no-mail", null, " ", null, null));

        Assert.Empty(ok);
        Assert.Equal(["email", "mensaje", "nombre"], mal.Keys.Order());
    }

    private AdminCredentials Credenciales(string? hash = null) => new(
        Options.Create(new AdminOptions { Usuario = "Gaston", PasswordHash = hash ?? AdminCredentials.Hashear("clave-segura") }), _db);

    [Fact]
    public async Task AdminCredentials_ValidaSoloConClaveCorrecta()
    {
        var creds = Credenciales();

        Assert.True(await creds.ValidarAsync("gaston", "clave-segura"));
        Assert.False(await creds.ValidarAsync("gaston", "otra"));
        Assert.False(await creds.ValidarAsync("otro", "clave-segura"));
        Assert.False(await new AdminCredentials(Options.Create(new AdminOptions()), _db).ValidarAsync("", ""));
    }

    [Theory]
    [InlineData("mal-actual", "nueva-clave-1", "nueva-clave-1", ErrorCambioClave.ActualIncorrecta)]
    [InlineData("clave-segura", "corta", "corta", ErrorCambioClave.MuyCorta)]
    [InlineData("clave-segura", "nueva-clave-1", "nueva-clave-2", ErrorCambioClave.NoCoinciden)]
    [InlineData("clave-segura", "clave-segura", "clave-segura", ErrorCambioClave.IgualALaActual)]
    public async Task CambiarClave_RechazaDatosInvalidos(string actual, string nueva, string repetida, ErrorCambioClave motivo)
    {
        var creds = Credenciales();
        Assert.Equal(motivo, await creds.CambiarAsync(actual, nueva, repetida));
        Assert.True(await creds.ValidarAsync("gaston", "clave-segura")); // no cambió nada
    }

    [Fact]
    public async Task Login_HashMalConfigurado_NoRompe()
    {
        // Antes: FormatException → error 500 en el login.
        Assert.False(await Credenciales("esto-no-es-un-hash").ValidarAsync("gaston", "lo-que-sea"));
    }

    [Theory]
    [InlineData(null, "/admin")]
    [InlineData("/admin/presupuestos/3?x=1", "/admin/presupuestos/3?x=1")]
    [InlineData("/admin", "/admin")]
    [InlineData("https://otro-sitio.com/admin", "/admin")]
    [InlineData("//otro-sitio.com/admin", "/admin")]
    [InlineData("/administrador-falso", "/admin")]
    [InlineData("/admin/\\otro-sitio.com", "/admin")]
    [InlineData("/admin/login?error=1", "/admin")]
    public void Login_DestinoSeguro_SoloPaginasDelPanel(string? returnUrl, string esperado) =>
        Assert.Equal(esperado, DeepArquitectura.Web.Endpoints.PublicEndpoints.DestinoSeguro(returnUrl));

    [Fact]
    public async Task CambiarClave_ReemplazaLaDeConfiguracionYCambiaElSello()
    {
        var creds = Credenciales();
        var selloAnterior = await creds.SelloAsync();

        Assert.Null(await creds.CambiarAsync("clave-segura", "nueva-clave-1", "nueva-clave-1"));

        Assert.True(await creds.ValidarAsync("gaston", "nueva-clave-1"));
        Assert.False(await creds.ValidarAsync("gaston", "clave-segura"));
        Assert.NotEqual(selloAnterior, await creds.SelloAsync());

        // Otra instancia (p. ej. reinicio de la app) lee la clave de la base, no la de la configuración.
        var reiniciada = Credenciales();
        Assert.True(await reiniciada.ValidarAsync("gaston", "nueva-clave-1"));
        Assert.Equal(await creds.SelloAsync(), await reiniciada.SelloAsync());

        // Recuperación: vuelve a valer la de la configuración.
        Assert.True(await reiniciada.RestablecerAsync());
        Assert.True(await reiniciada.ValidarAsync("gaston", "clave-segura"));
    }

    [Fact]
    public void Instrucciones_IncluyenGuiaDeMarca()
    {
        var prompt = CommunityManagerAgent.Instrucciones("COLOR #4A3728");
        Assert.Contains("COLOR #4A3728", prompt);
        Assert.Contains("GuardarBorrador", prompt);
    }
}
