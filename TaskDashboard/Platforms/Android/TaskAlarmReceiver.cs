using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace TaskDashboard.Platforms.Android;

/// <summary>
/// Receives the scheduled alarm and posts the notification. Runs on its own
/// even when the app process is not in the foreground, which is how reminders
/// reach the user while the app is closed.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class TaskAlarmReceiver : BroadcastReceiver
{
    public const string Action = "com.aedev2025.taskdashboard.TASK_REMINDER";
    public const string ChannelId = "task_reminders";

    public const string ExtraId = "id";
    public const string ExtraTitle = "title";
    public const string ExtraBody = "body";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var id = intent.GetIntExtra(ExtraId, 0);
        var title = intent.GetStringExtra(ExtraTitle) ?? "Task reminder";
        var body = intent.GetStringExtra(ExtraBody) ?? string.Empty;

        EnsureChannel(context);

        // Setters as statements, not a fluent chain: the AndroidX fluent
        // returns are annotated nullable, so chaining trips the null analyzer
        // while the builder from `new` is plainly non-null.
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetSmallIcon(context.ApplicationInfo!.Icon);
        builder.SetAutoCancel(true);
        builder.SetPriority(NotificationCompat.PriorityHigh);

        // Tapping the notification opens the app.
        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (launch is not null)
        {
            launch.AddFlags(ActivityFlags.SingleTop);
            builder.SetContentIntent(PendingIntent.GetActivity(
                context, id, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        }

        // From is annotated nullable in the binding but never returns null.
        NotificationManagerCompat.From(context)!.Notify(id, builder.Build());
    }

    /// <summary>Creates the reminders notification channel if it does not exist
    /// yet. Idempotent and cheap; required before posting on API 26+.</summary>
    public static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId, "Task reminders", NotificationImportance.High)
        {
            Description = "Deadline reminders for your tasks",
        };
        manager.CreateNotificationChannel(channel);
    }
}
