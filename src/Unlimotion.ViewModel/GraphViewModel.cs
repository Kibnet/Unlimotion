using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
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
    private static readonly ReadOnlyObservableCollection<TaskStatusFilter> EmptyStatusFilters =
        new(new ObservableCollectionExtended<TaskStatusFilter>());

    private MainWindowViewModel? _mainWindowViewModel;
    private INotifyPropertyChanged? _mainWindowPropertyChangedSource;

    public GraphViewModel()
    {
        Tasks = EmptyTaskWrappers;
        UnlockedTasks = EmptyTaskWrappers;
        EmojiFilters = EmptyEmojiFilters;
        EmojiExcludeFilters = EmptyEmojiFilters;
        StatusFilters = EmptyStatusFilters;
    }

    public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
    {
        if (_mainWindowPropertyChangedSource != null)
        {
            _mainWindowPropertyChangedSource.PropertyChanged -= HandleMainWindowViewModelPropertyChanged;
        }

        _mainWindowViewModel = mainWindowViewModel;
        _mainWindowPropertyChangedSource = mainWindowViewModel as INotifyPropertyChanged;
        if (_mainWindowPropertyChangedSource != null)
        {
            _mainWindowPropertyChangedSource.PropertyChanged += HandleMainWindowViewModelPropertyChanged;
        }

        NotifyWantedFilterProxyChanged();
        OnPropertyChanged(nameof(StatusFilters));
    }

    public MainWindowViewModel? MainWindowViewModel => _mainWindowViewModel;

    public ReadOnlyObservableCollection<TaskWrapperViewModel> Tasks { get; set; }
    public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedTasks { get; set; }

    public ReadOnlyObservableCollection<EmojiFilter> EmojiFilters { get; set; }
    public ReadOnlyObservableCollection<EmojiFilter> EmojiExcludeFilters { get; set; }
    public ReadOnlyObservableCollection<TaskStatusFilter> StatusFilters
    {
        get => _mainWindowViewModel?.RoadmapStatusFilters ?? EmptyStatusFilters;
        set { }
    }

    public bool UpdateGraph { get; set; }

    public bool OnlyUnlocked { get; set; }

    public IReadOnlyList<WantedFilterOption> WantedFilterDefinitions =>
        _mainWindowViewModel?.WantedFilterDefinitions ?? WantedFilterOption.All;

    public WantedFilterOption CurrentWantedFilter
    {
        get => _mainWindowViewModel?.CurrentWantedFilter ?? WantedFilterOption.Find(null);
        set
        {
            if (_mainWindowViewModel != null && value != null)
            {
                ShowWanted = value.Value;
            }
        }
    }

    [AlsoNotifyFor(nameof(CurrentWantedFilter))]
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

    public ICommand? ResetTaskFiltersCommand => _mainWindowViewModel?.ResetTaskFiltersCommand;

    private void HandleMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(MainWindowViewModel.ShowWanted)
                or nameof(MainWindowViewModel.CurrentWantedFilter))
        {
            NotifyWantedFilterProxyChanged();
        }

        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(MainWindowViewModel.RoadmapStatusFilters))
        {
            OnPropertyChanged(nameof(StatusFilters));
        }
    }

    private void NotifyWantedFilterProxyChanged()
    {
        OnPropertyChanged(nameof(ShowWanted));
        OnPropertyChanged(nameof(CurrentWantedFilter));
    }

    private void OnPropertyChanged(string propertyName)
    {
        if (this is not INotifyPropertyChanged propertyChangedSource)
        {
            return;
        }

        var propertyChangedHandler = propertyChangedSource
            .GetType()
            .GetField(
                nameof(INotifyPropertyChanged.PropertyChanged),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(propertyChangedSource) as PropertyChangedEventHandler;

        propertyChangedHandler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
