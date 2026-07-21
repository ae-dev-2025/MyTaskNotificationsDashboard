using System.ComponentModel.DataAnnotations;

namespace TaskDashboard.Models;

/// <summary>
/// Editable shape of a blocked period, used by the add and edit dialog on the
/// blocked-time page. Mirrors the TaskForm pattern.
/// </summary>
public class BlockedForm : IValidatableObject
{
    [Required(ErrorMessage = "Give the period a label.")]
    [MaxLength(100, ErrorMessage = "Keep the label under 100 characters.")]
    public string Label { get; set; } = string.Empty;

    public bool IsRecurring { get; set; } = true;

    public List<DayOfWeek> Days { get; set; } = AllDays();

    public TimeOnly? StartTime { get; set; } = new(23, 0);

    public TimeOnly? EndTime { get; set; } = new(7, 0);

    public DateTimeOffset? Start { get; set; }

    public DateTimeOffset? End { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsRecurring)
        {
            if (Days.Count == 0)
            {
                yield return new ValidationResult("Pick at least one day.", [nameof(Days)]);
            }

            if (StartTime is null || EndTime is null)
            {
                yield return new ValidationResult("Set a start and end time.", [nameof(StartTime)]);
            }
            else if (StartTime == EndTime)
            {
                yield return new ValidationResult("Start and end time cannot be the same.", [nameof(EndTime)]);
            }
        }
        else
        {
            if (Start is null || End is null)
            {
                yield return new ValidationResult("Set a start and an end.", [nameof(Start)]);
            }
            else if (End <= Start)
            {
                yield return new ValidationResult("The end must be after the start.", [nameof(End)]);
            }
        }
    }

    public static BlockedForm From(BlockedPeriod period) => new()
    {
        Label = period.Label,
        IsRecurring = period.IsRecurring,
        Days = [.. period.Days],
        StartTime = period.StartTime,
        EndTime = period.EndTime,
        Start = period.Start,
        End = period.End,
    };

    public void Reset()
    {
        Label = string.Empty;
        IsRecurring = true;
        Days = AllDays();
        StartTime = new(23, 0);
        EndTime = new(7, 0);
        Start = null;
        End = null;
    }

    public void ApplyTo(BlockedPeriod period)
    {
        period.Label = Label.Trim();
        period.IsRecurring = IsRecurring;
        period.Days = IsRecurring ? [.. Days] : [];
        period.StartTime = IsRecurring ? StartTime : null;
        period.EndTime = IsRecurring ? EndTime : null;
        period.Start = IsRecurring ? null : Start;
        period.End = IsRecurring ? null : End;
    }

    private static List<DayOfWeek> AllDays() =>
        [.. Enum.GetValues<DayOfWeek>()];
}
