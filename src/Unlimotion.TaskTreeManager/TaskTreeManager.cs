using System;
using Polly;
using Polly.Retry;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public class TaskTreeManager : ITaskTreeManager
{
    private IStorage Storage { get; init; }
    public TaskTreeManager(IStorage storage)
    {
        Storage = storage;
    }

    public async Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask = null, bool isBlocked = false)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        // Create
        if (currentTask is null)
        {
            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    change.Version = 1;
                    await Storage.Save(change);
                    change.SortOrder = DateTime.Now;
                    result.AddOrUpdate(change.Id, change);

                    return true;
                }
                catch
                {
                    return false;
                }
            });

            return result.Dict.Values.ToList(); // Явное преобразование в список
        }
        // CreateSibling, CreateBlockedSibling
        else
        {
            string newTaskId = null;

            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    if (newTaskId is null)
                    {
                        change.Version = 1;
                        await Storage.Save(change);
                        change.SortOrder = DateTime.Now;
                        newTaskId = change.Id;
                        result.AddOrUpdate(change.Id, change);
                    }

                    if ((currentTask.ParentTasks ?? new List<string>()).Count > 0)
                    {
                        foreach (var parent in currentTask.ParentTasks)
                        {
                            var parentModel = await Storage.Load(parent);
                            result.AddOrUpdateRange(
                                (await CreateParentChildRelation(parentModel, change)).Dict
                            );
                        }
                    }

                    if (isBlocked && currentTask != null)
                    {
                        result.AddOrUpdateRange(
                            (await CreateBlockingBlockedByRelation(change, currentTask)).Dict
                        );
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });

            return result.Dict.Values.ToList(); // Явное преобразование в список
        }
    }


    public async Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        string newTaskId = null;

        //CreateInner
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    change.Version = 1;
                    await Storage.Save(change);
                    change.SortOrder = DateTime.Now;
                    newTaskId = change.Id;
                }

                result.AddOrUpdateRange((await CreateParentChildRelation(currentTask, change)).Dict);

                // Recalculate availability for the parent task
                var affectedTasks = await CalculateAndUpdateAvailability(currentTask);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList(); // Явное преобразование в список
    }

    public async Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage = true)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        var tasksToRecalculate = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Collect tasks that need recalculation before breaking relations
                if (change.ParentTasks?.Any() == true)
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

                if (change.BlocksTasks?.Any() == true)
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
                if (change.ContainsTasks.Any())
                {
                    foreach (var child in change.ContainsTasks)
                    {
                        var childItem = await Storage.Load(child);
                        result.AddOrUpdateRange(
                            (await BreakParentChildRelation(change, childItem)).Dict
                        );
                    }
                }

                // Удаление связей с родителями
                if (change.ParentTasks.Any())
                {
                    foreach (var parent in change.ParentTasks)
                    {
                        var parentItem = await Storage.Load(parent);
                        result.AddOrUpdateRange(
                            (await BreakParentChildRelation(parentItem, change)).Dict
                        );
                    }
                }

                // Удаление блокирующих связей
                if (change.BlockedByTasks.Any())
                {
                    foreach (var blocker in change.BlockedByTasks)
                    {
                        var blockerItem = await Storage.Load(blocker);
                        result.AddOrUpdateRange(
                            (await BreakBlockingBlockedByRelation(change, blockerItem)).Dict
                        );
                    }
                }

                // Удаление связей с блокируемыми задачами
                if (change.BlocksTasks.Any())
                {
                    foreach (var blocked in change.BlocksTasks)
                    {
                        var blockedItem = await Storage.Load(blocked);
                        result.AddOrUpdateRange(
                            (await BreakBlockingBlockedByRelation(blockedItem, change)).Dict
                        );
                    }
                }

                // Recalculate availability for affected tasks
                foreach (var taskToRecalc in tasksToRecalculate)
                {
                    var affectedTasks = await CalculateAndUpdateAvailability(taskToRecalc);
                    foreach (var task in affectedTasks)
                    {
                        result.AddOrUpdate(task.Id, task);
                    }
                }

                // Удаление самой задачи
                if (deleteInStorage)
                {
                    // В случае разрыва отношений (задача/подзадача), удаляемая таска может попасть в результат
                    // в этом случае файл после удаления создатся снова.
                    // Удаляем из результата
                    result.Dict.Remove(change.Id);
                    await Storage.Remove(change.Id);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList(); // Явное преобразование в список
    }

    public async Task<List<TaskItem>> UpdateTask(TaskItem change)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
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
                        result.AddOrUpdate(task.Id, task);
                    }
                }
                else
                {
                    // Regular update without IsCompleted change
                    await Storage.Save(change);
                    result.AddOrUpdate(change.Id, change);
                }
                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList(); // Явное преобразование в список
    }
    public async Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
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
                        var childRelation = await this.CreateParentChildRelation(clone, child);
                        result.AddOrUpdateRange(childRelation.Dict);
                    }

                    var affectedTasks = await CalculateAndUpdateAvailability(clone);
                    foreach (var task in affectedTasks)
                    {
                        result.AddOrUpdate(task.Id, task);
                    }
                }

                if (stepParents?.Count > 0)
                {
                    foreach (var parent in stepParents)
                    {
                        var childRelation = await this.CreateParentChildRelation(parent, clone);
                        result.AddOrUpdateRange(childRelation.Dict);

                        var affectedTasks = await CalculateAndUpdateAvailability(parent);
                        foreach (var task in affectedTasks)
                        {
                            result.AddOrUpdate(task.Id, task);
                        }
                    }
                }

                if (change.BlockedByTasks?.Count > 0)
                {
                    foreach (var blockedById in change.BlockedByTasks)
                    {
                        var blockedBy = await Storage.Load(blockedById);
                        var blockedByRelation = await this.CreateBlockingBlockedByRelation(clone, blockedBy);
                        result.AddOrUpdateRange(blockedByRelation.Dict);
                    }

                    var affectedTasks = await CalculateAndUpdateAvailability(clone);
                    foreach (var task in affectedTasks)
                    {
                        result.AddOrUpdate(task.Id, task);
                    }
                }

                if (change.BlocksTasks?.Count > 0)
                {
                    foreach (var blocksId in change.BlocksTasks)
                    {
                        var blockTask = await Storage.Load(blocksId);
                        var blockedByRelation = await this.CreateBlockingBlockedByRelation(blockTask, clone);
                        result.AddOrUpdateRange(blockedByRelation.Dict);
                        
                        var affectedTasks = await CalculateAndUpdateAvailability(blockTask);
                        foreach (var task in affectedTasks)
                        {
                            result.AddOrUpdate(task.Id, task);
                        }
                    }
                }

                clone.SortOrder = DateTime.Now;
                result.AddOrUpdate(clone.Id, clone);

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList();
    }

    public async Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        result.AddOrUpdateRange((await CreateParentChildRelation(additionalParent, change)).Dict);

        return result.Dict.Values.ToList();
    }

    public async Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        if (prevParent is not null)
        {
            result.AddOrUpdateRange((await BreakParentChildRelation(prevParent, change)).Dict);
        }
        result.AddOrUpdateRange((await CreateParentChildRelation(newParent, change)).Dict);

        return result.Dict.Values.ToList();
    }
    public async Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        result.AddOrUpdateRange((await BreakBlockingBlockedByRelation(taskToUnblock, blockingTask)).Dict);

        return result.Dict.Values.ToList();
    }

    public async Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        result.AddOrUpdateRange((await CreateBlockingBlockedByRelation(taskToBlock, blockingTask)).Dict);

        return result.Dict.Values.ToList();
    }

    public async Task<TaskItem> LoadTask(string taskId)
    {
        return await Storage.Load(taskId);
    }

    public async Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        result.AddOrUpdateRange((await BreakParentChildRelation(parent, child)).Dict);

        return result.Dict.Values.ToList();
    }

    private async Task<AutoUpdatingDictionary<string, TaskItem>> BreakParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (parent.ContainsTasks.Contains(child.Id))
                {
                    parent.ContainsTasks.Remove(child.Id);
                    await Storage.Save(parent);
                    result.AddOrUpdate(parent.Id, parent);
                }

                if ((child.ParentTasks ?? new List<string>()).Contains(parent.Id))
                {
                    child.ParentTasks!.Remove(parent.Id);
                    await Storage.Save(child);
                    result.AddOrUpdate(child.Id, child);
                }

                // Recalculate availability for the parent task
                var affectedTasks = await CalculateAndUpdateAvailability(parent);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
            ;
        });

        return result;
    }

    private async Task<AutoUpdatingDictionary<string, TaskItem>> CreateParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (!parent.ContainsTasks.Contains(child.Id))
                {
                    parent.ContainsTasks.Add(child.Id);
                    await Storage.Save(parent);
                    result.AddOrUpdate(parent.Id, parent);
                }

                if (!(child.ParentTasks ?? new List<string>()).Contains(parent.Id))
                {
                    child.ParentTasks!.Add(parent.Id);
                    await Storage.Save(child);
                    result.AddOrUpdate(child.Id, child);
                }

                // Recalculate availability for the parent task
                var affectedTasks = await CalculateAndUpdateAvailability(parent);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
            ;
        });

        return result;
    }

    private async Task<AutoUpdatingDictionary<string, TaskItem>> CreateBlockingBlockedByRelation(TaskItem taskToBlock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (!blockingTask.BlocksTasks.Contains(taskToBlock.Id))
                {
                    blockingTask.BlocksTasks.Add(taskToBlock.Id);
                    await Storage.Save(blockingTask);
                    result.AddOrUpdate(blockingTask.Id, blockingTask);
                }

                if (!taskToBlock.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToBlock.BlockedByTasks.Add(blockingTask.Id);
                    await Storage.Save(taskToBlock);
                    result.AddOrUpdate(taskToBlock.Id, taskToBlock);
                }

                // Recalculate availability for the blocked task only
                var affectedTasks = await CalculateAndUpdateAvailability(taskToBlock);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
            ;
        });

        return result;
    }

    private async Task<AutoUpdatingDictionary<string, TaskItem>> BreakBlockingBlockedByRelation(TaskItem taskToUnblock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                if (blockingTask.BlocksTasks.Contains(taskToUnblock.Id))
                {
                    blockingTask.BlocksTasks.Remove(taskToUnblock.Id);
                    await Storage.Save(blockingTask);
                    result.AddOrUpdate(blockingTask.Id, blockingTask);
                }

                if (taskToUnblock.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToUnblock.BlockedByTasks.Remove(blockingTask.Id);
                    await Storage.Save(taskToUnblock);
                    result.AddOrUpdate(taskToUnblock.Id, taskToUnblock);
                }

                // Recalculate availability for the unblocked task only
                var affectedTasks = await CalculateAndUpdateAvailability(taskToUnblock);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
            ;
        });

        return result;
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

        if (res == false)
            throw new TimeoutException(
                $"Операция не была корректно завершена за заданный таймаут {timeout}");
        return (res);
    }

    public async Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Calculate availability for the given task
                var updatedTasks = await CalculateAvailabilityForTask(task);
                result.AddOrUpdateRange(updatedTasks);

                // Collect and recalculate affected tasks
                var affectedTasks = await GetAffectedTasks(task);
                foreach (var affectedTask in affectedTasks)
                {
                    var affectedUpdatedTasks = await CalculateAvailabilityForTask(affectedTask);
                    result.AddOrUpdateRange(affectedUpdatedTasks);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList();
    }

    private async Task<Dictionary<string, TaskItem>> CalculateAvailabilityForTask(TaskItem task)
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
        bool previousIsCanBeCompleted = task.IsCanBeCompleted;

        // Update IsCanBeCompleted
        task.IsCanBeCompleted = newIsCanBeCompleted;

        // Manage UnlockedDateTime based on availability changes
        if (newIsCanBeCompleted && (!previousIsCanBeCompleted || task.UnlockedDateTime == null))
        {
            // Task became available - set UnlockedDateTime
            task.UnlockedDateTime = DateTimeOffset.UtcNow;
        }
        else if (!newIsCanBeCompleted && previousIsCanBeCompleted)
        {
            // Task became blocked - clear UnlockedDateTime
            task.UnlockedDateTime = null;
        }

        // Save the updated task
        await Storage.Save(task);
        result.Add(task.Id, task);

        return result;
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
        var result = new AutoUpdatingDictionary<string, TaskItem>();

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
                    if (task.Repeater != null && task.Repeater.Type != RepeaterType.None && task.PlannedBeginDateTime.HasValue)
                    {
                        var clone = new TaskItem
                        {
                            BlocksTasks = task.BlocksTasks.ToList(),
                            BlockedByTasks = task.BlockedByTasks.ToList(),
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
                                clone.PlannedBeginDateTime.Value.Add(task.PlannedEndDateTime.Value - task.PlannedBeginDateTime.Value);
                        }

                        // Save the cloned task
                        clone.Version = 1;
                        await Storage.Save(clone);
                        clone.SortOrder = DateTime.Now;
                        result.AddOrUpdate(clone.Id, clone);
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
                result.AddOrUpdate(task.Id, task);

                var affectedTasks = await CalculateAndUpdateAvailability(task);
                foreach (var task in affectedTasks)
                {
                    result.AddOrUpdate(task.Id, task);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result.Dict.Values.ToList();
    }
}