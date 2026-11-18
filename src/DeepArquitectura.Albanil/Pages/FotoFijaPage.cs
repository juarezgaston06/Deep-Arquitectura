using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Foto desde un punto fijo: muestra la foto anterior para pararse en el mismo lugar y encuadrar igual.</summary>
public sealed class FotoFijaPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly PuntoFotoDto _punto;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly Image _referencia = new() { Aspect = Aspect.AspectFit, HeightRequest = 260, IsVisible = false };
    private readonly Label _estadoReferencia = Ui.Suave("Buscando la foto anterior…");

    public FotoFijaPage(ObraResumenDto obra, PuntoFotoDto punto)
    {
        _obra = obra;
        _punto = punto;
        Title = "Foto fija";
        BackgroundColor = Ui.Fondo;
        Content = Ui.Contenedor(
            Ui.Titulo(punto.Nombre),
            Ui.Texto(punto.Indicacion ?? "Parate en el mismo lugar que la vez anterior y encuadrá igual.", 16),
            Ui.Seccion("Así salió la vez anterior"),
            _referencia,
            _estadoReferencia,
            Ui.Boton("📷  Sacar la foto", SacarAsync, color: Ui.Verde),
            Ui.Suave("Teléfono horizontal, a la altura del pecho, sin zoom."));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_punto.TieneReferencia) { _estadoReferencia.Text = "Es la primera: elegí un buen lugar, que se pueda repetir."; return; }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var bytes = await App.Servicio<ApiCliente>().UltimaFotoPuntoAsync(_obra.Id, _punto.Id, cts.Token);
            if (bytes is null) { _estadoReferencia.Text = "Todavía no llegó ninguna foto de este punto."; return; }
            _referencia.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            _referencia.IsVisible = true;
            _estadoReferencia.Text = _punto.UltimaUtc is { } u ? $"Del {Ui.FechaCorta(u)}" : "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ErrorApiException or SesionVencidaException)
        {
            _estadoReferencia.Text = "Sin señal para mostrar la foto anterior. Igual podés sacarla.";
        }
    }

    private async Task SacarAsync()
    {
        FileResult? foto;
        try { foto = await Medios.SacarFotoAsync(); }
        catch (PermissionException) { await DisplayAlertAsync("Cámara", "Sin permiso para usar la cámara.", "Listo"); return; }
        if (foto is null) return;
        await _cargas.AvanceAsync(_obra, null, null, null, false, null, [foto], _punto);
        App.Servicio<EstadoLocal>().MarcarHecho("punto", _obra.Id, _punto.Id);
        await Ui.AvisarAsync(this, "Foto fija", "Guardada. Se envía sola.");
        await Navigation.PopAsync();
    }
}
