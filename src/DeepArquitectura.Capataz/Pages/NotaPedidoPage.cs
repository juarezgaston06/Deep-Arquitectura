using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>Nota de pedido al estudio: queda asentada y numerada en el libro de obra (un pedido o aviso formal).</summary>
public sealed class NotaPedidoPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly Editor _texto = Ui.Area("Ej.: Pedimos definir la altura de la mesada de la cocina antes del jueves.", 4000);
    private readonly AdjuntosView _fotos = new(conVideos: false);

    public NotaPedidoPage(ObraResumenDto obra)
    {
        _obra = obra;
        Title = "Nota al estudio";
        BackgroundColor = Ui.Fondo;
        Content = Ui.Contenedor(
            Ui.Titulo("Nota de pedido"),
            Ui.Suave("Queda asentada y numerada en el libro de obra. Para algo urgente, además llamá al estudio."),
            Ui.ConDictado(_texto),
            Ui.Seccion("Fotos (opcional)"),
            _fotos,
            Ui.Boton("Enviar nota", EnviarAsync, color: Ui.Verde));
    }

    private async Task EnviarAsync()
    {
        if (string.IsNullOrWhiteSpace(_texto.Text))
        {
            await DisplayAlertAsync("Nota", "Escribí o dictá la nota.", "Corregir");
            return;
        }
        await App.Servicio<Cargas>().NotaPedidoAsync(_obra, _texto.Text, _fotos.Archivos);
        await Ui.AvisarAsync(this, "Guardada", "La nota se envía sola al estudio.");
        await Navigation.PopAsync();
    }
}
