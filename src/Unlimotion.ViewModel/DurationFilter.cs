using System;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class DurationFilter
{
    public string Title { get; set; }
    public bool ShowTasks { get; set; }
    public Func<TaskItemViewModel, bool> Predicate { get; set; }

    public static ReadOnlyObservableCollection<DurationFilter> GetDefinitions()
    {
        return new ReadOnlyObservableCollection<DurationFilter>(new ObservableCollectionExtended<DurationFilter>
        {
            new()
            {
                Title = "No duration",
                Predicate = e => e.PlannedDuration == null
            },
            new()
            {
                Title = "<=5m",
                Predicate = e => e.PlannedDuration <= TimeSpan.FromMinutes(5)
            },
            new()
            {
                Title = "5m< & <=30m",
                Predicate = e => TimeSpan.FromMinutes(5) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromMinutes(30)
            },
            new()
            {
                Title = "30m< & <=2h",
                Predicate = e => TimeSpan.FromMinutes(30) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromHours(2)
            },
            new()
            {
                Title = "2h< & <=1d",
                Predicate = e => TimeSpan.FromHours(2) < e.PlannedDuration && e.PlannedDuration <= TimeSpan.FromDays(1)
            },
            new()
            {
                Title = "1d<",
                Predicate = e => TimeSpan.FromDays(1) < e.PlannedDuration
            }
        });
    }
}