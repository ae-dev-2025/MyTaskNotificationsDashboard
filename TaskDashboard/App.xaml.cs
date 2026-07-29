using Microsoft.Extensions.DependencyInjection;
using TaskDashboard.Services;

namespace TaskDashboard;

public partial class App : Application
{
	private readonly IServiceProvider services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		this.services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "TaskDashboard" };

		// Re-arm the keep-awake countdown every time the app comes back to the
		// front. Without this the duration would be measured once from launch,
		// so a tablet left running for a day would stop holding its screen on
		// long before the user next looked at it.
		window.Resumed += (_, _) => services.GetService<ScreenWakeCoordinator>()?.Apply();

		return window;
	}

	protected override void OnStart()
	{
		base.OnStart();

		// Request permission and schedule reminders once the app is up. Fire
		// and forget: notifications must never delay or block launch.
		_ = services.GetService<NotificationCoordinator>()?.StartAsync();
		_ = services.GetService<ScreenWakeCoordinator>()?.StartAsync();
	}
}
