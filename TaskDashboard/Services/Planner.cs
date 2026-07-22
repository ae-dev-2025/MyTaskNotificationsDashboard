using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>A concrete span of time. Blocked periods expand into these.</summary>
public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End);

/// <summary>Expands blocked periods into the concrete ranges they occupy
/// inside a window, in local time.</summary>
public static class BlockedTime
{
    public static List<TimeRange> Expand(
        IEnumerable<BlockedPeriod> periods,
        DateTimeOffset from,
        DateTimeOffset to) =>
        [.. ExpandDetailed(periods, from, to).Select(x => x.Range)];

    /// <summary>Like <see cref="Expand"/>, but keeps the source period with
    /// each range so the calendar can label the shading.</summary>
    public static List<(BlockedPeriod Period, TimeRange Range)> ExpandDetailed(
        IEnumerable<BlockedPeriod> periods,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var ranges = new List<(BlockedPeriod, TimeRange)>();

        foreach (var period in periods)
        {
            if (!period.IsRecurring)
            {
                if (period is { Start: { } start, End: { } end } && end > from && start < to)
                {
                    ranges.Add((period, new TimeRange(start, end)));
                }

                continue;
            }

            if (period.StartTime is not { } startTime || period.EndTime is not { } endTime)
            {
                continue;
            }

            // Walk one day beyond each edge so windows that cross midnight are
            // caught from both sides.
            var firstDay = from.ToLocalTime().Date.AddDays(-1);
            var lastDay = to.ToLocalTime().Date.AddDays(1);

            for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                if (!period.Days.Contains(day.DayOfWeek))
                {
                    continue;
                }

                var occurrenceStart = Local(day + startTime.ToTimeSpan());
                var occurrenceEnd = endTime > startTime
                    ? Local(day + endTime.ToTimeSpan())
                    : Local(day.AddDays(1) + endTime.ToTimeSpan());

                if (occurrenceEnd > from && occurrenceStart < to)
                {
                    ranges.Add((period, new TimeRange(occurrenceStart, occurrenceEnd)));
                }
            }
        }

        return ranges;
    }

    private static DateTimeOffset Local(DateTime dt) =>
        new(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
}

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

    /// <summary>An in-progress task is never planned shorter than this, even
    /// when it has already overrun its estimate.</summary>
    public static readonly TimeSpan MinimumRemaining = TimeSpan.FromMinutes(5);

    public static List<PlannedSlot> Plan(
        IEnumerable<TodoItem> tasks,
        IReadOnlyList<TimeRange> busy,
        DateTimeOffset from,
        TimeSpan horizon,
        TimeSpan breakBetween)
    {
        var limit = from + horizon;
        var blocked = MergeOverlapping(busy, from, limit);
        var pending = tasks.Where(t => !t.IsDone).ToList();

        var slots = new List<PlannedSlot>();
        var cursor = from;

        // Reality first: a started task is being worked on NOW, so it occupies
        // the front of the plan for its remaining time — deliberately ignoring
        // blocked ranges, because it is already happening.
        foreach (var task in pending.Where(t => t.IsInProgress).OrderBy(t => t.StartedAt))
        {
            var duration = RemainingWork(task, from);
            var end = cursor + duration;
            if (end > limit)
            {
                break;
            }

            slots.Add(new PlannedSlot(
                task.Id, cursor, end,
                task.Deadline is { } due && end > due));
            cursor = end + breakBetween;
        }

        var ordered = pending
            .Where(t => !t.IsInProgress)
            .OrderBy(t => t.Deadline ?? DateTimeOffset.MaxValue)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.EstimatedTime ?? DefaultEstimate)
            .ThenBy(t => t.CreatedAt); // stable tie-break so plans don't shuffle

        foreach (var task in ordered)
        {
            var duration = task.EstimatedTime ?? DefaultEstimate;

            // Honor the task's earliest allowed start; the shared cursor is
            // deliberately NOT rewound for later tasks (plan stability over
            // gap-filling — the planner never reorders around a not-before).
            var earliest = task.NotBefore is { } notBefore && notBefore > cursor ? notBefore : cursor;
            var start = NextFreeStart(earliest, duration, blocked);

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

            cursor = end + breakBetween;
        }

        return slots;
    }

    /// <summary>Estimate minus time already spent, floored at
    /// <see cref="MinimumRemaining"/> so an overrun task still holds a slot.</summary>
    public static TimeSpan RemainingWork(TodoItem task, DateTimeOffset now)
    {
        var estimate = task.EstimatedTime ?? DefaultEstimate;
        if (task.StartedAt is { } started && started <= now)
        {
            var remaining = estimate - (now - started);
            return remaining > MinimumRemaining ? remaining : MinimumRemaining;
        }

        return estimate;
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
