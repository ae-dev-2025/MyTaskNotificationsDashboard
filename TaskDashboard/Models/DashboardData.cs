namespace TaskDashboard.Models;

/// <summary>
/// The persisted document: everything the dashboard stores, in one file so a
/// single atomic write keeps tasks and blocked periods consistent together.
/// Version 1 was a bare JSON array of tasks; the loader migrates it.
/// </summary>
public class DashboardData
{
    public int Version { get; set; } = 2;

    public List<TodoItem> Tasks { get; set; } = [];

    public List<BlockedPeriod> BlockedPeriods { get; set; } = [];

    /// <summary>Mandatory gap the planner leaves between tasks, in minutes.
    /// Older files lack the property and fall back to the default.</summary>
    public int BreakMinutes { get; set; } = 15;

    /// <summary>Theme preference: "system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
}
