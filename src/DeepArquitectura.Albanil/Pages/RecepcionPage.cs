using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>
/// Llegó material: de un pedido (se carga cuánto llegó de cada renglón) o sin pedido. Con foto del remito.
/// Si falta algo del pedido, el estudio recibe el aviso solo.
/// </summary>
public sealed class RecepcionPage : ContentPage
{
    private sealed record Renglon(int? PedidoItemId, Entry Material, Entry Cantidad, Picker Unidad, string? MaterialFijo, string? UnidadFija);

    private readonly ObraResumenDto _obra;
    private readonly PedidoDto? _pedido;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly List<Renglon> _renglones = [];
    private readonly VerticalStackLayout _lista = new() { Spacing = 10 };
    private readonly Entry _proveedor = Ui.Campo("Corralón / proveedor (opcional)", max: 150);
    private readonly Entry _remito = Ui.Campo("N° de remito (opcional)", max: 50);
    private readonly Editor _nota = Ui.Area("Nota (opcional): vino roto, mojado, otra marca…", 1000);
    private readonly AdjuntosView _fotos = new();

    public RecepcionPage(ObraResumenDto obra, PedidoDto? pedido)
    {
        _obra = obra;
        _pedido = pedido;
        Title = "Llegó material";
        BackgroundColor = Ui.Fondo;

        if (pedido?.Items is { Count: > 0 } items)
            foreach (var i in items) AgregarDePedido(i);
        else
            AgregarLibre();

        Content = Ui.Contenedor(
            Ui.Titulo(pedido is null ? "¿Qué llegó?" : $"Pedido #{pedido.Id}"),
            Ui.Suave(pedido is null ? obra.Nombre : "Cargá cuánto llegó de cada cosa. Si no llegó, dejá 0."),
            _lista,
            Ui.Boton("+ Llegó otra cosa", () => { AgregarLibre(); return Task.CompletedTask; }, primario: false),
            _proveedor, _remito,
            Ui.Seccion("Foto del remito"),
            _fotos,
            Ui.ConDictado(_nota),
            Ui.Boton("Enviar", EnviarAsync, color: Ui.Verde));
    }

    private void AgregarDePedido(PedidoItemDto i)
    {
        var falta = Math.Max(0, i.Cantidad - i.Recibido);
        var cantidad = Ui.Campo("Cantidad", Keyboard.Numeric, 10);
        cantidad.Text = Ui.Num(falta);
        var fila = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(130))], ColumnSpacing = 8 };
        fila.Add(new VerticalStackLayout
        {
            Children =
            {
                Ui.Texto($"{i.Material}", 18),
                Ui.Suave($"Pedido {Ui.Num(i.Cantidad)} {i.Unidad}{(i.Recibido > 0 ? $" · ya llegó {Ui.Num(i.Recibido)}" : "")}"),
            },
        }, 0, 0);
        fila.Add(new VerticalStackLayout { Children = { cantidad, Ui.Suave(i.Unidad) } }, 1, 0);
        _renglones.Add(new Renglon(i.Id, new Entry(), cantidad, new Picker(), i.Material, i.Unidad));
        _lista.Children.Add(Ui.Tarjeta(fila));
    }

    private void AgregarLibre()
    {
        var material = Ui.Campo("Material", max: 150);
        var cantidad = Ui.Campo("Cantidad", Keyboard.Numeric, 10);
        var unidad = new Picker { Title = "Unidad", FontSize = 17, BackgroundColor = Colors.White, TextColor = Ui.Marron, MinimumHeightRequest = 52 };
        foreach (var u in ValoresAlbanil.UnidadesMaterial) unidad.Items.Add(u);
        unidad.SelectedIndex = 0;
        var fila = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        fila.Add(cantidad, 0, 0);
        fila.Add(unidad, 1, 0);
        _renglones.Add(new Renglon(null, material, cantidad, unidad, null, null));
        _lista.Children.Add(Ui.Tarjeta(new VerticalStackLayout { Spacing = 8, Children = { material, fila } }));
    }

    private async Task EnviarAsync()
    {
        var items = new List<RecepcionItemRequest>();
        foreach (var r in _renglones)
        {
            var material = r.MaterialFijo ?? r.Material.Text?.Trim();
            if (string.IsNullOrEmpty(material)) { if (string.IsNullOrWhiteSpace(r.Cantidad.Text)) continue; await Error("Falta escribir qué material."); return; }
            var cant = Ui.Decimal(r.Cantidad.Text) ?? 0;
            if (cant < 0) { await Error($"La cantidad de «{material}» no es válida."); return; }
            if (r.PedidoItemId is null && cant == 0) continue;
            items.Add(new RecepcionItemRequest(r.PedidoItemId, material, cant, r.UnidadFija ?? (string)r.Unidad.SelectedItem));
        }
        if (items.Count == 0 || items.All(i => i.Cantidad == 0)) { await Error("Cargá al menos una cantidad."); return; }

        if (_pedido?.Items is { } pedidos)
        {
            var faltan = pedidos.Where(p => items.FirstOrDefault(i => i.PedidoItemId == p.Id) is var i && p.Recibido + (i?.Cantidad ?? 0) < p.Cantidad).ToList();
            if (faltan.Count > 0 && !await DisplayAlertAsync("Material", $"Falta: {string.Join(", ", faltan.Select(f => f.Material))}. Se avisa al estudio. ¿Enviar así?", "Enviar", "Corregir"))
                return;
        }
        if (_fotos.Archivos.Count == 0 && !await DisplayAlertAsync("Remito", "No sacaste foto del remito. ¿Enviar igual?", "Enviar", "Sacar foto"))
            return;

        await _cargas.RecepcionAsync(_obra, _pedido, _proveedor.Text, _remito.Text, _nota.Text, items, _fotos.Archivos);
        await Ui.AvisarAsync(this, "Material", "Guardado. Se envía solo al estudio.");
        await Navigation.PopAsync();
    }

    private Task Error(string texto) => DisplayAlertAsync("Material", texto, "Corregir");
}
