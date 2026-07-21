using System.Text.Json.Serialization;

namespace TaskDashboard.Models;

/// <summary>
/// How urgent a task is. Serialized by name rather than by number so that
/// reordering or inserting values later does not reinterpret data already
/// stored on disk.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))]
public enum TaskPriority
{
    Low,
    Normal,
    High,
    Urgent,
}
