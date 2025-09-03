using Polly;
using Polly.Retry;
using System.Collections;
using System.Collections.Generic;
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
                    change.SortOrder = DateTime.Now;
                    await Storage.Save(change);
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
                    newTaskId = change.Id;
                }

                result.AddOrUpdateRange((await CreateParentChildRelation(currentTask, change)).Dict);

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

        await IsCompletedAsync(async () =>
        {
            try
            {
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

    public async Task UpdateTask(TaskItem change)
    {
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                await Storage.Save(change);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
    public async Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        string newTaskId = null;

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    change.Version = 1;
                    await Storage.Save(change);
                    newTaskId = change.Id;
                }

                if (stepParents?.Count > 0)
                {
                    foreach (var parent in stepParents)
                    {
                        var dict = await CreateParentChildRelation(parent, change);
                        result.AddOrUpdateRange(dict.Dict);
                    }
                }
                else
                {
                    change.SortOrder = DateTime.Now;
                    result.AddOrUpdate(change.Id, change);
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
        var task = await Storage.Load(taskId);

        if (task?.Version != 0)
            return task;

        if (task.ContainsTasks is not null)
        {
            foreach (var childTask in task.ContainsTasks)
            {
                var childItem = await Storage.Load(childTask);

                if (childItem != null
                    && !(childItem.ParentTasks ?? new List<string>()).Contains(task.Id))
                {
                    if (childItem.ParentTasks is null)
                        childItem.ParentTasks = new List<string>();
                    childItem.ParentTasks!.Add(task.Id);
                    await Storage.Save(childItem);
                }
            }
        }

        if (task.BlocksTasks is not null)
        {
            foreach (var blockedTask in task.BlocksTasks)
            {
                var blockedItem = await Storage.Load(blockedTask);

                if (blockedItem != null
                    && !(blockedItem.BlockedByTasks ?? new List<string>()).Contains(task.Id))
                {
                    if (blockedItem.BlockedByTasks is null)
                        blockedItem.BlockedByTasks = new List<string>();
                    blockedItem.BlockedByTasks!.Add(task.Id);
                    await Storage.Save(blockedItem);
                }
            }
        }

        task.Version = 1;
        await Storage.Save(task);

        return task;
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

                return true;
            }
            catch
            {
                return false;
            };
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
                    parent.SortOrder = DateTime.Now;
                    await Storage.Save(parent);
                    result.AddOrUpdate(parent.Id, parent);
                }

                if (!(child.ParentTasks ?? new List<string>()).Contains(parent.Id))
                {
                    child.ParentTasks!.Add(parent.Id);
                    child.SortOrder = DateTime.Now;
                    await Storage.Save(child);
                    result.AddOrUpdate(child.Id, child);
                }

                return true;
            }
            catch
            {
                return false;
            };
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
                    blockingTask.SortOrder = DateTime.Now;
                    await Storage.Save(blockingTask);
                    result.AddOrUpdate(blockingTask.Id, blockingTask);
                }

                if (!taskToBlock.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToBlock.BlockedByTasks.Add(blockingTask.Id);
                    taskToBlock.SortOrder = DateTime.Now;
                    await Storage.Save(taskToBlock);
                    result.AddOrUpdate(taskToBlock.Id, taskToBlock);
                }

                return true;
            }
            catch
            {
                return false;
            };
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

                return true;
            }
            catch
            {
                return false;
            };
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
}