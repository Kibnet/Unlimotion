using System;
using System.Collections.ObjectModel;
using DynamicData.Binding;
using ReactiveUI;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel
{
    public class UnlockedTimeFilter : ReactiveObject
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
