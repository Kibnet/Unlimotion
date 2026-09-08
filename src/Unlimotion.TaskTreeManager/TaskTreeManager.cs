using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public class TaskTreeManager
{
    private readonly AsyncLocal<int> _mutationLockDepth = new();

    public IStorage Storage { get; init; }
    public Func<TaskItem, string>? StatusAuthorProvider { get; set; }

    public TaskTreeManager(IStorage storage)
    {
        Storage = storage;
    }

    private string ResolveStatusAuthor(TaskItem task) =>
        TaskItem.NormalizeAuthor(StatusAuthorProvider?.Invoke(task) ?? task.UserId ?? "local-user");

    private bool ShouldAcquireMutationLock =>
        _mutationLockDepth.Value == 0 && Storage is ITaskGraphWriteLock;

    private async Task<T> ExecuteWithMutationLockAsync<T>(Func<Task<T>> operation)
    {
        if (Storage is not ITaskGraphWriteLock writeLock || _mutationLockDepth.Value > 0)
        {
            return await operation();
        }

        return await writeLock.WithWriteLockAsync(async () =>
        {
            _mutationLockDepth.Value++;
            try
            {
                return await operation();
            }
            finally
            {
                _mutationLockDepth.Value--;
            }
        });
    }

    public async Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask = null, bool isBlocked = false)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => AddTask(change, currentTask, isBlocked));
        }

        var result = new Dictionary<string, TaskItem>();

        // Create
        if (currentTask is null)
        {
            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    change.Version = 1;
                    change.UpdatedDateTime ??= change.CreatedDateTime;
                    change.EnsureStatusHistory(ResolveStatusAuthor(change));
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
                        change.UpdatedDateTime ??= change.CreatedDateTime;
                        change.EnsureStatusHistory(ResolveStatusAuthor(change));
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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => AddChildTask(change, currentTask));
        }

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
                    change.Wanted = currentTask.Wanted;
                    change.UpdatedDateTime ??= change.CreatedDateTime;
                    change.EnsureStatusHistory(ResolveStatusAuthor(change));
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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => DeleteTask(change, deleteInStorage));
        }

        if (!deleteInStorage)
        {
            return await DeleteSingleTask(change, false);
        }

        var tasksToDelete = await GetTaskAndContainedTasksForDelete(change);
        if (tasksToDelete.Count <= 1)
        {
            return await DeleteSingleTask(change);
        }

        var result = new Dictionary<string, TaskItem>();
        var deletedTaskIds = tasksToDelete
            .Select(static task => task.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var taskToDelete in tasksToDelete.AsEnumerable().Reverse())
        {
            result.AddOrUpdateRange(
                (await DeleteSingleTask(taskToDelete))
                .Where(task => !deletedTaskIds.Contains(task.Id)));
            result.Remove(taskToDelete.Id);
        }

        return result.Values.ToList();
    }

    private async Task<List<TaskItem>> GetTaskAndContainedTasksForDelete(TaskItem change)
    {
        var result = new List<TaskItem>();
        var visitedTaskIds = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<TaskItem>();
        queue.Enqueue(change);

        while (queue.TryDequeue(out var current))
        {
            if (current == null || string.IsNullOrWhiteSpace(current.Id) || !visitedTaskIds.Add(current.Id))
            {
                continue;
            }

            var currentFromStorage = await Storage.Load(current.Id) ?? current;
            result.Add(currentFromStorage);

            if (currentFromStorage.ContainsTasks?.Any() != true)
            {
                continue;
            }

            foreach (var childId in currentFromStorage.ContainsTasks)
            {
                if (string.IsNullOrWhiteSpace(childId))
                {
                    continue;
                }

                var child = await Storage.Load(childId);
                queue.Enqueue(child ?? new TaskItem { Id = childId });
            }
        }

        return result;
    }

    private async Task<List<TaskItem>> DeleteSingleTask(TaskItem change, bool deleteInStorage = true)
    {
        var result = new Dictionary<string, TaskItem>();
        var taskIdsToRecalculate = new HashSet<string>(StringComparer.Ordinal);

        await IsCompletedAsync(async () =>
        {
            try
            {
                // Collect tasks that need recalculation before breaking relations
                if (change.ParentTasks.Any())
                {
                    foreach (var parentId in change.ParentTasks)
                    {
                        if (!string.IsNullOrWhiteSpace(parentId))
                        {
                            taskIdsToRecalculate.Add(parentId);
                        }
                    }
                }

                if (change.ContainsTasks?.Any() == true)
                {
                    foreach (var childId in change.ContainsTasks)
                    {
                        if (!string.IsNullOrWhiteSpace(childId))
                        {
                            taskIdsToRecalculate.Add(childId);
                        }
                    }
                }

                if (change.BlocksTasks.Any())
                {
                    foreach (var blockedId in change.BlocksTasks)
                    {
                        if (!string.IsNullOrWhiteSpace(blockedId))
                        {
                            taskIdsToRecalculate.Add(blockedId);
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

                // Удаление самой задачи
                if (deleteInStorage)
                {
                    // В случае разрыва отношений (задача/подзадача), удаляемая таска может попасть в результат
                    // в этом случае файл после удаления создатся снова.
                    // Удаляем из результата
                    result.Remove(change.Id);
                    await Storage.Remove(change.Id);
                }

                // Recalculate availability after relations are updated using freshly loaded
                // tasks so detached storage implementations see the final graph state.
                foreach (var taskIdToRecalculate in taskIdsToRecalculate)
                {
                    var taskToRecalculate = await Storage.Load(taskIdToRecalculate);
                    if (taskToRecalculate != null)
                    {
                        result.AddOrUpdateRange(
                            await CalculateAndUpdateAvailability(taskToRecalculate));
                    }
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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => UpdateTask(change));
        }

        var result = new Dictionary<string, TaskItem>();
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                // Load the existing task to check if Status changed
                var existingTask = await Storage.Load(change.Id);
                var isStatusChanged = existingTask?.Status != change.Status;

                if (isStatusChanged)
                {
                    result.AddOrUpdateRange(await HandleTaskStatusChange(change, existingTask));
                }
                else
                {
                    change.EnsureStatusHistory(ResolveStatusAuthor(change));
                    ApplyAutomaticInProgressRollbackIfNeeded(change);
                    change.UpdatedDateTime = GetNextUpdatedDateTime(change);
                    await Storage.Save(change);
                    result.AddOrUpdate(change);
                    result.AddOrUpdateRange(await CalculateAndUpdateAvailability(change));
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

    internal async Task<List<TaskItem>> UpdateTaskWithinExistingMutationLockAsync(TaskItem change)
    {
        _mutationLockDepth.Value++;
        try
        {
            return await UpdateTask(change);
        }
        finally
        {
            _mutationLockDepth.Value--;
        }
    }

    public async Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => CloneTask(change, stepParents));
        }

        var result = new Dictionary<string, TaskItem>();

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
                    IsGoal = change.IsGoal,
                    AreaIds = change.AreaIds?.ToList() ?? [],
                    Version = 1,
                };
                clone.EnsureStatusHistory(ResolveStatusAuthor(clone));

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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => AddNewParentToTask(change, additionalParent));
        }

        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await CreateParentChildRelation(additionalParent, change));

        result.AddOrUpdateRange(
            await CalculateAndUpdateAvailability(change));

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => MoveTaskToNewParent(change, newParent, prevParent));
        }

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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => UnblockTask(taskToUnblock, blockingTask));
        }

        var result = new Dictionary<string, TaskItem>();

        result.AddOrUpdateRange(
            await BreakBlockingBlockedByRelation(taskToUnblock, blockingTask));

        return result.Values.ToList();
    }

    public async Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => BlockTask(taskToBlock, blockingTask));
        }

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
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => DeleteParentChildRelation(parent, child));
        }

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
                result.AddOrUpdateRange(
                    await CalculateAndUpdateAvailability(child));

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

                if (taskToBlock != null && blockingTask != null && !taskToBlock.BlockedByTasks.Contains(blockingTask.Id))
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

                if (taskToUnblock != null && blockingTask != null && taskToUnblock.BlockedByTasks.Contains(blockingTask.Id))
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

    private async Task<bool> IsCompletedAsync(Func<Task<bool>> task)
    {
        var res = await task.Invoke();

        if (!res)
            throw new TimeoutException(
                "Task graph mutation failed. It was not retried because the operation may have been partially persisted.");
        return (res);
    }

    public async Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => CalculateAndUpdateAvailability(task));
        }

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

        // Availability combines local child completion with blockers inherited
        // through the whole parent chain.
        var allContainsCompleted = await AreContainedTasksCompleted(task);
        var hasIncompleteBlockerInTaskOrAncestors =
            await HasIncompleteBlockerInTaskOrAncestors(task, new HashSet<string>(StringComparer.Ordinal));
        bool newIsCanBeCompleted = allContainsCompleted && !hasIncompleteBlockerInTaskOrAncestors;

        var oldIsCanBeCompleted = task.IsCanBeCompleted;
        var oldUnlockedDateTime = task.UnlockedDateTime;

        DateTimeOffset? newUnlockedDateTime = null;

        // Manage UnlockedDateTime based on availability changes
        if (newIsCanBeCompleted && task.UnlockedDateTime == null)
        {
            // Task became available - set UnlockedDateTime
            newUnlockedDateTime = DateTimeOffset.UtcNow;
        }
        else if (!newIsCanBeCompleted)
        {
            // Task became blocked - clear UnlockedDateTime
            newUnlockedDateTime = null;
        }
        else
        {
            newUnlockedDateTime = task.UnlockedDateTime;
        }

        if (oldIsCanBeCompleted != newIsCanBeCompleted || oldUnlockedDateTime != newUnlockedDateTime)
        {
            // Update IsCanBeCompleted only when it changed to avoid unnecessary disk writes.
            task.IsCanBeCompleted = newIsCanBeCompleted;
            task.UnlockedDateTime = newUnlockedDateTime;
            ApplyAutomaticInProgressRollbackIfNeeded(task);
            task.UpdatedDateTime = GetNextUpdatedDateTime(task);
            await Storage.Save(task);
            result.AddOrUpdate(task);
        }

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

        // Collect all contained tasks because inherited blockers propagate from
        // parents down to all descendants.
        if (task.ContainsTasks?.Any() == true)
        {
            foreach (var childId in task.ContainsTasks)
            {
                if (!processedIds.Contains(childId))
                {
                    var childTask = await Storage.Load(childId);
                    if (childTask != null)
                    {
                        affectedTasks.Add(childTask);
                        processedIds.Add(childId);
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

    private async Task<bool> AreContainedTasksCompleted(TaskItem task)
    {
        if (task.ContainsTasks?.Any() != true)
        {
            return true;
        }

        foreach (var childId in task.ContainsTasks)
        {
            var childTask = await Storage.Load(childId);
            if (childTask != null && childTask.Status.IsIncompleteForAvailability())
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> HasIncompleteBlockerInTaskOrAncestors(
        TaskItem task,
        ISet<string> visitedTaskIds)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Id) || !visitedTaskIds.Add(task.Id))
        {
            return false;
        }

        if (await HasIncompleteDirectBlocker(task))
        {
            return true;
        }

        if (task.ParentTasks?.Any() != true)
        {
            return false;
        }

        foreach (var parentId in task.ParentTasks)
        {
            var parentTask = await Storage.Load(parentId);
            if (parentTask != null &&
                await HasIncompleteBlockerInTaskOrAncestors(parentTask, visitedTaskIds))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> HasIncompleteDirectBlocker(TaskItem task)
    {
        if (task.BlockedByTasks?.Any() != true)
        {
            return false;
        }

        foreach (var blockerId in task.BlockedByTasks)
        {
            var blockerTask = await Storage.Load(blockerId);
            if (blockerTask != null && blockerTask.Status.IsIncompleteForAvailability())
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> CanTransitionToStatus(TaskItem task, DomainTaskStatus targetStatus)
    {
        var tasks = new List<TaskItem>();
        await foreach (var storedTask in Storage.GetAll())
        {
            if (!string.Equals(storedTask.Id, task.Id, StringComparison.Ordinal))
            {
                tasks.Add(storedTask);
            }
        }

        tasks.Add(task);
        return new TaskAvailabilityService(tasks)
            .EvaluateStatusTransition(task, targetStatus)
            .Allowed;
    }

    private static bool HasFuturePlannedBegin(TaskItem task) =>
        task.PlannedBeginDateTime.HasValue &&
        task.PlannedBeginDateTime.Value > DateTimeOffset.UtcNow;

    private static void ApplyAutomaticInProgressRollbackIfNeeded(TaskItem task)
    {
        if (task.Status != DomainTaskStatus.InProgress)
        {
            return;
        }

        if (!task.IsCanBeCompleted || HasFuturePlannedBegin(task))
        {
            task.SetStatus(DomainTaskStatus.Prepared, DateTimeOffset.UtcNow, "System");
        }
    }

    private async Task<List<TaskItem>> CreateNextOccurrenceSubtree(TaskItem root, DateTimeOffset now)
    {
        if (root.Repeater == null || !root.PlannedBeginDateTime.HasValue)
        {
            return [];
        }

        var sources = new Dictionary<string, TaskItem>(StringComparer.Ordinal)
        {
            [root.Id] = TaskItemSnapshot.Clone(root)
        };
        var sourceOrder = new List<string> { root.Id };
        var queue = new Queue<string>();
        queue.Enqueue(root.Id);
        while (queue.Count > 0)
        {
            var sourceId = queue.Dequeue();
            var source = sources[sourceId];
            EnsureNoDuplicateRelationIds(source, source.ContainsTasks, nameof(TaskItem.ContainsTasks));
            foreach (var childId in source.ContainsTasks ?? [])
            {
                if (sources.ContainsKey(childId))
                {
                    continue;
                }

                var child = await Storage.Load(childId) ??
                            throw new InvalidOperationException(
                                $"Cannot clone repeating task '{root.Id}' because contained task '{childId}' is missing.");
                sources.Add(childId, TaskItemSnapshot.Clone(child));
                sourceOrder.Add(childId);
                queue.Enqueue(childId);
            }
        }

        EnsureAcyclicContainment(root.Id, sources);
        var nextRootBegin = root.Repeater.GetNextOccurrence(root.PlannedBeginDateTime.Value);
        var dateOffset = nextRootBegin - root.PlannedBeginDateTime.Value;
        var clonesBySourceId = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        foreach (var sourceId in sourceOrder)
        {
            clonesBySourceId[sourceId] = CreateOccurrenceClone(
                sources[sourceId],
                isRoot: string.Equals(sourceId, root.Id, StringComparison.Ordinal),
                dateOffset,
                now);
        }

        foreach (var sourceId in sourceOrder)
        {
            var source = sources[sourceId];
            var clone = clonesBySourceId[sourceId];
            foreach (var childId in source.ContainsTasks ?? [])
            {
                var childClone = clonesBySourceId[childId];
                clone.ContainsTasks.Add(childClone.Id);
                childClone.ParentTasks.Add(clone.Id);
            }

            foreach (var blockedId in source.BlocksTasks ?? [])
            {
                if (!clonesBySourceId.TryGetValue(blockedId, out var blockedClone))
                {
                    continue;
                }

                AddRelationId(clone.BlocksTasks, blockedClone.Id);
                AddRelationId(blockedClone.BlockedByTasks, clone.Id);
            }

            foreach (var blockerId in source.BlockedByTasks ?? [])
            {
                if (!clonesBySourceId.TryGetValue(blockerId, out var blockerClone))
                {
                    continue;
                }

                AddRelationId(clone.BlockedByTasks, blockerClone.Id);
                AddRelationId(blockerClone.BlocksTasks, clone.Id);
            }
        }

        foreach (var sourceId in sourceOrder.AsEnumerable().Reverse())
        {
            await Storage.Save(clonesBySourceId[sourceId]);
        }

        var result = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        result.AddOrUpdateRange(clonesBySourceId.Values);
        result.AddOrUpdateRange(await CalculateAndUpdateAvailability(clonesBySourceId[root.Id]));
        return result.Values.ToList();
    }

    private static TaskItem CreateOccurrenceClone(
        TaskItem source,
        bool isRoot,
        TimeSpan dateOffset,
        DateTimeOffset now)
    {
        var snapshot = TaskItemSnapshot.Clone(source);
        var clone = new TaskItem
        {
            Id = Guid.NewGuid().ToString(),
            UserId = snapshot.UserId,
            Title = snapshot.Title,
            Description = snapshot.Description,
            Status = isRoot ? DomainTaskStatus.Prepared : DomainTaskStatus.NotReady,
            StatusHistory = [],
            CompletionCriteria = snapshot.CompletionCriteria
                .Where(static criterion => criterion != null)
                .Select(static criterion => new TaskCompletionCriterion
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = criterion.Text,
                    IsSatisfied = false,
                    ExtensionData = criterion.ExtensionData?.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value == null ? null! : pair.Value.DeepClone())
                })
                .ToList(),
            IsCanBeCompleted = true,
            CreatedDateTime = now,
            UpdatedDateTime = null,
            UnlockedDateTime = null,
            PlannedBeginDateTime = snapshot.PlannedBeginDateTime?.Add(dateOffset),
            PlannedEndDateTime = snapshot.PlannedEndDateTime?.Add(dateOffset),
            PlannedDuration = snapshot.PlannedDuration,
            ContainsTasks = [],
            ParentTasks = [],
            BlocksTasks = [],
            BlockedByTasks = [],
            Repeater = snapshot.Repeater,
            Importance = snapshot.Importance,
            Wanted = snapshot.Wanted,
            IsGoal = snapshot.IsGoal,
            AreaIds = snapshot.AreaIds?.ToList() ?? [],
            Version = 1,
            ExtensionData = snapshot.ExtensionData
        };
        clone.EnsureStatusHistory("System");
        return clone;
    }

    private static void EnsureNoDuplicateRelationIds(
        TaskItem source,
        IEnumerable<string>? relationIds,
        string relationName)
    {
        var duplicate = relationIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(static id => id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new InvalidOperationException(
                $"Cannot clone task '{source.Id}': {relationName} contains duplicate task '{duplicate.Key}'.");
        }
    }

    private static void EnsureAcyclicContainment(
        string rootId,
        IReadOnlyDictionary<string, TaskItem> sources)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal) { [rootId] = 1 };
        var stack = new Stack<OccurrenceTraversalFrame>();
        stack.Push(CreateOccurrenceFrame(sources[rootId]));
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.NextChildIndex >= frame.ChildIds.Length)
            {
                states[frame.TaskId] = 2;
                continue;
            }

            var childId = frame.ChildIds[frame.NextChildIndex];
            stack.Push(frame with { NextChildIndex = frame.NextChildIndex + 1 });
            if (!states.TryGetValue(childId, out var childState))
            {
                states[childId] = 1;
                stack.Push(CreateOccurrenceFrame(sources[childId]));
                continue;
            }

            if (childState == 1)
            {
                throw new InvalidOperationException(
                    $"Cannot clone repeating task '{rootId}' because containment has a cycle through '{childId}'.");
            }
        }
    }

    private static OccurrenceTraversalFrame CreateOccurrenceFrame(TaskItem task) => new(
        task.Id,
        task.ContainsTasks?.ToArray() ?? Array.Empty<string>(),
        0);

    private static void AddRelationId(ICollection<string> relation, string id)
    {
        if (!relation.Contains(id, StringComparer.Ordinal))
        {
            relation.Add(id);
        }
    }

    private readonly record struct OccurrenceTraversalFrame(
        string TaskId,
        string[] ChildIds,
        int NextChildIndex);

    /// <summary>
    /// Handles logic when a task's Status property changes
    /// </summary>
    /// <param name="task">The task that has changed</param>
    /// <param name="existingTask">The existing persisted task before the status change.</param>
    /// <returns>List of affected tasks</returns>
    public async Task<List<TaskItem>> HandleTaskStatusChange(TaskItem task, TaskItem? existingTask = null)
    {
        if (ShouldAcquireMutationLock)
        {
            return await ExecuteWithMutationLockAsync(() => HandleTaskStatusChange(task, existingTask));
        }

        var result = new Dictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                existingTask ??= await Storage.Load(task.Id);
                var requestedStatus = task.Status;
                var author = ResolveStatusAuthor(task);
                var now = DateTimeOffset.UtcNow;

                if (existingTask != null)
                {
                    task.StatusHistory = existingTask.StatusHistory?.ToList() ?? new List<TaskStatusHistoryEntry>();
                    task.Status = existingTask.Status;
                }
                else
                {
                    task.Status = DomainTaskStatus.NotReady;
                }

                if (!await CanTransitionToStatus(task, requestedStatus))
                {
                    task.UpdatedDateTime = GetNextUpdatedDateTime(task);
                    await Storage.Save(task);
                    result.AddOrUpdate(task);
                    return true;
                }

                task.SetStatus(requestedStatus, now, author);

                if (task.Status == DomainTaskStatus.Completed)
                {
                    // Handle repeater logic
                    if (task.Repeater != null && task.Repeater.Type != RepeaterType.None &&
                        task.PlannedBeginDateTime.HasValue)
                    {
                        result.AddOrUpdateRange(await CreateNextOccurrenceSubtree(task, now));
                    }
                }

                ApplyAutomaticInProgressRollbackIfNeeded(task);

                // Save the updated task
                task.UpdatedDateTime = GetNextUpdatedDateTime(task);
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

    private static DateTimeOffset GetNextUpdatedDateTime(TaskItem task)
    {
        var now = DateTimeOffset.UtcNow;
        if (task.UpdatedDateTime.HasValue && now <= task.UpdatedDateTime.Value)
        {
            return task.UpdatedDateTime.Value.AddSeconds(1);
        }

        return now;
    }
}
