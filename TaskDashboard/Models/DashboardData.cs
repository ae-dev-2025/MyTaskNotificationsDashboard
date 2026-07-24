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

    /// <summary>Whether deadline reminders are delivered as local
    /// notifications. Older files lack the property and default to on.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>How long before a task's deadline the "due soon" reminder
    /// fires, in minutes. Older files fall back to the default.</summary>
    public int NotificationLeadMinutes { get; set; } = 60;

    /// <summary>Whether a persistent notification keeps showing the task to do
    /// right now (Android only). Older files lack the property and default
    /// to on.</summary>
    public bool ShowCurrentTaskNotification { get; set; } = true;
}
