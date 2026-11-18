using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>
/// Tareas pasadas de fecha: el capataz anota por qué (lluvia, falta de material, cambio del cliente…). Con eso el
/// estudio ve qué es lo que más atrasa las obras. Si el sistema tiene una pista (días de lluvia, avisos de falta de
/// material), la propone.
/// </summary>
public sealed class AtrasosPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly ObraPage _padre;
    private readonly Cargas _cargas;
    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(16, 12, 16, 32), Spacing = 12 };

    public AtrasosPage(ObraResumenDto obra, ObraPage padre)
    {
        _obra = obra;
        _padre = padre;
        _cargas = App.Servicio<Cargas>();
        Title = "Tareas atrasadas";
        BackgroundColor = Ui.Fondo;
        Content = new ScrollView { Content = _pila };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await DibujarAsync();
    }

    private async Task DibujarAsync()
    {
        _pila.Children.Clear();
        var resueltos = await _padre.ResueltosAsync();
        var tareas = _padre.Obra?.Atrasadas ?? [];
        _pila.Children.Add(Ui.Titulo("Tareas atrasadas"));
        if (_padre.Obra is null)
        {
            _pila.Children.Add(Ui.Tarjeta(Ui.Texto("Sin señal y sin datos guardados de la obra. Probá cuando haya señal."), Ui.FondoAlerta));
            return;
        }
        if (tareas.Count == 0)
        {
            _pila.Children.Add(Ui.Tarjeta(Ui.Texto("No hay tareas atrasadas. ✔"), Ui.FondoOk));
            return;
        }
        _pila.Children.Add(Ui.Suave("Contá por qué se atrasó cada una. Si ya la anotaste, podés cambiarla."));
        foreach (var t in tareas) _pila.Children.Add(Tarjeta(t, ClavesEnvio.CausaLocal(resueltos, t.Id)));
    }

    private View Tarjeta(TareaAtrasadaDto t, string? causaLocal)
    {
        var causa = causaLocal ?? t.Causa;
        var pila = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = t.Titulo, FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron },
                Ui.Suave(string.Join(" · ", new[] { t.Etapa, t.FechaLimite is { } f ? $"era para el {f:dd/MM}" : null, $"va {t.Porcentaje}%" }.Where(x => x is not null))),
            },
        };
        if (causa is not null)
            pila.Children.Add(Ui.Texto($"Causa: {ValoresCapataz.Etiqueta(ValoresCapataz.CausasAtraso, causa)}{(causaLocal is not null ? " (recién anotada)" : "")}"
                + (causaLocal is null && t.Detalle is not null ? $" — {t.Detalle}" : ""), 15, Ui.Verde));

        var opciones = ValoresCapataz.CausasAtraso.Select(c => c.Etiqueta).ToList();
        var sugerida = Array.FindIndex(ValoresCapataz.CausasAtraso, c => c.Valor == (causa ?? t.CausaSugerida));
        var selector = Ui.Selector("¿Por qué se atrasó?", opciones, sugerida);
        if (causa is null && t.CausaSugerida is not null && t.MotivoSugerido is not null)
            pila.Children.Add(Ui.Suave($"Sugerido: {t.MotivoSugerido}."));
        var detalle = Ui.Area("Detalle (opcional): qué pasó, cuántos días…", 500);
        detalle.MinimumHeightRequest = 60;
        pila.Children.Add(selector);
        pila.Children.Add(Ui.ConDictado(detalle));
        pila.Children.Add(Ui.Boton(causa is null ? "Anotar la causa" : "Cambiar la causa", async () =>
        {
            if (selector.SelectedIndex < 0)
            {
                await DisplayAlertAsync("Atraso", "Elegí por qué se atrasó.", "Listo");
                return;
            }
            await _cargas.AtrasoAsync(_obra, t, ValoresCapataz.CausasAtraso[selector.SelectedIndex].Valor, detalle.Text);
            await _padre.RefrescarAsync();
            await DibujarAsync();
        }));
        return Ui.Tarjeta(pila, causa is null ? null : Ui.FondoOk);
    }
}
