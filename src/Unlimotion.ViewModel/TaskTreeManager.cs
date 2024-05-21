using DynamicData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public class TaskTreeManager
{
    public async Task<List<TaskItem>> AddTask(TaskItem change, ITaskStorage taskStorage, TaskItem? currentTask = null)
    {
        var result = new List<TaskItem>();

        //Create
        if (currentTask is null)
        {
            await taskStorage.Save(change);
            result.Add(change);
            return result;
        }
        //CreateSibling
        else
        {
            await taskStorage.Save(change);
            foreach (var parent in currentTask.ParentTasks)
            {
                change.ParentTasks.Add(parent);                

                TaskItem parentTask = await taskStorage.Load(parent);
                parentTask.ContainsTasks.Add(change.Id);
                await taskStorage.Save(parentTask);
                result.Add(parentTask);
            }

            await taskStorage.Save(change);
            result.Add(change);

            return result;
        }
    }
    public async Task<List<TaskItem>> AddChildTask(TaskItem change, ITaskStorage taskStorage, TaskItem currentTask)
    {
        var result = new List<TaskItem>();

        //CreateInner
        change.ParentTasks.Add(currentTask.Id);
        await taskStorage.Save(change);

        currentTask.ContainsTasks.Add(change.Id);        
        await taskStorage.Save(currentTask);
        
        result.Add(change);
        result.Add(currentTask);

        return result;        
    }
    public async Task<List<TaskItem>> DeleteTask(TaskItem change, ITaskStorage taskStorage)
    {
        var result = new List<TaskItem>();

        TaskItem deletingTask = await taskStorage.Load(change.Id);

        //удалить во всех детях ссылки в Parents на удаляемый таск
        foreach (var child in deletingTask.ContainsTasks)
        {
            var childTask = await taskStorage.Load(child);
            childTask.ParentTasks.Remove(deletingTask.Id);
            await taskStorage.Save(childTask);
            result.Add(childTask);
        }
        //удалить во всех grandParents ссылки в Contains на удаляемый таск
        foreach (var parent in deletingTask.ParentTasks)
        {
            var parentTask = await taskStorage.Load(parent);
            parentTask.ContainsTasks.Remove(deletingTask.Id);
            await taskStorage.Save(parentTask);
            result.Add(parentTask);
        }
        //удалить сам таск из БД 
        await taskStorage.Remove(change.Id);
        return result;
    }
    public async Task UpdateTask(TaskItem change, ITaskStorage taskStorage)
    {
        await taskStorage.Save(change);
    }
    public async Task<List<TaskItem>> CloneTask(TaskItem change, ITaskStorage taskStorage, List<TaskItem> stepParents)
    {
        var result = new List<TaskItem>();
        await taskStorage.Save(change);
        
        foreach (var parent in stepParents)
        {
            parent.ContainsTasks.Add(change.Id);
            await taskStorage.Save(parent);
            result.Add(parent);

            change.ParentTasks.Add(parent.Id);
        }

        await taskStorage.Save(change);
        result.Add(change);

        return result;
    }
    public async Task<List<TaskItem>> CopyTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItem additionalParent)
    {
        var result = new List<TaskItem>();

        var newParentIsAddedToTask = false;
        while (newParentIsAddedToTask == false)
        {
            newParentIsAddedToTask = await AddNewParentToTask();            
        }

        var taskIsAddedToNewParent = false;
        while (taskIsAddedToNewParent == false)
        {
            taskIsAddedToNewParent = await AddTaskToNewParent();
        }
        
        async Task<bool> AddNewParentToTask()
        {
            try
            {
                change = await taskStorage.Load(change.Id);
                if (!change.ParentTasks.Contains(additionalParent.Id))
                { 
                   change.ParentTasks.Add(additionalParent.Id);
                   await taskStorage.Save(change);                    
                }

                result.Add(change);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        async Task<bool> AddTaskToNewParent()
        {
            try
            {
                change = await taskStorage.Load(change.Id);
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
        }   

        return result;
    }
    public async Task<List<TaskItem>> MoveTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItem newParent, TaskItem? prevParent)
    {
        var result = new List<TaskItem>();

        if (prevParent is not null)
        {
            var prevParentIsDisconnected = false;
            while (prevParentIsDisconnected == false)
            {
                prevParentIsDisconnected = await DisconnectPrevParentWithTask();                
            }            
        }

        var taskIsAddedToNewParent = false;
        while (!taskIsAddedToNewParent)
        {
            taskIsAddedToNewParent = await AddTaskToNewParent();
        }

        var parentsAreUpdatedInTask = false;
        while (!parentsAreUpdatedInTask)
        {
            parentsAreUpdatedInTask = await UpdateParentsInTask();
        }


        return result;

        async Task<bool> DisconnectPrevParentWithTask()
        {
            try 
            {
                prevParent.ContainsTasks.Remove(change.Id);
                await taskStorage.Save(prevParent);
                result.Add(prevParent);
                                
                return true;    
            }
            catch (Exception ex) 
            {
                return false;
            }
        }
        
        async Task<bool> AddTaskToNewParent()
        {
            try
            {
                newParent = await taskStorage.Load(newParent.Id);
                if (!newParent.ParentTasks.Contains(change.Id))
                {
                    newParent.ContainsTasks.Add(change.Id);
                    await taskStorage.Save(newParent);
                    
                }
                result.Add(newParent);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        
        async Task<bool> UpdateParentsInTask()
        {
            try
            {
                change = await taskStorage.Load(change.Id);
                
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
        }
    }    
}
