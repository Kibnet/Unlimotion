using System;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using ReactiveUI;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public class DurationFilter : ReactiveObject
{
    private string _title = string.Empty;
    private string _resourceKey = string.Empty;
    private bool _showTasks;

    public string Title
    {
        get => string.IsNullOrWhiteSpace(ResourceKey) ? _title : L10n.Get(ResourceKey);
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string ResourceKey
    {
        get => _resourceKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _resourceKey, value);
            this.RaisePropertyChanged(nameof(Title));
        }
    }

    public bool ShowTasks
    {
        get => _showTasks;
        set => this.RaiseAndSetIfChanged(ref _showTasks, value);
    }

    public Func<TaskItemViewModel, bool> Predicate { get; set; } = null!;

    public void RefreshLocalization() => this.RaisePropertyChanged(nameof(Title));

    public override string ToString() => Title;

    public static ReadOnlyObservableCollection<DurationFilter> GetDefinitions()
    {
        return new ReadOnlyObservableCollection<DurationFilter>(new ObservableCollectionExtended<DurationFilter>
        {
            new()
                {
                    Title = "No duration",
                    ResourceKey = "DurationFilterNoDuration",
                    Predicate = e => e.PlannedDuration == null
                },
                new()
                {
                    Title = "<=5m",
                    ResourceKey = "DurationFilter5m",
                    Predicate = e => e.PlannedDuration <= TimeSpan.FromMinutes(5)
                },
                new()
                {
                    Title = "5m< & <=30m",
                    ResourceKey = "DurationFilter5mTo30m",
                    Predicate = e => TimeSpan.FromMinutes(5) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromMinutes(30)
                },
                new()
                {
                    Title = "30m< & <=2h",
                    ResourceKey = "DurationFilter30mTo2h",
                    Predicate = e => TimeSpan.FromMinutes(30) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromHours(2)
                },
                new()
                {
                    Title = "2h< & <=1d",
                    ResourceKey = "DurationFilter2hTo1d",
                    Predicate = e => TimeSpan.FromHours(2) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromDays(1)
                },
                new()
                {
                    Title = "1d<",
                    ResourceKey = "DurationFilter1dLess",
                    Predicate = e => TimeSpan.FromDays(1) < e.PlannedDuration
                }
        });
    }
}
