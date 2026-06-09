using System;
using System.Collections.Generic;
using Unlimotion.Domain;

namespace Unlimotion.Interface
{
    public class TaskItemHubMold
    {
        public string Id { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public TaskStatus Status { get; set; } = TaskStatus.NotReady;
        public List<TaskStatusHistoryEntry> StatusHistory { get; set; } = new();
        public List<TaskCompletionCriterion> CompletionCriteria { get; set; } = new();
        public DateTimeOffset? UpdatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        public TimeSpan? PlannedDuration { get; set; }
        public List<string> ContainsTasks { get; set; } = null!;
        public List<string>? ParentTasks { get; set; }
        public List<string> BlocksTasks { get; set; } = null!;
        public List<string> BlockedByTasks { get; set; } = new();
        public RepeaterPatternHubMold? Repeater { get; set; }
        public int Importance { get; set; }
        public bool Wanted { get; set; }
        public int Version { get; set; } = 0;
    }
}
