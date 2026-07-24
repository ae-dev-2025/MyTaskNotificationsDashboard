using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TaskDashboard.Models;
using TaskDashboard.Services;

namespace TaskDashboard.Platforms.Windows;

/// <summary>
/// Delivers deadline reminders as Windows toasts. The app is unpackaged, which
/// rules out OS-scheduled toasts that fire while it is closed, so reminders are
/// driven by an in-process timer and appear while the app is running (open or
/// minimized). The scheduling logic is shared with Android; only delivery
/// differs.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    // A reminder may land up to this late — imperceptible for deadline alerts,
    // and far cheaper than a per-notification timer.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly HashSet<string> shown = [];
    private List<ScheduledNotification> pending = [];
    private Timer? timer;
    private bool registered;

    public Task EnsurePermissionAsync()
    {
        if (!registered)
        {
            AppNotificationManager.Default.Register();
            registered = true;
        }

        timer ??= new Timer(_ => Fire(), null, TickInterval, TickInterval);
        return Task.CompletedTask;
    }

    public Task SyncAsync(IReadOnlyList<TodoItem> tasks, int leadMinutes, bool enabled)
    {
        var desired = enabled
            ? NotificationScheduling.Compute(tasks, leadMinutes, DateTimeOffset.Now)
            : [];

        lock (gate)
        {
            pending = [.. desired];
        }

        // Deliver anything already due at sync time (e.g. lead is short and the
        // deadline is imminent) without waiting for the next tick.
        Fire();
        return Task.CompletedTask;
    }

    // Windows has no persistent, non-dismissible notification, so the
    // always-live "current task" notification is an Android-only feature.
    public Task UpdateCurrentTaskAsync(string? title, string? body) => Task.CompletedTask;

    private void Fire()
    {
        var now = DateTimeOffset.Now;
        List<ScheduledNotification> due;

        lock (gate)
        {
            due = pending.Where(n => n.FireAt <= now && shown.Add(n.Key)).ToList();
        }

        foreach (var n in due)
        {
            var toast = new AppNotificationBuilder()
                .AddText(n.Title)
                .AddText(n.Body)
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
    }
}
