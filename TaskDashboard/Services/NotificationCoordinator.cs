using Microsoft.Extensions.Logging;

namespace TaskDashboard.Services;

/// <summary>
/// Keeps delivered notifications in step with the data. It performs the initial
/// permission request and sync at startup, then re-syncs whenever the
/// dashboard changes — every add, edit, complete or delete raises
/// <see cref="DashboardService.Changed"/>. It also refreshes the always-live
/// "current task" notification on a timer, since which task is current advances
/// with the clock even when nothing was edited.
/// </summary>
public sealed class NotificationCoordinator
{
    private static readonly TimeSpan PlanHorizon = TimeSpan.FromDays(14);
    private static readonly TimeSpan CurrentTaskRefresh = TimeSpan.FromMinutes(1);

    private readonly DashboardService data;
    private readonly INotificationService notifications;
    private readonly ILogger<NotificationCoordinator> log;

    private PeriodicTimer? refreshTimer;

    public NotificationCoordinator(
        DashboardService data,
        INotificationService notifications,
        ILogger<NotificationCoordinator> log)
    {
        this.data = data;
        this.notifications = notifications;
        this.log = log;

        this.data.Changed += OnChanged;
    }

    /// <summary>Runs once at app start: loads the data, asks for permission,
    /// schedules reminders, shows the current-task notification, and starts the
    /// refresh loop that keeps it current.</summary>
    public async Task StartAsync()
    {
        try
        {
            await data.LoadAsync();
            await notifications.EnsurePermissionAsync();
            await SyncRemindersAsync();
            await UpdateCurrentTaskAsync();
            StartRefreshLoop();
        }
        catch (Exception ex)
        {
            // Notifications are a convenience; never let them break launch.
            log.LogError(ex, "Notification startup failed.");
        }
    }

    private void OnChanged()
    {
        _ = RunSafely(SyncRemindersAsync, "Notification re-sync failed.");
        _ = RunSafely(UpdateCurrentTaskAsync, "Current-task notification update failed.");
    }

    private void StartRefreshLoop()
    {
        if (refreshTimer is not null)
        {
            return;
        }

        refreshTimer = new PeriodicTimer(CurrentTaskRefresh);
        _ = RefreshLoopAsync(refreshTimer);
    }

    private async Task RefreshLoopAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                await RunSafely(UpdateCurrentTaskAsync, "Current-task notification refresh failed.");
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Current-task refresh loop stopped.");
        }
    }

    // ---- deadline reminders ----

    private Task SyncRemindersAsync()
    {
        // Snapshot on the caller's thread: Changed fires synchronously after a
        // save completes, so the list is stable here but may be mutated by the
        // time the platform layer runs.
        var snapshot = data.Items.ToArray();
        return notifications.SyncAsync(
            snapshot, data.NotificationLeadMinutes, data.NotificationsEnabled);
    }

    // ---- always-live current-task notification ----

    private Task UpdateCurrentTaskAsync()
    {
        if (!data.ShowCurrentTaskNotification)
        {
            return notifications.UpdateCurrentTaskAsync(null, null);
        }

        var current = ResolveCurrentTask();
        return current is { } c
            ? notifications.UpdateCurrentTaskAsync(c.Title, c.Body)
            : notifications.UpdateCurrentTaskAsync(null, null);
    }

    /// <summary>The task to do right now: the one being tracked, or failing
    /// that the task whose planned slot covers this minute. Mirrors the
    /// dashboard's "Now" panel so the notification and the app agree.</summary>
    private (string Title, string Body)? ResolveCurrentTask()
    {
        var now = DateTimeOffset.Now;

        var task = data.Items.FirstOrDefault(i => i.IsInProgress);
        if (task is null)
        {
            var busy = BlockedTime.Expand(data.BlockedPeriods, now, now + PlanHorizon);
            var plan = Planner.Plan(data.Items, busy, now, PlanHorizon, data.BreakBetweenTasks);
            var slot = plan.FirstOrDefault(s => s.Start <= now && s.End > now);
            task = slot is null ? null : data.Items.FirstOrDefault(t => t.Id == slot.TaskId);
        }

        if (task is null)
        {
            return null;
        }

        var body = task.Deadline is { } due
            ? $"{task.Title} · due {due.ToLocalTime():t}"
            : task.Title;
        return ("Doing now", body);
    }

    private async Task RunSafely(Func<Task> work, string failureMessage)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{Message}", failureMessage);
        }
    }
}
