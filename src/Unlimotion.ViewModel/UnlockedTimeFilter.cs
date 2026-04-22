using System;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using PropertyChanged;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class UnlockedTimeFilter
    {
        private string _title = string.Empty;

        public string Title
        {
            get => string.IsNullOrWhiteSpace(ResourceKey) ? _title : L10n.Get(ResourceKey);
            set => _title = value;
        }

        public string ResourceKey { get; set; } = string.Empty;

        public bool ShowTasks { get; set; }
        public Func<TaskItemViewModel, bool> Predicate { get; set; }

        public override string ToString() => Title;

        public static ReadOnlyObservableCollection<UnlockedTimeFilter> GetDefinitions()
        {
            return new ReadOnlyObservableCollection<UnlockedTimeFilter>(new ObservableCollectionExtended<UnlockedTimeFilter>
            {
                new()
                {
                    Title = "Unplanned",
                    ResourceKey = "UnlockedTimeFilterUnplanned",
                    Predicate = e => e.PlannedBeginDateTime == null && e.PlannedEndDateTime == null
                },
                new()
                {
                    Title = "Overdue",
                    ResourceKey = "UnlockedTimeFilterOverdue",
                    Predicate = e => e.PlannedEndDateTime != null && DateTime.Now.Date > e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Urgent",
                    ResourceKey = "UnlockedTimeFilterUrgent",
                    Predicate = e => e.PlannedEndDateTime != null && DateTime.Now.Date == e.PlannedEndDateTime?.Date
                },
                new()
                {
                    Title = "Today",
                    ResourceKey = "UnlockedTimeFilterToday",
                    Predicate = e => e.PlannedBeginDateTime != null && DateTime.Now.Date == e.PlannedBeginDateTime?.Date
                },
                new()
                {
                    Title = "Maybe",
                    ResourceKey = "UnlockedTimeFilterMaybe",
                    Predicate = e => (e.PlannedBeginDateTime == null && e.PlannedEndDateTime != null && DateTime.Now.Date < e.PlannedEndDateTime?.Date) || 
                                     (e.PlannedBeginDateTime != null && e.PlannedEndDateTime == null && e.PlannedBeginDateTime?.Date < DateTime.Now.Date) ||
                                     (e.PlannedBeginDateTime != null && e.PlannedEndDateTime != null && e.PlannedBeginDateTime?.Date < DateTime.Now.Date && DateTime.Now.Date < e.PlannedEndDateTime?.Date)
                },
                new()
                {
                    Title = "Future",
                    ResourceKey = "UnlockedTimeFilterFuture",
                    Predicate = e => e.PlannedBeginDateTime != null && e.PlannedBeginDateTime?.Date > DateTime.Now.Date
                }
            });
        }

        public static readonly Predicate<TaskItemViewModel> IsUnlocked = e => e.IsCanBeCompleted && 
                                                                                    e.IsCompleted == false;
    }
}
