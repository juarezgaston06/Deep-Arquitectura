using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>Control de una etapa (antes de seguir o tapar): está bien u observado, con foto.</summary>
public sealed class ControlPage : ContentPage
{
    private readonly ObraResumenDto _obra;
    private readonly ControlDto _control;
    private readonly Cargas _cargas = App.Servicio<Cargas>();
    private readonly Editor _nota = Ui.Area("Qué está mal o qué falta (obligatorio si está observado)", 1000);
    private readonly AdjuntosView _fotos = new();

    public ControlPage(ObraResumenDto obra, ControlDto control)
    {
        _obra = obra;
        _control = control;
        Title = "Control";
        BackgroundColor = Ui.Fondo;
        var vistas = new List<View> { Ui.Suave(control.Etapa), Ui.Titulo(control.Texto) };
        if (control.Resultado is { } r)
            vistas.Add(Ui.Tarjeta(Ui.Texto($"Último: {(r == "Ok" ? "está bien" : "observado")}{(control.Nota is null ? "" : $" — {control.Nota}")}", 15),
                r == "Ok" ? Ui.FondoOk : Ui.FondoAlerta));
        vistas.AddRange([
            Ui.Seccion(control.RequiereFoto ? "Fotos (obligatorias)" : "Fotos"), _fotos,
            Ui.ConDictado(_nota),
            Ui.Boton("✔  Está bien", () => EnviarAsync(true), color: Ui.Verde),
            Ui.Boton("✖  Está mal / falta algo", () => EnviarAsync(false), color: Ui.Rojo),
        ]);
        Content = Ui.Contenedor([.. vistas]);
    }

    private async Task EnviarAsync(bool ok)
    {
        if (_control.RequiereFoto && _fotos.Archivos.Count == 0) { await DisplayAlertAsync("Control", "Este control lleva foto: sacala antes de enviar.", "Listo"); return; }
        if (!ok && string.IsNullOrWhiteSpace(_nota.Text)) { await DisplayAlertAsync("Control", "Contá qué está mal.", "Listo"); return; }
        await _cargas.ControlAsync(_obra, _control, ok, _nota.Text, _fotos.Archivos);
        App.Servicio<EstadoLocal>().MarcarHecho("control", _obra.Id, _control.Id);
        await Ui.AvisarAsync(this, "Control", ok ? "Control guardado. Se envía solo." : "Observación guardada: le llega al capataz y al estudio.");
        await Navigation.PopAsync();
    }
}
