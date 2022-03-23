using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;
using ReactiveUI;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class TaskWrapperViewModel : DisposableList
{
    private ReadOnlyObservableCollection<TaskWrapperViewModel> _subTasks;
    private readonly Func<TaskItemViewModel, IObservable<IChangeSet<TaskItemViewModel>>> _childSelector;
    private readonly Action<TaskWrapperViewModel> _removeAction;

    public TaskWrapperViewModel(TaskWrapperViewModel parent, TaskItemViewModel task, Func<TaskItemViewModel,IObservable<IChangeSet<TaskItemViewModel>>> childSelector, Action<TaskWrapperViewModel> removeAction)
    {
        TaskItem = task;
        Parent = parent;
        RemoveCommand = ReactiveCommand.Create(() =>
        {
            removeAction.Invoke(this);
        });
        _childSelector = childSelector;
        _removeAction = removeAction;
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
                var tasks = _childSelector.Invoke(TaskItem);
                tasks
                    .Transform(model => new TaskWrapperViewModel(this, model, _childSelector, _removeAction))
                    .Bind(out _subTasks)
                    .Subscribe()
                    .AddToDispose(this);
            }

            return _subTasks;
        }
        set => _subTasks = value;
    }
}