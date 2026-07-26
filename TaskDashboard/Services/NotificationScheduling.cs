using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>Which deadline reminder a notification represents.</summary>
public enum NotificationKind
{
    /// <summary>Fired ahead of the deadline as a heads-up.</summary>
    DueSoon,

    /// <summary>Fired at the deadline itself.</summary>
    Overdue,
}

/// <summary>A single notification the platform layer should deliver: what to
/// show and when. Platform-agnostic — the Android and Windows services both
/// consume this.</summary>
public readonly record struct ScheduledNotification(
    Guid TaskId,
    NotificationKind Kind,
    string Title,
    string Body,
    DateTimeOffset FireAt)
{
    /// <summary>Stable identity for this notification, so a re-sync overwrites
    /// the previous alarm for the same task+kind rather than duplicating it.
    /// Encodes the fire time too, so moving a deadline is treated as a new
    /// reminder the user can be notified about again.</summary>
    public string Key => $"{TaskId:N}:{Kind}:{FireAt.ToUnixTimeSeconds()}";

    /// <summary>A stable non-negative 31-bit id derived from <see cref="Key"/>,
    /// for APIs that key on an int (Android request codes and notification
    /// ids). Uses FNV-1a rather than string.GetHashCode, which is randomized
    /// per process and so would not line up across launches.</summary>
    public int NumericId => NumericIdFor(Key);

    /// <summary>The numeric id for a bare key, so a stale key read back from
    /// storage can be turned into the request code that cancels its alarm.</summary>
    public static int NumericIdFor(string key)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var c in key)
            {
                hash = (hash ^ c) * prime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}

/// <summary>
/// Turns the current task list into the exact set of deadline reminders to
/// deliver. Pure and deterministic — no platform calls, no clock of its own —
/// so it is unit-testable and both platform services share one definition of
/// "what should fire and when".
/// </summary>
public static class NotificationScheduling
{
    /// <summary>Widest sensible lead time: a full day before the deadline.</summary>
    public const int MaxLeadMinutes = 24 * 60;

    /// <summary>
    /// Computes the reminders for <paramref name="tasks"/> given the user's
    /// lead time and the current instant. Only reminders whose fire time is
    /// still in the future are returned: a moment that has already passed
    /// either fired while it was scheduled or was never relevant, and
    /// re-emitting it on every sync would spam the user.
    /// </summary>
    public static IReadOnlyList<ScheduledNotification> Compute(
        IEnumerable<TodoItem> tasks,
        int leadMinutes,
        DateTimeOffset now)
    {
        var lead = TimeSpan.FromMinutes(Math.Clamp(leadMinutes, 0, MaxLeadMinutes));
        var result = new List<ScheduledNotification>();

        foreach (var task in tasks)
        {
            if (task.IsDone || task.Deadline is not { } deadline)
            {
                continue;
            }

            var localDue = deadline.ToLocalTime();

            // Heads-up before the deadline. Skipped when the lead is zero or
            // the lead window has already elapsed.
            if (lead > TimeSpan.Zero)
            {
                var remindAt = deadline - lead;
                if (remindAt > now)
                {
                    result.Add(new ScheduledNotification(
                        task.Id,
                        NotificationKind.DueSoon,
                        task.Title,
                        $"Due at {localDue:t} · in {DescribeLead(lead)}",
                        remindAt));
                }
            }

            // At the deadline itself.
            if (deadline > now)
            {
                result.Add(new ScheduledNotification(
                    task.Id,
                    NotificationKind.Overdue,
                    task.Title,
                    $"Due now — {localDue:t}",
                    deadline));
            }
        }

        return result;
    }

    private static string DescribeLead(TimeSpan lead)
    {
        if (lead.TotalMinutes < 60)
        {
            return $"{(int)lead.TotalMinutes} min";
        }

        // Whole hours read as "2 h"; a leftover shows as e.g. "1 h 30 min".
        var wholeHours = (int)lead.TotalHours;
        var minutes = (int)(lead.TotalMinutes - wholeHours * 60);
        return minutes == 0 ? $"{wholeHours} h" : $"{wholeHours} h {minutes} min";
    }
}
