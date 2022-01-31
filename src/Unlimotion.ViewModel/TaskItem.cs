using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel
{
    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public bool? IsCompleted { get; set; } = false;
        public DateTimeOffset CreatedDateTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public List<string> ContainsTasks { get; set; } = new();
        public List<string> BlocksTasks { get; set; } = new();
    }
}
