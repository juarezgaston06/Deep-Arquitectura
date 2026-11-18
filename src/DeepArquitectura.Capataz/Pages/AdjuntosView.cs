using DeepArquitectura.Capataz.Services;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Botones de cámara (foto / video / galería) y las miniaturas de lo que se va a enviar. Tocar una la quita.</summary>
public sealed class AdjuntosView : ContentView
{
    private readonly List<FileResult> _archivos = [];
    private readonly FlexLayout _miniaturas = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start };
    private readonly Label _resumen = Ui.Suave("Sin fotos ni videos todavía.");
    private readonly int _maximo;
    private readonly bool _conVideos;

    public IReadOnlyList<FileResult> Archivos => _archivos;

    /// <param name="conVideos">false = solo fotos (comprobantes de gasto).</param>
    /// <param name="maximo">Cuántos archivos como mucho (0 = sin límite).</param>
    public AdjuntosView(bool conVideos = true, int maximo = 0)
    {
        _maximo = maximo;
        _conVideos = conVideos;
        if (!conVideos) _resumen.Text = "Sin fotos todavía.";
        var botones = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 10,
            RowSpacing = 10,
        };
        if (conVideos)
        {
            botones.Add(Ui.Boton("📷  Sacar foto", () => Agregar(Medios.SacarFotoAsync)), 0, 0);
            botones.Add(Ui.Boton("🎥  Grabar video", () => Agregar(Medios.GrabarVideoAsync)), 1, 0);
            botones.Add(Ui.Boton("🖼  Fotos de la galería", () => AgregarVarios(false), primario: false), 0, 1);
            botones.Add(Ui.Boton("🎞  Videos de la galería", () => AgregarVarios(true), primario: false), 1, 1);
        }
        else
        {
            botones.Add(Ui.Boton("📷  Sacar foto", () => Agregar(Medios.SacarFotoAsync)), 0, 0);
            botones.Add(Ui.Boton("🖼  De la galería", () => AgregarVarios(false), primario: false), 1, 0);
        }

        Content = new VerticalStackLayout { Spacing = 10, Children = { botones, _resumen, _miniaturas } };
    }

    private async Task Agregar(Func<Task<FileResult?>> capturar)
    {
        try
        {
            if (await capturar() is { } f) Sumar([f]);
            else if (!MediaPicker.Default.IsCaptureSupported) await Avisar("Este teléfono no tiene cámara disponible.");
        }
        catch (PermissionException) { await Avisar("Sin permiso para usar la cámara. Activalo en Ajustes → Apps → Deep Obra."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { await Avisar("No se pudo usar la cámara: " + ex.Message); }
    }

    private async Task AgregarVarios(bool videos)
    {
        try { Sumar(await Medios.ElegirDeGaleriaAsync(videos)); }
        catch (PermissionException) { await Avisar("Sin permiso para ver la galería."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { await Avisar("No se pudo abrir la galería: " + ex.Message); }
    }

    private void Sumar(IEnumerable<FileResult> archivos)
    {
        foreach (var f in archivos)
        {
            if (_maximo > 0 && _archivos.Count >= _maximo) break;
            _archivos.Add(f);
            _miniaturas.Children.Add(Miniatura(f));
        }
        ActualizarResumen();
    }

    private View Miniatura(FileResult f)
    {
        View contenido = Medios.EsVideo(f)
            ? new Label { Text = "🎥\nvideo", FontSize = 16, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, TextColor = Ui.Marron }
            : new Image { Source = ImageSource.FromFile(f.FullPath), Aspect = Aspect.AspectFill };
        var marco = new Border
        {
            WidthRequest = 96, HeightRequest = 96, Margin = new Thickness(0, 0, 8, 8),
            StrokeThickness = 0, BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = new Grid { Children = { contenido, new Label { Text = "✕", FontSize = 18, TextColor = Colors.White, BackgroundColor = Color.FromRgba(0, 0, 0, 0.45), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Padding = new Thickness(6, 0) } } },
        };
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) =>
        {
            if (Window?.Page is not { } p || !await p.DisplayAlertAsync("Quitar", "¿Sacar este archivo del envío?", "Quitar", "Dejar")) return;
            _archivos.Remove(f);
            _miniaturas.Children.Remove(marco);
            ActualizarResumen();
        };
        marco.GestureRecognizers.Add(toque);
        return marco;
    }

    private void ActualizarResumen()
    {
        var videos = _archivos.Count(Medios.EsVideo);
        var fotos = _archivos.Count - videos;
        _resumen.Text = _archivos.Count == 0 ? (_conVideos ? "Sin fotos ni videos todavía." : "Sin fotos todavía.")
            : _conVideos ? $"{fotos} foto(s) y {videos} video(s). Tocá una para quitarla."
            : $"{fotos} foto(s). Tocá una para quitarla.";
    }

    private Task Avisar(string texto) => Window?.Page?.DisplayAlertAsync("Cámara", texto, "Listo") ?? Task.CompletedTask;
}
