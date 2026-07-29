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

    /// <summary>Saved task defaults, offered when adding a task. A new optional
    /// list needs no version bump — absent in an older file, it deserializes to
    /// the empty default.</summary>
    public List<TaskPreset> Presets { get; set; } = [];

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

    /// <summary>Whether the calendar shows its zoom slider. Older files lack
    /// the property and default to shown.</summary>
    public bool ShowCalendarZoom { get; set; } = true;

    /// <summary>How long the display is held awake while the dashboard is in
    /// front: 0 never (the default, and what an older file deserializes to),
    /// <see cref="KeepScreenOnAlways"/> for as long as the app is open, and any
    /// positive value a number of minutes. Android only — see
    /// <c>IScreenWakeService</c>.</summary>
    public int KeepScreenOnMinutes { get; set; }

    /// <summary>The sentinel <see cref="KeepScreenOnMinutes"/> uses for "no
    /// expiry". Negative rather than a large number, so it cannot be confused
    /// with a duration a user might pick.</summary>
    public const int KeepScreenOnAlways = -1;

    /// <summary>Whether the calendar draws deadline markers. Toggled from the
    /// calendar's own control panel; persisted so a grid deliberately cleared of
    /// markers stays that way. Older files lack the property and default to
    /// shown, which is the behaviour before the panel existed.</summary>
    public bool ShowCalendarDeadlines { get; set; } = true;

    /// <summary>Whether collapsing the sidebar leaves an icon rail rather than
    /// hiding it entirely. Defaults to fully hidden, the original behaviour,
    /// which is also what an older file deserializes to.</summary>
    public bool CollapseNavToIcons { get; set; }
}
