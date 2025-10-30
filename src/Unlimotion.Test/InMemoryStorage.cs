using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

public class InMemoryStorage : IStorage
{
    private readonly Dictionary<string, TaskItem> _tasks = new();

    public Task<TaskItem> Load(string id)
    {
        return Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);
    }

    public Task<bool> Save(TaskItem taskItem)
    {
        var clone = new TaskItem
        {
            Id = taskItem.Id??Guid.NewGuid().ToString(),
            UserId = taskItem.UserId,
            Title = taskItem.Title,
            Description = taskItem.Description,
            IsCompleted = taskItem.IsCompleted,
            IsCanBeCompleted = taskItem.IsCanBeCompleted,
            CreatedDateTime = taskItem.CreatedDateTime,
            UnlockedDateTime = taskItem.UnlockedDateTime,
            CompletedDateTime = taskItem.CompletedDateTime,
            ArchiveDateTime = taskItem.ArchiveDateTime,
            PlannedBeginDateTime = taskItem.PlannedBeginDateTime,
            PlannedEndDateTime = taskItem.PlannedEndDateTime,
            PlannedDuration = taskItem.PlannedDuration,
            ContainsTasks = taskItem.ContainsTasks?.ToList(),
            ParentTasks = taskItem.ParentTasks?.ToList(),
            BlocksTasks = taskItem.BlocksTasks?.ToList(),
            BlockedByTasks = taskItem.BlockedByTasks?.ToList(),
            Repeater = taskItem.Repeater,
            Importance = taskItem.Importance,
            Wanted = taskItem.Wanted,
            Version = taskItem.Version,
            SortOrder = taskItem.SortOrder
        };
        taskItem.Id = clone.Id;
        _tasks[clone.Id] = clone;

        return Task.FromResult(true);
    }

    public Task<bool> Remove(string id)
    {
        _tasks.Remove(id);
        return Task.FromResult(true);
    }

    public void Clear() => _tasks.Clear();
}