namespace TodoApp.Models;

public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the task is due. Null means no deadline was set.</summary>
    public DateTimeOffset? Deadline { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>Estimated effort to complete. Null means not estimated.</summary>
    public TimeSpan? EstimatedTime { get; set; }

    /// <summary>True when an unfinished task's deadline has already passed.</summary>
    public bool IsOverdue(DateTimeOffset now) => !IsDone && Deadline is { } due && due < now;

    /// <summary>True when an unfinished task is due within <paramref name="window"/>.</summary>
    public bool IsDueSoon(DateTimeOffset now, TimeSpan window) =>
        !IsDone && Deadline is { } due && due >= now && due - now <= window;
}
