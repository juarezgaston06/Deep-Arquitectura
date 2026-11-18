using DeepArquitectura.Albanil.Services;

namespace DeepArquitectura.Albanil.Pages;

public partial class LoginPage : ContentPage
{
    private readonly ApiCliente _api;
    private readonly Sesion _sesion;

    public LoginPage(ApiCliente api, Sesion sesion)
    {
        InitializeComponent();
        _api = api;
        _sesion = sesion;
        Usuario.Text = Preferences.Default.Get("ultimoUsuario", "");
        Usuario.Completed += (_, _) => Pin.Focus();
    }

    public string? Aviso { set => MostrarAviso(value); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ServidorActual.Text = _sesion.Servidor;
    }

    private async void OnIngresar(object? sender, EventArgs e)
    {
        var usuario = Usuario.Text?.Trim() ?? "";
        var pin = Pin.Text ?? "";
        if (usuario.Length == 0 || pin.Length == 0)
        {
            MostrarAviso("Completá usuario y PIN.");
            return;
        }

        Ocupado(true);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var r = await _api.LoginAsync(usuario, pin, cts.Token);
            await _sesion.GuardarAsync(r.Token, r.Albanil);
            Preferences.Default.Set("ultimoUsuario", usuario);
            if (r.Albanil.Obras.Count == 0)
            {
                MostrarAviso($"Entraste, pero todavía no tenés obras asignadas. Pedile a {r.Albanil.Capataz} o al estudio que te asigne una.");
                await _api.LogoutAsync();
                _sesion.Cerrar();
                return;
            }
            await ((App)Application.Current!).MostrarInicioAsync();
        }
        catch (ErrorApiException ex) { MostrarAviso(ex.Message); }
        catch (SesionVencidaException ex) { MostrarAviso(ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            MostrarAviso(ex is HttpRequestException { StatusCode: not null } h ? h.Message
                : "No se pudo conectar con el estudio. Revisá la señal o los datos móviles y probá de nuevo.");
        }
        finally
        {
            Ocupado(false);
            Pin.Text = "";
        }
    }

    private async void OnConfigurarServidor(object? sender, EventArgs e)
    {
        var url = await DisplayPromptAsync("Servidor", "Dirección del sistema del estudio (te la pasa el estudio).",
            initialValue: _sesion.Servidor, keyboard: Keyboard.Url, accept: "Guardar", cancel: "Cancelar");
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MostrarAviso("La dirección no es válida. Tiene que empezar con https:// (te la pasa el estudio).");
            return;
        }
        _sesion.Servidor = uri.GetLeftPart(UriPartial.Authority);
        ServidorActual.Text = _sesion.Servidor;
    }

    private void Ocupado(bool si)
    {
        BotonIngresar.IsEnabled = !si;
        Cargando.IsVisible = Cargando.IsRunning = si;
        if (si) AvisoBorde.IsVisible = false;
    }

    private void MostrarAviso(string? texto)
    {
        AvisoTexto.Text = texto;
        AvisoBorde.IsVisible = !string.IsNullOrEmpty(texto);
    }
}
