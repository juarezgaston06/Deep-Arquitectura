using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Avisar un problema: qué tipo, qué tan urgente, qué pasó y fotos/videos.</summary>
public sealed class ProblemaPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly IReadOnlyList<TareaDto> _tareas;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly Dictionary<string, Button> _tipos = [];
    private readonly Dictionary<string, Button> _urgencias = [];
    private readonly Picker _tarea = new() { Title = "Tarea afectada (opcional)", FontSize = 17, BackgroundColor = Colors.White, TextColor = Ui.Marron };
    private readonly Editor _descripcion = Ui.Area("¿Qué pasó? Contalo como se lo dirías al capataz.");
    private readonly AdjuntosView _adjuntos = new();
    private string? _tipo;
    private string _urgencia = "Media";

    public ProblemaPage(ObraResumenDto obra, IReadOnlyList<TareaDto> tareas)
    {
        _obra = obra;
        _tareas = tareas;
        Title = "Avisar un problema";
        BackgroundColor = Ui.Fondo;

        var tipos = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < ValoresAlbanil.TiposProblema.Length; i++)
        {
            var valor = ValoresAlbanil.TiposProblema[i];
            var b = Ui.Boton(ValoresAlbanil.Etiqueta(valor), () => { Elegir(_tipos, valor); _tipo = valor; return Task.CompletedTask; }, primario: false);
            _tipos[valor] = b;
            if (i / 2 >= tipos.RowDefinitions.Count) tipos.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            tipos.Add(b, i % 2, i / 2);
        }

        var urgencias = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        for (var i = 0; i < ValoresAlbanil.Urgencias.Length; i++)
        {
            var valor = ValoresAlbanil.Urgencias[i];
            var color = valor == "Alta" ? Ui.Rojo : valor == "Media" ? Ui.Ambar : Ui.Marron;
            var b = Ui.Boton(valor, () => { Elegir(_urgencias, valor); _urgencia = valor; return Task.CompletedTask; }, primario: false, color: color);
            _urgencias[valor] = b;
            urgencias.Add(b, i, 0);
        }
        Elegir(_urgencias, _urgencia);

        _tarea.Items.Add("Ninguna en especial");
        foreach (var t in tareas) _tarea.Items.Add(t.Titulo);
        _tarea.SelectedIndex = 0;

        Content = Ui.Contenedor(
            Ui.Titulo("¿Qué problema hay?"),
            Ui.Suave(_obra.Nombre + " · si alguien se lastimó, llamá primero al capataz o a emergencias (107)."),
            tipos,
            Ui.Seccion("Urgencia"),
            urgencias,
            Ui.Seccion("Qué pasó"),
            Ui.ConDictado(_descripcion),
            _tarea,
            Ui.Seccion("Fotos y videos"),
            _adjuntos,
            Ui.Boton("Enviar aviso", EnviarAsync, color: Ui.Rojo));
    }

    /// <summary>Marca el botón elegido (relleno) y deja los demás con borde.</summary>
    private static void Elegir(Dictionary<string, Button> grupo, string valor)
    {
        foreach (var (k, b) in grupo)
        {
            var color = b.BorderColor;
            b.BackgroundColor = k == valor ? color : Colors.White;
            b.TextColor = k == valor ? Colors.White : color;
        }
    }

    private async Task EnviarAsync()
    {
        if (_tipo is null) { await DisplayAlertAsync("Problema", "Elegí qué tipo de problema es.", "Corregir"); return; }
        if (string.IsNullOrWhiteSpace(_descripcion.Text)) { await DisplayAlertAsync("Problema", "Contá qué pasó.", "Corregir"); return; }
        var tareaId = _tarea.SelectedIndex > 0 ? _tareas[_tarea.SelectedIndex - 1].Id : (int?)null;
        await _cargas.ProblemaAsync(_obra, tareaId, _tipo, _urgencia, _descripcion.Text, _adjuntos.Archivos);
        await Ui.AvisarAsync(this, "Aviso", _urgencia == "Alta"
            ? "Aviso guardado y enviándose. Si es urgente de verdad, llamá también al capataz."
            : "Aviso guardado. Se envía solo al estudio.");
        await Navigation.PopAsync();
    }
}
