using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Unlimotion.Domain
{
    public record TaskItem
    {
        public string Id { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public TaskStatus Status { get; set; } = TaskStatus.NotReady;
        public List<TaskStatusHistoryEntry> StatusHistory { get; set; } = new();
        public List<TaskCompletionCriterion> CompletionCriteria { get; set; } = new();
        public bool IsCanBeCompleted { get; set; } = true;
        public DateTimeOffset CreatedDateTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        public TimeSpan? PlannedDuration { get; set; }
        public List<string> ContainsTasks { get; set; } = new();
        public List<string> ParentTasks { get; set; } = new();
        public List<string> BlocksTasks { get; set; } = new();
        public List<string> BlockedByTasks { get; set; } = new();
        public RepeaterPattern? Repeater { get; set; }
        public int Importance { get; set; }
        public bool Wanted { get; set; }
        public int Version { get; set; } = 0;

        [IgnoreDataMember]
        [JsonIgnore]
        public bool? IsCompleted
        {
            get => Status switch
            {
                TaskStatus.Completed => true,
                TaskStatus.Archived => null,
                _ => false
            };
            set => Status = value switch
            {
                true => TaskStatus.Completed,
                null => TaskStatus.Archived,
                _ => TaskStatus.NotReady
            };
        }

        [IgnoreDataMember]
        [JsonIgnore]
        public DateTimeOffset? CompletedDateTime
        {
            get => Status == TaskStatus.Completed
                ? StatusHistory.LastChangedAt(TaskStatus.Completed)
                : null;
            set
            {
                if (value.HasValue)
                {
                    SetStatusHistoryTimestamp(TaskStatus.Completed, value.Value, "System");
                }
            }
        }

        [IgnoreDataMember]
        [JsonIgnore]
        public DateTimeOffset? ArchiveDateTime
        {
            get => Status == TaskStatus.Archived
                ? StatusHistory.LastChangedAt(TaskStatus.Archived)
                : null;
            set
            {
                if (value.HasValue)
                {
                    SetStatusHistoryTimestamp(TaskStatus.Archived, value.Value, "System");
                }
            }
        }

        [IgnoreDataMember]
        [JsonIgnore]
        public DateTimeOffset? StartedDateTime => Status == TaskStatus.InProgress
            ? StatusHistory.LastChangedAt(TaskStatus.InProgress)
            : null;

        [IgnoreDataMember]
        [JsonIgnore]
        public DateTimeOffset? PreparedDateTime => Status == TaskStatus.Prepared
            ? StatusHistory.LastChangedAt(TaskStatus.Prepared)
            : null;

        public void EnsureStatusHistory(string author = "System")
        {
            StatusHistory ??= new List<TaskStatusHistoryEntry>();
            CompletionCriteria ??= new List<TaskCompletionCriterion>();

            if (StatusHistory.Count == 0)
            {
                StatusHistory.Add(new TaskStatusHistoryEntry
                {
                    Status = Status,
                    ChangedAt = CreatedDateTime,
                    Author = NormalizeAuthor(author)
                });
                return;
            }

            var latest = StatusHistory.OrderBy(entry => entry.ChangedAt).Last();
            if (latest.Status != Status)
            {
                StatusHistory.Add(new TaskStatusHistoryEntry
                {
                    Status = Status,
                    ChangedAt = UpdatedDateTime ?? DateTimeOffset.UtcNow,
                    Author = NormalizeAuthor(author)
                });
            }
        }

        public void SetStatus(TaskStatus status, DateTimeOffset changedAt, string author)
        {
            StatusHistory ??= new List<TaskStatusHistoryEntry>();
            CompletionCriteria ??= new List<TaskCompletionCriterion>();

            if (Status == status && StatusHistory.LastOrDefault()?.Status == status)
            {
                return;
            }

            Status = status;
            StatusHistory.Add(new TaskStatusHistoryEntry
            {
                Status = status,
                ChangedAt = changedAt,
                Author = NormalizeAuthor(author)
            });
        }

        public TaskStatus GetRestoreStatusAfterArchive() =>
            StatusHistory.LastNonArchivedStatus() ?? TaskStatus.NotReady;

        public static string NormalizeAuthor(string? author) =>
            string.IsNullOrWhiteSpace(author) ? "local-user" : author.Trim();

        private void SetStatusHistoryTimestamp(TaskStatus status, DateTimeOffset changedAt, string author)
        {
            StatusHistory ??= new List<TaskStatusHistoryEntry>();

            var existing = StatusHistory
                .Where(entry => entry.Status == status)
                .OrderBy(entry => entry.ChangedAt)
                .LastOrDefault();

            if (existing == null)
            {
                StatusHistory.Add(new TaskStatusHistoryEntry
                {
                    Status = status,
                    ChangedAt = changedAt,
                    Author = NormalizeAuthor(author)
                });
                return;
            }

            existing.ChangedAt = changedAt;
            existing.Author = NormalizeAuthor(existing.Author);
        }
    }
}
