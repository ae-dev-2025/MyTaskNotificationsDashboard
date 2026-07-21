using Microsoft.Extensions.Logging;
using TaskDashboard.Services;

namespace TaskDashboard;

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
			});

		builder.Services.AddMauiBlazorWebView();

		// Singleton, not scoped: the dashboard data is app-wide state in a
		// native app, and there is only ever one user in front of it.
		builder.Services.AddSingleton<DashboardService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
