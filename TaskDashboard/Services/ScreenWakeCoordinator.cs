using Microsoft.Extensions.Logging;

namespace TaskDashboard.Services;

/// <summary>
/// Applies the keep-awake preference to the platform. It runs at startup, again
/// on every <see cref="DashboardService.Changed"/> so a change on the Settings
/// page takes effect without a relaunch, and again each time the app returns to
/// the foreground — which is what makes a duration mean "this long from when I
/// picked the tablet up" rather than "this long from launch, once, ever".
/// </summary>
public sealed class ScreenWakeCoordinator
{
    private readonly DashboardService data;
    private readonly IScreenWakeService screen;
    private readonly ILogger<ScreenWakeCoordinator> log;

    public ScreenWakeCoordinator(
        DashboardService data,
        IScreenWakeService screen,
        ILogger<ScreenWakeCoordinator> log)
    {
        this.data = data;
        this.screen = screen;
        this.log = log;

        this.data.Changed += Apply;
    }

    public async Task StartAsync()
    {
        try
        {
            await data.LoadAsync();
            Apply();
        }
        catch (Exception ex)
        {
            // Keeping the screen on is a convenience; never let it break launch.
            log.LogError(ex, "Screen-wake startup failed.");
        }
    }

    /// <summary>Re-reads the preference and hands it to the platform. Restarts
    /// the countdown, so calling it on resume is the whole re-arm.</summary>
    public void Apply()
    {
        try
        {
            if (data.KeepScreenOnEnabled)
            {
                screen.KeepAwake(data.KeepScreenOnFor);
            }
            else
            {
                screen.Release();
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Applying the screen-wake preference failed.");
        }
    }
}
