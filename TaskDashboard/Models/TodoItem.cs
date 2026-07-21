using System.Text.Json.Serialization;

namespace TaskDashboard.Models;

public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    /// <summary>When work on the task actually began. Kept after completion so
    /// history shows the real span; cleared if the task is un-done.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the task was marked done. Null while unfinished, and for
    /// tasks completed before this field existed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonIgnore]
    public bool IsInProgress => !IsDone && StartedAt is not null;

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
