namespace TaskDashboard.Models;

/// <summary>
/// A saved set of task defaults, so a task done regularly need not be retyped.
/// Deliberately carries no dates: when a task is due is the part that differs
/// every time it is added, so a preset holds only the parts that repeat.
/// </summary>
public class TaskPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The task title this preset fills in. Doubles as the preset's
    /// own name — there is nothing else to call it.</summary>
    public string Title { get; set; } = string.Empty;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>Estimated effort to fill in. Null leaves the estimate blank.</summary>
    public TimeSpan? EstimatedTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
