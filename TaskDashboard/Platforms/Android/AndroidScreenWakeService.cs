using Android.Views;
using Microsoft.Maui.ApplicationModel;
using TaskDashboard.Services;
using AndroidLog = Android.Util.Log;

namespace TaskDashboard.Platforms.Android;

/// <summary>
/// Keeps the display on with <see cref="WindowManagerFlags.KeepScreenOn"/> on
/// the activity's window. Deliberately not a <c>PowerManager</c> wake lock: the
/// window flag needs no permission, and Android drops it the moment the window
/// stops being visible, so a backgrounded or crashed app can never leave the
/// tablet awake. The trade-off is that it only works while the dashboard is the
/// thing on screen — which is exactly the case it is for.
/// </summary>
public sealed class AndroidScreenWakeService : IScreenWakeService
{
    // Android.Util.Log rather than ILogger: the Debug logging provider the app
    // registers only surfaces with a debugger attached, so nothing it writes
    // reaches logcat on a device being driven by adb — which is the only place
    // this behaviour can be observed at all.
    private const string LogTag = "TaskDashboardWake";

    private readonly Lock gate = new();
    private Timer? expiry;

    public void KeepAwake(TimeSpan? duration)
    {
        lock (gate)
        {
            AndroidLog.Info(LogTag, $"hold for {duration?.ToString() ?? "no expiry"}");
            // Whatever was running is replaced, so re-applying the preference
            // (on resume, or when the setting changes) restarts the countdown
            // rather than stacking a second one that would clear the flag early.
            expiry?.Dispose();
            expiry = null;

            if (duration is { } d && d <= TimeSpan.Zero)
            {
                SetFlag(false);
                return;
            }

            SetFlag(true);

            if (duration is { } limit)
            {
                expiry = new Timer(
                    _ =>
                    {
                        AndroidLog.Info(LogTag, "hold expired, releasing");
                        SetFlag(false);
                    },
                    null, limit, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Release()
    {
        lock (gate)
        {
            expiry?.Dispose();
            expiry = null;
            SetFlag(false);
        }
    }

    /// <summary>Window flags may only be touched on the UI thread; the timer
    /// callback and the settings page both arrive on other ones.</summary>
    private static void SetFlag(bool on) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (Platform.CurrentActivity?.Window is not { } window)
        {
            AndroidLog.Warn(LogTag, $"no current activity, cannot set the flag {on}");
            return;
        }

        AndroidLog.Info(LogTag, on ? "flag added" : "flag cleared");

        if (on)
        {
            window.AddFlags(WindowManagerFlags.KeepScreenOn);
        }
        else
        {
            window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }
    });
}
