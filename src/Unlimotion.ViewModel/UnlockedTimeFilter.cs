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
                    Title = "All Planned",
                    Predicate = e => MustBeCompleted(e) && (e.PlannedBeginDateTime != null || e.PlannedEndDateTime != null)
                },
                new()
                {
                    Title = "Urgent",
                    Predicate = e => MustBeCompleted(e) && DateTime.Now.Date == e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Overdue",
                    Predicate = e => MustBeCompleted(e) && DateTime.Now.Date > e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Current",
                    Predicate = e => MustBeCompleted(e) && 
                                     (e.PlannedBeginDateTime == null && e.PlannedEndDateTime != null && e.PlannedEndDateTime?.Date >= DateTime.Now.Date) || 
                                     (e.PlannedEndDateTime == null && e.PlannedBeginDateTime != null && e.PlannedBeginDateTime?.Date <= DateTime.Now.Date) ||
                                     (e.PlannedBeginDateTime?.Date <= DateTime.Now.Date && DateTime.Now.Date <= e.PlannedEndDateTime?.Date)
                },
                new()
                {
                    Title = "Future",
                    Predicate = e => MustBeCompleted(e) && e.PlannedBeginDateTime?.Date > DateTime.Now.Date
                }
            });
        }

        private static readonly Predicate<TaskItemViewModel> MustBeCompleted = e => e.IsCanBeCompleted && 
                                                                                    e.IsCompleted == false && 
                                                                                    e.ArchiveDateTime == null;
    }
}
