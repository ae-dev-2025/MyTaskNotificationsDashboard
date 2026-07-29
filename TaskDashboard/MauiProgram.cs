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

		// Notification delivery is platform-specific; the coordinator wires it
		// to data changes. Registered as a singleton and started from
		// App.OnStart so reminders track the task list for the app's lifetime.
#if ANDROID
		builder.Services.AddSingleton<INotificationService, Platforms.Android.AndroidNotificationService>();
#elif WINDOWS
		builder.Services.AddSingleton<INotificationService, Platforms.Windows.WindowsNotificationService>();
#else
		builder.Services.AddSingleton<INotificationService, NullNotificationService>();
#endif
		builder.Services.AddSingleton<NotificationCoordinator>();

		// Holding the display awake is a window flag on the Android activity;
		// Windows has no equivalent the unpackaged app can use, so the setting
		// is readable and writable everywhere but only acts on Android.
#if ANDROID
		builder.Services.AddSingleton<IScreenWakeService, Platforms.Android.AndroidScreenWakeService>();
#else
		builder.Services.AddSingleton<IScreenWakeService, NullScreenWakeService>();
#endif
		builder.Services.AddSingleton<ScreenWakeCoordinator>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
