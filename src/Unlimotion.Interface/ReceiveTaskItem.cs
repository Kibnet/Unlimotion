using System;
using System.Collections.Generic;
using SignalR.EasyUse.Interface;

namespace Unlimotion.Interface
{
    public class DeleteTaskItem : IClientMethod
    {
        public string Id { get; set; } = null!;
        public string UserId { get; set; } = null!;
    }

    public class ReceiveTaskItem : IClientMethod
    {
        public string Id { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public bool? IsCompleted { get; set; } = false;
        public bool IsCanBeCompleted { get; set; } = false;
        public DateTimeOffset CreatedDateTime { get; set; }
        public DateTimeOffset? UpdatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
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
