using System.Collections.ObjectModel;
using ReactiveUI;
using Unlimotion.ViewModel.Search;

namespace Unlimotion.ViewModel;

/// <summary>Mutable filters owned by one logical task-list route.</summary>
public sealed class TaskListFilterScope : ReactiveObject
{
    private TaskGoalFilterMode goalFilterMode;

    public TaskListFilterScope()
    {
        ReadOnlyEmojiFilters = new ReadOnlyObservableCollection<EmojiFilter>(EmojiFilters);
        ReadOnlyEmojiExcludeFilters = new ReadOnlyObservableCollection<EmojiFilter>(EmojiExcludeFilters);
    }

    public TaskGoalFilterMode GoalFilterMode
    {
        get => goalFilterMode;
        set
        {
            this.RaiseAndSetIfChanged(ref goalFilterMode, value);
            this.RaisePropertyChanged(nameof(CurrentGoalFilter));
        }
    }

    public TaskGoalFilterOption CurrentGoalFilter
    {
        get => TaskGoalFilterOption.Find(GoalFilterMode);
        set
        {
            if (value is not null) GoalFilterMode = value.Mode;
        }
    }

    public SearchDefinition Search { get; } = new();
    public ObservableCollection<EmojiFilter> EmojiFilters { get; } = [];
    public ObservableCollection<EmojiFilter> EmojiExcludeFilters { get; } = [];
    public ReadOnlyObservableCollection<EmojiFilter> ReadOnlyEmojiFilters { get; }
    public ReadOnlyObservableCollection<EmojiFilter> ReadOnlyEmojiExcludeFilters { get; }

    public void Reset()
    {
        GoalFilterMode = TaskGoalFilterMode.All;
        Search.SearchText = string.Empty;
        foreach (var filter in EmojiFilters) filter.ShowTasks = false;
        foreach (var filter in EmojiExcludeFilters) filter.ShowTasks = false;
    }
}
