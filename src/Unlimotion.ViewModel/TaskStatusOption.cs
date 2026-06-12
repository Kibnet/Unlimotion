using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData.Binding;
using ReactiveUI;
using Unlimotion.Domain;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public class TaskStatusOption : ReactiveObject
{
    private bool _isEnabled = true;
    private string? _toolTip;

    public TaskStatus Status { get; init; }

    public string ResourceKey { get; init; } = string.Empty;

    public string Title => L10n.Get(ResourceKey);

    public string DisplayText => Title;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public string ToolTip
    {
        get => _toolTip ?? Title;
        set => this.RaiseAndSetIfChanged(ref _toolTip, value);
    }

    public override string ToString() => DisplayText;

    public override bool Equals(object? obj) =>
        obj is TaskStatusOption option && option.Status == Status;

    public override int GetHashCode() => Status.GetHashCode();

    public static IReadOnlyList<TaskStatusOption> All { get; } =
    [
        new() { Status = TaskStatus.NotReady, ResourceKey = "TaskStatusNotReady" },
        new() { Status = TaskStatus.Prepared, ResourceKey = "TaskStatusPrepared" },
        new() { Status = TaskStatus.InProgress, ResourceKey = "TaskStatusInProgress" },
        new() { Status = TaskStatus.Completed, ResourceKey = "TaskStatusCompleted" },
        new() { Status = TaskStatus.Archived, ResourceKey = "TaskStatusArchived" }
    ];

    public static TaskStatusOption Find(TaskStatus status) =>
        All.First(option => option.Status == status);

    public static TaskStatusOption CreateTransitionOption(TaskStatus status)
    {
        var source = Find(status);
        return new TaskStatusOption
        {
            Status = source.Status,
            ResourceKey = source.ResourceKey
        };
    }
}

public sealed class TaskStatusFilter : ReactiveObject
{
    private bool _showTasks;

    public TaskStatusOption Option { get; init; } = null!;

    public TaskStatus Status => Option.Status;

    public string Title => Option.Title;

    public string DisplayText => Option.DisplayText;

    public bool ShowTasks
    {
        get => _showTasks;
        set => this.RaiseAndSetIfChanged(ref _showTasks, value);
    }

    public void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(DisplayText));
    }

    public override string ToString() => DisplayText;

    public static ReadOnlyObservableCollection<TaskStatusFilter> GetDefinitions(
        IReadOnlyDictionary<TaskStatus, bool>? selectedStatuses = null)
    {
        var filters = new ObservableCollectionExtended<TaskStatusFilter>();
        foreach (var option in TaskStatusOption.All)
        {
            filters.Add(new TaskStatusFilter
            {
                Option = option,
                ShowTasks = selectedStatuses?.TryGetValue(option.Status, out var selected) != true || selected
            });
        }

        return new ReadOnlyObservableCollection<TaskStatusFilter>(filters);
    }
}
