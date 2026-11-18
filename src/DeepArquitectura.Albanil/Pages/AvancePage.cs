using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>
/// Cargar avance: cuánto se hizo (en la unidad de la tarea o en %), si se terminó, una nota y fotos/videos.
/// Sin tarea: fotos y videos del avance general de la obra.
/// </summary>
public sealed class AvancePage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly TareaDto? _tarea;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly Entry? _cantidad;
    private readonly Slider? _porcentaje;
    private readonly Label? _porcentajeTexto;
    private readonly CheckBox _terminada = new() { Color = Ui.Verde, VerticalOptions = LayoutOptions.Center };
    private readonly Editor _nota = Ui.Area("Nota (opcional): qué hiciste, algo para avisar…");
    private readonly AdjuntosView _adjuntos = new();

    public AvancePage(ObraResumenDto obra, TareaDto? tarea)
    {
        _obra = obra;
        _tarea = tarea;
        Title = tarea is null ? "Fotos de avance" : "Cargar avance";
        BackgroundColor = Ui.Fondo;

        var pila = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 32), Spacing = 12 };
        if (tarea is null)
        {
            pila.Children.Add(Ui.Titulo("Fotos y videos de la obra"));
            pila.Children.Add(Ui.Suave("Para mostrar cómo va la obra en general. Si es de una tarea, entrá desde la tarea así suma al avance."));
        }
        else
        {
            pila.Children.Add(Ui.Titulo(tarea.Titulo));
            if (tarea.Etapa is not null) pila.Children.Add(Ui.Suave(tarea.Etapa));
            if (!string.IsNullOrWhiteSpace(tarea.Descripcion)) pila.Children.Add(Ui.Texto(tarea.Descripcion, 15));
            pila.Children.Add(Ui.Progreso(tarea.Porcentaje));

            if (tarea.Unidad is not null)
            {
                pila.Children.Add(Ui.Texto($"Hecho hasta ahora: {Ui.Num(tarea.CantidadHecha)} de {Ui.Num(tarea.CantidadObjetivo ?? 0)} {tarea.Unidad}", 15));
                pila.Children.Add(new Label { Text = $"¿Cuánto hiciste ahora? ({tarea.Unidad})", Style = Estilo("Etiqueta") });
                _cantidad = Ui.Campo($"ej. 8 {tarea.Unidad}", Keyboard.Numeric, 12);
                pila.Children.Add(_cantidad);
            }
            else
            {
                pila.Children.Add(new Label { Text = "¿Cómo va la tarea en total?", Style = Estilo("Etiqueta") });
                _porcentajeTexto = new Label { FontSize = 28, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron, HorizontalOptions = LayoutOptions.Center };
                _porcentaje = new Slider { Minimum = 0, Maximum = 100, Value = tarea.Porcentaje, MinimumTrackColor = Ui.Marron, ThumbColor = Ui.Marron, HeightRequest = 48 };
                _porcentaje.ValueChanged += (_, e) => Redondear(e.NewValue);
                Redondear(tarea.Porcentaje);
                var rapidos = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
                int[] valores = [25, 50, 75, 90];
                for (var i = 0; i < valores.Length; i++)
                {
                    var v = valores[i];
                    rapidos.Add(Ui.Boton($"{v}%", () => { _porcentaje.Value = v; return Task.CompletedTask; }, primario: false), i, 0);
                }
                pila.Children.Add(_porcentajeTexto);
                pila.Children.Add(_porcentaje);
                pila.Children.Add(rapidos);
            }

            var fila = new HorizontalStackLayout { Spacing = 8, Children = { _terminada, new Label { Text = "Terminé esta tarea", FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Ui.Verde, VerticalOptions = LayoutOptions.Center } } };
            var toque = new TapGestureRecognizer();
            toque.Tapped += (_, _) => _terminada.IsChecked = !_terminada.IsChecked;
            fila.GestureRecognizers.Add(toque);
            pila.Children.Add(Ui.Tarjeta(fila));
        }

        pila.Children.Add(Ui.Seccion("Fotos y videos"));
        pila.Children.Add(_adjuntos);
        pila.Children.Add(Ui.Seccion("Nota"));
        pila.Children.Add(Ui.ConDictado(_nota));
        pila.Children.Add(Ui.Boton("Enviar avance", EnviarAsync, color: Ui.Verde));
        pila.Children.Add(Ui.Suave("Se guarda en el teléfono y se envía solo al estudio, aunque ahora no haya señal."));
        Content = new ScrollView { Content = pila };
    }

    private void Redondear(double valor)
    {
        var v = (int)(Math.Round(valor / 5) * 5);
        if (_porcentaje is not null && Math.Abs(_porcentaje.Value - v) > 0.01) _porcentaje.Value = v;
        if (_porcentajeTexto is not null) _porcentajeTexto.Text = $"{v}%";
    }

    private async Task EnviarAsync()
    {
        decimal? cantidad = null;
        int? porcentaje = null;
        if (_cantidad is not null && !string.IsNullOrWhiteSpace(_cantidad.Text))
        {
            cantidad = Ui.Decimal(_cantidad.Text);
            if (cantidad is null or < 0) { await DisplayAlertAsync("Avance", "La cantidad no es un número válido.", "Corregir"); return; }
        }
        if (_porcentaje is not null && _tarea is not null && (int)_porcentaje.Value != _tarea.Porcentaje) porcentaje = (int)_porcentaje.Value;

        var hayAlgo = cantidad > 0 || porcentaje is not null || _terminada.IsChecked || !string.IsNullOrWhiteSpace(_nota.Text) || _adjuntos.Archivos.Count > 0;
        if (!hayAlgo)
        {
            await DisplayAlertAsync("Avance", _tarea is null ? "Sacá al menos una foto o un video." : "Cargá cuánto hiciste, una foto o una nota.", "Listo");
            return;
        }
        if (_tarea?.Unidad is not null && _tarea.CantidadObjetivo is > 0 && cantidad is > 0 && _tarea.CantidadHecha + cantidad > _tarea.CantidadObjetivo * 1.5m
            && !await DisplayAlertAsync("Avance", $"{Ui.Num(cantidad.Value)} {_tarea.Unidad} supera bastante lo que falta. ¿Es correcto?", "Sí, enviar", "Corregir"))
            return;

        await _cargas.AvanceAsync(_obra, _tarea, cantidad, porcentaje, _terminada.IsChecked, _nota.Text, _adjuntos.Archivos);
        await Ui.AvisarAsync(this, "Guardado", _adjuntos.Archivos.Count > 0
            ? "Avance guardado. Las fotos y videos se envían solos (si no hay señal, cuando vuelva)."
            : "Avance guardado. Se envía solo al estudio.");
        await Navigation.PopAsync();
    }

    private static Style? Estilo(string clave) =>
        Application.Current?.Resources.TryGetValue(clave, out var s) == true ? s as Style : null;
}
