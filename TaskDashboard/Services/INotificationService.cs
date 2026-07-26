using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>
/// Platform-specific delivery of deadline reminders. Implementations translate
/// the shared <see cref="ScheduledNotification"/> set into OS notifications:
/// scheduled alarms on Android, in-session toasts on Windows.
/// </summary>
public interface INotificationService
{
    /// <summary>Requests any OS permission notifications need and prepares the
    /// delivery channel. Safe to call more than once.</summary>
    Task EnsurePermissionAsync();

    /// <summary>Replaces all currently scheduled reminders with those computed
    /// from <paramref name="tasks"/>. Idempotent: the platform layer cancels
    /// reminders no longer wanted and (re)schedules the rest. When
    /// <paramref name="enabled"/> is false everything is cancelled.</summary>
    Task SyncAsync(IReadOnlyList<TodoItem> tasks, int leadMinutes, bool enabled);

    /// <summary>Shows (or refreshes) a persistent notification with the task to
    /// do right now. Passing a null <paramref name="title"/> removes it. Only
    /// Android renders this; platforms without a persistent notification
    /// concept ignore it.</summary>
    Task UpdateCurrentTaskAsync(string? title, string? body);
}

/// <summary>Does nothing. Used on platforms without a notification
/// implementation and at design time, so DI always resolves the service.</summary>
public sealed class NullNotificationService : INotificationService
{
    public Task EnsurePermissionAsync() => Task.CompletedTask;

    public Task SyncAsync(IReadOnlyList<TodoItem> tasks, int leadMinutes, bool enabled) =>
        Task.CompletedTask;

    public Task UpdateCurrentTaskAsync(string? title, string? body) => Task.CompletedTask;
}
