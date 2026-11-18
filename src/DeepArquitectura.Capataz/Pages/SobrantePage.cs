using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Material que sobró y qué se hizo (depósito, devuelto, a otra obra o desperdicio): mide el desperdicio real.</summary>
public sealed class SobrantePage : ContentPage
{
    private static readonly string[] Unidades = ["bolsas", "u", "m³", "m²", "ml", "kg", "barras", "latas", "rollos"];

    private readonly ObraResumenDto _obra;
    private readonly List<ObraResumenDto> _otras;
    private readonly Entry _material = Ui.Campo("Material (ej. cemento, ladrillo hueco 12)", max: 150);
    private readonly Entry _cantidad = Ui.Campo("Cantidad", Keyboard.Numeric, 12);
    private readonly Picker _unidad = Ui.Selector("Unidad", Unidades, 0);
    private readonly Picker _destino = Ui.Selector("¿Qué se hizo?", ValoresCapataz.DestinosSobrante.Select(d => d.Etiqueta), 0);
    private readonly Picker _obraDestino;
    private readonly Editor _nota = Ui.Area("Nota (opcional)", 500);

    public SobrantePage(ObraResumenDto obra)
    {
        _obra = obra;
        _otras = (App.Servicio<Sesion>().Perfil?.Obras ?? []).Where(o => o.Id != obra.Id).ToList();
        Title = "Material que sobró";
        BackgroundColor = Ui.Fondo;
        _nota.MinimumHeightRequest = 60;
        _obraDestino = Ui.Selector("¿A qué obra?", _otras.Select(o => o.Nombre));
        _obraDestino.IsVisible = false;
        _destino.SelectedIndexChanged += (_, _) => _obraDestino.IsVisible = Destino == "OtraObra";

        Content = Ui.Contenedor(
            Ui.Titulo("Material que sobró"),
            Ui.Suave($"Obra {obra.Nombre}. Sirve para saber cuánto material se pierde de verdad."),
            _material,
            new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 10,
                Children = { _cantidad, Columna(_unidad, 1) },
            },
            _destino,
            _obraDestino,
            Ui.ConDictado(_nota),
            Ui.Boton("Guardar", GuardarAsync, color: Ui.Verde));
    }

    private string Destino => ValoresCapataz.DestinosSobrante[Math.Max(0, _destino.SelectedIndex)].Valor;

    private static View Columna(View v, int columna)
    {
        Grid.SetColumn(v, columna);
        return v;
    }

    private async Task GuardarAsync()
    {
        var material = _material.Text?.Trim();
        if (string.IsNullOrEmpty(material))
        {
            await DisplayAlertAsync("Sobrante", "Escribí qué material sobró.", "Corregir");
            return;
        }
        if (Ui.Decimal(_cantidad.Text) is not (decimal cantidad and > 0))
        {
            await DisplayAlertAsync("Sobrante", "Poné la cantidad.", "Corregir");
            return;
        }
        int? obraDestino = null;
        if (Destino == "OtraObra")
        {
            if (_obraDestino.SelectedIndex < 0)
            {
                await DisplayAlertAsync("Sobrante", _otras.Count == 0 ? "No tenés otras obras asignadas: elegí otro destino." : "Elegí a qué obra se llevó.", "Corregir");
                return;
            }
            obraDestino = _otras[_obraDestino.SelectedIndex].Id;
        }
        var unidad = Unidades[Math.Max(0, _unidad.SelectedIndex)];
        await App.Servicio<Cargas>().SobranteAsync(_obra, material, cantidad, unidad, Destino, obraDestino, _nota.Text);
        await Ui.AvisarAsync(this, "Guardado", "Se envía solo al estudio.");
        await Navigation.PopAsync();
    }
}
