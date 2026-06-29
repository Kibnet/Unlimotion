using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public sealed class TaskAvailabilityAnalyzer
{
    private readonly TaskAvailabilityService _service;

    public TaskAvailabilityAnalyzer(IEnumerable<TaskItem> tasks)
    {
        _service = new TaskAvailabilityService(tasks);
    }

    public IReadOnlyCollection<TaskItem> Tasks => _service.Tasks;

    public bool TryGetTask(string taskId, out TaskItem? task) =>
        _service.TryGetTask(taskId, out task);

    public IReadOnlyList<TaskAvailabilityAnalysis> AnalyzeAll() =>
        _service.AnalyzeAll();

    public TaskAvailabilityAnalysis Analyze(string taskId) =>
        _service.Analyze(taskId);

    public TaskAvailabilityAnalysis Analyze(TaskItem task) =>
        _service.Analyze(task);

    public TaskGraphValidationResult Validate() =>
        _service.Validate();
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
