using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>What the app has learned about the user's estimates. Factor is
/// null until enough samples exist; 1.4 means tasks take about 1.4× their
/// estimate.</summary>
public sealed record EstimationInsight(int SampleCount, double? Factor);

/// <summary>
/// Computes how the user's estimates compare to reality from completed tasks
/// that have a real [StartedAt, CompletedAt] span and an estimate. Pure and
/// display-only by product decision: the planner keeps using raw estimates.
/// </summary>
public static class Estimation
{
    /// <summary>Samples needed before a factor is reported.</summary>
    public const int MinimumSamples = 5;

    // Guards against junk samples: sub-5-minute actuals are misclicks, and
    // ratios outside [0.25, 4] are almost always a task left running overnight
    // or completed without real work — not evidence about estimating skill.
    private static readonly TimeSpan MinimumActual = TimeSpan.FromMinutes(5);
    private const double MinimumRatio = 0.25;
    private const double MaximumRatio = 4.0;

    public static EstimationInsight Compute(IEnumerable<TodoItem> tasks)
    {
        var ratios = tasks
            .Where(t => t.IsDone
                && t.StartedAt is not null
                && t.CompletedAt is not null
                && t.EstimatedTime is { } estimate && estimate > TimeSpan.Zero)
            .Select(t => new
            {
                Actual = t.CompletedAt!.Value - t.StartedAt!.Value,
                Estimate = t.EstimatedTime!.Value,
            })
            .Where(x => x.Actual >= MinimumActual)
            .Select(x => x.Actual.TotalMinutes / x.Estimate.TotalMinutes)
            .Where(r => r is >= MinimumRatio and <= MaximumRatio)
            .OrderBy(r => r)
            .ToList();

        var factor = ratios.Count >= MinimumSamples ? Median(ratios) : (double?)null;
        return new EstimationInsight(ratios.Count, factor);
    }

    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
