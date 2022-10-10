using System;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class UnlockedTimeFilter
    {
        public string Title { get; set; }
        public bool ShowTasks { get; set; }
        public Func<TaskItemViewModel, bool> Predicate { get; set; }

        public static ReadOnlyObservableCollection<UnlockedTimeFilter> GetDefinitions()
        {
            return new ReadOnlyObservableCollection<UnlockedTimeFilter>(new ObservableCollectionExtended<UnlockedTimeFilter>
            {
                new()
                {
                    Title = "Unplanned",
                    Predicate = e => e.PlannedBeginDateTime == null && e.PlannedEndDateTime == null
                },
                new()
                {
                    Title = "Overdue",
                    Predicate = e => e.PlannedEndDateTime != null && DateTime.Now.Date > e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Urgent",
                    Predicate = e => e.PlannedEndDateTime != null && DateTime.Now.Date == e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Today",
                    Predicate = e => e.PlannedBeginDateTime != null && DateTime.Now.Date == e.PlannedBeginDateTime?.Date
                },
                new()
                {
                    Title = "Maybe",
                    Predicate = e => (e.PlannedBeginDateTime == null && e.PlannedEndDateTime != null && DateTime.Now.Date < e.PlannedEndDateTime?.Date) || 
                                     (e.PlannedBeginDateTime != null && e.PlannedEndDateTime == null && e.PlannedBeginDateTime?.Date < DateTime.Now.Date) ||
                                     (e.PlannedBeginDateTime != null && e.PlannedEndDateTime != null && e.PlannedBeginDateTime?.Date < DateTime.Now.Date && DateTime.Now.Date < e.PlannedEndDateTime?.Date)
                },
                new()
                {
                    Title = "Future",
                    Predicate = e => e.PlannedBeginDateTime != null && e.PlannedBeginDateTime?.Date > DateTime.Now.Date
                }
            });
        }

        public static readonly Predicate<TaskItemViewModel> IsUnlocked = e => e.IsCanBeCompleted && 
                                                                                    e.IsCompleted == false;
    }
}
