using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;
using ReactiveUI;

namespace Unlimotion.ViewModel;

public class TaskWrapperActions
{
    public Func<TaskItemViewModel, IObservable<IChangeSet<TaskItemViewModel>>> ChildSelector;
    public Action<TaskWrapperViewModel> RemoveAction;
}

[AddINotifyPropertyChangedInterface]
public class TaskWrapperViewModel : DisposableList
{
    private ReadOnlyObservableCollection<TaskWrapperViewModel> _subTasks;
    private readonly TaskWrapperActions _actions;

    public TaskWrapperViewModel(TaskWrapperViewModel parent, TaskItemViewModel task, TaskWrapperActions actions)
    {
        TaskItem = task;
        Parent = parent;
        _actions = actions;
        RemoveCommand = ReactiveCommand.Create(() =>
        {
            _actions.RemoveAction.Invoke(this);
        });
    }

    public ICommand RemoveCommand { get; }

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
        get
        {
            if (_subTasks == null)
            {
                var tasks = _actions.ChildSelector.Invoke(TaskItem);
                tasks
                    .Transform(model => new TaskWrapperViewModel(this, model, _actions))
                    .Bind(out _subTasks)
                    .Subscribe()
                    .AddToDispose(this);
            }

            return _subTasks;
        }
        set => _subTasks = value;
    }
}