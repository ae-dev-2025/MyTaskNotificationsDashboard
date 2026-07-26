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
		return new Window(new MainPage()) { Title = "TaskDashboard" };
	}

	protected override void OnStart()
	{
		base.OnStart();

		// Request permission and schedule reminders once the app is up. Fire
		// and forget: notifications must never delay or block launch.
		_ = services.GetService<NotificationCoordinator>()?.StartAsync();
	}
}
