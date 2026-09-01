using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public sealed record TaskOperationResult
{
    public bool Success { get; init; }
    public TaskOperationDeniedReason? DeniedReason { get; init; }
    public IReadOnlyList<TaskItem> ChangedTasks { get; init; } = Array.Empty<TaskItem>();
    public TaskItem? AuthoritativeTask { get; init; }
    public TaskAvailabilityAnalysis? Before { get; init; }
    public TaskAvailabilityAnalysis? After { get; init; }
    public TaskGraphValidationReport? Validation { get; init; }
    public long StorageRevision { get; init; }

    public static TaskOperationResult Succeeded(
        IReadOnlyList<TaskItem> changedTasks,
        TaskAvailabilityAnalysis? before,
        TaskAvailabilityAnalysis? after,
        TaskGraphValidationReport? validation) =>
        Succeeded(changedTasks, before, after, validation, authoritativeTask: null);

    public static TaskOperationResult Succeeded(
        IReadOnlyList<TaskItem> changedTasks,
        TaskAvailabilityAnalysis? before,
        TaskAvailabilityAnalysis? after,
        TaskGraphValidationReport? validation,
        TaskItem? authoritativeTask) => new()
        {
            Success = true,
            ChangedTasks = changedTasks,
            AuthoritativeTask = authoritativeTask,
            Before = before,
            After = after,
            Validation = validation
        };

    public static TaskOperationResult Denied(
        TaskOperationDeniedReason reason,
        TaskAvailabilityAnalysis? before = null,
        TaskAvailabilityAnalysis? after = null,
        TaskGraphValidationReport? validation = null) => new()
        {
            Success = false,
            DeniedReason = reason,
            Before = before,
            After = after,
            Validation = validation
        };

    public static TaskOperationResult DeniedWithAuthoritativeTask(
        TaskOperationDeniedReason reason,
        TaskItem? authoritativeTask,
        TaskAvailabilityAnalysis? before = null,
        TaskAvailabilityAnalysis? after = null,
        TaskGraphValidationReport? validation = null) => new()
        {
            Success = false,
            DeniedReason = reason,
            AuthoritativeTask = authoritativeTask,
            Before = before,
            After = after,
            Validation = validation
        };
}

public sealed record TaskOperationDeniedReason
{
    public TaskOperationDeniedKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? TaskId { get; init; }
    public DomainTaskStatus? RequestedStatus { get; init; }
    public string? CriterionId { get; init; }
    public TaskStatusTransitionDenialReason? StatusTransitionReason { get; init; }

    public static TaskOperationDeniedReason Create(
        TaskOperationDeniedKind kind,
        string message,
        string? taskId = null,
        DomainTaskStatus? requestedStatus = null,
        string? criterionId = null) => new()
        {
            Kind = kind,
            Message = message,
            TaskId = taskId,
            RequestedStatus = requestedStatus,
            CriterionId = criterionId
        };

    public static TaskOperationDeniedReason CreateWithStatusTransition(
        TaskOperationDeniedKind kind,
        string message,
        TaskStatusTransitionDenialReason? statusTransitionReason,
        string? taskId = null,
        DomainTaskStatus? requestedStatus = null,
        string? criterionId = null) => new()
        {
            Kind = kind,
            Message = message,
            TaskId = taskId,
            RequestedStatus = requestedStatus,
            CriterionId = criterionId,
            StatusTransitionReason = statusTransitionReason
        };
}

public enum TaskOperationDeniedKind
{
    ValidationFailed = 0,
    TaskNotFound = 1,
    CriterionNotFound = 2,
    StatusTransitionDenied = 3,
    CompletedCriteriaImmutable = 4,
    StorageFailed = 5,
    OutcomeUnknown = 6,
    StatusPreconditionFailed = 7
}
