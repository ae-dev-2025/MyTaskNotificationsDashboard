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
}
