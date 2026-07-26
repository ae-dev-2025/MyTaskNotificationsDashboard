using System.ComponentModel.DataAnnotations;

namespace TaskDashboard.Models;

/// <summary>
/// Editable shape of a <see cref="TaskPreset"/>, shared by the add and edit
/// flows on the presets page so the two cannot drift apart. Mirrors
/// <see cref="TaskForm"/>, minus the fields a preset deliberately omits.
/// </summary>
public class PresetForm
{
    [Required(ErrorMessage = "Give the preset a title.")]
    [MaxLength(200, ErrorMessage = "Keep the title under 200 characters.")]
    public string Title { get; set; } = string.Empty;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    [Range(1, 100_000, ErrorMessage = "Estimate must be between 1 and 100000 minutes.")]
    public int? EstimatedMinutes { get; set; }

    public TimeSpan? EstimatedTime =>
        EstimatedMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;

    public static PresetForm From(TaskPreset preset) => new()
    {
        Title = preset.Title,
        Priority = preset.Priority,
        EstimatedMinutes = preset.EstimatedTime is { } estimate
            ? (int)Math.Round(estimate.TotalMinutes)
            : null,
    };

    /// <summary>Builds the form from a task, for "save as preset" — the task's
    /// dates are dropped, which is the whole point of a preset.</summary>
    public static PresetForm FromTask(TaskForm task) => new()
    {
        Title = task.Title,
        Priority = task.Priority,
        EstimatedMinutes = task.EstimatedMinutes,
    };

    public void ApplyTo(TaskPreset preset)
    {
        preset.Title = Title.Trim();
        preset.Priority = Priority;
        preset.EstimatedTime = EstimatedTime;
    }

    /// <summary>Copies another form's values in place. The pages hold one form
    /// instance and reuse it, so every field must be assigned — leaving one out
    /// silently carries the previous edit's value into the next one.</summary>
    public void CopyFrom(PresetForm source)
    {
        Title = source.Title;
        Priority = source.Priority;
        EstimatedMinutes = source.EstimatedMinutes;
    }

    public void Reset()
    {
        Title = string.Empty;
        Priority = TaskPriority.Normal;
        EstimatedMinutes = null;
    }
}
