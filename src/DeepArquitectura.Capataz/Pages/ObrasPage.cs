using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Obras asignadas al capataz (si hay una sola, o la última usada, se entra directo).</summary>
public sealed class ObrasPage : ContentPage
{
    private readonly Sesion _sesion;
    private readonly ApiCliente _api;
    private readonly EstadoLocal _estado;
    private readonly VerticalStackLayout _lista = new() { Spacing = 12 };
    private readonly RefreshView _refresh;

    public ObrasPage(Sesion sesion, ApiCliente api, EstadoLocal estado)
    {
        _sesion = sesion;
        _api = api;
        _estado = estado;
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ErrorApiException) { /* sin señal: queda lo guardado */ }
    }

    private void Dibujar()
    {
        _lista.Children.Clear();
        if (_sesion.Perfil is not { } p) return;
        _lista.Children.Add(Ui.Titulo($"Hola, {p.Nombre.Split(' ')[0]}"));
        _lista.Children.Add(Ui.Seccion("¿En qué obra estás hoy?"));
        if (p.Obras.Count == 0)
            _lista.Children.Add(Ui.Tarjeta(Ui.Texto("No tenés obras asignadas. Pedile al estudio que te asigne una."), Ui.FondoAlerta));
        foreach (var o in p.Obras)
        {
            // Lo que se sabe de la última vez que se abrió (sin señal también).
            var guardada = _estado.ObraGuardada(o.Id);
            var pendientes = guardada?.PorValidar?.Count ?? 0;
            var extra = pendientes > 0 ? $"\n✔ {pendientes} para validar" : "";
            _lista.Children.Add(Ui.Boton($"{o.Nombre}\n{o.Direccion ?? o.Localidad ?? o.Codigo} · {o.PorcentajeAvance}%{extra}", () => AbrirAsync(o)));
        }
    }

    private Task AbrirAsync(ObraResumenDto obra)
    {
        _sesion.ObraActual = obra.Id;
        return Navigation.PushAsync(new ObraPage(obra));
    }
}
