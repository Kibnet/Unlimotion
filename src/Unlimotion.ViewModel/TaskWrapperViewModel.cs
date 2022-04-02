using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
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
    public Func<TaskWrapperViewModel, string> GetBreadScrumbs;
    public IObservable<IComparer<TaskWrapperViewModel>> SortComparer = Comparers.Default;
}

public static class BredScrumbsAlgorithms
{
    public static string WrapperParent(TaskWrapperViewModel current)
    {
        var nodes = new List<string>();
        while (current != null)
        {
            nodes.Insert(0, current.TaskItem.Title);
            current = current.Parent;
        }

        return String.Join(" / ", nodes);
    }

    public static string FirstTaskParent(TaskWrapperViewModel current)
    {
        var nodes = new List<string>();
        var task = current.TaskItem;
        while (task != null)
        {
            nodes.Insert(0, current.TaskItem.Title);
            task = task.ParentsTasks.FirstOrDefault();
        }

        return String.Join(" / ", nodes);
    }
}

public static class Comparers
{
    public static IObservable<IComparer<TaskWrapperViewModel>> Default = Observable.Return(
        new SortExpressionComparer<TaskWrapperViewModel>()
            { new SortExpression<TaskWrapperViewModel>(m => m.TaskItem.CreatedDateTime) });
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

    public string BreadScrumbs => _actions.GetBreadScrumbs?.Invoke(this);

    public ReadOnlyObservableCollection<TaskWrapperViewModel> SubTasks
    {
        get
        {
            if (_subTasks == null)
            {
                var tasks = _actions.ChildSelector.Invoke(TaskItem);
                tasks
                    .Transform(model => new TaskWrapperViewModel(this, model, _actions))
                    .Sort(_actions.SortComparer)
                    .Bind(out _subTasks)
                    .Subscribe()
                    .AddToDispose(this);
            }

            return _subTasks;
        }
        set => _subTasks = value;
    }
}