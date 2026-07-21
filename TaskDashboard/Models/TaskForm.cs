using System.ComponentModel.DataAnnotations;

namespace TaskDashboard.Models;

/// <summary>
/// Editable shape of a task, used by both the add form and the inline editor so
/// the two cannot drift apart. Effort is captured in whole minutes because that
/// is what the number input produces; <see cref="EstimatedTime"/> converts it.
/// </summary>
public class TaskForm
{
    [Required(ErrorMessage = "Give the task a title.")]
    [MaxLength(200, ErrorMessage = "Keep the title under 200 characters.")]
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset? Deadline { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    [Range(1, 100_000, ErrorMessage = "Estimate must be between 1 and 100000 minutes.")]
    public int? EstimatedMinutes { get; set; }

    public TimeSpan? EstimatedTime =>
        EstimatedMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;

    public static TaskForm From(TodoItem item) => new()
    {
        Title = item.Title,
        Deadline = item.Deadline,
        Priority = item.Priority,
        EstimatedMinutes = item.EstimatedTime is { } estimate
            ? (int)Math.Round(estimate.TotalMinutes)
            : null,
    };

    public void Reset()
    {
        Title = string.Empty;
        Deadline = null;
        Priority = TaskPriority.Normal;
        EstimatedMinutes = null;
    }
}
