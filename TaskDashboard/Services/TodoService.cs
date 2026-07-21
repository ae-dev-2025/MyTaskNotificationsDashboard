using System.Text.Json;
using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>
/// Holds the task list and mirrors it into a JSON file in the app's private
/// data directory. In the web version this lived in browser localStorage; a
/// native app owns real storage, and WebView-local storage can be wiped with
/// the WebView cache, so a file is the durable choice here.
/// </summary>
public class TodoService
{
    private static string StoragePath => Path.Combine(FileSystem.AppDataDirectory, "tasks.json");

    private readonly List<TodoItem> items = [];
    private bool loaded;

    public IReadOnlyList<TodoItem> Items => items;

    public async Task LoadAsync()
    {
        if (loaded)
        {
            return;
        }

        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath);
                var stored = JsonSerializer.Deserialize(json, TodoJsonContext.Default.ListTodoItem);
                if (stored is not null)
                {
                    items.AddRange(stored);
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Corrupt or unreadable payload — start fresh rather than breaking the app.
        }

        loaded = true;
    }

    public async Task AddAsync(
        string title,
        DateTimeOffset? deadline,
        TaskPriority priority,
        TimeSpan? estimatedTime)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            return;
        }

        items.Add(new TodoItem
        {
            Title = title,
            Deadline = deadline,
            Priority = priority,
            EstimatedTime = estimatedTime,
        });

        await SaveAsync();
    }

    public async Task ToggleAsync(Guid id)
    {
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return;
        }

        item.IsDone = !item.IsDone;
        await SaveAsync();
    }

    public async Task UpdateAsync(
        Guid id,
        string title,
        DateTimeOffset? deadline,
        TaskPriority priority,
        TimeSpan? estimatedTime)
    {
        title = title.Trim();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item is null || title.Length == 0)
        {
            return;
        }

        item.Title = title;
        item.Deadline = deadline;
        item.Priority = priority;
        item.EstimatedTime = estimatedTime;

        await SaveAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        if (items.RemoveAll(i => i.Id == id) > 0)
        {
            await SaveAsync();
        }
    }

    public async Task ClearCompletedAsync()
    {
        if (items.RemoveAll(i => i.IsDone) > 0)
        {
            await SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(items, TodoJsonContext.Default.ListTodoItem);

        // Write to a sibling temp file and swap it in, so a crash mid-write
        // leaves the previous task list intact instead of a truncated file.
        var tempPath = StoragePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, StoragePath, overwrite: true);
    }
}
