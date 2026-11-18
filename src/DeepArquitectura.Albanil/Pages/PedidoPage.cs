using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Pedido de materiales: uno o varios renglones (qué, cuánto, unidad), para qué tarea, si es urgente y una nota.</summary>
public sealed class PedidoPage : ContentPage
{
    private sealed record Renglon(View Vista, Entry Material, Entry Cantidad, Picker Unidad);

    private readonly ObraResumenDto _obra;
    private readonly IReadOnlyList<TareaDto> _tareas;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly List<Renglon> _renglones = [];
    private readonly VerticalStackLayout _lista = new() { Spacing = 10 };
    private readonly Picker _tarea = new() { Title = "Para qué tarea (opcional)", FontSize = 17, BackgroundColor = Colors.White, TextColor = Ui.Marron };
    private readonly Switch _urgente = new() { OnColor = Ui.Rojo, VerticalOptions = LayoutOptions.Center };
    private readonly Editor _nota = Ui.Area("Nota (opcional): marca, medida, para cuándo…", 1000);

    public PedidoPage(ObraResumenDto obra, IReadOnlyList<TareaDto> tareas)
    {
        _obra = obra;
        _tareas = tareas;
        Title = "Pedir material";
        BackgroundColor = Ui.Fondo;

        _tarea.Items.Add("Ninguna en especial");
        foreach (var t in tareas) _tarea.Items.Add(t.Titulo);
        _tarea.SelectedIndex = 0;
        AgregarRenglon();

        var urgente = new HorizontalStackLayout { Spacing = 10, Children = { _urgente, Ui.Texto("Es urgente (se frena el trabajo)", 17, Ui.Rojo) } };
        Content = Ui.Contenedor(
            Ui.Titulo("¿Qué material necesitás?"),
            Ui.Suave(_obra.Nombre),
            _lista,
            Ui.Boton("+ Agregar otro material", () => { AgregarRenglon(); return Task.CompletedTask; }, primario: false),
            Ui.Seccion("Para"),
            _tarea,
            Ui.Tarjeta(urgente),
            Ui.ConDictado(_nota),
            Ui.Boton("Enviar pedido", EnviarAsync, color: Ui.Verde),
            Ui.Suave("El estudio lo ve en el panel y te contesta acá (aprobado, rechazado o entregado)."));
    }

    private void AgregarRenglon()
    {
        if (_renglones.Count >= RutasAlbanil.MaximoItemsPedido) return;
        var material = Ui.Campo("Material (ej. cemento, arena, ladrillo hueco 12)", max: 150);
        var cantidad = Ui.Campo("Cantidad", Keyboard.Numeric, 10);
        var unidad = new Picker { Title = "Unidad", FontSize = 17, BackgroundColor = Colors.White, TextColor = Ui.Marron, MinimumHeightRequest = 52 };
        foreach (var u in ValoresAlbanil.UnidadesMaterial) unidad.Items.Add(u);
        unidad.SelectedIndex = 0;

        var fila = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        fila.Add(cantidad, 0, 0);
        fila.Add(unidad, 1, 0);
        var pila = new VerticalStackLayout { Spacing = 8, Children = { material, fila } };
        var tarjeta = Ui.Tarjeta(pila);
        var renglon = new Renglon(tarjeta, material, cantidad, unidad);
        if (_renglones.Count > 0)
        {
            var quitar = new Button { Text = "Quitar", FontSize = 15, BackgroundColor = Colors.Transparent, TextColor = Ui.Rojo, HorizontalOptions = LayoutOptions.End };
            quitar.Clicked += (_, _) => { _renglones.Remove(renglon); _lista.Children.Remove(tarjeta); };
            pila.Children.Add(quitar);
        }
        _renglones.Add(renglon);
        _lista.Children.Add(tarjeta);
        material.Focus();
    }

    private async Task EnviarAsync()
    {
        var items = new List<PedidoItemRequest>();
        foreach (var r in _renglones)
        {
            var material = r.Material.Text?.Trim();
            if (string.IsNullOrEmpty(material) && string.IsNullOrWhiteSpace(r.Cantidad.Text)) continue; // renglón vacío
            if (string.IsNullOrEmpty(material)) { await Error("Falta escribir qué material."); return; }
            if (Ui.Decimal(r.Cantidad.Text) is not { } cant || cant <= 0) { await Error($"Falta la cantidad de «{material}»."); return; }
            items.Add(new PedidoItemRequest(material, cant, (string)r.Unidad.SelectedItem));
        }
        if (items.Count == 0) { await Error("Agregá al menos un material."); return; }

        var tareaId = _tarea.SelectedIndex > 0 ? _tareas[_tarea.SelectedIndex - 1].Id : (int?)null;
        await _cargas.PedidoAsync(_obra, tareaId, _urgente.IsToggled, _nota.Text, items);
        await Ui.AvisarAsync(this, "Pedido", "Pedido guardado. Se envía solo al estudio.");
        await Navigation.PopAsync();
    }

    private Task Error(string texto) => DisplayAlertAsync("Pedido", texto, "Corregir");
}
