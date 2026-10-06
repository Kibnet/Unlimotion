using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public sealed class TaskAvailabilityService
{
    private readonly IReadOnlyList<TaskItem> _allTasks;
    private readonly IReadOnlyDictionary<string, TaskItem> _tasks;
    private readonly DateTimeOffset? _evaluatedAt;
    private readonly CancellationToken _cancellationToken;

    public TaskAvailabilityService(IEnumerable<TaskItem> tasks, DateTimeOffset? evaluatedAt = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _evaluatedAt = evaluatedAt;
        _cancellationToken = cancellationToken;

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

        _cancellationToken.ThrowIfCancellationRequested();

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

        var plannedBeginIsFuture = task.PlannedBeginDateTime > (_evaluatedAt ?? DateTimeOffset.UtcNow);
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

        var isTerminal = task.Status is DomainTaskStatus.Completed or DomainTaskStatus.Archived;
        if (task.Status == DomainTaskStatus.Completed)
        {
            reasons.Add(new TaskAvailabilityReason
            {
                Kind = TaskAvailabilityReasonKind.AlreadyCompleted,
                SubjectId = task.Id,
                SubjectTitle = task.Title,
                Details = "Task is already completed."
            });
        }
        else if (task.Status == DomainTaskStatus.Archived)
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

    public TaskStatusTransitionDecision EvaluateStatusTransition(TaskItem task, DomainTaskStatus requestedStatus)
    {
        ArgumentNullException.ThrowIfNull(task);

        var analysis = Analyze(task);
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            requestedStatus,
            new TaskStatusTransitionFacts(
                task.Status,
                analysis.IsCanBeCompleted,
                analysis.PlannedBeginIsFuture,
                analysis.CompletionCriteriaSatisfied));

        if (evaluation.IsAllowed)
        {
            return TaskStatusTransitionDecision.Allow(analysis, evaluation);
        }

        var denialMessage = requestedStatus switch
        {
            DomainTaskStatus.Archived => $"Task '{task.Id}' cannot move to Archived from its current status.",
            DomainTaskStatus.InProgress => $"Task '{task.Id}' cannot move to InProgress because it is not startable.",
            DomainTaskStatus.Completed => $"Task '{task.Id}' cannot move to Completed because it is not completable.",
            _ => $"Task '{task.Id}' cannot move to {requestedStatus}."
        };
        return TaskStatusTransitionDecision.Deny(analysis, evaluation, denialMessage);
    }

    public TaskGraphValidationResult Validate()
    {
        var referenceIssues = new List<TaskGraphReferenceIssue>();
        foreach (var task in _tasks.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ValidateCompletionCriteria(referenceIssues, task);
            ValidateRelation(referenceIssues, task, nameof(TaskItem.ContainsTasks), task.ContainsTasks, nameof(TaskItem.ParentTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.ParentTasks), task.ParentTasks, nameof(TaskItem.ContainsTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.BlocksTasks), task.BlocksTasks, nameof(TaskItem.BlockedByTasks));
            ValidateRelation(referenceIssues, task, nameof(TaskItem.BlockedByTasks), task.BlockedByTasks, nameof(TaskItem.BlocksTasks));
        }

        ValidateContainmentCycles(referenceIssues);
        ValidateDependencyCycles(referenceIssues);

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

    private void ValidateContainmentCycles(ICollection<TaskGraphReferenceIssue> issues)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var start in _tasks.Values.OrderBy(static task => task.Id, StringComparer.Ordinal))
        {
            if (states.ContainsKey(start.Id))
            {
                continue;
            }

            states[start.Id] = 1;
            var stack = new Stack<ContainmentTraversalFrame>();
            stack.Push(CreateContainmentFrame(start));
            while (stack.Count > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var frame = stack.Pop();
                if (frame.NextChildIndex >= frame.ChildIds.Length)
                {
                    states[frame.TaskId] = 2;
                    continue;
                }

                var childId = frame.ChildIds[frame.NextChildIndex];
                stack.Push(frame with { NextChildIndex = frame.NextChildIndex + 1 });
                if (!_tasks.TryGetValue(childId, out var child) ||
                    string.Equals(frame.TaskId, childId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!states.TryGetValue(childId, out var childState))
                {
                    states[childId] = 1;
                    stack.Push(CreateContainmentFrame(child));
                    continue;
                }

                if (childState == 1)
                {
                    var source = _tasks[frame.TaskId];
                    issues.Add(new TaskGraphReferenceIssue
                    {
                        Kind = TaskGraphReferenceIssueKind.ContainmentCycle,
                        SourceTaskId = source.Id,
                        SourceTaskTitle = source.Title,
                        Relation = nameof(TaskItem.ContainsTasks),
                        TargetTaskId = child.Id,
                        TargetTaskTitle = child.Title,
                        InverseRelation = nameof(TaskItem.ParentTasks),
                        Details = $"{nameof(TaskItem.ContainsTasks)} contains a cycle through task '{child.Id}'."
                    });
                }
            }
        }
    }

    private void ValidateDependencyCycles(ICollection<TaskGraphReferenceIssue> issues)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var start in _tasks.Values.OrderBy(static task => task.Id, StringComparer.Ordinal))
        {
            if (states.ContainsKey(start.Id))
            {
                continue;
            }

            var stack = new Stack<ContainmentTraversalFrame>();
            states[start.Id] = 1;
            stack.Push(Frame(start));
            while (stack.TryPop(out var frame))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (frame.NextChildIndex == frame.ChildIds.Length)
                {
                    states[frame.TaskId] = 2;
                    continue;
                }
                stack.Push(frame with { NextChildIndex = frame.NextChildIndex + 1 });
                var blockedId = frame.ChildIds[frame.NextChildIndex];
                if (!_tasks.TryGetValue(blockedId, out var blocked)) continue;
                if (!states.TryGetValue(blockedId, out var state))
                {
                    states[blockedId] = 1;
                    stack.Push(Frame(blocked));
                    continue;
                }
                if (state == 1)
                {
                    var current = _tasks[frame.TaskId];
                    issues.Add(new TaskGraphReferenceIssue
                    {
                        Kind = TaskGraphReferenceIssueKind.DependencyCycle,
                        SourceTaskId = current.Id,
                        SourceTaskTitle = current.Title,
                        Relation = nameof(TaskItem.BlocksTasks),
                        TargetTaskId = blocked.Id,
                        TargetTaskTitle = blocked.Title,
                        InverseRelation = nameof(TaskItem.BlockedByTasks),
                        Details = $"{nameof(TaskItem.BlocksTasks)} contains a dependency cycle through task '{blocked.Id}'."
                    });
                }
            }

        }
        static ContainmentTraversalFrame Frame(TaskItem task) => new(task.Id,
            DistinctIds(task.BlocksTasks).Order(StringComparer.Ordinal).ToArray(), 0);
    }

    private static ContainmentTraversalFrame CreateContainmentFrame(TaskItem task) => new(
        task.Id,
        task.ContainsTasks?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>(),
        0);

    private readonly record struct ContainmentTraversalFrame(
        string TaskId,
        string[] ChildIds,
        int NextChildIndex);

    private void ValidateRelation(
        ICollection<TaskGraphReferenceIssue> issues,
        TaskItem source,
        string relationName,
        IEnumerable<string>? targetIds,
        string inverseRelationName)
    {
        var relationIds = targetIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? Array.Empty<string>();

        foreach (var duplicate in relationIds
                     .GroupBy(static id => id, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            issues.Add(new TaskGraphReferenceIssue
            {
                Kind = TaskGraphReferenceIssueKind.DuplicateRelation,
                SourceTaskId = source.Id,
                SourceTaskTitle = source.Title,
                Relation = relationName,
                TargetTaskId = duplicate.Key,
                Details = $"{relationName} contains task '{duplicate.Key}' {duplicate.Count()} times."
            });
        }

        foreach (var targetId in relationIds.Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(source.Id, targetId, StringComparison.Ordinal))
            {
                issues.Add(new TaskGraphReferenceIssue
                {
                    Kind = TaskGraphReferenceIssueKind.SelfRelation,
                    SourceTaskId = source.Id,
                    SourceTaskTitle = source.Title,
                    Relation = relationName,
                    TargetTaskId = targetId,
                    Details = $"{relationName} contains a self-reference to task '{targetId}'."
                });
                continue;
            }

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

    private static void ValidateCompletionCriteria(
        ICollection<TaskGraphReferenceIssue> issues,
        TaskItem task)
    {
        foreach (var duplicate in (task.CompletionCriteria ?? [])
                     .Where(static criterion => !string.IsNullOrWhiteSpace(criterion.Id))
                     .GroupBy(static criterion => criterion.Id, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            issues.Add(new TaskGraphReferenceIssue
            {
                Kind = TaskGraphReferenceIssueKind.DuplicateCriterionId,
                SourceTaskId = task.Id,
                SourceTaskTitle = task.Title,
                Relation = nameof(TaskItem.CompletionCriteria),
                TargetTaskId = duplicate.Key,
                Details = $"CompletionCriteria contains criterion id '{duplicate.Key}' {duplicate.Count()} times."
            });
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
        var pending = new Stack<(TaskItem Task, bool Inherited)>();
        pending.Push((taskWithRelations, inherited));
        var initial = true;
        while (pending.TryPop(out var frame))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!initial && !visitedParentIds.Add(frame.Task.Id)) continue;
            initial = false;
            foreach (var blockerId in DistinctIds(frame.Task.BlockedByTasks))
            {
                if (!_tasks.TryGetValue(blockerId, out var blockerTask) || !blockerTask.Status.IsIncompleteForAvailability()) continue;
                reasons.Add(new TaskAvailabilityReason
                {
                    Kind = frame.Inherited ? TaskAvailabilityReasonKind.IncompleteInheritedBlocker : TaskAvailabilityReasonKind.IncompleteDirectBlocker,
                    SubjectId = blockerTask.Id,
                    SubjectTitle = blockerTask.Title,
                    SubjectStatus = blockerTask.Status,
                    SourceTaskId = frame.Task.Id,
                    SourceTaskTitle = frame.Task.Title,
                    Details = frame.Inherited ? $"Parent task '{frame.Task.Id}' has incomplete blocker." : "Task has incomplete direct blocker."
                });
            }
            foreach (var parentId in DistinctIds(frame.Task.ParentTasks).Reverse())
                if (_tasks.TryGetValue(parentId, out var parentTask)) pending.Push((parentTask, true));
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

public sealed record TaskStatusTransitionDecision
{
    public bool Allowed { get; init; }
    public string? DenialMessage { get; init; }
    public TaskAvailabilityAnalysis Analysis { get; init; } = new();
    public TaskStatusTransitionEvaluation Evaluation { get; init; }

    public static TaskStatusTransitionDecision Allow(TaskAvailabilityAnalysis analysis) =>
        Allow(
            analysis,
            new TaskStatusTransitionEvaluation(
                IsAllowed: true,
                TaskStatusTransitionDenialReason.None));

    public static TaskStatusTransitionDecision Allow(
        TaskAvailabilityAnalysis analysis,
        TaskStatusTransitionEvaluation evaluation) => new()
    {
        Allowed = true,
        Analysis = analysis,
        Evaluation = evaluation
    };

    public static TaskStatusTransitionDecision Deny(TaskAvailabilityAnalysis analysis, string message) =>
        Deny(
            analysis,
            new TaskStatusTransitionEvaluation(
                IsAllowed: false,
                TaskStatusTransitionDenialReason.None),
            message);

    public static TaskStatusTransitionDecision Deny(
        TaskAvailabilityAnalysis analysis,
        TaskStatusTransitionEvaluation evaluation,
        string message) => new()
    {
        Allowed = false,
        DenialMessage = message,
        Analysis = analysis,
        Evaluation = evaluation
    };
}
