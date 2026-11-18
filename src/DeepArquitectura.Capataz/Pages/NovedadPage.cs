using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Fotos, videos y novedades de la obra para la bitácora (lo ve el estudio y, en el seguimiento, el cliente).</summary>
public sealed class NovedadPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly IReadOnlyList<EtapaDto> _etapas;
    private readonly Entry _titulo = Ui.Campo("Título corto (ej. Llenado de losa)", max: 200);
    private readonly Editor _detalle = Ui.Area("Detalle (opcional): qué se hizo, qué falta, algo para avisar…", 4000);
    private readonly Picker _etapa;
    private readonly AdjuntosView _adjuntos = new();

    public NovedadPage(ObraResumenDto obra, IReadOnlyList<EtapaDto> etapas)
    {
        _obra = obra;
        _etapas = etapas.Where(e => e.Estado != "Omitida").OrderBy(e => e.Orden).ToList();
        Title = "Fotos y novedades";
        BackgroundColor = Ui.Fondo;
        // Por defecto, la etapa que está en curso.
        var enCurso = _etapas.ToList().FindIndex(e => e.Estado == "EnCurso");
        _etapa = Ui.Selector("Etapa (opcional)", _etapas.Select(e => $"{e.Orden}. {e.Nombre}").Prepend("Ninguna en especial"), enCurso + 1);

        Content = Ui.Contenedor(
            Ui.Titulo("Fotos y novedades"),
            Ui.Suave("Sacá fotos o videos y contá qué pasa. Se guarda en el teléfono y se envía solo, aunque ahora no haya señal."),
            Ui.Seccion("Fotos y videos"),
            _adjuntos,
            Ui.Seccion("¿Qué pasa?"),
            _titulo,
            Ui.ConDictado(_detalle),
            Ui.Seccion("Etapa"),
            _etapa,
            Ui.Boton("Enviar al estudio", EnviarAsync, color: Ui.Verde));
    }

    private async Task EnviarAsync()
    {
        var titulo = _titulo.Text?.Trim();
        var archivos = _adjuntos.Archivos;
        if (string.IsNullOrEmpty(titulo))
        {
            if (archivos.Count == 0 && string.IsNullOrWhiteSpace(_detalle.Text))
            {
                await DisplayAlertAsync("Novedad", "Sacá una foto o contá qué pasa.", "Corregir");
                return;
            }
            var videos = archivos.Count(Medios.EsVideo);
            titulo = archivos.Count == 0 ? "Novedad de obra" : videos == archivos.Count ? "Video de obra" : "Fotos de obra";
        }
        int? etapaId = _etapa.SelectedIndex > 0 ? _etapas[_etapa.SelectedIndex - 1].Id : null;
        await App.Servicio<Cargas>().NovedadAsync(_obra, titulo, _detalle.Text, etapaId, archivos);
        await Ui.AvisarAsync(this, "Guardado", "Se envía solo al estudio (si no hay señal, cuando vuelva).");
        await Navigation.PopAsync();
    }
}
