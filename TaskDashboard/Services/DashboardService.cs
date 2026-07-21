using System.Text.Json;
using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>
/// Holds the dashboard's data — tasks and blocked periods — and mirrors it
/// into a single JSON document in the app's private data directory, written
/// atomically so tasks and blocked periods stay consistent together.
/// </summary>
public class DashboardService
{
    private static string StoragePath => Path.Combine(FileSystem.AppDataDirectory, "tasks.json");

    private DashboardData data = new();
    private bool loaded;

    public IReadOnlyList<TodoItem> Items => data.Tasks;

    public IReadOnlyList<BlockedPeriod> BlockedPeriods => data.BlockedPeriods;

    public async Task LoadAsync()
    {
        if (loaded)
        {
            return;
        }

        var migrated = false;
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath);
                data = Parse(json, out migrated);
            }
        }
        catch (IOException)
        {
            // Unreadable file — start fresh rather than breaking the app.
        }

        loaded = true;

        if (migrated)
        {
            // Rewrite the legacy file in the current format straight away, so
            // the migration path runs once rather than on every launch.
            await SaveAsync();
        }
    }

    /// <summary>Reads the current envelope, or migrates the v1 format (a bare
    /// array of tasks) into it. Corrupt payloads yield an empty document.</summary>
    private static DashboardData Parse(string json, out bool migrated)
    {
        migrated = false;

        try
        {
            var envelope = JsonSerializer.Deserialize(json, TodoJsonContext.Default.DashboardData);
            if (envelope is { Tasks: not null })
            {
                return envelope;
            }
        }
        catch (JsonException)
        {
            // Not the envelope shape — fall through to the legacy format.
        }

        try
        {
            var legacy = JsonSerializer.Deserialize(json, TodoJsonContext.Default.ListTodoItem);
            if (legacy is not null)
            {
                migrated = true;
                return new DashboardData { Tasks = legacy };
            }
        }
        catch (JsonException)
        {
            // Corrupt — start fresh.
        }

        return new DashboardData();
    }

    // ---- tasks ----

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

        data.Tasks.Add(new TodoItem
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
        var item = data.Tasks.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return;
        }

        item.IsDone = !item.IsDone;
        if (item.IsDone)
        {
            // StartedAt is kept: [StartedAt, CompletedAt] is the real record
            // of when the work happened.
            item.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
            item.StartedAt = null;
        }

        await SaveAsync();
    }

    /// <summary>Marks a task as the one being worked on. Only one task can be
    /// in progress at a time — starting one stops any other.</summary>
    public async Task StartAsync(Guid id)
    {
        var item = data.Tasks.FirstOrDefault(i => i.Id == id);
        if (item is null || item.IsDone)
        {
            return;
        }

        foreach (var other in data.Tasks.Where(t => t.IsInProgress && t.Id != id))
        {
            other.StartedAt = null;
        }

        item.StartedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
    }

    /// <summary>Abandons an in-progress start without completing the task.</summary>
    public async Task StopAsync(Guid id)
    {
        var item = data.Tasks.FirstOrDefault(i => i.Id == id);
        if (item is null || item.StartedAt is null)
        {
            return;
        }

        item.StartedAt = null;
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
        var item = data.Tasks.FirstOrDefault(i => i.Id == id);
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
        if (data.Tasks.RemoveAll(i => i.Id == id) > 0)
        {
            await SaveAsync();
        }
    }

    public async Task ClearCompletedAsync()
    {
        if (data.Tasks.RemoveAll(i => i.IsDone) > 0)
        {
            await SaveAsync();
        }
    }

    // ---- blocked periods ----

    public async Task AddBlockedAsync(BlockedForm form)
    {
        var period = new BlockedPeriod();
        form.ApplyTo(period);
        data.BlockedPeriods.Add(period);
        await SaveAsync();
    }

    public async Task UpdateBlockedAsync(Guid id, BlockedForm form)
    {
        var period = data.BlockedPeriods.FirstOrDefault(p => p.Id == id);
        if (period is null)
        {
            return;
        }

        form.ApplyTo(period);
        await SaveAsync();
    }

    public async Task DeleteBlockedAsync(Guid id)
    {
        if (data.BlockedPeriods.RemoveAll(p => p.Id == id) > 0)
        {
            await SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(data, TodoJsonContext.Default.DashboardData);

        // Write to a sibling temp file and swap it in, so a crash mid-write
        // leaves the previous document intact instead of a truncated file.
        var tempPath = StoragePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, StoragePath, overwrite: true);
    }
}
