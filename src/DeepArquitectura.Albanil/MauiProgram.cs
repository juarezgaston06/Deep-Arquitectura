using DeepArquitectura.Albanil.Pages;
using DeepArquitectura.Albanil.Services;
using Microsoft.Extensions.Logging;

namespace DeepArquitectura.Albanil;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<Sesion>();
		builder.Services.AddSingleton<ApiCliente>();
		builder.Services.AddSingleton<ColaEnvios>();
		builder.Services.AddSingleton<EstadoLocal>();
		builder.Services.AddSingleton<Cargas>();

		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<ObrasPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
