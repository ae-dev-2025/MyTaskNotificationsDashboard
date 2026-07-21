using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>A concrete span of time. Blocked periods expand into these.</summary>
public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End);

/// <summary>A task placed into a specific span of time by the planner.</summary>
public sealed record PlannedSlot(Guid TaskId, DateTimeOffset Start, DateTimeOffset End, bool MissesDeadline);

/// <summary>
/// Places unfinished tasks into free time. Pure and deterministic: same inputs,
/// same plan. Ordering follows the product rule — deadline first (tasks without
/// one go last), then priority, then the shorter estimate — and placement is
/// greedy and contiguous from <c>from</c>, skipping busy ranges. Tasks are not
/// split across gaps in v1.
/// </summary>
public static class Planner
{
    /// <summary>Assumed length of a task that has no estimate.</summary>
    public static readonly TimeSpan DefaultEstimate = TimeSpan.FromMinutes(30);

    public static List<PlannedSlot> Plan(
        IEnumerable<TodoItem> tasks,
        IReadOnlyList<TimeRange> busy,
        DateTimeOffset from,
        TimeSpan horizon)
    {
        var limit = from + horizon;
        var blocked = MergeOverlapping(busy, from, limit);

        var ordered = tasks
            .Where(t => !t.IsDone)
            .OrderBy(t => t.Deadline ?? DateTimeOffset.MaxValue)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.EstimatedTime ?? DefaultEstimate)
            .ThenBy(t => t.CreatedAt); // stable tie-break so plans don't shuffle

        var slots = new List<PlannedSlot>();
        var cursor = from;

        foreach (var task in ordered)
        {
            var duration = task.EstimatedTime ?? DefaultEstimate;
            var start = NextFreeStart(cursor, duration, blocked);

            if (start + duration > limit)
            {
                break; // no room left inside the horizon; remaining tasks stay unplanned
            }

            var end = start + duration;
            slots.Add(new PlannedSlot(
                task.Id,
                start,
                end,
                task.Deadline is { } deadline && end > deadline));

            cursor = end;
        }

        return slots;
    }

    /// <summary>Earliest start at or after <paramref name="cursor"/> where the
    /// whole duration fits without touching a blocked range.</summary>
    private static DateTimeOffset NextFreeStart(
        DateTimeOffset cursor,
        TimeSpan duration,
        List<TimeRange> blocked)
    {
        foreach (var range in blocked)
        {
            var overlaps = range.Start < cursor + duration && range.End > cursor;
            if (overlaps)
            {
                cursor = range.End;
            }
        }

        return cursor;
    }

    /// <summary>Clips ranges to the window, sorts them, and merges overlaps so
    /// <see cref="NextFreeStart"/> can walk them in one pass.</summary>
    private static List<TimeRange> MergeOverlapping(
        IReadOnlyList<TimeRange> ranges,
        DateTimeOffset from,
        DateTimeOffset limit)
    {
        var clipped = ranges
            .Where(r => r.End > from && r.Start < limit)
            .Select(r => new TimeRange(
                r.Start < from ? from : r.Start,
                r.End > limit ? limit : r.End))
            .OrderBy(r => r.Start)
            .ToList();

        var merged = new List<TimeRange>();
        foreach (var range in clipped)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with
                {
                    End = range.End > merged[^1].End ? range.End : merged[^1].End,
                };
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
