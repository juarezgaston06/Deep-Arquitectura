using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Parte del día: horas por tarea y un resumen. Mandarlo de nuevo el mismo día lo corrige.</summary>
public sealed class PartePage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly List<(int? TareaId, Entry Horas, Entry? Detalle)> _filas = [];
    private readonly Label _total = new() { FontSize = 20, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron };
    private readonly Editor _resumen = Ui.Area("¿Qué hiciste hoy? (ej. terminé el paño norte, faltó arena a la tarde)");
    private readonly DateOnly _fecha = DateOnly.FromDateTime(DateTime.Now);

    public PartePage(ObraResumenDto obra, IReadOnlyList<TareaDto> tareas, ParteDto? parteDeHoy)
    {
        _obra = obra;
        Title = "Parte del día";
        BackgroundColor = Ui.Fondo;

        var previo = parteDeHoy is not null && parteDeHoy.Fecha == _fecha ? parteDeHoy : null;
        _resumen.Text = previo?.Resumen;

        var filas = new VerticalStackLayout { Spacing = 10 };
        foreach (var t in tareas)
            filas.Children.Add(Fila(t.Id, t.Titulo, previo?.Items.FirstOrDefault(i => i.TareaId == t.Id)?.Horas, null));
        var otras = previo?.Items.FirstOrDefault(i => i.TareaId is null);
        filas.Children.Add(Fila(null, "Otras tareas (limpieza, acarreo, ayuda…)", otras?.Horas, otras?.Detalle));

        var jornada = App.Servicio<EstadoLocal>().Jornada(obra.Id);
        var fichado = jornada is null ? null : Ui.Suave($"Fichaste la entrada a las {Ui.Hora(jornada.EntradaUtc)} ({Ui.Duracion(DateTime.UtcNow - jornada.EntradaUtc)} hasta ahora).");

        var vistas = new List<View>
        {
            Ui.Titulo($"Parte del {_fecha:dd/MM}"),
            Ui.Suave(previo is null ? obra.Nombre : $"{obra.Nombre} · ya mandaste un parte hoy: lo que envíes ahora lo reemplaza."),
        };
        if (fichado is not null) vistas.Add(fichado);
        vistas.AddRange([Ui.Seccion("Horas por tarea"), filas, _total, Ui.Seccion("Resumen"), Ui.ConDictado(_resumen), Ui.Boton("Enviar parte", EnviarAsync, color: Ui.Verde)]);
        Content = Ui.Contenedor([.. vistas]);
        Recalcular();
    }

    private View Fila(int? tareaId, string titulo, decimal? horas, string? detalle)
    {
        var entrada = Ui.Campo("horas", Keyboard.Numeric, 5);
        entrada.Text = horas is null ? null : Ui.Num(horas.Value);
        entrada.WidthRequest = 110;
        entrada.HorizontalTextAlignment = TextAlignment.Center;
        entrada.TextChanged += (_, _) => Recalcular();
        Entry? det = null;
        var grilla = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 10 };
        grilla.Add(new Label { Text = titulo, FontSize = 17, TextColor = Ui.Marron, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap }, 0, 0);
        grilla.Add(entrada, 1, 0);
        var pila = new VerticalStackLayout { Spacing = 8, Children = { grilla } };
        if (tareaId is null)
        {
            det = Ui.Campo("Qué otras tareas (opcional)", max: 300);
            det.Text = detalle;
            pila.Children.Add(det);
        }
        _filas.Add((tareaId, entrada, det));
        return Ui.Tarjeta(pila);
    }

    private decimal Total() => _filas.Sum(f => Ui.Decimal(f.Horas.Text) ?? 0);

    private void Recalcular()
    {
        var total = Total();
        _total.Text = $"Total: {Ui.Num(total)} h";
        _total.TextColor = total > RutasAlbanil.MaximoHorasDia ? Ui.Rojo : Ui.Marron;
    }

    private async Task EnviarAsync()
    {
        var items = new List<ParteItemDto>();
        foreach (var (tareaId, horas, detalle) in _filas)
        {
            if (string.IsNullOrWhiteSpace(horas.Text)) continue;
            if (Ui.Decimal(horas.Text) is not { } h || h <= 0 || h > RutasAlbanil.MaximoHorasDia)
            {
                await DisplayAlertAsync("Parte", "Revisá las horas: tienen que ser un número entre 0 y 24.", "Corregir");
                return;
            }
            items.Add(new ParteItemDto(tareaId, h, string.IsNullOrWhiteSpace(detalle?.Text) ? null : detalle.Text.Trim()));
        }
        if (Total() > RutasAlbanil.MaximoHorasDia) { await DisplayAlertAsync("Parte", "El total del día no puede pasar de 24 horas.", "Corregir"); return; }
        if (items.Count == 0 && string.IsNullOrWhiteSpace(_resumen.Text)) { await DisplayAlertAsync("Parte", "Cargá las horas o contá qué hiciste.", "Corregir"); return; }

        await _cargas.ParteAsync(_obra, _fecha, _resumen.Text, items);
        await Ui.AvisarAsync(this, "Parte", "Parte guardado. Se envía solo al estudio.");
        await Navigation.PopAsync();
    }
}
