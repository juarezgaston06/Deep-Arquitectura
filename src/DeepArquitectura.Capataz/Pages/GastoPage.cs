using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>
/// Gasto que pagó el capataz (flete, volquete, changa, ferretería…) con la foto del comprobante. Va al costo real de la
/// obra. Gasto y fotos viajan juntos: o llega todo al estudio o nada (no queda un gasto sin su comprobante a medias).
/// </summary>
public sealed class GastoPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly IReadOnlyList<EtapaDto> _etapas;
    private readonly Editor _descripcion = Ui.Area("¿Qué se pagó? (ej. Flete de arena y piedra)", 300);
    private readonly Entry _monto = Ui.Campo("Monto en pesos (ej. 45000)", Keyboard.Numeric, 15);
    private readonly Picker _categoria = Ui.Selector("Tipo de gasto", ValoresCapataz.CategoriasGasto.Select(c => c.Etiqueta), 0);
    private readonly Entry _proveedor = Ui.Campo("A quién se le pagó (opcional)", max: 150);
    private readonly Entry _comprobante = Ui.Campo("N° de factura o ticket (opcional)", max: 60);
    private readonly Picker _etapa;
    private readonly AdjuntosView _fotos = new(conVideos: false, maximo: RutasApp.MaximoComprobantes);

    public GastoPage(ObraResumenDto obra, IReadOnlyList<EtapaDto> etapas)
    {
        _obra = obra;
        _etapas = etapas.Where(e => e.Estado != "Omitida").OrderBy(e => e.Orden).ToList();
        Title = "Cargar un gasto";
        BackgroundColor = Ui.Fondo;
        _descripcion.MinimumHeightRequest = 60;
        var enCurso = _etapas.ToList().FindIndex(e => e.Estado == "EnCurso");
        _etapa = Ui.Selector("Etapa (opcional)", _etapas.Select(e => $"{e.Orden}. {e.Nombre}").Prepend("Ninguna en especial"), enCurso + 1);

        Content = Ui.Contenedor(
            Ui.Titulo("Cargar un gasto"),
            Ui.Suave($"Obra {obra.Nombre}. Lo ve el estudio en los costos de la obra."),
            Ui.ConDictado(_descripcion),
            _monto,
            _categoria,
            _proveedor,
            _comprobante,
            Ui.Seccion("Etapa"),
            _etapa,
            Ui.Seccion("Foto del comprobante"),
            _fotos,
            Ui.Boton("Guardar el gasto", GuardarAsync, color: Ui.Verde));
    }

    private async Task GuardarAsync()
    {
        var descripcion = _descripcion.Text?.Trim();
        if (string.IsNullOrEmpty(descripcion))
        {
            await DisplayAlertAsync("Gasto", "Escribí qué se pagó.", "Corregir");
            return;
        }
        if (Ui.Decimal(_monto.Text) is not (decimal monto and > 0))
        {
            await DisplayAlertAsync("Gasto", "Poné el monto (solo números).", "Corregir");
            return;
        }
        // Las fotos salen de la cámara a ~1 MB (2560 px): muy por debajo del máximo de 15 MB por comprobante.
        var fotos = _fotos.Archivos;
        if (fotos.Count == 0 && !await DisplayAlertAsync("Comprobante", "No sacaste foto del comprobante. ¿Guardar igual?", "Guardar igual", "Sacar foto"))
            return;
        if (!await DisplayAlertAsync("Gasto", $"¿Guardar ${Ui.Num(monto)} por «{descripcion}»?", "Guardar", "Corregir")) return;

        int? etapaId = _etapa.SelectedIndex > 0 ? _etapas[_etapa.SelectedIndex - 1].Id : null;
        var categoria = ValoresCapataz.CategoriasGasto[Math.Max(0, _categoria.SelectedIndex)].Valor;
        await App.Servicio<Cargas>().GastoAsync(_obra, descripcion, monto, categoria, _proveedor.Text, _comprobante.Text, etapaId, fotos);
        await Ui.AvisarAsync(this, "Guardado", "El gasto se envía solo al estudio (si no hay señal, cuando vuelva).");
        await Navigation.PopAsync();
    }
}
