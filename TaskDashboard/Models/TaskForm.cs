using System.ComponentModel.DataAnnotations;

namespace TaskDashboard.Models;

/// <summary>
/// Editable shape of a task, used by both the add form and the inline editor so
/// the two cannot drift apart. Effort is captured in whole minutes because that
/// is what the number input produces; <see cref="EstimatedTime"/> converts it.
/// </summary>
public class TaskForm : IValidatableObject
{
    [Required(ErrorMessage = "Give the task a title.")]
    [MaxLength(200, ErrorMessage = "Keep the title under 200 characters.")]
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset? Deadline { get; set; }

    public DateTimeOffset? NotBefore { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    [Range(1, 100_000, ErrorMessage = "Estimate must be between 1 and 100000 minutes.")]
    public int? EstimatedMinutes { get; set; }

    public TimeSpan? EstimatedTime =>
        EstimatedMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NotBefore is { } notBefore && Deadline is { } deadline && notBefore >= deadline)
        {
            yield return new ValidationResult(
                "The earliest start must be before the deadline.", [nameof(NotBefore)]);
        }
    }

    public static TaskForm From(TodoItem item) => new()
    {
        Title = item.Title,
        Deadline = item.Deadline,
        NotBefore = item.NotBefore,
        Priority = item.Priority,
        EstimatedMinutes = item.EstimatedTime is { } estimate
            ? (int)Math.Round(estimate.TotalMinutes)
            : null,
    };

    public void Reset()
    {
        Title = string.Empty;
        Deadline = null;
        NotBefore = null;
        Priority = TaskPriority.Normal;
        EstimatedMinutes = null;
    }
}
