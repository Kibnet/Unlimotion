using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public class TaskTreeManager
{
    public IStorage Storage { get; init; }

    public TaskTreeManager(IStorage storage)
    {
        Storage = storage;
    }

    public async Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask = null, bool isBlocked = false)
    {
        var result = new Dictionary<string, TaskItem>();

        // Create
        if (currentTask is null)
        {
            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    change.Version = 1;
                    await Storage.Save(change);
                    result.AddOrUpdate(change);

                    return true;
                }
                catch
                {
                    return false;
                }
            });
            // Явное преобразование в список
        }
        // CreateSibling, CreateBlockedSibling
        else
        {
            string? newTaskId = null;

            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    if (newTaskId is null)
                    {
                        change.Version = 1;
                        await Storage.Save(change);
                        newTaskId = change.Id;
                        result.AddOrUpdate(change);
                    }

                    if (currentTask.ParentTasks.Count > 0)
                    {
                        foreach (var parent in currentTask.ParentTasks)
                        {
                            var parentModel = await Storage.Load(parent);
                            if (parentModel != null)
                            {
                                result.AddOrUpdateRange(
                                    await CreateParentChildRelation(parentModel, change));
                            }
                        }
                    }

                    if (isBlocked)
                    {
                        result.AddOrUpdateRange(
                            await CreateBlockingBlockedByRelation(change, currentTask));
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
            // Явное преобразование в список
        }

        return result.Values.ToList(); // Явное преобразование в список
    }


    public async Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask)
    {
        var result = new Dictionary<string, TaskItem>();
        string? newTaskId = null;

        //CreateInner
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    change.Version = 1;
                    await Storage.Save(change);
                    newTaskId = change.Id;
                    result.AddOrUpdate(change);
                }

                result.AddOrUpdateRange(
                    await CreateParentChildRelation(currentTask, change));
                result.AddOrUpdateRange(
                    await CalculateAndUpdateAvailability(currentTask));
                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList(); // Явное преобразование в список
    }

    public async Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage = true)
    {
        var result = new Dictionary<string, TaskItem>();
        var tasksToRecalculate = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Collect tasks that need recalculation before breaking relations
                if (change.ParentTasks.Any())
                {
                    foreach (var parentId in change.ParentTasks)
                    {
                        var parentItem = await Storage.Load(parentId);
                        if (parentItem != null)
                        {
                            tasksToRecalculate.Add(parentItem);
                        }
                    }
                }

                if (change.BlocksTasks.Any())
                {
                    foreach (var blockedId in change.BlocksTasks)
                    {
                        var blockedItem = await Storage.Load(blockedId);
                        if (blockedItem != null)
                        {
                            tasksToRecalculate.Add(blockedItem);
                        }
                    }
                }

                // Удаление связей с детьми
                if (change.ContainsTasks?.Any() == true)
                {
                    foreach (var child in change.ContainsTasks)
                    {
                        var childItem = await Storage.Load(child);
                        if (childItem == null) continue;
                        try
                        {
                            if (childItem.ParentTasks.Contains(change.Id))
                            {
                                childItem.ParentTasks!.Remove(change.Id);
                                await Storage.Save(childItem);
                                result.AddOrUpdate(childItem);
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }

                // Удаление связей с родителями
                if (change.ParentTasks?.Any() == true)
                {
                    foreach (var parent in change.ParentTasks)
                    {
                        var parentItem = await Storage.Load(parent);
                        if (parentItem == null) continue;
                        try
                        {
                            if (parentItem.ContainsTasks.Contains(change.Id))
                            {
                                parentItem.ContainsTasks.Remove(change.Id);
                                await Storage.Save(parentItem);
                                result.AddOrUpdate(parentItem);
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }

                // Удаление блокирующих связей
                if (change.BlockedByTasks?.Any() == true)
                {
                    foreach (var blocker in change.BlockedByTasks)
                    {
                        var blockerItem = await Storage.Load(blocker);
                        if (blockerItem == null) continue;
                        try
                        {
                            if (blockerItem.BlocksTasks.Contains(change.Id))
                            {
                                blockerItem.BlocksTasks.Remove(change.Id);
                                await Storage.Save(blockerItem);
                                result.AddOrUpdate(blockerItem);
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }

                // Удаление связей с блокируемыми задачами
                if (change.BlocksTasks?.Any() == true)
                {
                    foreach (var blocked in change.BlocksTasks)
                    {
                        var blockedItem = await Storage.Load(blocked);
                        if (blockedItem == null) continue;
                        try
                        {
                            if (blockedItem.BlockedByTasks.Contains(change.Id))
                            {
                                blockedItem.BlockedByTasks.Remove(change.Id);
                                await Storage.Save(blockedItem);
                                result.AddOrUpdate(blockedItem);
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }

                // Recalculate availability for affected tasks
                foreach (var taskToRecalc in tasksToRecalculate)
                {
                    result.AddOrUpdateRange(
                        await CalculateAndUpdateAvailability(taskToRecalc));
                }

                // Удаление самой задачи
                if (deleteInStorage)
                {
                    // В случае разрыва отношений (задача/подзадача), удаляемая таска может попасть в результат
                    // в этом случае файл после удаления создатся снова.
                    // Удаляем из результата
                    result.Remove(change.Id);
                    await Storage.Remove(change.Id);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList(); // Явное преобразование в список
    }

    public async Task<List<TaskItem>> UpdateTask(TaskItem change)
    {
        var result = new Dictionary<string, TaskItem>();
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                // Load the existing task to check if IsCompleted changed
                var existingTask = await Storage.Load(change.Id);
                bool isCompletedChanged = existingTask?.IsCompleted != change.IsCompleted;

                if (isCompletedChanged)
                {
                    // Handle IsCompleted changes with the dedicated method
                    var completionTasks = await HandleTaskCompletionChange(change);
                    foreach (var task in completionTasks)
                    {
                        result.AddOrUpdate(task);
                    }
                }
                else
                {
                    // Regular update without IsCompleted change
                    await Storage.Save(change);
                    result.AddOrUpdate(change);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList(); // Явное преобразование в список
    }

    public async Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
    {
        var result = new Dictionary<string, TaskItem>();
        string newTaskId = null;

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                var clone = new TaskItem
                {
                    Description = change.Description,
                    Title = change.Title,
                    PlannedDuration = change.PlannedDuration,
                    Repeater = change.Repeater,
                    Wanted = change.Wanted,
                    Version = 1,
                };

                await Storage.Save(clone);

                if (change.ContainsTasks?.Count > 0)
                {
                    foreach (var containsId in change.ContainsTasks)
                    {
                        var child = await Storage.Load(containsId);
                        if (child != null)
                        {
                            result.AddOrUpdateRange(
                                await CreateParentChildRelation(clone, child));
                        }
                    }

                    result.AddOrUpdateRange(
                        await CalculateAndUpdateAvailability(clone));
                }

                if (stepParents?.Count > 0)
                {
                    foreach (var parent in stepParents)
                    {
                        result.AddOrUpdateRange(
                            await CreateParentChildRelation(parent, clone));
                        result.AddOrUpdateRange(
                            await CalculateAndUpdateAvailability(parent));
                    }
                }

                if (change.BlockedByTasks?.Count > 0)
                {
                    foreach (var blockedById in change.BlockedByTasks)
                    {
                        var blockedBy = await Storage.Load(blockedById);
                        if (blockedBy != null)
                        {
                            result.AddOrUpdateRange(
                                await CreateBlockingBlockedByRelation(clone, blockedBy));
                        }
                    }

                    result.AddOrUpdateRange(
                        await CalculateAndUpdateAvailability(clone));
                }

                if (change.BlocksTasks?.Count > 0)
                {
                    foreach (var blocksId in change.BlocksTasks)
                    {
                        var blockTask = await Storage.Load(blocksId);
                        if (blockTask != null)
                        {
                            result.AddOrUpdateRange(
                                await CreateBlockingBlockedByRelation(blockTask, clone));

                            result.AddOrUpdateRange(
                                await CalculateAndUpdateAvailability(blockTask));
                        }
                    }
                }

                result.AddOrUpdate(clone);

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent)
    {
        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await CreateParentChildRelation(additionalParent, change));

        result.AddOrUpdateRange(
            await CalculateAndUpdateAvailability(change));

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await CreateParentChildRelation(newParent, change));

        if (prevParent is not null)
        {
            result.AddOrUpdateRange(
                await BreakParentChildRelation(prevParent, change));
        }

        result.AddOrUpdateRange(
            await CalculateAndUpdateAvailability(change));
        
        // Also recalculate availability for both parents
        result.AddOrUpdateRange(
            await CalculateAndUpdateAvailability(newParent));
            
        if (prevParent is not null)
        {
            result.AddOrUpdateRange(
                await CalculateAndUpdateAvailability(prevParent));
        }

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
    {
        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await BreakBlockingBlockedByRelation(taskToUnblock, blockingTask));

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
    {
        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await CreateBlockingBlockedByRelation(taskToBlock, blockingTask));

        return result.Values.ToList();
    }

    public async Task<TaskItem?> LoadTask(string taskId)
    {
        return await Storage.Load(taskId);
    }

    public async Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await BreakParentChildRelation(parent, child));

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> BreakParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new Dictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (parent.ContainsTasks.Contains(child.Id))
                {
                    parent.ContainsTasks.Remove(child.Id);
                    await Storage.Save(parent);
                    result.AddOrUpdate(parent);
                }

                if ((child.ParentTasks ?? new List<string>()).Contains(parent.Id))
                {
                    child.ParentTasks!.Remove(parent.Id);
                    await Storage.Save(child);
                    result.AddOrUpdate(child);
                }

                result.AddOrUpdateRange(
                    await CalculateAndUpdateAvailability(parent));

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> CreateParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new Dictionary<string, TaskItem>();

        // Prevent invalid self-relations such as task -> itself.
        if (parent == null || child == null || string.IsNullOrWhiteSpace(parent.Id) ||
            string.IsNullOrWhiteSpace(child.Id) || parent.Id == child.Id)
        {
            return result.Values.ToList();
        }

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (!parent.ContainsTasks.Contains(child.Id))
                {
                    parent.ContainsTasks.Add(child.Id);
                    await Storage.Save(parent);
                    result.AddOrUpdate(parent);
                }

                if (!(child.ParentTasks ?? new List<string>()).Contains(parent.Id))
                {
                    child.ParentTasks!.Add(parent.Id);
                    await Storage.Save(child);
                    result.AddOrUpdate(child);
                }

                result.AddOrUpdateRange(
                    await CalculateAndUpdateAvailability(parent));

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> CreateBlockingBlockedByRelation(TaskItem taskToBlock,
        TaskItem blockingTask)
    {
        var result = new Dictionary<string, TaskItem>();

        // Prevent invalid self-relations such as task blocked by itself.
        if (taskToBlock == null || blockingTask == null || string.IsNullOrWhiteSpace(taskToBlock.Id) ||
            string.IsNullOrWhiteSpace(blockingTask.Id) || taskToBlock.Id == blockingTask.Id)
        {
            return result.Values.ToList();
        }

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (blockingTask != null && !blockingTask.BlocksTasks.Contains(taskToBlock.Id))
                {
                    blockingTask.BlocksTasks.Add(taskToBlock.Id);
                    await Storage.Save(blockingTask);
                    result.AddOrUpdate(blockingTask);
                }

                if (taskToBlock != null && !taskToBlock.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToBlock.BlockedByTasks.Add(blockingTask.Id);
                    await Storage.Save(taskToBlock);
                    result.AddOrUpdate(taskToBlock);
                }

                // Recalculate availability for the blocked task only
                if (taskToBlock != null)
                {
                    result.AddOrUpdateRange(
                        await CalculateAndUpdateAvailability(taskToBlock));
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> BreakBlockingBlockedByRelation(TaskItem taskToUnblock,
        TaskItem blockingTask)
    {
        var result = new Dictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (blockingTask != null && blockingTask.BlocksTasks.Contains(taskToUnblock.Id))
                {
                    blockingTask.BlocksTasks.Remove(taskToUnblock.Id);
                    await Storage.Save(blockingTask);
                    result.AddOrUpdate(blockingTask);
                }

                if (taskToUnblock != null && taskToUnblock.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToUnblock.BlockedByTasks.Remove(blockingTask.Id);
                    await Storage.Save(taskToUnblock);
                    result.AddOrUpdate(taskToUnblock);
                }

                // Recalculate availability for the unblocked task only
                if (taskToUnblock != null)
                {
                    result.AddOrUpdateRange(
                        await CalculateAndUpdateAvailability(taskToUnblock));
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    private async Task<bool> IsCompletedAsync(Func<Task<bool>> task, TimeSpan? timeout = null)
    {
        TimeSpan countRetry = timeout ?? TimeSpan.FromMinutes(2);

        AsyncRetryPolicy<bool>? retryPolicy = Policy.HandleResult<bool>(x => !x)
            .WaitAndRetryAsync(
                (int)countRetry.TotalSeconds, _ => TimeSpan.FromSeconds(1), (_, _, count,
                    _) =>
                {
                    //_logger.Error($"Попытка выполнения операции с таском  №{count}");
                });

        var res = await retryPolicy.ExecuteAsync(() => task.Invoke());

        if (!res)
            throw new TimeoutException(
                $"Операция не была корректно завершена за заданный таймаут {timeout}");
        return (res);
    }

    public async Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task)
    {
        var result = new Dictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Recalculate transitively:
                // if task X changes, it may affect:
                // - parents of X
                // - tasks blocked by X
                // and then their dependents recursively.
                var queue = new Queue<TaskItem>();
                var processedIds = new HashSet<string>();
                queue.Enqueue(task);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (current == null || string.IsNullOrEmpty(current.Id))
                        continue;
                    if (!processedIds.Add(current.Id))
                        continue;

                    // Preserve reference identity for the entry task (some callers/tests
                    // expect this exact instance to be returned with updated fields).
                    var currentFromStorage = ReferenceEquals(current, task)
                        ? current
                        : await Storage.Load(current.Id) ?? current;

                    result.AddOrUpdateRange(
                        await CalculateAvailabilityForTask(currentFromStorage));

                    var affectedTasks = await GetAffectedTasks(currentFromStorage);
                    foreach (var affectedTask in affectedTasks)
                    {
                        if (affectedTask != null && !processedIds.Contains(affectedTask.Id))
                        {
                            queue.Enqueue(affectedTask);
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> CalculateAvailabilityForTask(TaskItem task)
    {
        var result = new Dictionary<string, TaskItem>();

        // Calculate IsCanBeCompleted based on business rules:
        // 1. All contained tasks must be completed (IsCompleted != false)
        // 2. All blocking tasks must be completed (IsCompleted != false)
        bool allContainsCompleted = true;
        bool allBlockersCompleted = true;

        // Check all contained tasks
        if (task.ContainsTasks?.Any() == true)
        {
            foreach (var childId in task.ContainsTasks)
            {
                var childTask = await Storage.Load(childId);
                if (childTask != null && childTask.IsCompleted == false)
                {
                    allContainsCompleted = false;
                    break;
                }
            }
        }

        // Check all blocking tasks
        if (task.BlockedByTasks?.Any() == true)
        {
            foreach (var blockerId in task.BlockedByTasks)
            {
                var blockerTask = await Storage.Load(blockerId);
                if (blockerTask != null && blockerTask.IsCompleted == false)
                {
                    allBlockersCompleted = false;
                    break;
                }
            }
        }

        bool newIsCanBeCompleted = allContainsCompleted && allBlockersCompleted;

        // Update IsCanBeCompleted
        task.IsCanBeCompleted = newIsCanBeCompleted;

        // Manage UnlockedDateTime based on availability changes
        if (newIsCanBeCompleted && task.UnlockedDateTime == null)
        {
            // Task became available - set UnlockedDateTime
            task.UnlockedDateTime = DateTimeOffset.UtcNow;
        }
        else if (!newIsCanBeCompleted)
        {
            // Task became blocked - clear UnlockedDateTime
            task.UnlockedDateTime = null;
        }

        // Save the updated task
        await Storage.Save(task);
        result.AddOrUpdate(task);

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> GetAffectedTasks(TaskItem task)
    {
        var affectedTasks = new List<TaskItem>();
        var processedIds = new HashSet<string>();

        // Collect all parent tasks (because their availability depends on this task)
        if (task.ParentTasks?.Any() == true)
        {
            foreach (var parentId in task.ParentTasks)
            {
                if (!processedIds.Contains(parentId))
                {
                    var parentTask = await Storage.Load(parentId);
                    if (parentTask != null)
                    {
                        affectedTasks.Add(parentTask);
                        processedIds.Add(parentId);
                    }
                }
            }
        }

        // Collect all tasks blocked by this task (because their availability depends on this task)
        if (task.BlocksTasks?.Any() == true)
        {
            foreach (var blockedId in task.BlocksTasks)
            {
                if (!processedIds.Contains(blockedId))
                {
                    var blockedTask = await Storage.Load(blockedId);
                    if (blockedTask != null)
                    {
                        affectedTasks.Add(blockedTask);
                        processedIds.Add(blockedId);
                    }
                }
            }
        }

        return affectedTasks;
    }

    /// <summary>
    /// Handles logic when a task's IsCompleted property changes
    /// </summary>
    /// <param name="task">The task that has changed</param>
    /// <param name="previousIsCompleted">The previous value of IsCompleted</param>
    /// <returns>List of affected tasks</returns>
    public async Task<List<TaskItem>> HandleTaskCompletionChange(TaskItem task)
    {
        var result = new Dictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Handle completion state changes
                if (task.IsCompleted == true && task.CompletedDateTime == null)
                {
                    task.CompletedDateTime ??= DateTimeOffset.UtcNow;
                    task.ArchiveDateTime = null;

                    // Handle repeater logic
                    if (task.Repeater != null && task.Repeater.Type != RepeaterType.None &&
                        task.PlannedBeginDateTime.HasValue)
                    {
                        var clone = new TaskItem
                        {
                            BlocksTasks = task.BlocksTasks.ToList(),
                            BlockedByTasks =
                                task.BlockedByTasks.ToList(), //TODO добавить тест на срабатывание блокировки
                            ContainsTasks = task.ContainsTasks.ToList(),
                            Description = task.Description,
                            Title = task.Title,
                            PlannedDuration = task.PlannedDuration,
                            Repeater = task.Repeater,
                            Wanted = task.Wanted,
                        };
                        clone.PlannedBeginDateTime = task.Repeater.GetNextOccurrence(task.PlannedBeginDateTime.Value);
                        if (task.PlannedEndDateTime.HasValue)
                        {
                            clone.PlannedEndDateTime =
                                clone.PlannedBeginDateTime.Value.Add(task.PlannedEndDateTime.Value -
                                                                     task.PlannedBeginDateTime.Value);
                        }

                        // Save the cloned task
                        clone.Version = 1;
                        await Storage.Save(clone);
                        result.AddOrUpdate(clone);

                        // Restore reverse links for cloned relations, so model stays symmetric:
                        // child.ParentTasks, blocker.BlocksTasks, blocked.BlockedByTasks.
                        if (clone.ContainsTasks?.Count > 0)
                        {
                            foreach (var containsId in clone.ContainsTasks)
                            {
                                var child = await Storage.Load(containsId);
                                if (child != null)
                                {
                                    result.AddOrUpdateRange(
                                        await CreateParentChildRelation(clone, child));
                                }
                            }
                        }

                        if (clone.BlockedByTasks?.Count > 0)
                        {
                            foreach (var blockerId in clone.BlockedByTasks)
                            {
                                var blocker = await Storage.Load(blockerId);
                                if (blocker != null)
                                {
                                    result.AddOrUpdateRange(
                                        await CreateBlockingBlockedByRelation(clone, blocker));
                                }
                            }
                        }

                        if (clone.BlocksTasks?.Count > 0)
                        {
                            foreach (var blockedId in clone.BlocksTasks)
                            {
                                var blocked = await Storage.Load(blockedId);
                                if (blocked != null)
                                {
                                    result.AddOrUpdateRange(
                                        await CreateBlockingBlockedByRelation(blocked, clone));
                                }
                            }
                        }

                        // Always normalize clone availability even if it has no relations.
                        result.AddOrUpdateRange(
                            await CalculateAndUpdateAvailability(clone));
                    }
                }

                if (task.IsCompleted == false)
                {
                    task.ArchiveDateTime = null;
                    task.CompletedDateTime = null;
                }

                if (task.IsCompleted == null && task.ArchiveDateTime == null)
                {
                    task.ArchiveDateTime ??= DateTimeOffset.UtcNow;
                }

                // Save the updated task
                await Storage.Save(task);
                result.AddOrUpdate(task);

                result.AddOrUpdateRange(
                    await CalculateAndUpdateAvailability(task));

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Values.ToList();
    }
}
