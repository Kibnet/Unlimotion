using System;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.Domain;

public static class TaskStatusExtensions
{
    public static bool IsActive(this TaskStatus status) =>
        status is TaskStatus.NotReady or TaskStatus.Prepared or TaskStatus.InProgress;

    public static bool IsIncompleteForAvailability(this TaskStatus status) =>
        status is TaskStatus.NotReady or TaskStatus.Prepared or TaskStatus.InProgress;

    public static int WorkflowOrder(this TaskStatus status) => status switch
    {
        TaskStatus.NotReady => 0,
        TaskStatus.Prepared => 1,
        TaskStatus.InProgress => 2,
        TaskStatus.Completed => 3,
        TaskStatus.Archived => 4,
        _ => 0
    };

    public static string ToLegacyMarker(this TaskStatus status) => status switch
    {
        TaskStatus.NotReady => "[ ]",
        TaskStatus.Prepared => "[!]",
        TaskStatus.InProgress => "[>]",
        TaskStatus.Completed => "[x]",
        TaskStatus.Archived => "[#]",
        _ => "[ ]"
    };

    public static DateTimeOffset? LastChangedAt(
        this IEnumerable<TaskStatusHistoryEntry>? history,
        TaskStatus status)
    {
        return history?
            .Where(entry => entry.Status == status)
            .OrderBy(entry => entry.ChangedAt)
            .LastOrDefault()
            ?.ChangedAt;
    }

    public static TaskStatus? LastNonArchivedStatus(this IEnumerable<TaskStatusHistoryEntry>? history)
    {
        return history?
            .Where(entry => entry.Status != TaskStatus.Archived)
            .OrderBy(entry => entry.ChangedAt)
            .LastOrDefault()
            ?.Status;
    }
}
