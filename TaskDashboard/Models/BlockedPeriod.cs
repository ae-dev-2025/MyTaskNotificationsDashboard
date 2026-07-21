namespace TaskDashboard.Models;

/// <summary>
/// A period in which no tasks may be scheduled. Either a weekly recurring
/// pattern (sleep, work hours) or a single one-off range (an appointment).
/// Days are stored as BCL <see cref="DayOfWeek"/> values, whose numbering is
/// fixed by the framework, so numeric serialization is safe here.
/// </summary>
public class BlockedPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;

    public bool IsRecurring { get; set; }

    // Recurring shape: which weekdays, and the daily window. An end time at or
    // before the start time means the window crosses midnight (23:00–07:00).
    public List<DayOfWeek> Days { get; set; } = [];

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    // One-off shape: a single concrete range.
    public DateTimeOffset? Start { get; set; }

    public DateTimeOffset? End { get; set; }
}
