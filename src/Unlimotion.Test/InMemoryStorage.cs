using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

public class InMemoryStorage : IStorage, ITaskGraphDiagnosticStorage
{
    private readonly Dictionary<string, TaskItem> _tasks = new();

    public Task<TaskItem?> Load(string id)
    {
        return Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);
    }

    public async IAsyncEnumerable<TaskItem> GetAll()
    {
        foreach (var tasksValue in _tasks.Values)
        {
            yield return tasksValue;
        }
    }

    public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
    {
        foreach (var taskItem in taskItems)
        {
            await Save(taskItem);
        }
    }

    public event EventHandler<TaskStorageUpdateEventArgs> Updating
    {
        add { }
        remove { }
    }
    public async Task<bool> Connect()
    {
        return true;
    }

    public async Task Disconnect()
    {
    }

    public event Action<Exception?>? OnConnectionError
    {
        add { }
        remove { }
    }

    public async Task<TaskItem> Save(TaskItem taskItem)
    {
        var clone = CloneTask(taskItem);
        clone.Id ??= Guid.NewGuid().ToString();
        taskItem.Id = clone.Id;
        _tasks[clone.Id] = clone;

        return taskItem;
    }

    public Task<TaskGraphReadResult> ReadGraphAsync()
    {
        var tasks = _tasks.Values.Select(CloneTask).ToArray();
        var filesByTaskId = tasks.ToDictionary(
            static task => task.Id,
            static task => $"<memory:{task.Id}>",
            StringComparer.Ordinal);
        return Task.FromResult(new TaskGraphReadResult(
            tasks,
            filesByTaskId,
            Array.Empty<TaskGraphLoadError>(),
            Array.Empty<TaskGraphDuplicateIdIssue>()));
    }

    public Task<bool> Remove(string id)
    {
        _tasks.Remove(id);
        return Task.FromResult(true);
    }

    public void Clear() => _tasks.Clear();

    private static TaskItem CloneTask(TaskItem taskItem) => taskItem with
    {
        StatusHistory = taskItem.StatusHistory?
            .Select(entry => entry == null
                ? null!
                : new TaskStatusHistoryEntry
                {
                    Status = entry.Status,
                    ChangedAt = entry.ChangedAt,
                    Author = entry.Author,
                    ExtensionData = entry.ExtensionData
                })
            .ToList() ?? new(),
        CompletionCriteria = taskItem.CompletionCriteria?
            .Select(criterion => new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied,
                ExtensionData = criterion.ExtensionData
            })
            .ToList() ?? new(),
        ContainsTasks = taskItem.ContainsTasks?.ToList() ?? new(),
        ParentTasks = taskItem.ParentTasks?.ToList() ?? new(),
        BlocksTasks = taskItem.BlocksTasks?.ToList() ?? new(),
        BlockedByTasks = taskItem.BlockedByTasks?.ToList() ?? new(),
        Repeater = taskItem.Repeater == null
            ? null
            : new RepeaterPattern
            {
                Type = taskItem.Repeater.Type,
                Period = taskItem.Repeater.Period,
                AfterComplete = taskItem.Repeater.AfterComplete,
                Pattern = taskItem.Repeater.Pattern?.ToList()!,
                ExtensionData = taskItem.Repeater.ExtensionData?.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.DeepClone())
            }
    };
}
