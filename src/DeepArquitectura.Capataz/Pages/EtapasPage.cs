using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Etapas de la obra: el capataz las marca en curso o terminadas (omitir una lo decide el estudio).</summary>
public sealed class EtapasPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly ObraPage _padre;
    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(16, 12, 16, 32), Spacing = 12 };

    public EtapasPage(ObraResumenDto obra, ObraPage padre)
    {
        _obra = obra;
        _padre = padre;
        Title = "Etapas";
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
        _pila.Children.Add(Ui.Titulo("Etapas de la obra"));
        var etapas = _padre.Obra?.Etapas ?? [];
        if (etapas.Count == 0)
        {
            _pila.Children.Add(Ui.Tarjeta(Ui.Texto(_padre.Obra is null ? "Sin señal y sin datos guardados de la obra." : "La obra no tiene etapas cargadas."), Ui.FondoAlerta));
            return;
        }
        foreach (var e in etapas.OrderBy(e => e.Orden))
        {
            var local = ClavesEnvio.EstadoEtapaLocal(resueltos, e.Id);
            var estado = local ?? e.Estado;
            var pila = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label { Text = $"{e.Orden}. {e.Nombre}", FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { Ui.Chip(Ui.TextoEstado(estado), Ui.ColorEstado(estado)), Ui.Suave(local is null ? "" : "recién cambiada") },
                    },
                },
            };
            if (estado == "Omitida")
                pila.Children.Add(Ui.Suave("El estudio la marcó como omitida."));
            else
            {
                var botones = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
                if (estado == "Completada")
                    botones.Add(Ui.Boton("↺ Reabrir", () => CambiarAsync(e, "EnCurso"), primario: false), 0, 0);
                else
                {
                    if (estado != "EnCurso") botones.Add(Ui.Boton("▶ En curso", () => CambiarAsync(e, "EnCurso"), primario: false), 0, 0);
                    botones.Add(Ui.Boton("✔ Terminada", () => CambiarAsync(e, "Completada"), color: Ui.Verde), 1, 0);
                }
                pila.Children.Add(botones);
            }
            _pila.Children.Add(Ui.Tarjeta(pila));
        }
    }

    private async Task CambiarAsync(EtapaDto etapa, string estado)
    {
        var texto = estado == "Completada" ? $"¿Marcar «{etapa.Nombre}» como terminada?" : $"¿Pasar «{etapa.Nombre}» a en curso?";
        if (!await DisplayAlertAsync("Etapa", texto, "Sí", "Cancelar")) return;
        await App.Servicio<Cargas>().EtapaAsync(_obra, etapa, estado);
        await _padre.RefrescarAsync();
        await DibujarAsync();
    }
}
