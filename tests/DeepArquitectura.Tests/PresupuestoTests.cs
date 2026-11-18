using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Presupuestos;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Tests;

/// <summary>La calculadora contra los montos acumulados que publica el propio PDF del Colegio (1er semestre 2026).</summary>
public class CalculadoraHonorariosTests
{
    private readonly Arancel _a = ArancelSemilla.Crear();
    private static decimal R(decimal v) => CalculadoraHonorarios.Redondear(v);

    private static PresupuestoEntrada SinTareas(Action<PresupuestoEntrada>? config = null)
    {
        var e = new PresupuestoEntrada
        {
            Proyecto = NivelProyecto.Ninguno, DireccionTecnica = false, Carpeta = false, RegistroDiferenciado = false,
        };
        config?.Invoke(e);
        return e;
    }

    [Theory]
    [InlineData(127_662_405, 11_489_616)]             // 9% hasta el 1er tramo (PDF: $11.489.616)
    [InlineData(1_276_623_898, 11_489_616 + 80_427_305)] // + 7% del 2º tramo (PDF: $80.427.305)
    public void Art78_Arquitectura1aClase_CoincideConElPdf(decimal monto, decimal esperado) =>
        Assert.Equal(esperado, R(_a.ProyectoArquitectura.Calcular(monto, 0)));

    [Fact]
    public void Art63_Relevamiento_TramosYMinimo()
    {
        Assert.Equal(22_353_451m, R(_a.Relevamiento.Calcular(2_235_345_086m)));
        Assert.Equal(22_353_451m + 4_470_690m, R(_a.Relevamiento.Calcular(2_794_181_357m)));
        Assert.Equal(167_542m, _a.Relevamiento.Calcular(1_000_000m)); // 1% = 10.000 < mínimo
        Assert.Equal(0m, _a.Relevamiento.Calcular(0m));
    }

    [Fact]
    public void Art109_InformeTecnico_y_Art116_Representacion_CoincidenConElPdf()
    {
        Assert.Equal(22_353m + 9_780m, R(_a.InformeTecnico.Calcular(1_676_505m)));
        Assert.Equal(3_829_872m + 10_212_991m, R(_a.RepresentacionTecnica.Calcular(638_311_949m)));
    }

    [Fact]
    public void ViviendaDe100m2_ObraNueva_CalculaMontoHonorariosYGastos()
    {
        var e = new PresupuestoEntrada { Superficies = [new() { UsoCodigo = "1.a", M2 = 100 }] };
        PlantillasPresupuesto.Todas[0].Aplicar(e); // proyecto con detalles + dirección + diligenciamiento + carpeta + registro

        var r = CalculadoraHonorarios.Calcular(_a, e);

        Assert.Equal(59_280_000m, r.MontoObra);                      // 100 × 741.000 × 0,80
        var proyecto = r.Lineas.Single(l => l.Concepto.Contains("Proyecto con detalles"));
        Assert.Equal(5_335_200m, proyecto.Importe);                  // 9% × (60% + 40%)
        Assert.Equal(439_038m, r.Lineas.Single(l => l.Concepto.StartsWith("Diligenciamiento")).Importe);
        Assert.Equal(148_200m, r.Lineas.Single(l => l.Concepto.StartsWith("Registro")).Importe); // vivienda ≤100 m² → 0,25%
        Assert.Equal(30_000m, r.Lineas.Single(l => l.Concepto.StartsWith("Carpeta")).Importe);
        Assert.Equal(5_335_200m + 439_038m + 148_200m + 30_000m, r.Total);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void Art79_SoloAnteproyecto_Aplica20PorcientoYPrimeraCategoriaSuma20()
    {
        var e = SinTareas(x =>
        {
            x.MontoObraManual = 100_000_000m;
            x.Proyecto = NivelProyecto.Anteproyecto;
            x.PrimeraCategoria = true;
        });
        var r = CalculadoraHonorarios.Calcular(_a, e);
        Assert.Equal(1_800_000m, r.Lineas[0].Importe);   // 9% = 9.000.000 × 20%
        Assert.Equal(1_800_000m, r.Lineas[1].Importe);   // +20% de lo emergente del art. 78
    }

    [Fact]
    public void Representacion_ComitentePrivado_Reduce20()
    {
        var e = SinTareas(x => { x.MontoObraManual = 100_000_000m; x.RepresentacionTecnica = true; x.ComitentePrivado = true; });
        Assert.Equal(2_400_000m, CalculadoraHonorarios.Calcular(_a, e).Total); // 3% × 0,8
    }

    [Fact]
    public void Art28_DiasEnTerreno_PorTramos()
    {
        // 10 × 189.389 + 20 × 161.514 + 5 × 129.946
        Assert.Equal(5_773_900m, CalculadoraHonorarios.ImporteDiasTerreno(_a.Fijos, 35));
        Assert.Equal(3 * 189_389m, CalculadoraHonorarios.ImporteDiasTerreno(_a.Fijos, 3));
    }

    [Theory]
    [InlineData(150, 100, 0.75)] // 250 m² en total ≤ 300 → mayor alícuota (vivienda 200-300 = 0,75%)
    [InlineData(250, 100, 1.00)] // 350 m² con varios destinos → 1%
    public void RegistroDiferenciado_VariosDestinos(decimal m2Vivienda, decimal m2Galpon, decimal esperado)
    {
        var e = SinTareas(x => x.Superficies = [new() { UsoCodigo = "1.c", M2 = m2Vivienda }, new() { UsoCodigo = "6.d", M2 = m2Galpon }]);
        Assert.Equal(esperado, CalculadoraHonorarios.AlicuotaRegistro(_a, e, m2Vivienda + m2Galpon));
    }

    [Fact]
    public void RegistroDiferenciado_CocheraSigueAlDestinoPrincipal()
    {
        // vivienda 120 m² + cochera 18 m² = 138 m² → vivienda 100-200 m² = 0,50% (no 1%)
        var conCasa = SinTareas(x => x.Superficies = [new() { UsoCodigo = "1.b", M2 = 120 }, new() { UsoCodigo = "4.c", M2 = 18 }]);
        Assert.Equal(0.50m, CalculadoraHonorarios.AlicuotaRegistro(_a, conCasa, 138));

        var sola = SinTareas(x => x.Superficies = [new() { UsoCodigo = "4.a", M2 = 30 }]);
        Assert.Equal(1.00m, CalculadoraHonorarios.AlicuotaRegistro(_a, sola, 30));
    }

    [Theory]
    [InlineData(DestinoRegistro.Vivienda, 100, 0.25)]
    [InlineData(DestinoRegistro.Vivienda, 101, 0.50)]
    [InlineData(DestinoRegistro.Vivienda, 301, 1.00)]
    [InlineData(DestinoRegistro.Galpon, 400, 0.50)]
    [InlineData(DestinoRegistro.Comercial, 120, 1.00)]
    [InlineData(DestinoRegistro.Otro, 50, 1.00)]
    public void RegistroDiferenciado_UnDestino(DestinoRegistro d, decimal m2, decimal esperado) =>
        Assert.Equal(esperado, CalculadoraHonorarios.AlicuotaDestino(_a, d, m2));

    [Fact]
    public void Bonificacion_SoloSobreHonorarios()
    {
        var e = SinTareas(x =>
        {
            x.MontoObraManual = 100_000_000m; x.ConduccionTecnica = true; // 4.000.000
            x.Carpeta = true; x.BonificacionPct = 10;
        });
        var r = CalculadoraHonorarios.Calcular(_a, e);
        Assert.Equal(3_600_000m, r.Subtotal(Secciones.Honorarios));
        Assert.Equal(30_000m, r.Subtotal(Secciones.Gastos));
    }

    [Fact]
    public void UsoPorPresupuesto_SinMontoManual_Avisa()
    {
        var e = SinTareas(x => { x.Superficies = [new() { UsoCodigo = "8", M2 = 500 }]; x.Proyecto = NivelProyecto.ProyectoConDetalles; });
        var r = CalculadoraHonorarios.Calcular(_a, e);
        Assert.Equal(0m, r.MontoObra);
        Assert.Contains(r.Avisos, a => a.Contains("por presupuesto"));
    }

    [Fact]
    public void EscalarMontos_ActualizaLimitesYFijos_PeroNoPorcentajes()
    {
        var a = ArancelSemilla.Crear();
        a.EscalarMontos(1.1m);
        Assert.Equal(R(127_662_405m * 1.1m), a.ProyectoArquitectura.Tramos[0].Hasta);
        Assert.Equal(9m, a.ProyectoArquitectura.Tramos[0].Porcentajes[0]);
        Assert.Equal(33_000m, a.Fijos.Carpeta);
        Assert.Equal(0.80m, a.BuscarUso("1.a")!.Coeficiente);
    }

    [Fact]
    public void Arancel_SobreviveIdaYVueltaAJson()
    {
        var copia = PresupuestoService.Clonar(_a);
        Assert.Equal(_a.Usos.Count, copia.Usos.Count);
        Assert.Equal(_a.ProyectoInstalacion.Tramos[4].Porcentajes, copia.ProyectoInstalacion.Tramos[4].Porcentajes);
        Assert.Null(copia.ProyectoArquitectura.Tramos[^1].Hasta);
        Assert.Equal(DestinoRegistro.Galpon, copia.BuscarUso("6.d")!.Destino);
    }

    [Fact]
    public void ElegirVigente_PrefiereLaQueCubreLaFecha_YSiNoLaUltimaQueEmpezo()
    {
        var v1 = new ArancelVersion { Id = 1, VigenteDesde = new(2026, 4, 1), VigenteHasta = new(2026, 9, 30) };
        var v2 = new ArancelVersion { Id = 2, VigenteDesde = new(2026, 10, 1), VigenteHasta = new(2027, 3, 31) };
        var desc = new[] { v2, v1 };
        Assert.Equal(1, PresupuestoService.ElegirVigente(desc, new(2026, 5, 1))!.Id);
        Assert.Equal(2, PresupuestoService.ElegirVigente(desc, new(2026, 11, 7))!.Id);
        Assert.Equal(1, PresupuestoService.ElegirVigente([v1], new(2026, 11, 7))!.Id); // venció y no hay nueva: sigue la última
    }
}

public class PresupuestoServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PresupuestoService _svc;

    public PresupuestoServiceTests() => _svc = new PresupuestoService(_db, new EstudioService(_db));
    public void Dispose() => _db.Dispose();

    private async Task<int> ArancelIdAsync()
    {
        await _svc.AsegurarArancelInicialAsync();
        await using var db = _db.CreateDbContext();
        return (await db.Aranceles.SingleAsync()).Id;
    }

    private static PresupuestoEntrada Vivienda100()
    {
        var e = new PresupuestoEntrada { Superficies = [new() { UsoCodigo = "1.a", M2 = 100 }] };
        PlantillasPresupuesto.Todas[0].Aplicar(e);
        return e;
    }

    [Fact]
    public async Task AsegurarArancelInicial_EsIdempotente()
    {
        await _svc.AsegurarArancelInicialAsync();
        await _svc.AsegurarArancelInicialAsync();
        await using var db = _db.CreateDbContext();
        var v = await db.Aranceles.SingleAsync();
        Assert.Equal(741_000m, v.ValorBasico);
        Assert.Equal(new DateOnly(2026, 9, 30), v.VigenteHasta);
    }

    [Fact]
    public async Task Guardar_NumeraCorrelativoYGuardaRenglones_YAlEditarLosReemplaza()
    {
        var arancelId = await ArancelIdAsync();
        var fecha = new DateOnly(2026, 11, 7);

        var p1 = await _svc.GuardarAsync(new Presupuesto { Titulo = "Casa", ArancelVersionId = arancelId, Fecha = fecha }, Vivienda100());
        var p2 = await _svc.GuardarAsync(new Presupuesto { Titulo = "Otra", ArancelVersionId = arancelId, Fecha = fecha }, Vivienda100());
        Assert.Equal("PR-2026-001", p1.Numero);
        Assert.Equal("PR-2026-002", p2.Numero);
        Assert.Equal(5_952_438m, p1.Total);

        // Editar: sacar la dirección técnica → menos renglones y nuevo total.
        var e = Vivienda100(); e.DireccionTecnica = false; e.Diligenciamiento = false;
        await _svc.GuardarAsync(new Presupuesto { Id = p1.Id, Titulo = "Casa", ArancelVersionId = arancelId, Fecha = fecha }, e);

        await using var db = _db.CreateDbContext();
        var guardado = await db.Presupuestos.Include(p => p.Items).SingleAsync(p => p.Id == p1.Id);
        Assert.Equal("PR-2026-001", guardado.Numero);
        Assert.Equal(3, guardado.Items.Count);                       // proyecto, carpeta, registro
        Assert.Equal(3_201_120m + 30_000m + 148_200m, guardado.Total);  // 5.335.200 × 60%
        Assert.Equal(guardado.Items.Sum(i => i.Importe), guardado.Total);
        var entrada = PresupuestoService.Deserializar<PresupuestoEntrada>(guardado.Parametros);
        Assert.False(entrada.DireccionTecnica);
    }

    [Fact]
    public async Task CrearObra_RequiereCliente_LuegoCreaObraConElTotalYEsIdempotente()
    {
        var arancelId = await ArancelIdAsync();
        var sinCliente = await _svc.GuardarAsync(new Presupuesto { Titulo = "Sin cliente", ArancelVersionId = arancelId, Destinatario = "Pepe" }, Vivienda100());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CrearObraAsync(sinCliente.Id));

        int clienteId;
        await using (var db = _db.CreateDbContext())
        {
            var c = new Cliente { Nombre = "Ana" };
            db.Clientes.Add(c);
            await db.SaveChangesAsync();
            clienteId = c.Id;
        }
        var p = await _svc.GuardarAsync(new Presupuesto
        {
            Titulo = "Vivienda Ana", ArancelVersionId = arancelId, ClienteId = clienteId, Servicio = TipoServicio.PlanosMunicipales, Ubicacion = "Calle 1",
        }, Vivienda100());

        var obraId = await _svc.CrearObraAsync(p.Id);
        Assert.Equal(obraId, await _svc.CrearObraAsync(p.Id));

        await using var db2 = _db.CreateDbContext();
        var obra = await db2.Obras.Include(o => o.Etapas).SingleAsync(o => o.Id == obraId);
        Assert.Equal(p.Total, obra.MontoPresupuesto);
        Assert.Equal("Vivienda Ana", obra.Nombre);
        Assert.NotEmpty(obra.Etapas);
        var pr = await db2.Presupuestos.SingleAsync(x => x.Id == p.Id);
        Assert.Equal(EstadoPresupuesto.Aceptado, pr.Estado);
        Assert.Equal(obraId, pr.ObraId);
    }

    [Fact]
    public async Task GuardarArancel_OrdenaTramos_YValida()
    {
        var a = ArancelSemilla.Crear();
        a.Relevamiento.Tramos.Reverse();
        var v = await _svc.GuardarArancelAsync(new ArancelVersion { VigenteDesde = new(2026, 10, 1), VigenteHasta = new(2027, 3, 31) }, a);

        var leido = PresupuestoService.Deserializar<Arancel>(v.Contenido);
        Assert.Equal(2_235_345_086m, leido.Relevamiento.Tramos[0].Hasta);
        Assert.Null(leido.Relevamiento.Tramos[^1].Hasta);
        Assert.StartsWith("Arancel desde", v.Nombre);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.GuardarArancelAsync(new ArancelVersion { VigenteDesde = new(2027, 1, 1), VigenteHasta = new(2026, 1, 1) }, ArancelSemilla.Crear()));
    }
}
