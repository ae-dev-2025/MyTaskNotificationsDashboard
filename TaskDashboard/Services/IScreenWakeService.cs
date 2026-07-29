namespace TaskDashboard.Services;

/// <summary>
/// Platform-specific control over whether the device's display is allowed to
/// sleep while the dashboard is in front. Implementations hold the screen on
/// only for as long as the app is actually visible — this is a window flag on
/// Android, not a wake lock, so it costs nothing once the app is backgrounded
/// and needs no extra permission.
/// </summary>
public interface IScreenWakeService
{
    /// <summary>Holds the display on. <paramref name="duration"/> is measured
    /// from now; null means no expiry, so the screen stays on for as long as
    /// the app is in front. Calling it again restarts the countdown.</summary>
    void KeepAwake(TimeSpan? duration);

    /// <summary>Lets the display sleep on the system's own timeout again.
    /// Safe to call when nothing is being held.</summary>
    void Release();
}

/// <summary>Does nothing. Used on platforms with no way to keep the display
/// awake — Windows today — and at design time, so DI always resolves the
/// service and the setting can still be read and written everywhere.</summary>
public sealed class NullScreenWakeService : IScreenWakeService
{
    public void KeepAwake(TimeSpan? duration)
    {
    }

    public void Release()
    {
    }
}
