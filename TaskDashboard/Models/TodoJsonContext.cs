using System.Text.Json.Serialization;

namespace TaskDashboard.Models;

// Source-generated serialization so persistence keeps working under the
// trimming and AOT compilation MAUI applies on Android release builds.
[JsonSerializable(typeof(List<TodoItem>))]
internal sealed partial class TodoJsonContext : JsonSerializerContext
{
}
