using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>
/// Lo que informó la cuadrilla y espera al capataz: aprobar, corregir la cantidad (o el %) o rechazar con motivo
/// (lo ve el albañil). Lo aprobado es lo que cuenta para el avance, los rendimientos y los certificados.
/// Funciona sin señal: la decisión queda en la cola y se envía sola.
/// </summary>
public sealed class ValidarPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly ObraPage _padre;
    private readonly Cargas _cargas;
    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(16, 12, 16, 32), Spacing = 12 };

    public ValidarPage(ObraResumenDto obra, ObraPage padre)
    {
        _obra = obra;
        _padre = padre;
        _cargas = App.Servicio<Cargas>();
        Title = "Validar avances";
        BackgroundColor = Ui.Fondo;
        Content = new ScrollView { Content = _pila };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dibujar();
    }

    private void Dibujar()
    {
        _pila.Children.Clear();
        var lista = _padre.PorValidar();
        _pila.Children.Add(Ui.Titulo("Avances para validar"));
        if (lista.Count == 0)
        {
            _pila.Children.Add(Ui.Tarjeta(Ui.Texto("No queda nada para validar. ✔"), Ui.FondoOk));
            return;
        }
        _pila.Children.Add(Ui.Suave("Revisá lo que cargó cada uno. Lo que apruebes suma al avance de la obra."));
        if (lista.Count > 1)
            _pila.Children.Add(Ui.Boton($"✔  Aprobar los {lista.Count}", () => AprobarTodosAsync(lista), color: Ui.Verde, primario: false));
        foreach (var p in lista) _pila.Children.Add(Tarjeta(p));
    }

    private View Tarjeta(AvancePorValidarDto p)
    {
        var informado = p.Terminada ? "Dice que la terminó"
            : p.Unidad is not null ? $"Informó {Ui.Num(p.Cantidad ?? 0)} {p.Unidad}"
            : $"Dice que va al {p.Porcentaje ?? 0}%";
        var contexto = p.Unidad is not null && p.CantidadObjetivo is decimal objetivo
            ? $"Aprobado hasta ahora: {Ui.Num(p.CantidadAprobada)} de {Ui.Num(objetivo)} {p.Unidad}" : null;

        var pila = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = $"{p.Albanil} · {p.Tarea}", FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron },
                Ui.Texto(informado, 17),
                Ui.Suave(Ui.FechaCorta(p.FechaUtc) + (contexto is null ? "" : " · " + contexto)),
            },
        };
        if (!string.IsNullOrWhiteSpace(p.Nota)) pila.Children.Add(Ui.Texto($"«{p.Nota}»", 15));
        if (p.FotoIds.Count > 0)
            pila.Children.Add(Ui.Boton($"📷  Ver {p.FotoIds.Count} foto(s)", () => Navigation.PushAsync(new FotosPage(p)), primario: false));
        else if (p.Fotos == 0)
            pila.Children.Add(Ui.Suave("Sin fotos."));
        if (p.Videos > 0) pila.Children.Add(Ui.Suave($"🎥 {p.Videos} video(s): se ven en el panel del estudio."));

        var botones = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        botones.Add(Ui.Boton("✔\nAprobar", () => DecidirAsync(p, new ValidacionItem(p.RegistroId, ValoresCapataz.Aprobar), "Aprobado"), color: Ui.Verde), 0, 0);
        botones.Add(Ui.Boton("✎\nCorregir", () => CorregirAsync(p), primario: false), 1, 0);
        botones.Add(Ui.Boton("✖\nRechazar", () => RechazarAsync(p), color: Ui.Rojo), 2, 0);
        pila.Children.Add(botones);
        return Ui.Tarjeta(pila);
    }

    private async Task CorregirAsync(AvancePorValidarDto p)
    {
        if (p.Unidad is not null)
        {
            var texto = await DisplayPromptAsync("Corregir", $"¿Cuánto hizo {p.Albanil.Split(' ')[0]} de verdad? ({p.Unidad})",
                accept: "Guardar", cancel: "Cancelar", initialValue: Ui.Num(p.Cantidad ?? 0), keyboard: Keyboard.Numeric);
            if (texto is null) return;
            if (Ui.Decimal(texto) is not (decimal cantidad and >= 0))
            {
                await DisplayAlertAsync("Corregir", "La cantidad no es un número válido.", "Listo");
                return;
            }
            await DecidirAsync(p, new ValidacionItem(p.RegistroId, ValoresCapataz.Corregir, Cantidad: cantidad), $"Corregido a {Ui.Num(cantidad)} {p.Unidad}");
        }
        else
        {
            var texto = await DisplayPromptAsync("Corregir", "¿Cuánto va la tarea de verdad? (0 a 100 %)",
                accept: "Guardar", cancel: "Cancelar", initialValue: (p.Porcentaje ?? 0).ToString(), keyboard: Keyboard.Numeric);
            if (texto is null) return;
            if (!int.TryParse(texto.Trim().TrimEnd('%'), out var porcentaje) || porcentaje is < 0 or > 100)
            {
                await DisplayAlertAsync("Corregir", "Poné un porcentaje de 0 a 100.", "Listo");
                return;
            }
            await DecidirAsync(p, new ValidacionItem(p.RegistroId, ValoresCapataz.Corregir, Porcentaje: porcentaje), $"Corregido a {porcentaje}%");
        }
    }

    private async Task RechazarAsync(AvancePorValidarDto p)
    {
        var motivo = await DisplayPromptAsync("Rechazar", "¿Por qué? Lo ve el albañil en su app.", accept: "Rechazar", cancel: "Cancelar", maxLength: 500);
        if (motivo is null) return;
        if (string.IsNullOrWhiteSpace(motivo))
        {
            await DisplayAlertAsync("Rechazar", "Para rechazar escribí el motivo.", "Listo");
            return;
        }
        await DecidirAsync(p, new ValidacionItem(p.RegistroId, ValoresCapataz.Rechazar, Motivo: motivo.Trim()), "Rechazado");
    }

    private async Task DecidirAsync(AvancePorValidarDto p, ValidacionItem item, string que)
    {
        await _cargas.ValidarAsync(_obra, [item], $"{que}: {p.Albanil}, {p.Tarea}");
        await _padre.RefrescarAsync();
        Dibujar();
    }

    private async Task AprobarTodosAsync(List<AvancePorValidarDto> lista)
    {
        if (!await DisplayAlertAsync("Aprobar todos", $"¿Aprobar los {lista.Count} avances tal como los cargaron?", "Aprobar", "Cancelar")) return;
        // Se mandan los que se ven ahora (no «todo lo pendiente»): lo que llegue después se revisa aparte.
        await _cargas.ValidarAsync(_obra, lista.Select(p => new ValidacionItem(p.RegistroId, ValoresCapataz.Aprobar)).ToList(),
            $"Aprobados {lista.Count} avances");
        await _padre.RefrescarAsync();
        Dibujar();
    }
}

/// <summary>Las fotos de un avance (se bajan del estudio: necesita señal).</summary>
public sealed class FotosPage : ContentPage
{
    private readonly AvancePorValidarDto _avance;
    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(12), Spacing = 12 };

    public FotosPage(AvancePorValidarDto avance)
    {
        _avance = avance;
        Title = $"{avance.Albanil} · {avance.Tarea}";
        BackgroundColor = Ui.Fondo;
        Content = new ScrollView { Content = _pila };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_pila.Children.Count > 0) return;
        var api = App.Servicio<ApiCliente>();
        var cargando = Ui.Suave("Bajando las fotos…");
        _pila.Children.Add(cargando);
        var bajadas = 0;
        foreach (var id in _avance.FotoIds)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var bytes = await api.FotoAsync(id, cts.Token);
                _pila.Children.Add(new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes)), Aspect = Aspect.AspectFit });
                bajadas++;
            }
            catch (SesionVencidaException ex) { await ((App)Application.Current!).IrAlLoginAsync(ex.Message); return; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ErrorApiException) { /* se avisa abajo */ }
        }
        cargando.Text = bajadas == _avance.FotoIds.Count ? $"{bajadas} foto(s)."
            : bajadas == 0 ? "No se pudieron bajar las fotos: hace falta señal. Probá de nuevo más tarde."
            : $"Se bajaron {bajadas} de {_avance.FotoIds.Count}. Para las otras hace falta señal.";
    }
}
