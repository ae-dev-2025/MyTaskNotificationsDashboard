using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Microsoft.Maui.ApplicationModel;
using TaskDashboard.Models;
using TaskDashboard.Services;
using Application = Android.App.Application;

namespace TaskDashboard.Platforms.Android;

/// <summary>
/// Schedules deadline reminders with <see cref="AlarmManager"/> so they fire
/// even when the app is closed. Inexact-while-idle alarms are used
/// deliberately: they need no special permission and are battery-friendly,
/// and a reminder that lands within a few minutes of the deadline is fine.
/// </summary>
public sealed class AndroidNotificationService : INotificationService
{
    private const string PrefsName = "task_notifications";
    private const string ScheduledKeysPref = "scheduled_keys";

    // The always-live "current task" notification: its own quiet channel and a
    // fixed id so each refresh replaces the previous one rather than stacking.
    private const string OngoingChannelId = "current_task";
    private const int OngoingId = 1;

    private static Context Context => Application.Context;

    public async Task EnsurePermissionAsync()
    {
        TaskAlarmReceiver.EnsureChannel(Context);

        // POST_NOTIFICATIONS is a runtime permission on API 33+; a no-op below.
        await Permissions.RequestAsync<Permissions.PostNotifications>();
    }

    public Task SyncAsync(IReadOnlyList<TodoItem> tasks, int leadMinutes, bool enabled)
    {
        var alarmManager = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;

        var desired = enabled
            ? NotificationScheduling.Compute(tasks, leadMinutes, DateTimeOffset.Now)
            : [];
        var desiredKeys = desired.Select(n => n.Key).ToHashSet();

        // Cancel alarms scheduled last time that are no longer wanted — a task
        // completed or deleted, or a deadline moved. Their keys are persisted
        // so this survives the app being closed between changes.
        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        foreach (var staleKey in ReadKeys(prefs).Where(k => !desiredKeys.Contains(k)))
        {
            CancelAlarm(alarmManager, ScheduledNotification.NumericIdFor(staleKey));
        }

        foreach (var notification in desired)
        {
            ScheduleAlarm(alarmManager, notification);
        }

        WriteKeys(prefs, desiredKeys);
        return Task.CompletedTask;
    }

    public Task UpdateCurrentTaskAsync(string? title, string? body)
    {
        // From is annotated nullable in the AndroidX binding but never returns
        // null in practice.
        var manager = NotificationManagerCompat.From(Context)!;

        if (title is null)
        {
            manager.Cancel(OngoingId);
            return Task.CompletedTask;
        }

        EnsureOngoingChannel(Context);

        // Setters are called as statements rather than chained: the fluent
        // returns are annotated nullable, so chaining trips the null analyzer
        // while the builder from `new` is plainly non-null.
        var builder = new NotificationCompat.Builder(Context, OngoingChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetSmallIcon(Context.ApplicationInfo!.Icon);
        // Ongoing keeps it out of a swipe-to-dismiss on Android 13 and below;
        // only-alert-once means silent refreshes as "now" changes.
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetPriority(NotificationCompat.PriorityLow);

        var launch = Context.PackageManager?.GetLaunchIntentForPackage(Context.PackageName!);
        if (launch is not null)
        {
            launch.AddFlags(ActivityFlags.SingleTop);
            builder.SetContentIntent(PendingIntent.GetActivity(
                Context, OngoingId, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        }

        manager.Notify(OngoingId, builder.Build());
        return Task.CompletedTask;
    }

    private static void EnsureOngoingChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(OngoingChannelId) is not null)
        {
            return;
        }

        // Low importance: shows silently in the shade, no heads-up pop.
        var channel = new NotificationChannel(
            OngoingChannelId, "Current task", NotificationImportance.Low)
        {
            Description = "A persistent reminder of the task to do now",
        };
        manager.CreateNotificationChannel(channel);
    }

    private static void ScheduleAlarm(AlarmManager alarmManager, ScheduledNotification n)
    {
        var pending = PendingIntent.GetBroadcast(
            Context, n.NumericId, BuildIntent(n),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

        alarmManager.SetAndAllowWhileIdle(
            AlarmType.RtcWakeup, n.FireAt.ToUnixTimeMilliseconds(), pending);
    }

    private static void CancelAlarm(AlarmManager alarmManager, int requestCode)
    {
        var intent = new Intent(Context, typeof(TaskAlarmReceiver)).SetAction(TaskAlarmReceiver.Action);
        var pending = PendingIntent.GetBroadcast(
            Context, requestCode, intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);

        if (pending is not null)
        {
            alarmManager.Cancel(pending);
            pending.Cancel();
        }
    }

    private static Intent BuildIntent(ScheduledNotification n) =>
        new Intent(Context, typeof(TaskAlarmReceiver))
            .SetAction(TaskAlarmReceiver.Action)
            .PutExtra(TaskAlarmReceiver.ExtraId, n.NumericId)
            .PutExtra(TaskAlarmReceiver.ExtraTitle, n.Title)
            .PutExtra(TaskAlarmReceiver.ExtraBody, n.Body);

    private static HashSet<string> ReadKeys(ISharedPreferences prefs) =>
        prefs.GetStringSet(ScheduledKeysPref, null)?.ToHashSet() ?? [];

    private static void WriteKeys(ISharedPreferences prefs, ICollection<string> keys)
    {
        using var editor = prefs.Edit()!;
        editor.PutStringSet(ScheduledKeysPref, keys);
        editor.Apply();
    }
}
