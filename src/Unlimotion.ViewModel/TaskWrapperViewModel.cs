using System;
using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class TaskWrapperViewModel: DisposableList
{
    private ReadOnlyObservableCollection<TaskWrapperViewModel> _subTasks;

    public TaskWrapperViewModel(TaskWrapperViewModel parent, TaskItemViewModel task)
    {
        TaskItem = task;
        Parent = parent;
        task.ContainsTasks
            .ToObservableChangeSet()
            .Transform(model => new TaskWrapperViewModel(this, model))
            .Bind(out _subTasks)
            .Subscribe()
            .AddToDispose(this);
    }

    public TaskItemViewModel TaskItem { get; set; }
    public TaskWrapperViewModel Parent { get; set; }

    public ReadOnlyObservableCollection<TaskWrapperViewModel> SubTasks
    {
        get => _subTasks;
        set => _subTasks = value;
    }
}