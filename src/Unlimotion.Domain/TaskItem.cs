using System;
using System.Collections.Generic;

namespace Unlimotion.Domain
{
    public class TaskItem
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool? IsCompleted { get; set; } = false;
        public bool IsCanBeCompleted { get; set; } = true;
        public DateTimeOffset CreatedDateTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        public TimeSpan? PlannedDuration { get; set; }
        public List<string> ContainsTasks { get; set; } = new();
        public List<string>? ParentTasks { get; set; } = new();
        public List<string> BlocksTasks { get; set; } = new();
        public List<string>? BlockedByTasks { get; set; } = new();
        public RepeaterPattern Repeater { get; set; }
        public int Importance { get; set; }
        public bool Wanted { get; set; }
        public int Version { get; set; } = 0;
        public DateTime? SortOrder { get; set; }

    }
}
