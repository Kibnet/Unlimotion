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
    public IObservable<Func<TaskItemViewModel, bool>> Filter = Filters.Default;
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
        return FirstTaskParent(current.TaskItem);
    }

    public static string FirstTaskParent(TaskItemViewModel current)
    {
        var nodes = new List<string>();
        var task = current;
        while (task != null)
        {
            nodes.Insert(0, task.Title);
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

public static class Filters
{
    public static IObservable<Func<TaskItemViewModel, bool>> Default =
        Observable.Return<Func<TaskItemViewModel, bool>>(m => m!=null);
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
        RemoveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _actions.RemoveAction.Invoke(this);
        });
    }

    public ICommand RemoveCommand { get; }

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
                    .Filter(_actions.Filter)
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