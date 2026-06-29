using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public sealed record TaskOperationResult
{
    public bool Success { get; init; }
    public TaskOperationDeniedReason? DeniedReason { get; init; }
    public IReadOnlyList<TaskItem> ChangedTasks { get; init; } = Array.Empty<TaskItem>();
    public TaskAvailabilityAnalysis? Before { get; init; }
    public TaskAvailabilityAnalysis? After { get; init; }
    public TaskGraphValidationReport? Validation { get; init; }

    public static TaskOperationResult Succeeded(
        IReadOnlyList<TaskItem> changedTasks,
        TaskAvailabilityAnalysis? before,
        TaskAvailabilityAnalysis? after,
        TaskGraphValidationReport? validation) => new()
        {
            Success = true,
            ChangedTasks = changedTasks,
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
}

public sealed record TaskOperationDeniedReason
{
    public TaskOperationDeniedKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? TaskId { get; init; }
    public DomainTaskStatus? RequestedStatus { get; init; }
    public string? CriterionId { get; init; }

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
}

public enum TaskOperationDeniedKind
{
    ValidationFailed,
    TaskNotFound,
    CriterionNotFound,
    StatusTransitionDenied,
    CompletedCriteriaImmutable,
    StorageFailed
}
