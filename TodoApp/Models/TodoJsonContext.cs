using System.Text.Json.Serialization;

namespace TodoApp.Models;

// Source-generated serialization so persistence keeps working under the
// trimming that Blazor WebAssembly applies on publish.
[JsonSerializable(typeof(List<TodoItem>))]
internal sealed partial class TodoJsonContext : JsonSerializerContext
{
}
