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
    public Func<TaskItemViewModel, IObservable<IChangeSet<TaskItemViewModel>>> ChildSelector =
        _ => Observable.Empty<IChangeSet<TaskItemViewModel>>();
    public Action<TaskWrapperViewModel>? RemoveAction;
    public Func<TaskWrapperViewModel, string> GetBreadScrumbs = _ => string.Empty;
    public Func<TaskItemViewModel, bool?> GetExpansionState = _ => null;
    public Action<TaskItemViewModel, bool>? SetExpansionState;
    public List<IObservable<Func<TaskItemViewModel, bool>>> Filter = new() { Filters.Default };
    public IObservable<IComparer<TaskWrapperViewModel>> SortComparer = Comparers.Default;
}

public static class BredScrumbsAlgorithms
{
    public static string WrapperParent(TaskWrapperViewModel current)
    {
        var nodes = new List<string>();
        TaskWrapperViewModel? node = current;
        while (node != null)
        {
            nodes.Insert(0, node.TaskItem.Title);
            node = node.Parent;
        }

        return String.Join(" / ", nodes);
    }

    public static string FirstTaskParent(TaskWrapperViewModel current)
    {
        return FirstTaskParent(current?.TaskItem);
    }

    public static string FirstTaskParent(TaskItemViewModel? current)
    {
        var nodes = new List<string>();
        var visited = new HashSet<TaskItemViewModel>();
        var task = current;
        while (task != null && visited.Add(task))
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
        new SortExpressionComparer<TaskWrapperViewModel>
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
    private ReadOnlyObservableCollection<TaskWrapperViewModel>? _subTasks;
    private readonly TaskWrapperActions _actions;
    private bool _isExpanded;

    public static bool DefaultIsExpanded { get; set; }

    public TaskWrapperViewModel(TaskWrapperViewModel? parent, TaskItemViewModel task, TaskWrapperActions actions)
    {
        TaskItem = task;
        Parent = parent;
        _actions = actions;
        _isExpanded = _actions.GetExpansionState.Invoke(TaskItem) ?? DefaultIsExpanded;
        RemoveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_actions.RemoveAction != null) 
                _actions.RemoveAction?.Invoke(this);
        });
    }

    public ICommand RemoveCommand { get; }

    public TaskItemViewModel TaskItem { get; set; }
    public string Id => TaskItem.Id;
    public TaskWrapperViewModel? Parent { get; set; }
    public DateTimeOffset? SpecialDateTime { get; set; }

    public string BreadScrumbs => _actions.GetBreadScrumbs.Invoke(this);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            _actions.SetExpansionState?.Invoke(TaskItem, value);
        }
    }

    public ReadOnlyObservableCollection<TaskWrapperViewModel> SubTasks
    {
        get
        {
            if (_subTasks == null)
            {
                var tasks = _actions.ChildSelector.Invoke(TaskItem)
                    .AutoRefreshOnObservable(task => task.WhenAnyValue(child => child.Status));
                if (_actions.Filter.Count > 0)
                {
                    foreach (var filter in _actions.Filter)
                    {
                        tasks = tasks.Filter(filter);
                    }
                }

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
