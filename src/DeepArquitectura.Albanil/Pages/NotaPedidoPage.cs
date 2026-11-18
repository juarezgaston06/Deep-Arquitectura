using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Nota de pedido al estudio: queda numerada en el libro de obra (pedir una definición, dejar algo por escrito).</summary>
public sealed class NotaPedidoPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly Editor _texto = Ui.Area("Ej.: Pedimos definir la altura de la mesada de la cocina antes del jueves.", 4000);
    private readonly AdjuntosView _fotos = new();

    public NotaPedidoPage(ObraResumenDto obra)
    {
        _obra = obra;
        Title = "Nota al estudio";
        BackgroundColor = Ui.Fondo;
        Content = Ui.Contenedor(
            Ui.Titulo("Nota de pedido"),
            Ui.Suave("Queda asentada y numerada en el libro de obra. Para urgencias de seguridad usá «Avisar un problema» y llamá al capataz."),
            Ui.ConDictado(_texto),
            Ui.Seccion("Fotos (opcional)"),
            _fotos,
            Ui.Boton("Enviar nota", EnviarAsync, color: Ui.Verde));
    }

    private async Task EnviarAsync()
    {
        if (string.IsNullOrWhiteSpace(_texto.Text)) { await DisplayAlertAsync("Nota", "Escribí o dictá la nota.", "Listo"); return; }
        await _cargas.NotaPedidoAsync(_obra, _texto.Text, _fotos.Archivos);
        await Ui.AvisarAsync(this, "Nota", "Nota guardada. Se envía sola al estudio.");
        await Navigation.PopAsync();
    }
}
