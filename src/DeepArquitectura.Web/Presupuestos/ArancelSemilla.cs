namespace DeepArquitectura.Web.Presupuestos;

/// <summary>
/// Primera versión cargada: "Escala arancelaria y tabla de valores según uso y destino",
/// Colegio de Maestros Mayores de Obras y Técnicos de Córdoba — Regional 1 Capital,
/// vigente 01/04/2026 al 30/09/2026, valor básico $741.000. Las versiones siguientes se cargan desde el panel.
/// </summary>
public static class ArancelSemilla
{
    public const string Nombre = "Colegio MMO Regional 1 — 1er semestre 2026 (VB $741.000)";
    public static readonly DateOnly VigenteDesde = new(2026, 4, 1);
    public static readonly DateOnly VigenteHasta = new(2026, 9, 30);

    private static readonly List<string> ClasesIngenieria =
        ["1ª clase (hasta 10 kW)", "2ª clase (hasta 35 kW)", "3ª clase (más de 35 kW)"];

    public static Arancel Crear() => new()
    {
        ValorBasico = 741_000m,
        Usos = Usos(),

        ProyectoArquitectura = new()
        {
            Clases = ["1ª clase (obras en general)", "2ª clase (muebles, exposiciones, decoración)"],
            Tramos =
            [
                new() { Hasta = 127_662_405m, Porcentajes = [9m, 15m] },
                new() { Hasta = 1_276_623_898m, Porcentajes = [7m, 10m] },
                // El PDF no indica un 3er tramo para 2ª clase: se mantiene el 10%.
                new() { Hasta = null, Porcentajes = [5m, 10m] },
            ],
        },
        ProyectoIngenieria = new()
        {
            Clases = [.. ClasesIngenieria],
            Tramos =
            [
                new() { Hasta = 63_831_203m, Porcentajes = [8m, 10m, 12m] },
                new() { Hasta = 319_156_016m, Porcentajes = [7m, 8m, 10m] },
                new() { Hasta = 638_311_949m, Porcentajes = [6m, 7m, 9m] },
                new() { Hasta = 1_276_623_898m, Porcentajes = [5m, 6m, 8m] },
                new() { Hasta = null, Porcentajes = [4m, 5m, 7m] },
            ],
        },
        ProyectoInstalacion = new()
        {
            Clases = [.. ClasesIngenieria],
            Tramos =
            [
                new() { Hasta = 63_831_203m, Porcentajes = [3m, 4m, 5m] },
                new() { Hasta = 319_156_016m, Porcentajes = [1.5m, 2m, 2.5m] },
                new() { Hasta = 638_311_949m, Porcentajes = [0.75m, 1.5m, 1.25m] },
                new() { Hasta = 1_276_623_898m, Porcentajes = [0.375m, 0.5m, 0.625m] },
                new() { Hasta = null, Porcentajes = [0.1575m, 0.25m, 0.3125m] },
            ],
        },
        Relevamiento = new()
        {
            Minimo = 167_542m,
            Tramos =
            [
                new() { Hasta = 2_235_345_086m, Porcentajes = [1m] },
                new() { Hasta = 2_794_181_357m, Porcentajes = [0.8m] },
                new() { Hasta = 3_353_017_629m, Porcentajes = [0.65m] },
                new() { Hasta = 3_911_853_900m, Porcentajes = [0.5m] },
                new() { Hasta = 4_470_690_172m, Porcentajes = [0.4m] },
                new() { Hasta = null, Porcentajes = [0.3m] },
            ],
        },
        InformeTecnico = new()
        {
            Tramos =
            [
                new() { Hasta = 1_117_669m, Porcentajes = [2m] },
                new() { Hasta = 1_676_505m, Porcentajes = [1.75m] },
                new() { Hasta = 2_794_181_329m, Porcentajes = [1.5m] },
                new() { Hasta = 3_353_017_660m, Porcentajes = [1.25m] },
                new() { Hasta = 3_911_853_912m, Porcentajes = [1m] },
                new() { Hasta = 44_707_146_807m, Porcentajes = [0.75m] },
                new() { Hasta = null, Porcentajes = [0.5m] },
            ],
        },
        RepresentacionTecnica = new()
        {
            Tramos =
            [
                new() { Hasta = 127_662_405m, Porcentajes = [3m] },
                new() { Hasta = 638_311_949m, Porcentajes = [2m] },
                new() { Hasta = 1_276_623_898m, Porcentajes = [1m] },
                new() { Hasta = null, Porcentajes = [0.5m] },
            ],
        },

        Art79 = new() { Croquis = 5, Anteproyecto = 20, ProyectoSinDetalles = 40, ProyectoConDetalles = 60, DireccionTecnica = 40 },
        RecargoPrimeraCategoriaPct = 20,
        ReduccionComitentePrivadoPct = 20,
        ConduccionTecnicaPct = 4,

        Fijos = new()
        {
            DiaGabinete = 178_487m,
            DiaTerrenoPrimeros10 = 189_389m,
            DiaTerrenoSiguientes20 = 161_514m,
            DiaTerrenoExceso = 129_946m,
            DiaViaje = 78_988m,
            Diligenciamiento = 439_038m,
            ConsultaSinInspeccion = 52_684m,
            ConsultaConInspeccion = 78_985m,
            Carpeta = 30_000m,
            ArancelAdministrativo = 30_000m,
        },

        RegistroDiferenciado =
        [
            new() { Destino = DestinoRegistro.Vivienda, HastaM2 = 100, Porcentaje = 0.25m },
            new() { Destino = DestinoRegistro.Vivienda, HastaM2 = 200, Porcentaje = 0.50m },
            new() { Destino = DestinoRegistro.Vivienda, HastaM2 = 300, Porcentaje = 0.75m },
            new() { Destino = DestinoRegistro.Vivienda, HastaM2 = null, Porcentaje = 1m },
            new() { Destino = DestinoRegistro.Galpon, HastaM2 = 400, Porcentaje = 0.50m },
            new() { Destino = DestinoRegistro.Galpon, HastaM2 = null, Porcentaje = 1m },
            new() { Destino = DestinoRegistro.Comercial, HastaM2 = 100, Porcentaje = 0.50m },
            new() { Destino = DestinoRegistro.Comercial, HastaM2 = null, Porcentaje = 1m },
            new() { Destino = DestinoRegistro.Otro, HastaM2 = null, Porcentaje = 1m },
        ],
        RegistroTopeMultiplesDestinosM2 = 300,
        RegistroPorcentajeGeneral = 1,
    };

    private static List<UsoDestino> Usos()
    {
        const string G1 = "1. Viviendas unifamiliares", G2 = "2. Viviendas multifamiliares", G3 = "3. Edificios comerciales",
            G4 = "4. Cocheras o guarda coches", G5 = "5. Tinglados o cobertizos sin cerramientos",
            G6 = "6. Galpones de planta baja con cerramientos comunes", G7 = "7. Edificios especiales", G8 = "8. Obras por presupuesto";
        var V = DestinoRegistro.Vivienda; var C = DestinoRegistro.Comercial; var Ga = DestinoRegistro.Galpon; var O = DestinoRegistro.Otro; var Ac = DestinoRegistro.Accesorio;

        UsoDestino U(string cod, string grupo, string desc, decimal? coef, DestinoRegistro d) =>
            new() { Codigo = cod, Grupo = grupo, Descripcion = desc, Coeficiente = coef, Destino = d };

        return
        [
            U("1.a", G1, "Mampostería u otro material, cualquier cubierta, hasta 100 m²", 0.80m, V),
            U("1.b", G1, "Ídem, hasta 150 m²", 1.00m, V),
            U("1.c", G1, "Ídem, hasta 200 m²", 1.10m, V),
            U("1.d", G1, "Ídem, de 200 a 300 m²", 1.20m, V),
            U("1.e", G1, "Ídem, más de 300 m²", 1.35m, V),
            U("1.f", G1, "Más de 300 m² con aire acondicionado, calefacción central u otras inst. especiales y pisos de mármol/granito", 1.50m, V),
            U("1.g", G1, "Prefabricados (por presupuesto)", null, V),

            U("2.a", G2, "Departamentos en planta baja", 0.85m, V),
            U("2.b", G2, "Departamentos de hasta 2 plantas", 1.10m, V),
            U("2.c", G2, "Departamentos de más de 2 y hasta 4 plantas", 1.25m, V),
            U("2.d", G2, "Departamentos de más de 4 plantas", 1.40m, V),

            U("3.a", G3, "Oficinas y/o comercios en planta baja", 0.90m, C),
            U("3.b", G3, "Oficinas y/o comercios de 2 y hasta 4 plantas", 1.10m, C),
            U("3.c", G3, "Oficinas y/o comercios de más de 4 plantas", 1.20m, C),
            U("3.d", G3, "Galería comercial en un nivel", 1.10m, C),
            U("3.e", G3, "Galería comercial en 2 niveles", 1.20m, C),
            U("3.f", G3, "Galería comercial de más de 2 plantas", 1.30m, C),

            U("4.a", G4, "Cerramientos de mampostería y techo de Hº Aº en un solo nivel", 0.50m, Ac),
            U("4.b", G4, "Ídem en más de una planta", 0.65m, Ac),
            U("4.c", G4, "Ídem 4.a con techo de chapa, fibrocemento o similar", 0.35m, Ac),

            U("5.a", G5, "Cubierta de chapa/fibrocemento sobre estructura simple de madera o hierro", 0.15m, Ga),
            U("5.b", G5, "Ídem sobre estructura reticulada de madera o hierro", 0.25m, Ga),
            U("5.c", G5, "Con techo y/o estructura de Hº Aº, prefabricada o no", 0.35m, Ga),

            U("6.a", G6, "Cubierta de chapa/fibrocemento sobre estructura simple de madera o hierro", 0.25m, Ga),
            U("6.b", G6, "Ídem sobre estructura reticulada de madera o hierro", 0.35m, Ga),
            U("6.c", G6, "Con techo y/o estructura de Hº Aº, prefabricada o no", 0.50m, Ga),
            U("6.d", G6, "Galpones de uso familiar", 0.40m, Ga),

            U("7.a1", G7, "Bancos e instituciones financieras — en locales adaptados", 1.85m, C),
            U("7.a2", G7, "Bancos e instituciones financieras — proyectados ex profeso", 2.00m, C),
            U("7.b", G7, "Cines, auditorios", 2.50m, O),
            U("7.c", G7, "Casinos, salas de juego", 2.30m, O),
            U("7.d", G7, "Albergues estudiantiles", 1.20m, O),
            U("7.e", G7, "Moteles", 1.20m, O),
            U("7.f", G7, "Teatros", 2.60m, O),
            U("7.g", G7, "Restaurantes, confiterías, bares y locales bailables", 1.50m, C),
            U("7.h1", G7, "Educacional — jardines de infantes y guarderías", 1.10m, O),
            U("7.h2", G7, "Educacional — escuela primaria rural", 1.00m, O),
            U("7.h3", G7, "Educacional — escuela primaria urbana", 1.50m, O),
            U("7.h4", G7, "Educacional — escuela primaria especial", 1.80m, O),
            U("7.h5", G7, "Educacional — escuela secundaria común", 1.80m, O),
            U("7.h6", G7, "Educacional — escuela secundaria especial", 2.10m, O),
            U("7.j1", G7, "Salud — dispensarios o consultorios externos", 1.30m, O),
            U("7.j2", G7, "Salud — clínicas de hasta 200 m²", 2.00m, O),
            U("7.j3", G7, "Salud — clínicas de más de 200 m²", 2.20m, O),
            U("7.j4", G7, "Salud — sanatorios", 2.85m, O),
            U("7.j5", G7, "Salud — hospitales", 2.85m, O),
            U("7.k", G7, "Salas velatorias", 1.20m, O),
            U("7.l", G7, "Edificios institucionales (sindicatos, colegios o centros profesionales)", 1.80m, O),

            U("8", G8, "Panteones, monumentos, estaciones, universidades, clubes, estaciones de servicio, templos, hoteles y otras (monto sin IVA)", null, O),
        ];
    }
}
