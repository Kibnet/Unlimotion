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
        //CreateSibling, CreateInner
        else
        {
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
    public async Task<List<TaskItem>> CloneTask(TaskItemViewModel change, ITaskStorage taskStorage, params TaskItemViewModel[] stepParents)
    {
        var result = new List<TaskItem>();
        await taskStorage.Save(change.Model);

        foreach (var parent in stepParents)
        {
            parent.Model.ContainsTasks.Add(change.Model.Id);
            await taskStorage.Save(parent.Model);
            result.Add(parent.Model);

            change.Model.ParentTasks.Add(parent.Id);
        }

        await taskStorage.Save(change.Model);
        result.Add(change.Model);

        return result;
    }
    public async Task<List<TaskItem>> CopyTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItemViewModel additionalParent)
    {
        var result = new List<TaskItem>();

        change.ParentTasks.Add(additionalParent.Id);
        await taskStorage.Save(change);
        result.Add(change);

        additionalParent.Model.ContainsTasks.Add(change.Id);
        await taskStorage.Save(additionalParent.Model);
        result.Add(additionalParent.Model);

        return result;
    }
    public async Task<List<TaskItem>> MoveTaskInto(TaskItem change, ITaskStorage taskStorage, TaskItemViewModel newParent, TaskItemViewModel prevParent)
    {
        var result = new List<TaskItem>();

        prevParent.Model.ContainsTasks.Remove(change.Id);
        await taskStorage.Save(prevParent.Model);
        result.Add(prevParent.Model);

        change.ParentTasks.Add(newParent.Id);
        change.ParentTasks.Remove(prevParent.Id);
        await taskStorage.Save(change);
        result.Add(change);

        newParent.Model.ContainsTasks.Add(change.Id);
        await taskStorage.Save(newParent.Model);
        result.Add(newParent.Model);

        return result;
    }
}
