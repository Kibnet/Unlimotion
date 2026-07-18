using System;
using System.Collections.Generic;

namespace Unlimotion.Domain;

public readonly record struct TaskStatusTransitionFacts(
    TaskStatus CurrentStatus,
    bool IsGraphAvailable,
    bool PlannedBeginIsFuture,
    bool CompletionCriteriaSatisfied);

public enum TaskStatusTransitionDenialReason
{
    None = 0,
    TerminalCannotStart = 1,
    GraphUnavailableForStart = 2,
    FutureDatePreventsStart = 3,
    TerminalCannotComplete = 4,
    GraphUnavailableForCompletion = 5,
    CompletionCriteriaIncomplete = 6,
    CompletedCannotArchive = 7,
    InvalidTargetStatus = 8
}

public readonly record struct TaskStatusTransitionEvaluation(
    bool IsAllowed,
    TaskStatusTransitionDenialReason Reason);

public static class TaskStatusTransitionPolicy
{
    public static readonly TimeSpan RestoreStatusClockSkewTolerance = TimeSpan.FromMinutes(5);

    public static TaskStatusTransitionEvaluation Evaluate(
        TaskStatus requestedStatus,
        TaskStatusTransitionFacts facts)
    {
        if (!Enum.IsDefined(requestedStatus))
        {
            return Deny(TaskStatusTransitionDenialReason.InvalidTargetStatus);
        }

        return requestedStatus switch
        {
            TaskStatus.NotReady or TaskStatus.Prepared => Allow(),
            TaskStatus.Archived when facts.CurrentStatus == TaskStatus.Completed =>
                Deny(TaskStatusTransitionDenialReason.CompletedCannotArchive),
            TaskStatus.Archived => Allow(),
            TaskStatus.InProgress => EvaluateStart(facts),
            TaskStatus.Completed => EvaluateCompletion(facts),
            _ => Deny(TaskStatusTransitionDenialReason.InvalidTargetStatus)
        };
    }

    public static bool IsValidRestoreStatusHistoryEntry(
        TaskStatusHistoryEntry? entry,
        DateTimeOffset now)
    {
        if (entry is null ||
            !Enum.IsDefined(entry.Status) ||
            entry.Status == TaskStatus.Archived)
        {
            return false;
        }

        var latestAllowed = now > DateTimeOffset.MaxValue - RestoreStatusClockSkewTolerance
            ? DateTimeOffset.MaxValue
            : now + RestoreStatusClockSkewTolerance;
        return entry.ChangedAt <= latestAllowed;
    }

    public static TaskStatus NormalizeRestoreStatusAfterArchive(
        IEnumerable<TaskStatusHistoryEntry?>? history,
        DateTimeOffset now)
    {
        TaskStatusHistoryEntry? selected = null;
        var selectedIndex = -1;
        var index = 0;

        foreach (var entry in history ?? Array.Empty<TaskStatusHistoryEntry?>())
        {
            if (IsValidRestoreStatusHistoryEntry(entry, now) &&
                (selected is null ||
                 entry!.ChangedAt > selected.ChangedAt ||
                 entry.ChangedAt == selected.ChangedAt && index > selectedIndex))
            {
                selected = entry;
                selectedIndex = index;
            }

            index++;
        }

        return selected?.Status switch
        {
            TaskStatus.NotReady => TaskStatus.NotReady,
            TaskStatus.Prepared => TaskStatus.Prepared,
            TaskStatus.InProgress => TaskStatus.Prepared,
            TaskStatus.Completed => TaskStatus.NotReady,
            _ => TaskStatus.NotReady
        };
    }

    private static TaskStatusTransitionEvaluation EvaluateStart(TaskStatusTransitionFacts facts)
    {
        if (IsTerminal(facts.CurrentStatus))
        {
            return Deny(TaskStatusTransitionDenialReason.TerminalCannotStart);
        }

        if (!facts.IsGraphAvailable)
        {
            return Deny(TaskStatusTransitionDenialReason.GraphUnavailableForStart);
        }

        return facts.PlannedBeginIsFuture
            ? Deny(TaskStatusTransitionDenialReason.FutureDatePreventsStart)
            : Allow();
    }

    private static TaskStatusTransitionEvaluation EvaluateCompletion(TaskStatusTransitionFacts facts)
    {
        if (IsTerminal(facts.CurrentStatus))
        {
            return Deny(TaskStatusTransitionDenialReason.TerminalCannotComplete);
        }

        if (!facts.IsGraphAvailable)
        {
            return Deny(TaskStatusTransitionDenialReason.GraphUnavailableForCompletion);
        }

        return facts.CompletionCriteriaSatisfied
            ? Allow()
            : Deny(TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete);
    }

    private static bool IsTerminal(TaskStatus status) =>
        status is TaskStatus.Completed or TaskStatus.Archived;

    private static TaskStatusTransitionEvaluation Allow() =>
        new(true, TaskStatusTransitionDenialReason.None);

    private static TaskStatusTransitionEvaluation Deny(TaskStatusTransitionDenialReason reason) =>
        new(false, reason);
}
