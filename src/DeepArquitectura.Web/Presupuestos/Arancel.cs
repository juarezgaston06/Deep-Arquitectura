namespace DeepArquitectura.Web.Presupuestos;

/// <summary>
/// Escala arancelaria del Colegio (valor básico, tabla de uso y destino, escalas por artículo y valores fijos).
/// Se guarda como JSON en <c>ArancelVersion.Contenido</c>: cuando el Colegio publica una tabla nueva se carga
/// otra versión desde el panel y los presupuestos ya emitidos no cambian.
/// </summary>
public sealed class Arancel
{
    public decimal ValorBasico { get; set; }

    /// <summary>Tabla de valores según uso y destino: monto de obra = m² × valor básico × coeficiente.</summary>
    public List<UsoDestino> Usos { get; set; } = [];

    /// <summary>Art. 78 — Proyecto y dirección técnica, obras de arquitectura.</summary>
    public Escala ProyectoArquitectura { get; set; } = new();
    /// <summary>Art. 78 — Obras de ingeniería.</summary>
    public Escala ProyectoIngenieria { get; set; } = new();
    /// <summary>Art. 78 — Obras de ingeniería de instalación.</summary>
    public Escala ProyectoInstalacion { get; set; } = new();
    /// <summary>Art. 63 — Relevamiento.</summary>
    public Escala Relevamiento { get; set; } = new();
    /// <summary>Art. 109 — Informe técnico.</summary>
    public Escala InformeTecnico { get; set; } = new();
    /// <summary>Art. 116 — Representación técnica.</summary>
    public Escala RepresentacionTecnica { get; set; } = new();

    /// <summary>Art. 79 — porcentaje del honorario del art. 78 que corresponde a cada tarea parcial.</summary>
    public PorcentajesArt79 Art79 { get; set; } = new();

    /// <summary>Obras de 1ª categoría: planos de detalle, +X% de lo emergente del art. 78.</summary>
    public decimal RecargoPrimeraCategoriaPct { get; set; } = 20;
    /// <summary>Art. 117 — comitente privado: se puede reducir X% lo del art. 116.</summary>
    public decimal ReduccionComitentePrivadoPct { get; set; } = 20;
    /// <summary>Res. 909 — Conducción técnica: X% del monto de obra.</summary>
    public decimal ConduccionTecnicaPct { get; set; } = 4;

    public ValoresFijos Fijos { get; set; } = new();

    /// <summary>Res. 202 — Registro de obra diferenciado: alícuota según destino y superficie.</summary>
    public List<ReglaRegistro> RegistroDiferenciado { get; set; } = [];
    /// <summary>Punto i): con varios destinos se usa la mayor alícuota mientras la superficie total no supere este tope.</summary>
    public decimal RegistroTopeMultiplesDestinosM2 { get; set; } = 300;
    /// <summary>Punto k) y caso de múltiples destinos sobre el tope.</summary>
    public decimal RegistroPorcentajeGeneral { get; set; } = 1;

    public UsoDestino? BuscarUso(string? codigo) => Usos.FirstOrDefault(u => u.Codigo == codigo);

    /// <summary>
    /// Ayuda para cargar una tabla nueva: multiplica todos los montos (límites de tramos, mínimos y valores fijos)
    /// por <paramref name="factor"/>. Los porcentajes y coeficientes no cambian. Después conviene revisar contra el PDF.
    /// </summary>
    public void EscalarMontos(decimal factor)
    {
        static decimal R(decimal v, decimal f) => Math.Round(v * f, 0, MidpointRounding.AwayFromZero);
        foreach (var e in Escalas())
        {
            e.Minimo = R(e.Minimo, factor);
            foreach (var t in e.Tramos)
                if (t.Hasta is decimal h) t.Hasta = R(h, factor);
        }
        var f = Fijos;
        f.DiaGabinete = R(f.DiaGabinete, factor);
        f.DiaTerrenoPrimeros10 = R(f.DiaTerrenoPrimeros10, factor);
        f.DiaTerrenoSiguientes20 = R(f.DiaTerrenoSiguientes20, factor);
        f.DiaTerrenoExceso = R(f.DiaTerrenoExceso, factor);
        f.DiaViaje = R(f.DiaViaje, factor);
        f.Diligenciamiento = R(f.Diligenciamiento, factor);
        f.ConsultaSinInspeccion = R(f.ConsultaSinInspeccion, factor);
        f.ConsultaConInspeccion = R(f.ConsultaConInspeccion, factor);
        f.Carpeta = R(f.Carpeta, factor);
        f.ArancelAdministrativo = R(f.ArancelAdministrativo, factor);
    }

    public IEnumerable<Escala> Escalas() =>
        [ProyectoArquitectura, ProyectoIngenieria, ProyectoInstalacion, Relevamiento, InformeTecnico, RepresentacionTecnica];
}

public enum DestinoRegistro
{
    Vivienda = 0,
    Galpon = 1,
    Comercial = 2,
    Otro = 3,
    /// <summary>Accesorio (p. ej. cochera): toma el destino principal de la obra; si va solo, cuenta como «Otro».</summary>
    Accesorio = 4,
}

public sealed class UsoDestino
{
    /// <summary>Código de la tabla, ej. "1.a".</summary>
    public string Codigo { get; set; } = "";
    public string Grupo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>Null = "por presupuesto" (el monto de obra se carga a mano).</summary>
    public decimal? Coeficiente { get; set; }
    public DestinoRegistro Destino { get; set; }
}

/// <summary>Escala progresiva por tramos: a cada porción del monto se le aplica el % de su tramo.</summary>
public sealed class Escala
{
    /// <summary>Nombres de las columnas (clases). Cada tramo tiene un porcentaje por clase.</summary>
    public List<string> Clases { get; set; } = ["Única"];
    public List<Tramo> Tramos { get; set; } = [];
    /// <summary>Honorario mínimo (0 = sin mínimo).</summary>
    public decimal Minimo { get; set; }

    public decimal Calcular(decimal monto, int clase = 0)
    {
        if (monto <= 0 || Tramos.Count == 0) return 0;
        decimal total = 0, desde = 0;
        foreach (var t in Tramos)
        {
            if (monto <= desde) break;
            var hasta = t.Hasta ?? decimal.MaxValue;
            var porcion = Math.Min(monto, hasta) - desde;
            total += porcion * t.Porcentaje(clase) / 100m;
            if (t.Hasta is null) break;
            desde = hasta;
        }
        return Math.Max(total, Minimo);
    }
}

public sealed class Tramo
{
    /// <summary>Límite superior del tramo; null = "en adelante".</summary>
    public decimal? Hasta { get; set; }
    /// <summary>Porcentaje para cada clase de la escala (mismo orden que <see cref="Escala.Clases"/>).</summary>
    public List<decimal> Porcentajes { get; set; } = [];

    public decimal Porcentaje(int clase) =>
        Porcentajes.Count == 0 ? 0 : Porcentajes[Math.Clamp(clase, 0, Porcentajes.Count - 1)];
}

public sealed class PorcentajesArt79
{
    public decimal Croquis { get; set; } = 5;
    public decimal Anteproyecto { get; set; } = 20;
    public decimal ProyectoSinDetalles { get; set; } = 40;
    public decimal ProyectoConDetalles { get; set; } = 60;
    public decimal DireccionTecnica { get; set; } = 40;
}

public sealed class ValoresFijos
{
    // Art. 28
    public decimal DiaGabinete { get; set; }
    public decimal DiaTerrenoPrimeros10 { get; set; }
    public decimal DiaTerrenoSiguientes20 { get; set; }
    public decimal DiaTerrenoExceso { get; set; }
    public decimal DiaViaje { get; set; }
    // Art. 58
    public decimal Diligenciamiento { get; set; }
    // Art. 108
    public decimal ConsultaSinInspeccion { get; set; }
    public decimal ConsultaConInspeccion { get; set; }
    // Valores de carpeta
    public decimal Carpeta { get; set; }
    public decimal ArancelAdministrativo { get; set; }
}

/// <summary>Alícuota del registro diferenciado para un destino hasta cierta superficie total.</summary>
public sealed class ReglaRegistro
{
    public DestinoRegistro Destino { get; set; }
    /// <summary>Superficie total máxima (m²) a la que aplica; null = sin tope.</summary>
    public decimal? HastaM2 { get; set; }
    public decimal Porcentaje { get; set; }
}
