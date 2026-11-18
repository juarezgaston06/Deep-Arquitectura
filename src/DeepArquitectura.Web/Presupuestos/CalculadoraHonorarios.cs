using DeepArquitectura.Web.Services;

namespace DeepArquitectura.Web.Presupuestos;

public enum TipoObraArancel
{
    Arquitectura = 0,
    Ingenieria = 1,
}

public enum NivelProyecto
{
    Ninguno = 0,
    Croquis = 1,
    Anteproyecto = 2,
    ProyectoSinDetalles = 3,
    ProyectoConDetalles = 4,
}

/// <summary>
/// Lo que el profesional carga en el creador de presupuestos. Se guarda como JSON en el presupuesto
/// para poder reabrirlo y recalcularlo.
/// </summary>
public sealed class PresupuestoEntrada
{
    // ---- Monto de obra ----
    public List<SuperficieUso> Superficies { get; set; } = [new()];
    /// <summary>Si se carga, reemplaza al monto calculado por superficies (obras por presupuesto, ampliaciones, etc.).</summary>
    public decimal? MontoObraManual { get; set; }

    // ---- Art. 78/79: proyecto y dirección técnica ----
    public TipoObraArancel TipoObra { get; set; } = TipoObraArancel.Arquitectura;
    public int Clase { get; set; }
    public NivelProyecto Proyecto { get; set; } = NivelProyecto.ProyectoConDetalles;
    public bool DireccionTecnica { get; set; } = true;
    public bool PrimeraCategoria { get; set; }

    // ---- Instalación (art. 78, ingeniería de instalación), con su propio monto ----
    public bool IncluirInstalacion { get; set; }
    public int ClaseInstalacion { get; set; }
    public decimal MontoInstalacion { get; set; }
    public decimal PorcentajeInstalacion { get; set; } = 100;

    // ---- Otras tareas ----
    public bool Relevamiento { get; set; }
    public bool RepresentacionTecnica { get; set; }
    public bool ComitentePrivado { get; set; } = true;
    public bool ConduccionTecnica { get; set; }
    public bool InformeTecnico { get; set; }
    public bool Diligenciamiento { get; set; }
    public int ConsultasSinInspeccion { get; set; }
    public int ConsultasConInspeccion { get; set; }
    public int DiasGabinete { get; set; }
    public int DiasTerreno { get; set; }
    public int DiasViaje { get; set; }

    // ---- Gastos y aportes ----
    public bool Carpeta { get; set; } = true;
    public bool ArancelAdministrativo { get; set; }
    public bool RegistroDiferenciado { get; set; } = true;
    public bool ExentoInteresSocial { get; set; }

    // ---- Ajustes ----
    /// <summary>Bonificación sobre el subtotal de honorarios (%).</summary>
    public decimal BonificacionPct { get; set; }
    public List<ItemExtra> Extras { get; set; } = [];
}

public sealed class SuperficieUso
{
    public string? UsoCodigo { get; set; } = "1.a";
    public decimal M2 { get; set; }
    /// <summary>Para ampliaciones el Colegio permite tomar el coeficiente de otro grupo.</summary>
    public decimal? CoeficienteManual { get; set; }
}

public sealed class ItemExtra
{
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public bool EsHonorario { get; set; }
}

public static class Secciones
{
    public const string Honorarios = "Honorarios profesionales";
    public const string Gastos = "Gastos y aportes";
}

public sealed record LineaPresupuesto(string Seccion, string Concepto, string? Detalle, decimal Importe);

public sealed record ResultadoPresupuesto(
    decimal SuperficieTotal,
    decimal MontoObra,
    IReadOnlyList<LineaPresupuesto> Lineas,
    IReadOnlyList<string> Avisos)
{
    public decimal Total => Lineas.Sum(l => l.Importe);
    public decimal Subtotal(string seccion) => Lineas.Where(l => l.Seccion == seccion).Sum(l => l.Importe);
}

/// <summary>Aplica el arancel a la entrada. Función pura: no toca la base, así se testea contra los valores del PDF.</summary>
public static class CalculadoraHonorarios
{
    public static decimal Redondear(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    public static decimal MontoPorSuperficies(Arancel a, PresupuestoEntrada e) =>
        e.Superficies.Sum(s => s.M2 * a.ValorBasico * (CoeficienteDe(a, s) ?? 0));

    public static decimal? CoeficienteDe(Arancel a, SuperficieUso s) => s.CoeficienteManual ?? a.BuscarUso(s.UsoCodigo)?.Coeficiente;

    public static decimal PorcentajeArt79(Arancel a, PresupuestoEntrada e)
    {
        var p = e.Proyecto switch
        {
            NivelProyecto.Croquis => a.Art79.Croquis,
            NivelProyecto.Anteproyecto => a.Art79.Anteproyecto,
            NivelProyecto.ProyectoSinDetalles => a.Art79.ProyectoSinDetalles,
            NivelProyecto.ProyectoConDetalles => a.Art79.ProyectoConDetalles,
            _ => 0,
        };
        return p + (e.DireccionTecnica ? a.Art79.DireccionTecnica : 0);
    }

    public static ResultadoPresupuesto Calcular(Arancel a, PresupuestoEntrada e)
    {
        var lineas = new List<LineaPresupuesto>();
        var avisos = new List<string>();
        void H(string concepto, string? detalle, decimal importe) =>
            lineas.Add(new(Secciones.Honorarios, concepto, detalle, Redondear(importe)));
        void G(string concepto, string? detalle, decimal importe) =>
            lineas.Add(new(Secciones.Gastos, concepto, detalle, Redondear(importe)));

        var superficie = e.Superficies.Sum(s => s.M2);
        foreach (var s in e.Superficies.Where(s => s.M2 > 0 && CoeficienteDe(a, s) is null))
            avisos.Add($"El uso {s.UsoCodigo} es «por presupuesto»: cargá el monto de obra a mano.");

        var montoObra = Redondear(e.MontoObraManual is > 0 ? e.MontoObraManual.Value : MontoPorSuperficies(a, e));
        string M(decimal v) => Textos.Moneda(v);
        string P(decimal v) => $"{v:0.####}%";

        // ---- Art. 78 / 79 ----
        var pct79 = PorcentajeArt79(a, e);
        if (pct79 > 0 && montoObra > 0)
        {
            var escala = e.TipoObra == TipoObraArancel.Arquitectura ? a.ProyectoArquitectura : a.ProyectoIngenieria;
            var clase = Math.Clamp(e.Clase, 0, Math.Max(0, escala.Clases.Count - 1));
            var art78 = escala.Calcular(montoObra, clase);
            var tareas = new List<string>();
            if (e.Proyecto != NivelProyecto.Ninguno) tareas.Add(EtiquetaNivel(e.Proyecto));
            if (e.DireccionTecnica) tareas.Add("Dirección técnica");

            H(string.Join(" + ", tareas),
              $"Art. 78/79 · {(e.TipoObra == TipoObraArancel.Arquitectura ? "Arquitectura" : "Ingeniería")} {escala.Clases.ElementAtOrDefault(clase)} · " +
              $"escala sobre {M(montoObra)} = {M(Redondear(art78))} × {P(pct79)}",
              art78 * pct79 / 100m);

            if (e.PrimeraCategoria && e.Proyecto != NivelProyecto.Ninguno)
                H("Planos de detalle (obra de 1ª categoría)",
                  $"+{P(a.RecargoPrimeraCategoriaPct)} de lo emergente del art. 78 ({M(Redondear(art78))})",
                  art78 * a.RecargoPrimeraCategoriaPct / 100m);
        }
        else if (pct79 > 0)
        {
            avisos.Add("Falta el monto de obra para calcular proyecto y dirección.");
        }

        // ---- Instalación ----
        if (e.IncluirInstalacion)
        {
            if (e.MontoInstalacion <= 0) avisos.Add("Cargá el monto de la instalación.");
            else
            {
                var esc = a.ProyectoInstalacion;
                var clase = Math.Clamp(e.ClaseInstalacion, 0, Math.Max(0, esc.Clases.Count - 1));
                var hon = esc.Calcular(e.MontoInstalacion, clase);
                H("Proyecto y dirección de instalación",
                  $"Art. 78 · Ingeniería de instalación {esc.Clases.ElementAtOrDefault(clase)} · escala sobre {M(e.MontoInstalacion)} = {M(Redondear(hon))}" +
                  (e.PorcentajeInstalacion != 100 ? $" × {P(e.PorcentajeInstalacion)}" : ""),
                  hon * e.PorcentajeInstalacion / 100m);
            }
        }

        // ---- Tareas sobre el monto de obra ----
        if (e.Relevamiento)
        {
            if (montoObra <= 0) avisos.Add("Falta el monto de obra para el relevamiento.");
            else H("Relevamiento", $"Art. 63 · escala sobre {M(montoObra)} (mínimo {M(a.Relevamiento.Minimo)})", a.Relevamiento.Calcular(montoObra));
        }
        if (e.RepresentacionTecnica && montoObra > 0)
        {
            var rep = a.RepresentacionTecnica.Calcular(montoObra);
            if (e.ComitentePrivado)
                H("Representación técnica",
                  $"Art. 116 · escala sobre {M(montoObra)} = {M(Redondear(rep))} − {P(a.ReduccionComitentePrivadoPct)} comitente privado (art. 117)",
                  rep * (100 - a.ReduccionComitentePrivadoPct) / 100m);
            else
                H("Representación técnica", $"Art. 116 · escala sobre {M(montoObra)}", rep);
        }
        if (e.ConduccionTecnica && montoObra > 0)
            H("Conducción técnica", $"Res. 909 · {P(a.ConduccionTecnicaPct)} de {M(montoObra)}", montoObra * a.ConduccionTecnicaPct / 100m);
        if (e.InformeTecnico && montoObra > 0)
            H("Informe técnico", $"Art. 109 · escala sobre {M(montoObra)}", a.InformeTecnico.Calcular(montoObra));

        // ---- Valores fijos ----
        var f = a.Fijos;
        if (e.Diligenciamiento)
            H("Diligenciamiento de aprobación de planos", "Art. 58 · ante reparticiones públicas", f.Diligenciamiento);
        if (e.ConsultasSinInspeccion > 0)
            H("Consultas sin inspección ocular", $"Art. 108 · {e.ConsultasSinInspeccion} × {M(f.ConsultaSinInspeccion)}", e.ConsultasSinInspeccion * f.ConsultaSinInspeccion);
        if (e.ConsultasConInspeccion > 0)
            H("Consultas con inspección ocular", $"Art. 108 · {e.ConsultasConInspeccion} × {M(f.ConsultaConInspeccion)}", e.ConsultasConInspeccion * f.ConsultaConInspeccion);
        if (e.DiasGabinete > 0)
            H("Días de trabajo en gabinete", $"Art. 28 · {e.DiasGabinete} × {M(f.DiaGabinete)}", e.DiasGabinete * f.DiaGabinete);
        if (e.DiasTerreno > 0)
            H("Días de trabajo en terreno", $"Art. 28 · {DetalleDiasTerreno(f, e.DiasTerreno, M)}", ImporteDiasTerreno(f, e.DiasTerreno));
        if (e.DiasViaje > 0)
            H("Días de viaje (ida y vuelta)", $"Art. 28 · {e.DiasViaje} × {M(f.DiaViaje)}", e.DiasViaje * f.DiaViaje);

        foreach (var x in e.Extras.Where(x => x.EsHonorario && x.Importe != 0 && !string.IsNullOrWhiteSpace(x.Concepto)))
            H(x.Concepto.Trim(), null, x.Importe);

        // ---- Bonificación (solo sobre honorarios) ----
        var subtotalHonorarios = lineas.Where(l => l.Seccion == Secciones.Honorarios).Sum(l => l.Importe);
        if (e.BonificacionPct > 0 && subtotalHonorarios > 0)
            H($"Bonificación {P(e.BonificacionPct)}", "Sobre el subtotal de honorarios", -subtotalHonorarios * e.BonificacionPct / 100m);

        // ---- Gastos y aportes ----
        if (e.Carpeta) G("Carpeta de liquidación de honorarios", "Valores de carpeta · visación", f.Carpeta);
        if (e.ArancelAdministrativo) G("Arancel administrativo", "Valores de carpeta", f.ArancelAdministrativo);
        if (e.RegistroDiferenciado && montoObra > 0)
        {
            if (e.ExentoInteresSocial)
                avisos.Add("Registro diferenciado exento (vivienda de interés social, hasta 35 puntos de mejoras).");
            else
            {
                var alicuota = AlicuotaRegistro(a, e, superficie);
                G("Registro de obra diferenciado", $"Res. 202/JG/02 · {P(alicuota)} de {M(montoObra)}", montoObra * alicuota / 100m);
            }
        }
        foreach (var x in e.Extras.Where(x => !x.EsHonorario && x.Importe != 0 && !string.IsNullOrWhiteSpace(x.Concepto)))
            G(x.Concepto.Trim(), null, x.Importe);

        if (lineas.Count == 0) avisos.Add("Elegí al menos una tarea.");
        return new(superficie, montoObra, lineas, avisos);
    }

    /// <summary>
    /// Alícuota del registro diferenciado. Un destino: la regla de su tipo según la superficie total.
    /// Varios destinos (punto i): la mayor de ellas si la superficie total no supera el tope; si lo supera, la general.
    /// </summary>
    public static decimal AlicuotaRegistro(Arancel a, PresupuestoEntrada e, decimal superficieTotal)
    {
        var destinos = e.Superficies.Where(s => s.M2 > 0)
            .Select(s => a.BuscarUso(s.UsoCodigo)?.Destino ?? DestinoRegistro.Otro)
            .Distinct().ToList();
        // Los usos accesorios (cocheras) siguen al destino principal; solos cuentan como «Otro».
        if (destinos.Count > 1) destinos.Remove(DestinoRegistro.Accesorio);
        destinos = destinos.Select(d => d == DestinoRegistro.Accesorio ? DestinoRegistro.Otro : d).ToList();
        if (destinos.Count == 0) destinos.Add(DestinoRegistro.Otro);
        if (destinos.Count > 1 && superficieTotal > a.RegistroTopeMultiplesDestinosM2) return a.RegistroPorcentajeGeneral;
        return destinos.Max(d => AlicuotaDestino(a, d, superficieTotal));
    }

    public static decimal AlicuotaDestino(Arancel a, DestinoRegistro destino, decimal m2) =>
        a.RegistroDiferenciado
            .Where(r => r.Destino == destino && (r.HastaM2 is null || m2 <= r.HastaM2))
            .OrderBy(r => r.HastaM2 ?? decimal.MaxValue)
            .Select(r => (decimal?)r.Porcentaje)
            .FirstOrDefault() ?? a.RegistroPorcentajeGeneral;

    /// <summary>Art. 28: los primeros 10 días a un valor, los 20 siguientes a otro y el exceso sobre 30 a otro.</summary>
    public static decimal ImporteDiasTerreno(ValoresFijos f, int dias)
    {
        var (d1, d2, d3) = TramosDias(dias);
        return d1 * f.DiaTerrenoPrimeros10 + d2 * f.DiaTerrenoSiguientes20 + d3 * f.DiaTerrenoExceso;
    }

    private static (int, int, int) TramosDias(int dias) =>
        (Math.Clamp(dias, 0, 10), Math.Clamp(dias - 10, 0, 20), Math.Max(dias - 30, 0));

    private static string DetalleDiasTerreno(ValoresFijos f, int dias, Func<decimal, string> m)
    {
        var (d1, d2, d3) = TramosDias(dias);
        var partes = new List<string> { $"{d1} × {m(f.DiaTerrenoPrimeros10)}" };
        if (d2 > 0) partes.Add($"{d2} × {m(f.DiaTerrenoSiguientes20)}");
        if (d3 > 0) partes.Add($"{d3} × {m(f.DiaTerrenoExceso)}");
        return string.Join(" + ", partes);
    }

    public static string EtiquetaNivel(NivelProyecto n) => n switch
    {
        NivelProyecto.Croquis => "Croquis",
        NivelProyecto.Anteproyecto => "Anteproyecto",
        NivelProyecto.ProyectoSinDetalles => "Proyecto sin detalles",
        NivelProyecto.ProyectoConDetalles => "Proyecto con detalles",
        _ => "Sin proyecto",
    };
}
