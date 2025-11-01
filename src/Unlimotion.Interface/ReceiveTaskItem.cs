using System;
using System.Collections.Generic;
using SignalR.EasyUse.Interface;

namespace Unlimotion.Interface
{
    public class DeleteTaskItem : IClientMethod
    {
        public string Id { get; set; }
        public string UserId { get; set; }
    }

    public class ReceiveTaskItem : IClientMethod
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool? IsCompleted { get; set; } = false;
        public bool IsCanBeCompleted { get; set; } = false;
        public DateTimeOffset CreatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        public TimeSpan? PlannedDuration { get; set; }
        public List<string> ContainsTasks { get; set; }
        public List<string>? ParentTasks { get; set; }
        public List<string> BlocksTasks { get; set; }
        public List<string> BlockedByTasks { get; set; } = new();
        public RepeaterPatternHubMold Repeater { get; set; }
        public int Importance { get; set; }
        public bool Wanted { get; set; }

        public int Version { get; set; } = 0;
    }
}
