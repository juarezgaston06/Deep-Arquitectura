using DeepArquitectura.Capataz.Pages;
using DeepArquitectura.Capataz.Services;

namespace DeepArquitectura.Capataz;

public partial class App : Application
{
	private static IServiceProvider? _sp;
	private readonly Sesion _sesion;
	private readonly ColaEnvios _cola;

	public App(IServiceProvider sp, Sesion sesion, ColaEnvios cola)
	{
		InitializeComponent();
		_sp = sp;
		_sesion = sesion;
		_cola = cola;
		// Siempre tema claro: en obra, a pleno sol, se lee mejor.
		UserAppTheme = AppTheme.Light;
		_cola.SesionVencida += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
			_ = IrAlLoginAsync("Tu sesión se cerró. Ingresá de nuevo; lo pendiente se envía cuando entres."));
		_cola.EnvioRechazado += motivo => MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (Windows.Count > 0 && Windows[0].Page is { } p)
				await p.DisplayAlertAsync("No se pudo enviar", motivo + "\n\nRevisalo en el aviso de envíos de la obra.", "Listo");
		});
	}

	public static T Servicio<T>() where T : notnull => _sp!.GetRequiredService<T>();

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Con sesión guardada se entra directo (sin pedir el PIN cada vez).
		var window = new Window(new ContentPage { BackgroundColor = Ui.Fondo });
		window.Created += async (_, _) =>
		{
			if (await _sesion.CargarAsync() && _sesion.Perfil is not null)
				await MostrarInicioAsync();
			else
				await IrAlLoginAsync(null);
		};
		window.Resumed += (_, _) => _cola.Disparar();
		return window;
	}

	/// <summary>Después del login: lista de obras; si hay una sola (o la última usada), directo a esa obra.</summary>
	public async Task MostrarInicioAsync()
	{
		_cola.Iniciar();
		var nav = new NavigationPage(Servicio<ObrasPage>()) { BarBackgroundColor = Ui.Marron, BarTextColor = Colors.White };
		Windows[0].Page = nav;
		var obras = _sesion.Perfil?.Obras ?? [];
		var obra = obras.Count == 1 ? obras[0] : obras.FirstOrDefault(o => o.Id == _sesion.ObraActual);
		if (obra is not null) await nav.PushAsync(new ObraPage(obra), animated: false);
	}

	public Task IrAlLoginAsync(string? aviso)
	{
		_sesion.Cerrar();
		var login = Servicio<LoginPage>();
		login.Aviso = aviso;
		Windows[0].Page = login;
		return Task.CompletedTask;
	}

	public async Task CerrarSesionAsync()
	{
		var pendientes = (await _cola.PendientesAsync()).Count(e => !e.Rechazado);
		var mensaje = pendientes > 0
			? $"Tenés {pendientes} envío(s) esperando señal. Se mandan cuando vuelvas a entrar con tu usuario. ¿Salir igual?"
			: "¿Cerrar sesión en este teléfono?";
		if (Windows[0].Page is not { } p || !await p.DisplayAlertAsync("Salir", mensaje, "Salir", "Cancelar")) return;
		await Servicio<ApiCliente>().LogoutAsync();
		await _cola.DescartarRechazadosAsync();
		await IrAlLoginAsync(null);
	}
}
