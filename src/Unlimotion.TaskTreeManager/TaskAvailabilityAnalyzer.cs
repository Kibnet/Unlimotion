using System;
using System.Collections.Generic;
using System.Linq;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public sealed class TaskAvailabilityAnalyzer
{
    private readonly IReadOnlyList<TaskItem> _allTasks;
    private readonly IReadOnlyDictionary<string, TaskItem> _tasks;

    public TaskAvailabilityAnalyzer(IEnumerable<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        _allTasks = tasks.ToArray();
        _tasks = _allTasks
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(static task => task.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<TaskItem> Tasks => _tasks.Values.ToArray();

    public bool TryGetTask(string taskId, out TaskItem? task) =>
        _tasks.TryGetValue(taskId, out task);

    public IReadOnlyList<TaskAvailabilityAnalysis> AnalyzeAll() =>
        _tasks.Values
            .OrderBy(static task => task.Title, StringComparer.CurrentCulture)
            .ThenBy(static task => task.Id, StringComparer.Ordinal)
            .Select(Analyze)
            .ToArray();

    public TaskAvailabilityAnalysis Analyze(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            throw new KeyNotFoundException($"Task '{taskId}' was not found.");
        }

        return Analyze(task);
    }

    public TaskAvailabilityAnalysis Analyze(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var reasons = new List<TaskAvailabilityReason>();
        CollectIncompleteContainedTasks(task, reasons);
        CollectIncompleteBlockers(task, inherited: false, reasons, new HashSet<string>(StringComparer.Ordinal));

        var hasGraphBlockers = reasons.Any(static reason => reason.Kind is
            TaskAvailabilityReasonKind.IncompleteContainedTask or
            TaskAvailabilityReasonKind.IncompleteDirectBlocker or
            TaskAvailabilityReasonKind.IncompleteInheritedBlocker);

        var isCanBeCompleted = !hasGraphBlockers;
        var completionCriteriaSatisfied = AreCompletionCriteriaSatisfied(task);
        if (!completionCriteriaSatisfied)
        {
            AddUnsatisfiedCriteriaReasons(task, reasons);
        }

        var plannedBeginIsFuture = task.PlannedBeginDateTime > DateTimeOffset.UtcNow;
        if (plannedBeginIsFuture)
        {
            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.FuturePlannedBegin,
                SubjectId = task.Id,
                SubjectTitle = task.Title,
                Details = $"Planned begin date is {task.PlannedBeginDateTime:O}."
            });
        }

        var isTerminal = task.Status is Unlimotion.Domain.TaskStatus.Completed or Unlimotion.Domain.TaskStatus.Archived;
        if (task.Status == Unlimotion.Domain.TaskStatus.Completed)
        {
            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.AlreadyCompleted,
                SubjectId = task.Id,
                SubjectTitle = task.Title,
                Details = "Task is already completed."
            });
        }
        else if (task.Status == Unlimotion.Domain.TaskStatus.Archived)
        {
            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.Archived,
                SubjectId = task.Id,
                SubjectTitle = task.Title,
                Details = "Task is archived."
            });
        }

        return new TaskAvailabilityAnalysis
        {
            TaskId = task.Id,
            Title = task.Title,
            Status = task.Status,
            StoredIsCanBeCompleted = task.IsCanBeCompleted,
            IsCanBeCompleted = isCanBeCompleted,
            CanStart = isCanBeCompleted && !plannedBeginIsFuture && !isTerminal,
            CanComplete = isCanBeCompleted && completionCriteriaSatisfied && !isTerminal,
            CompletionCriteriaSatisfied = completionCriteriaSatisfied,
            PlannedBeginIsFuture = plannedBeginIsFuture,
            Reasons = reasons
        };
    }

    public TaskGraphValidationResult Validate()
    {
        var referenceIssues = new List<TaskGraphReferenceIssue>();
        foreach (var task in _tasks.Values)
        {
            ValidateRelation(referenceIssues, task, nameof(TaskItem.ContainsTasks), task.ContainsTasks, nameof(TaskItem.ParentTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.ParentTasks), task.ParentTasks, nameof(TaskItem.ContainsTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.BlocksTasks), task.BlocksTasks, nameof(TaskItem.BlockedByTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.BlockedByTasks), task.BlockedByTasks, nameof(TaskItem.BlocksTasks));
        }

        var availabilityMismatches = _tasks.Values
            .Select(Analyze)
            .Where(analysis => analysis.StoredIsCanBeCompleted != analysis.IsCanBeCompleted)
            .Select(static analysis => new TaskAvailabilityMismatch
            {
                TaskId = analysis.TaskId,
                Title = analysis.Title,
                StoredIsCanBeCompleted = analysis.StoredIsCanBeCompleted,
                ComputedIsCanBeCompleted = analysis.IsCanBeCompleted
            })
            .ToArray();

        var duplicateIds = _allTasks
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(static task => task.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => new TaskDuplicateIdIssue
            {
                TaskId = group.Key,
                Count = group.Count()
            })
            .ToArray();

        return new TaskGraphValidationResult
        {
            TaskCount = _tasks.Count,
            ReferenceIssues = referenceIssues,
            AvailabilityMismatches = availabilityMismatches,
            DuplicateIdIssues = duplicateIds
        };
    }

    private void ValidateRelation(
        ICollection<TaskGraphReferenceIssue> issues,
        TaskItem source,
        string relationName,
        IEnumerable<string>? targetIds,
        string inverseRelationName)
    {
        foreach (var targetId in DistinctIds(targetIds))
        {
            if (!_tasks.TryGetValue(targetId, out var target))
            {
                issues.Add(new TaskGraphReferenceIssue
                {
                    Kind = TaskGraphReferenceIssueKind.MissingReference,
                    SourceTaskId = source.Id,
                    SourceTaskTitle = source.Title,
                    Relation = relationName,
                    TargetTaskId = targetId,
                    Details = $"{relationName} references missing task '{targetId}'."
                });
                continue;
            }

            var inverseIds = GetRelationIds(target, inverseRelationName);
            if (!DistinctIds(inverseIds).Contains(source.Id, StringComparer.Ordinal))
            {
                issues.Add(new TaskGraphReferenceIssue
                {
                    Kind = TaskGraphReferenceIssueKind.MissingReverseLink,
                    SourceTaskId = source.Id,
                    SourceTaskTitle = source.Title,
                    Relation = relationName,
                    TargetTaskId = targetId,
                    TargetTaskTitle = target.Title,
                    InverseRelation = inverseRelationName,
                    Details = $"{relationName} -> {targetId} is missing reverse {inverseRelationName} -> {source.Id}."
                });
            }
        }
    }

    private void CollectIncompleteContainedTasks(TaskItem task, ICollection<TaskAvailabilityReason> reasons)
    {
        foreach (var childId in DistinctIds(task.ContainsTasks))
        {
            if (!_tasks.TryGetValue(childId, out var childTask) || !childTask.Status.IsIncompleteForAvailability())
            {
                continue;
            }

            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.IncompleteContainedTask,
                SubjectId = childTask.Id,
                SubjectTitle = childTask.Title,
                SubjectStatus = childTask.Status,
                SourceTaskId = task.Id,
                SourceTaskTitle = task.Title,
                Details = "Contained task is incomplete."
            });
        }
    }

    private void CollectIncompleteBlockers(
        TaskItem taskWithRelations,
        bool inherited,
        ICollection<TaskAvailabilityReason> reasons,
        ISet<string> visitedParentIds)
    {
        foreach (var blockerId in DistinctIds(taskWithRelations.BlockedByTasks))
        {
            if (!_tasks.TryGetValue(blockerId, out var blockerTask) || !blockerTask.Status.IsIncompleteForAvailability())
            {
                continue;
            }

            reasons.Add(new TaskAvailabilityReason
            {
                Kind = inherited
                    ? TaskAvailabilityReasonKind.IncompleteInheritedBlocker
                    : TaskAvailabilityReasonKind.IncompleteDirectBlocker,
                SubjectId = blockerTask.Id,
                SubjectTitle = blockerTask.Title,
                SubjectStatus = blockerTask.Status,
                SourceTaskId = taskWithRelations.Id,
                SourceTaskTitle = taskWithRelations.Title,
                Details = inherited
                    ? $"Parent task '{taskWithRelations.Id}' has incomplete blocker."
                    : "Task has incomplete direct blocker."
            });
        }

        foreach (var parentId in DistinctIds(taskWithRelations.ParentTasks))
        {
            if (!visitedParentIds.Add(parentId) || !_tasks.TryGetValue(parentId, out var parentTask))
            {
                continue;
            }

            CollectIncompleteBlockers(parentTask, inherited: true, reasons, visitedParentIds);
        }
    }

    private static bool AreCompletionCriteriaSatisfied(TaskItem task) =>
        task.CompletionCriteria?.All(static criterion => criterion.IsSatisfied) != false;

    private static void AddUnsatisfiedCriteriaReasons(TaskItem task, ICollection<TaskAvailabilityReason> reasons)
    {
        foreach (var criterion in task.CompletionCriteria?.Where(static criterion => !criterion.IsSatisfied) ?? Enumerable.Empty<TaskCompletionCriterion>())
        {
            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.UnsatisfiedCriterion,
                SubjectId = task.Id,
                SubjectTitle = task.Title,
                CriterionId = criterion.Id,
                Details = string.IsNullOrWhiteSpace(criterion.Text)
                    ? "Completion criterion is not satisfied."
                    : criterion.Text
            });
        }
    }

    private static IEnumerable<string> GetRelationIds(TaskItem task, string relationName) => relationName switch
    {
        nameof(TaskItem.ContainsTasks) => task.ContainsTasks ?? Enumerable.Empty<string>(),
        nameof(TaskItem.ParentTasks) => task.ParentTasks ?? Enumerable.Empty<string>(),
        nameof(TaskItem.BlocksTasks) => task.BlocksTasks ?? Enumerable.Empty<string>(),
        nameof(TaskItem.BlockedByTasks) => task.BlockedByTasks ?? Enumerable.Empty<string>(),
        _ => Enumerable.Empty<string>()
    };

    private static IEnumerable<string> DistinctIds(IEnumerable<string>? ids) =>
        ids?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
}

public sealed record TaskAvailabilityAnalysis
{
    public string TaskId { get; init; } = string.Empty;
    public string? Title { get; init; }
    public Unlimotion.Domain.TaskStatus Status { get; init; }
    public bool StoredIsCanBeCompleted { get; init; }
    public bool IsCanBeCompleted { get; init; }
    public bool CanStart { get; init; }
    public bool CanComplete { get; init; }
    public bool CompletionCriteriaSatisfied { get; init; }
    public bool PlannedBeginIsFuture { get; init; }
    public IReadOnlyList<TaskAvailabilityReason> Reasons { get; init; } = Array.Empty<TaskAvailabilityReason>();
}

public sealed record TaskAvailabilityReason
{
    public TaskAvailabilityReasonKind Kind { get; init; }
    public string SubjectId { get; init; } = string.Empty;
    public string? SubjectTitle { get; init; }
    public Unlimotion.Domain.TaskStatus? SubjectStatus { get; init; }
    public string? SourceTaskId { get; init; }
    public string? SourceTaskTitle { get; init; }
    public string? CriterionId { get; init; }
    public string Details { get; init; } = string.Empty;
}

public enum TaskAvailabilityReasonKind
{
    IncompleteContainedTask,
    IncompleteDirectBlocker,
    IncompleteInheritedBlocker,
    UnsatisfiedCriterion,
    FuturePlannedBegin,
    AlreadyCompleted,
    Archived
}

public sealed record TaskGraphValidationResult
{
    public int TaskCount { get; init; }
    public bool IsValid => ReferenceIssues.Count == 0 && AvailabilityMismatches.Count == 0 && DuplicateIdIssues.Count == 0;
    public IReadOnlyList<TaskGraphReferenceIssue> ReferenceIssues { get; init; } = Array.Empty<TaskGraphReferenceIssue>();
    public IReadOnlyList<TaskAvailabilityMismatch> AvailabilityMismatches { get; init; } = Array.Empty<TaskAvailabilityMismatch>();
    public IReadOnlyList<TaskDuplicateIdIssue> DuplicateIdIssues { get; init; } = Array.Empty<TaskDuplicateIdIssue>();
}

public sealed record TaskGraphReferenceIssue
{
    public TaskGraphReferenceIssueKind Kind { get; init; }
    public string SourceTaskId { get; init; } = string.Empty;
    public string? SourceTaskTitle { get; init; }
    public string Relation { get; init; } = string.Empty;
    public string TargetTaskId { get; init; } = string.Empty;
    public string? TargetTaskTitle { get; init; }
    public string? InverseRelation { get; init; }
    public string Details { get; init; } = string.Empty;
}

public enum TaskGraphReferenceIssueKind
{
    MissingReference,
    MissingReverseLink
}

public sealed record TaskAvailabilityMismatch
{
    public string TaskId { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool StoredIsCanBeCompleted { get; init; }
    public bool ComputedIsCanBeCompleted { get; init; }
}

public sealed record TaskDuplicateIdIssue
{
    public string TaskId { get; init; } = string.Empty;
    public int Count { get; init; }
}
