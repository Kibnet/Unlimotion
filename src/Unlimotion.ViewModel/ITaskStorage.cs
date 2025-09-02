using DynamicData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;


namespace Unlimotion.ViewModel;

public interface ITaskStorage
{
    public SourceCache<TaskItemViewModel, string> Tasks { get; }  
    public ITaskTreeManager TaskTreeManager { get; }
    public Task Init();    
    public IAsyncEnumerable<TaskItem> GetAll();
    Task<bool> Connect();
    Task Disconnect();
    public event EventHandler<TaskStorageUpdateEventArgs> Updating;
    public event Action<Exception?>? OnConnectionError;
    public Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null, bool isBlocked = false);
    public Task<bool> AddChild(TaskItemViewModel change, TaskItemViewModel currentTask);
    public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage  = true);
    public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent);
    public Task<bool> Update(TaskItemViewModel change);
    public Task<bool> Update(TaskItem change);
    public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents);
    public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents);
    public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask);
    public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask);
    public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask);
    public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child);

}