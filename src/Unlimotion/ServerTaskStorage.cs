using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using DynamicData;
using Splat;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class ServerTaskStorage : ITaskStorage
{
    public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
    private ServerStorage serverStorage;
    private TaskTreeManager? taskTreeManager;

    public TaskTreeManager TaskTreeManager
    {
        get { return taskTreeManager ??= new TaskTreeManager(serverStorage); }
    }

    public ServerTaskStorage(ServerStorage storage)
    {
        serverStorage = storage;
    }

    // Expose ServerStorage properties and events for backward compatibility
    public event Action<Exception?>? OnConnectionError
    {
        add => serverStorage.OnConnectionError += value;
        remove => serverStorage.OnConnectionError -= value;
    }

    public event Action? OnConnected
    {
        add => serverStorage.OnConnected += value;
        remove => serverStorage.OnConnected -= value;
    }

    public event Action? OnDisconnected
    {
        add => serverStorage.OnDisconnected += value;
        remove => serverStorage.OnDisconnected -= value;
    }

    public event EventHandler OnSignOut
    {
        add => serverStorage.OnSignOut += value;
        remove => serverStorage.OnSignOut -= value;
    }

    public event EventHandler OnSignIn
    {
        add => serverStorage.OnSignIn += value;
        remove => serverStorage.OnSignIn -= value;
    }

    public bool IsConnected => serverStorage.IsConnected;
    public bool IsSignedIn => serverStorage.IsSignedIn;

    public async Task SignOut()
    {
        await serverStorage.SignOut();
    }

    public async Task Disconnect()
    {
        await serverStorage.Disconnect();
    }

    public async Task Init()
    {
        Tasks = new(item => item.Id);

        await foreach (var task in TaskTreeManager.Storage.GetAll())
        {
            var vm = new TaskItemViewModel(task, this);
            Tasks.AddOrUpdate(vm);
        }

        TaskTreeManager.Storage.Updating += TaskStorageOnUpdating;
    }

    private async void TaskStorageOnUpdating(object? sender, TaskStorageUpdateEventArgs e)
    {
        switch (e.Type)
        {
            case UpdateType.Saved:
                var taskItem = await TaskTreeManager.Storage.Load(e.Id);
                if (taskItem?.Id != null)
                {
                    var vml = Tasks.Lookup(taskItem.Id);
                    if (vml.HasValue)
                    {
                        var vm = vml.Value;
                        vm.Update(taskItem);
                    }
                    else
                    {
                        var vm = new TaskItemViewModel(taskItem, this);
                        Tasks.AddOrUpdate(vm);
                    }
                }
                break;
            case UpdateType.Removed:
                var deletedItem = Tasks.Lookup(e.Id);
                if(deletedItem.HasValue)
                    await Delete(deletedItem.Value, false);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task<bool> Connect()
    {
        return await serverStorage.Connect();
    }


    public async Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null, bool isBlocked = false)
    {
        var taskItemList = await TaskTreeManager.AddTask(
            change.Model,
            currentTask?.Model,
            isBlocked);

        var newTask = taskItemList.Last();
        change.Id = newTask.Id;
        change.Update(newTask);
        Tasks.AddOrUpdate(change);

        foreach (var task in taskItemList.SkipLast(1))
        {
            UpdateCache(task);
        }
        return true;
    }

    public async Task<bool> AddChild(TaskItemViewModel change, TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.AddChildTask(
            change.Model,
            currentTask.Model);

        var newTask = taskItemList.Last();
        change.Id = newTask.Id;
        change.Update(newTask);
        Tasks.AddOrUpdate(change);

        foreach (var task in taskItemList.SkipLast(1))
        {
            UpdateCache(task);
        }

        return true;
    }

    public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
    {
        var parentsItemList = await TaskTreeManager.DeleteTask(change.Model, deleteInStorage);
        foreach (var parent in parentsItemList)
        {
            UpdateCache(parent);
        }
        Tasks.Remove(change);

        return true;
    }

    public async Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent)
    {
        var connItemList = await TaskTreeManager.DeleteParentChildRelation(parent.Model, change.Model);

        foreach (var task in connItemList)
        {
            UpdateCache(task);
        }
        return true;
    }

    public async Task<bool> Update(TaskItemViewModel change)
    {
        var connItemList = await TaskTreeManager.UpdateTask(change.Model);
        foreach (var task in connItemList)
        {
            UpdateCache(task);
        }
        return true;
    }

    public async Task<bool> Update(TaskItem change)
    {
        var connItemList = await TaskTreeManager.UpdateTask(change);
        foreach (var task in connItemList)
        {
            UpdateCache(task);
        }
        return true;
    }

    public async Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents)
    {
        var additionalItemParents = new List<TaskItem>();
        if (additionalParents != null)
        {
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(newParent.Model);
            }
        }
        var taskItemList = await TaskTreeManager.CloneTask(
            change.Model,
            additionalItemParents);
        foreach (var task in taskItemList)
        {
            UpdateCache(task);
        }

        var clone = taskItemList.OrderByDescending(item => item.Id).First();
        change.Id = clone.Id;
        change.Parents.Add(clone.ParentTasks);

        return change;
    }

    public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents)
    {
        var additionalItemParents = new List<TaskItem>();
        if (additionalParents != null)
        {
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(newParent.Model);
            }
        }

        if (additionalParents != null && additionalParents.Length > 0)
        {
            var taskItemList = await TaskTreeManager.AddNewParentToTask(
                    change.Model,
                    additionalParents[0].Model);

            foreach (var task in taskItemList)
            {
                UpdateCache(task);
            }
        }
        return true;
    }
    private void UpdateCache(TaskItem task)
    {
        var vm = Tasks.Lookup(task.Id);

        if (vm.HasValue)
            vm.Value.Update(task);
        // else
        // throw new NotFoundException($"No task with id = {task.Id} is found in cache");
    }

    public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents, TaskItemViewModel? currentTask)
    {
        var taskItemList = await TaskTreeManager.MoveTaskToNewParent(
                change.Model,
                additionalParents?.FirstOrDefault()?.Model!,
                currentTask?.Model);

        taskItemList.ForEach(UpdateCache);

        return true;
    }

    public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
    {
        var taskItemList = await TaskTreeManager.UnblockTask(
            taskToUnblock.Model,
            blockingTask.Model);

        taskItemList.ForEach(item => UpdateCache(item));

        return true;
    }

    public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.BlockTask(
            change.Model,
            currentTask.Model);

        taskItemList.ForEach(item => UpdateCache(item));

        return true;
    }

    public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
    {
        var taskItemList = await TaskTreeManager.DeleteParentChildRelation(
            parent.Model,
            child.Model);

        taskItemList.ForEach(item => UpdateCache(item));
    }
}
