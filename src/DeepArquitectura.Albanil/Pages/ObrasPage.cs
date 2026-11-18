using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Obras asignadas (si hay una sola, o la última usada, se entra directo).</summary>
public sealed class ObrasPage : ContentPage
{
    private readonly Sesion _sesion;
    private readonly ApiCliente _api;
    private readonly VerticalStackLayout _lista = new() { Spacing = 12 };
    private readonly RefreshView _refresh;

    public ObrasPage(Sesion sesion, ApiCliente api)
    {
        _sesion = sesion;
        _api = api;
        Title = "Mis obras";
        BackgroundColor = Ui.Fondo;
        _refresh = new RefreshView { Content = Ui.Contenedor(_lista), RefreshColor = Ui.Marron };
        _refresh.Refreshing += async (_, _) => { await ActualizarAsync(); _refresh.IsRefreshing = false; };
        Content = _refresh;
        ToolbarItems.Add(new ToolbarItem("Salir", null, async () => await ((App)Application.Current!).CerrarSesionAsync()));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Dibujar();
        await ActualizarAsync();
    }

    private async Task ActualizarAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _sesion.ActualizarPerfil(await _api.YoAsync(cts.Token));
            Dibujar();
        }
        catch (SesionVencidaException ex) { await ((App)Application.Current!).IrAlLoginAsync(ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* sin señal: queda lo guardado */ }
    }

    private void Dibujar()
    {
        _lista.Children.Clear();
        if (_sesion.Perfil is not { } p) return;
        _lista.Children.Add(Ui.Titulo($"Hola, {p.Nombre.Split(' ')[0]}"));
        _lista.Children.Add(Ui.Suave($"Cuadrilla de {p.Capataz}{(p.Oficio is null ? "" : $" · {p.Oficio}")}"));
        _lista.Children.Add(Ui.Seccion("¿En qué obra estás hoy?"));
        if (p.Obras.Count == 0)
            _lista.Children.Add(Ui.Tarjeta(Ui.Texto($"No tenés obras asignadas. Pedile a {p.Capataz} o al estudio que te asigne una."), Ui.FondoAlerta));
        foreach (var o in p.Obras)
            _lista.Children.Add(Ui.Boton($"{o.Nombre}\n{o.Direccion ?? o.Localidad ?? o.Codigo}", () => AbrirAsync(o)));
    }

    private Task AbrirAsync(ObraResumenDto obra)
    {
        _sesion.ObraActual = obra.Id;
        return Navigation.PushAsync(new ObraPage(obra));
    }
}
