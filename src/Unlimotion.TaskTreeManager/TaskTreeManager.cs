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

        //Create
        if (currentTask is null)
        {
            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    change.PrevVersion = false;
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

            return [..result.Dict.Values];            
        }
        //CreateSibling, CreateBlockedSibling
        else
        {
            string newTaskId = null;

            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    if (newTaskId is null)
                    {
                        change.PrevVersion = false;
                        await Storage.Save(change);
                        newTaskId = change.Id;
                        result.AddOrUpdate(change.Id, change);
                    }

                    if ((currentTask.ParentTasks ?? []).Count > 0)
                    {
                        currentTask.ParentTasks.ForEach(async parent =>
                        {
                            var parentModel = await Storage.Load(parent);
                            result.AddOrUpdateRange((
                            await CreateParentChildRelation(parentModel, change)).Dict);
                        });
                    }                       
   
                    if (isBlocked && currentTask != null)
                    {
                        result.AddOrUpdateRange((await CreateBlockingBlockedByRelation(change, currentTask)).Dict);                        
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });


            return [.. result.Dict.Values];
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
                    change.PrevVersion = false;
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

        return [.. result.Dict.Values];
    }
    public async Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage = true)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                //удалить во всех детях ссылки в Parents на удаляемый таск
                               
                if (change.ContainsTasks.Any())
                {
                    change.ContainsTasks.ForEach(async child =>
                    {
                        var childItem = await Storage.Load(child);
                        result.AddOrUpdateRange(
                        (await BreakParentChildRelation(change, childItem)).Dict);
                    });
                }                       
                
                //удалить во всех grandParents ссылки в Contains на удаляемый таск
                if (change.ParentTasks.Any())
                {
                    change.ParentTasks.ForEach(async parent =>
                    {
                        var parentItem = await Storage.Load(parent);
                        result.AddOrUpdateRange(
                        (await BreakParentChildRelation(parentItem, change)).Dict);
                    });
                }

                //удалить во всех блокирующих тасках ссылку на удаляемый таск
                if (change.BlockedByTasks.Any())
                {
                    change.BlockedByTasks.ForEach(async blocker =>
                    {
                        var blockerItem = await Storage.Load(blocker);
                        result.AddOrUpdateRange(
                        (await BreakBlockingBlockedByRelation(change, blockerItem)).Dict);
                    });
                }

                //удалить во всех блокируемых тасках ссылку на удаляемый таск 
                if (change.BlocksTasks.Any())
                {
                    change.BlocksTasks.ForEach(async blocked =>
                    {
                        var blockedItem = await Storage.Load(blocked);
                        result.AddOrUpdateRange(
                        (await BreakBlockingBlockedByRelation(blockedItem, change)).Dict);
                    });
                }

                //удалить сам таск из БД
                if (deleteInStorage) await Storage.Remove(change.Id);                

                return true;
            }
            catch
            {
                return false;
            };
        });

        return [.. result.Dict.Values];
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
                    change.PrevVersion = false;
                    await Storage.Save(change);
                    newTaskId = change.Id;
                }

                stepParents.ForEach(async parent => result.AddOrUpdateRange(
                (await CreateParentChildRelation(parent, change)).Dict));                                

                return true;
            }
            catch
            {
                return false;
            }
        });

        return [.. result.Dict.Values];
    }
    public async Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        result.AddOrUpdateRange((await CreateParentChildRelation(additionalParent, change)).Dict);

        return [.. result.Dict.Values];
    }
    public async Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        if (prevParent is not null)
        {
            result.AddOrUpdateRange((await BreakParentChildRelation(prevParent, change)).Dict);
        }
        result.AddOrUpdateRange((await CreateParentChildRelation(newParent, change)).Dict);

        return [.. result.Dict.Values];
    }
    public async Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        result.AddOrUpdateRange((await BreakBlockingBlockedByRelation(taskToUnblock, blockingTask)).Dict);

        return [.. result.Dict.Values];
    }

    public async Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();

        result.AddOrUpdateRange((await CreateBlockingBlockedByRelation(taskToBlock, blockingTask)).Dict);

        return [.. result.Dict.Values];
    }

    public async Task<TaskItem> LoadTask(string taskId)
    {
        var task = await Storage.Load(taskId);

        if (!task.PrevVersion) 
            return task;

        if (task.ContainsTasks is not null)
        {
           foreach (var childTask in task.ContainsTasks)
           {
               var childItem = await Storage.Load(childTask);

               if (childItem != null 
                    && !(childItem.ParentTasks ?? []).Contains(task.Id))
               {
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

              if (!(blockedItem.BlockedByTasks ?? []).Contains(task.Id))
              {
                 blockedItem.BlockedByTasks!.Add(task.Id);
                 await Storage.Save(blockedItem);
              }
           }
        }

        task.PrevVersion = false;
        await Storage.Save(task);

        return task;
    }

    public async Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child)
    {
        var result = new AutoUpdatingDictionary<string, TaskItem>();
        
        result.AddOrUpdateRange((await BreakParentChildRelation(parent, child)).Dict);

        return [.. result.Dict.Values];
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

                if ((child.ParentTasks ?? []).Contains(parent.Id))
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

               if (!(child.ParentTasks ?? []).Contains(parent.Id))
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


