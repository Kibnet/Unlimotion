using System;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class TaskWrapperViewModel : DisposableList
{
    private ReadOnlyObservableCollection<TaskWrapperViewModel> _subTasks;

    public TaskWrapperViewModel(TaskWrapperViewModel parent, TaskItemViewModel task, Func<TaskItemViewModel,IObservable<IChangeSet<TaskItemViewModel>>> childSelector)
    {
        TaskItem = task;
        Parent = parent;
        var tasks = childSelector.Invoke(task);
        tasks
            .Transform(model => new TaskWrapperViewModel(this, model, childSelector))
            .Bind(out _subTasks)
            .Subscribe()
            .AddToDispose(this);
    }

    public void Remove()
    {
        TaskItem.RemoveFunc.Invoke(Parent?.TaskItem);
    }

    public bool CanMoveInto(TaskWrapperViewModel destination)
    {
        return destination != null &&
               destination.TaskItem != this.TaskItem &&
               destination.TaskItem.GetAllParents().All(m => m.Id != this.TaskItem.Id) &&
               !destination.TaskItem.Contains.Contains(this.TaskItem.Id);
    }

    public TaskItemViewModel TaskItem { get; set; }
    public TaskWrapperViewModel Parent { get; set; }

    public ReadOnlyObservableCollection<TaskWrapperViewModel> SubTasks
    {
        get => _subTasks;
        set => _subTasks = value;
    }
}