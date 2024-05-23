using DynamicData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using System.Linq;

namespace Unlimotion.ViewModel;

public class TaskTreeManager
{
    public async Task<List<TaskItem>> AddTask(TaskItem change, ITaskStorage taskStorage, TaskItem? currentTask = null)
    {
        var result = new List<TaskItem>();

        //Create
        if (currentTask is null)
        {
            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    await taskStorage.Save(change);
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
        //CreateSibling
        else
        {
            string newTaskId = null;

            await IsCompletedAsync(async Task<bool> () =>
            {
                try
                {
                    if (newTaskId is null)
                    {
                        await taskStorage.Save(change);
                        newTaskId = change.Id;
                    }
                    
                    foreach (var parent in currentTask.ParentTasks)
                    {
                        change.ParentTasks.Add(parent);

                        TaskItem parentTask = await taskStorage.Load(parent);
                        if (!parentTask.ContainsTasks.Contains(newTaskId))
                        {
                            parentTask.ContainsTasks.Add(newTaskId);
                            await taskStorage.Save(parentTask);
                            result.Add(parentTask);
                        }                        
                    }

                    await taskStorage.Save(change);
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
    public async Task<List<TaskItem>> AddChildTask(TaskItem change, ITaskStorage taskStorage, TaskItem currentTask)
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
                    change.ParentTasks.Add(currentTask.Id);
                    await taskStorage.Save(change);
                    newTaskId = change.Id;
                }                

                currentTask.ContainsTasks.Add(change.Id);
                await taskStorage.Save(currentTask);

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
    public async Task<List<TaskItem>> DeleteTask(TaskItem change, ITaskStorage taskStorage)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async () =>
        {
            try
            {
                //удалить во всех детях ссылки в Parents на удаляемый таск
                var deletingTask = await taskStorage.Load(change.Id);

                if (deletingTask is not null)
                {
                    if (deletingTask.ContainsTasks.Any())
                    {
                        foreach (var child in deletingTask.ContainsTasks)
                        {
                            var childTask = await taskStorage.Load(child);
                            if (childTask.ParentTasks.Contains(deletingTask.Id))
                            {
                                childTask.ParentTasks.Remove(deletingTask.Id);
                                await taskStorage.Save(childTask);
                                result.Add(childTask);
                            }
                        }
                    }
                    //удалить во всех grandParents ссылки в Contains на удаляемый таск
                    if (deletingTask.ParentTasks.Any())
                    {
                        foreach (var parent in deletingTask.ParentTasks)
                        {
                            var parentTask = await taskStorage.Load(parent);
                            if (parentTask.ContainsTasks.Contains(deletingTask.Id))
                            {
                                parentTask.ContainsTasks.Remove(deletingTask.Id);
                                await taskStorage.Save(parentTask);
                                result.Add(parentTask);
                            }
                        }
                    }
                    //удалить сам таск из БД
                    await taskStorage.Remove(change.Id);
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
    public async Task UpdateTask(TaskItem change, ITaskStorage taskStorage)
    {
        await IsCompletedAsync(async Task<bool>() =>
        {
            try
            {
                await taskStorage.Save(change);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
    public async Task<List<TaskItem>> CloneTask(TaskItem change, ITaskStorage taskStorage, List<TaskItem> stepParents)
    {
        var result = new List<TaskItem>();
        string newTaskId = null; 

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                if (newTaskId is null)
                {
                    await taskStorage.Save(change);
                    newTaskId = change.Id;
                }                

                foreach (var parent in stepParents)
                {
                    var parentTask = await taskStorage.Load(parent.Id);

                    if (!parentTask.ContainsTasks.Contains(newTaskId))
                    {
                        parentTask.ContainsTasks.Add(newTaskId);
                        await taskStorage.Save(parentTask);    
                        result.Add(parentTask);
                    }

                    var changeTask = await taskStorage.Load(change.Id);

                    if (!changeTask.ParentTasks.Contains(parent.Id))
                    {
                        changeTask.ParentTasks.Add(parent.Id);
                        await taskStorage.Save(changeTask);
                        result.Add(changeTask);
                    }                    
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
    public async Task<List<TaskItem>> CopyTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItem additionalParent)
    {
        var result = new List<TaskItem>();
        
        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                //add new parent to task
                change = await taskStorage.Load(change.Id);
                if (!change.ParentTasks.Contains(additionalParent.Id))
                {
                    change.ParentTasks.Add(additionalParent.Id);
                    await taskStorage.Save(change);
                }
                result.Add(change);

                //add task to new parent
                if (!additionalParent.ContainsTasks.Contains(change.Id))
                {
                    additionalParent.ContainsTasks.Add(change.Id);
                    await taskStorage.Save(additionalParent);
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
    public async Task<List<TaskItem>> MoveTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new List<TaskItem>();

        await IsCompletedAsync(async Task<bool> () =>
        {
            try
            {
                //удаляем таск из prevParent.Contains
                if (prevParent is not null)
                {
                    var prevParentTask = await taskStorage.Load(prevParent.Id);
                    if (prevParentTask.ContainsTasks.Contains(change.Id))
                    {
                        prevParentTask.ContainsTasks.Remove(change.Id);
                        await taskStorage.Save(prevParentTask);
                        result.Add(prevParentTask);
                    }                    
                }

                //добавляем таск в newParent.Contains
                newParent = await taskStorage.Load(newParent.Id);
                if (!newParent.ParentTasks.Contains(change.Id))
                {
                    newParent.ContainsTasks.Add(change.Id);
                    await taskStorage.Save(newParent);

                }
                result.Add(newParent);

                //удаляем prevParent и добавляем newParent в таск
                change = await taskStorage.Load(change.Id);

                if (prevParent is not null)
                    change.ParentTasks.Remove(prevParent.Id);

                if (!change.ParentTasks.Contains(newParent.Id))
                {
                    change.ParentTasks.Add(newParent.Id);
                }

                await taskStorage.Save(change);
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
