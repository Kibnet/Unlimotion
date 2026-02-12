using System.Collections.ObjectModel;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class GraphViewModel
{
    private static readonly ReadOnlyObservableCollection<TaskWrapperViewModel> EmptyTaskWrappers =
        new(new ObservableCollectionExtended<TaskWrapperViewModel>());

    private static readonly ReadOnlyObservableCollection<EmojiFilter> EmptyEmojiFilters =
        new(new ObservableCollectionExtended<EmojiFilter>());

    private MainWindowViewModel? _mainWindowViewModel;

    public GraphViewModel()
    {
        Tasks = EmptyTaskWrappers;
        UnlockedTasks = EmptyTaskWrappers;
        EmojiFilters = EmptyEmojiFilters;
        EmojiExcludeFilters = EmptyEmojiFilters;
    }

    public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
    }

    public ReadOnlyObservableCollection<TaskWrapperViewModel> Tasks { get; set; }
    public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedTasks { get; set; }

    public ReadOnlyObservableCollection<EmojiFilter> EmojiFilters { get; set; }
    public ReadOnlyObservableCollection<EmojiFilter> EmojiExcludeFilters { get; set; }

    public bool UpdateGraph { get; set; }

    public bool OnlyUnlocked { get; set; }

    public bool? ShowWanted
    {
        get => _mainWindowViewModel?.ShowWanted;
        set
        {
            if (_mainWindowViewModel != null)
            {
                _mainWindowViewModel.ShowWanted = value;
            }
        }
    }

    public bool ShowCompleted
    {
        get => _mainWindowViewModel?.ShowCompleted ?? false;
        set
        {
            if (_mainWindowViewModel != null)
            {
                _mainWindowViewModel.ShowCompleted = value;
            }
        }
    }

    public bool ShowArchived
    {
        get => _mainWindowViewModel?.ShowArchived ?? false;
        set
        {
            if (_mainWindowViewModel != null)
            {
                _mainWindowViewModel.ShowArchived = value;
            }
        }
    }

    public SearchDefinition Search { get; set; } = new();
}
