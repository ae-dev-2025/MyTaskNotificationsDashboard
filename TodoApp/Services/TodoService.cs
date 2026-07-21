using System.Text.Json;
using Microsoft.JSInterop;
using TodoApp.Models;

namespace TodoApp.Services;

/// <summary>
/// Holds the task list and mirrors it into browser localStorage so it
/// survives a refresh.
/// </summary>
public class TodoService(IJSRuntime js)
{
    private const string StorageKey = "todoapp.items";

    private readonly List<TodoItem> items = [];
    private bool loaded;

    public IReadOnlyList<TodoItem> Items => items;

    public async Task LoadAsync()
    {
        if (loaded)
        {
            return;
        }

        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var stored = JsonSerializer.Deserialize(json, TodoJsonContext.Default.ListTodoItem);
                if (stored is not null)
                {
                    items.AddRange(stored);
                }
            }
            catch (JsonException)
            {
                // Corrupt or stale payload — start fresh rather than breaking the app.
            }
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
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }
}
