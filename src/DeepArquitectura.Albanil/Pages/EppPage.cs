using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>
/// Antes de fichar la entrada: ¿tenés la protección? Se tilda lo que tiene puesto. Si falta algo se ficha igual, pero
/// queda un aviso de seguridad para el capataz y el estudio (urgente si trabaja en altura sin arnés).
/// </summary>
public sealed class EppPage : ContentPage
{
    public sealed record Resultado(bool EnAltura, IReadOnlyList<string> Faltante);

    private readonly TaskCompletionSource<Resultado?> _tcs = new();
    private readonly Dictionary<string, CheckBox> _items = [];
    private readonly Switch _altura = new() { OnColor = Ui.Ambar, VerticalOptions = LayoutOptions.Center };
    private readonly View _filaArnes;

    private EppPage()
    {
        Title = "Protección";
        BackgroundColor = Ui.Fondo;
        var lista = new VerticalStackLayout { Spacing = 8 };
        foreach (var e in ValoresAlbanil.Epp) lista.Children.Add(Fila(e));
        _filaArnes = Fila(ValoresAlbanil.Arnes);
        _filaArnes.IsVisible = false;
        _altura.Toggled += (_, e) => _filaArnes.IsVisible = e.Value;

        Content = Ui.Contenedor(
            Ui.Titulo("Antes de entrar"),
            Ui.Suave("Tildá lo que tenés puesto."),
            lista,
            Ui.Tarjeta(new HorizontalStackLayout { Spacing = 10, Children = { _altura, Ui.Texto("Hoy trabajo en altura (andamio, techo, losa)", 17) } }),
            _filaArnes,
            Ui.Boton("Fichar entrada", Confirmar, color: Ui.Verde),
            Ui.Boton("Cancelar", () => Navigation.PopAsync(), primario: false));
    }

    private View Fila(string elemento)
    {
        var check = new CheckBox { Color = Ui.Verde, VerticalOptions = LayoutOptions.Center };
        _items[elemento] = check;
        var fila = new HorizontalStackLayout { Spacing = 10, Children = { check, Ui.Texto(elemento, 19) } };
        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) => check.IsChecked = !check.IsChecked;
        fila.GestureRecognizers.Add(toque);
        return Ui.Tarjeta(fila);
    }

    private async Task Confirmar()
    {
        var faltante = _items.Where(i => !i.Value.IsChecked && (i.Key != ValoresAlbanil.Arnes || _altura.IsToggled)).Select(i => i.Key).ToList();
        if (faltante.Count > 0 && !await DisplayAlertAsync("Protección",
                $"Te falta: {string.Join(", ", faltante)}. Se avisa al capataz.{(faltante.Contains(ValoresAlbanil.Arnes) ? " En altura sin arnés no trabajes." : "")} ¿Fichar igual?",
                "Fichar igual", "Volver"))
            return;
        _tcs.TrySetResult(new Resultado(_altura.IsToggled, faltante));
        await Navigation.PopAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _tcs.TrySetResult(null); // volvió atrás: no se ficha
    }

    /// <summary>Muestra la pantalla y espera: null si canceló.</summary>
    public static async Task<Resultado?> PedirAsync(INavigation nav)
    {
        var p = new EppPage();
        await nav.PushAsync(p);
        return await p._tcs.Task;
    }
}
