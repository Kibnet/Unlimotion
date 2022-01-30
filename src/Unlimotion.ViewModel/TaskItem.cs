using System;
using System.Collections.ObjectModel;
using PropertyChanged;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool? IsCompleted { get; set; }
        public DateTimeOffset CreatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public ObservableCollection<TaskItem> ContainsTasks { get; set; }
        public ObservableCollection<TaskItem> BlocksTasks { get; set; }
    }
}
