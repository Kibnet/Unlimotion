using Polly;
using Polly.Retry;
using Unlimotion.Server.Domain;

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
        var result = new List<TaskItem>();

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
                    result.Add(change);

                    return true;
                }
                catch
                {
                    return false;
                }
            });
            return result;
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
                    }

                    foreach (var parent in currentTask.ParentTasks)
                    {
                        change.ParentTasks.Add(parent);

                        TaskItem parentTask = await Storage.Load(parent);
                        if (!parentTask.ContainsTasks.Contains(newTaskId))
                        {
                            parentTask.ContainsTasks.Add(newTaskId);
                            parentTask.SortOrder = DateTime.Now;
                            await Storage.Save(parentTask);
                            result.Add(parentTask);
                        }
                    }

                    if (isBlocked)
                    {
                        TaskItem currentTaskItem = await Storage.Load(currentTask.Id);
                        if (!currentTaskItem.BlocksTasks.Contains(newTaskId))
                            currentTask.BlocksTasks.Add(newTaskId);
                        currentTask.SortOrder = DateTime.Now;
                        await Storage.Save(currentTask);
                        result.Add(currentTask);

                        change.BlockedByTasks.Add(currentTask.Id);
                    }

                    change.SortOrder = DateTime.Now;
                    await Storage.Save(change);
                    result.Add(change);

                    return true;
                }
                catch
                {
                    return false;
                }
            });


            return result;
        }
    }
    public async Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask)
    {
        var result = new List<TaskItem>();
        string newTaskId = null;

        //CreateInner
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    change.PrevVersion = false;
                    change.ParentTasks.Add(currentTask.Id);
                    await Storage.Save(change);
                    newTaskId = change.Id;
                }

                currentTask.ContainsTasks.Add(change.Id);
                currentTask.SortOrder = DateTime.Now;
                await Storage.Save(currentTask);

                change.SortOrder = DateTime.Now;
                await Storage.Save(change);

                result.Add(change);
                result.Add(currentTask);

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result;
    }
    public async Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage = true)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                //удалить во всех детях ссылки в Parents на удаляемый таск
                var deletingTask = (deleteInStorage) ? await Storage.Load(change.Id) : change;

                if (deletingTask is not null)
                {
                    if (deletingTask.ContainsTasks.Any())
                    {
                        foreach (var child in deletingTask.ContainsTasks)
                        {
                            var childTask = await Storage.Load(child);
                            if (childTask.ParentTasks.Contains(deletingTask.Id))
                            {
                                childTask.ParentTasks.Remove(deletingTask.Id);
                                await Storage.Save(childTask);
                                result.Add(childTask);
                            }
                        }
                    }
                    //удалить во всех grandParents ссылки в Contains на удаляемый таск
                    if (deletingTask.ParentTasks.Any())
                    {
                        foreach (var parent in deletingTask.ParentTasks)
                        {
                            var parentTask = await Storage.Load(parent);
                            if (parentTask.ContainsTasks.Contains(deletingTask.Id))
                            {
                                parentTask.ContainsTasks.Remove(deletingTask.Id);
                                await Storage.Save(parentTask);
                                result.Add(parentTask);
                            }
                        }
                    }

                    //удалить во всех блокирующих тасках ссылку на удаляемый таск
                    if (deletingTask.BlockedByTasks.Any())
                    {
                        foreach (var blocker in deletingTask.BlockedByTasks)
                        {
                            var blockerItem = await Storage.Load(blocker);
                            blockerItem.BlocksTasks.Remove(deletingTask.Id);
                            await Storage.Save(blockerItem);
                            result.Add(blockerItem);
                        }
                    }

                    //удалить во всех блокируемых тасках ссылку на удаляемый таск 
                    if (deletingTask.BlocksTasks.Any())
                    {
                        foreach (var blocked in deletingTask.BlocksTasks)
                        {
                            var blockedItem = await Storage.Load(blocked);
                            blockedItem.BlockedByTasks.Remove(deletingTask.Id);
                            await Storage.Save(blockedItem);
                            result.Add(blockedItem);
                        }
                    }

                    //удалить сам таск из БД
                    if (deleteInStorage) await Storage.Remove(change.Id);
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
        var result = new List<TaskItem>();
        string newTaskId = null;

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    change.PrevVersion = false;
                    change.SortOrder = DateTime.Now;
                    await Storage.Save(change);
                    newTaskId = change.Id;
                }

                foreach (var parent in stepParents)
                {
                    var parentTask = await Storage.Load(parent.Id);

                    if (!parentTask.ContainsTasks.Contains(newTaskId))
                    {
                        parentTask.ContainsTasks.Add(newTaskId);
                        parentTask.SortOrder = DateTime.Now;
                        await Storage.Save(parentTask);
                        result.Add(parentTask);
                    }

                    var changeTask = await Storage.Load(change.Id);

                    if (!changeTask.ParentTasks.Contains(parent.Id))
                    {
                        changeTask.ParentTasks.Add(parent.Id);                        
                    }
                    changeTask.SortOrder = DateTime.Now;
                    await Storage.Save(changeTask);
                    result.Add(changeTask);
                }                

                return true;
            }
            catch
            {
                return false;
            }
        });

        return result;
    }
    public async Task<List<TaskItem>> CopyTaskInto(TaskItem change, TaskItem additionalParent)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                //add new parent to task
                change = await Storage.Load(change.Id);
                if (!change.ParentTasks.Contains(additionalParent.Id))
                {
                    change.ParentTasks.Add(additionalParent.Id);
                    await Storage.Save(change);
                }
                result.Add(change);

                //add task to new parent
                if (!additionalParent.ContainsTasks.Contains(change.Id))
                {
                    additionalParent.ContainsTasks.Add(change.Id);
                    await Storage.Save(additionalParent);
                }
                result.Add(additionalParent);

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        });

        return result;
    }
    public async Task<List<TaskItem>> MoveTaskInto(TaskItem change, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                //удаляем таск из prevParent.Contains
                if (prevParent is not null)
                {
                    var prevParentTask = await Storage.Load(prevParent.Id);
                    if (prevParentTask.ContainsTasks.Contains(change.Id))
                    {
                        prevParentTask.ContainsTasks.Remove(change.Id);
                        await Storage.Save(prevParentTask);
                        result.Add(prevParentTask);
                    }
                }

                //добавляем таск в newParent.Contains
                newParent = await Storage.Load(newParent.Id);
                if (!newParent.ParentTasks.Contains(change.Id))
                {
                    newParent.ContainsTasks.Add(change.Id);
                    await Storage.Save(newParent);

                }
                result.Add(newParent);

                //удаляем prevParent и добавляем newParent в таск
                change = await Storage.Load(change.Id);

                if (prevParent is not null)
                    change.ParentTasks.Remove(prevParent.Id);

                if (!change.ParentTasks.Contains(newParent.Id))
                {
                    change.ParentTasks.Add(newParent.Id);
                }

                await Storage.Save(change);
                result.Add(change);

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        });

        return result;
    }
    public async Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                var blockingTaskItem = await Storage.Load(blockingTask.Id);
                if (blockingTaskItem.BlocksTasks.Contains(taskToUnblock.Id))
                {
                    blockingTask.BlocksTasks.Remove(taskToUnblock.Id);
                    await Storage.Save(blockingTask);
                    result.Add(blockingTask);
                }

                var taskToUnblockItem = await Storage.Load(taskToUnblock.Id);
                if (taskToUnblockItem.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToUnblock.BlockedByTasks.Remove(blockingTask.Id);
                    await Storage.Save(taskToUnblock);
                    result.Add(taskToUnblock);
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

    public async Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                var blockingTaskItem = await Storage.Load(blockingTask.Id);
                if (!blockingTaskItem.BlocksTasks.Contains(taskToBlock.Id))
                {
                    blockingTask.BlocksTasks.Add(taskToBlock.Id);
                    await Storage.Save(blockingTask);
                    result.Add(blockingTask);
                }

                var taskToBlockItem = await Storage.Load(taskToBlock.Id);
                if (!taskToBlockItem.BlockedByTasks.Contains(blockingTask.Id))
                {
                    taskToBlock.BlockedByTasks.Add(blockingTask.Id);
                    await Storage.Save(taskToBlock);
                    result.Add(taskToBlock);
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

               if (!(childItem.ParentTasks ?? []).Contains(task.Id))
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


