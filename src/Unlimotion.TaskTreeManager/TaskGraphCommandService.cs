using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public sealed class TaskGraphCommandService
{
    private readonly IStorage _storage;

    public TaskGraphCommandService(IStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public Func<TaskItem, string>? StatusAuthorProvider { get; set; }

    public Task<TaskOperationResult> TrySetStatusAsync(
        string taskId,
        DomainTaskStatus requestedStatus,
        string? author = null) =>
        ExecuteWriteAsync(() => TrySetStatusCoreAsync(taskId, requestedStatus, author));

    public Task<TaskOperationResult> TrySetCriterionAsync(
        string taskId,
        string criterionId,
        bool satisfied,
        string? author = null) =>
        ExecuteWriteAsync(() => TrySetCriterionCoreAsync(taskId, criterionId, satisfied, author));

    private async Task<TaskOperationResult> TrySetStatusCoreAsync(
        string taskId,
        DomainTaskStatus requestedStatus,
        string? author)
    {
        var readResult = await ReadGraphForWriteAsync();
        if (readResult.Result != null)
        {
            return readResult.Result;
        }

        var graph = readResult.Graph!;
        var validation = TaskGraphValidationReport.From(graph);
        if (!validation.IsWriteSafe)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.ValidationFailed,
                    validation.BuildWriteSafetyMessage(),
                    taskId,
                    requestedStatus),
                validation: validation);
        }

        if (!graph.TasksById.TryGetValue(taskId, out var task))
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.TaskNotFound,
                    $"Task '{taskId}' was not found.",
                    taskId,
                    requestedStatus),
                validation: validation);
        }

        var rules = new TaskAvailabilityService(graph.Tasks);
        var before = rules.Analyze(task);
        if (task.Status == requestedStatus)
        {
            return TaskOperationResult.Succeeded(Array.Empty<TaskItem>(), before, before, validation);
        }

        var transition = rules.EvaluateStatusTransition(task, requestedStatus);
        if (!transition.Allowed)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StatusTransitionDenied,
                    transition.DenialMessage ?? $"Task '{task.Id}' cannot move to {requestedStatus}.",
                    task.Id,
                    requestedStatus),
                before,
                validation: validation);
        }

        var change = CloneForUpdate(task);
        change.Status = requestedStatus;

        IReadOnlyList<TaskItem> changedTasks;
        TaskOperationReadResult afterRead;
        try
        {
            var manager = CreateManager(author);
            changedTasks = await manager.UpdateTask(change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return CreateOutcomeUnknownResult(
                ex,
                task.Id,
                requestedStatus,
                criterionId: null,
                before,
                validation);
        }

        if (afterRead.Result != null)
        {
            return CreateOutcomeUnknownResult(
                new InvalidOperationException(afterRead.Result.DeniedReason?.Message ?? "Post-write graph read failed."),
                task.Id,
                requestedStatus,
                criterionId: null,
                before,
                validation);
        }

        var afterGraph = afterRead.Graph!;
        if (!afterGraph.TasksById.TryGetValue(task.Id, out var afterTask) || afterTask.Status != requestedStatus)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.OutcomeUnknown,
                    $"Task '{task.Id}' was not persisted with requested status {requestedStatus}.",
                    task.Id,
                    requestedStatus),
                before,
                validation: validation);
        }

        var after = new TaskAvailabilityService(afterGraph.Tasks).Analyze(afterTask);
        return TaskOperationResult.Succeeded(changedTasks, before, after, validation);
    }

    private async Task<TaskOperationResult> TrySetCriterionCoreAsync(
        string taskId,
        string criterionId,
        bool satisfied,
        string? author)
    {
        var readResult = await ReadGraphForWriteAsync();
        if (readResult.Result != null)
        {
            return readResult.Result;
        }

        var graph = readResult.Graph!;
        var validation = TaskGraphValidationReport.From(graph);
        if (!validation.IsWriteSafe)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.ValidationFailed,
                    validation.BuildWriteSafetyMessage(),
                    taskId,
                    criterionId: criterionId),
                validation: validation);
        }

        if (!graph.TasksById.TryGetValue(taskId, out var task))
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.TaskNotFound,
                    $"Task '{taskId}' was not found.",
                    taskId,
                    criterionId: criterionId),
                validation: validation);
        }

        var rules = new TaskAvailabilityService(graph.Tasks);
        var before = rules.Analyze(task);
        if (task.Status == DomainTaskStatus.Completed)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.CompletedCriteriaImmutable,
                    $"Task '{task.Id}' is completed, so its completion criteria cannot be changed.",
                    task.Id,
                    criterionId: criterionId),
                before,
                validation: validation);
        }

        var change = CloneForUpdate(task);
        var criterion = change.CompletionCriteria.FirstOrDefault(criterion =>
            string.Equals(criterion.Id, criterionId, StringComparison.Ordinal));
        if (criterion == null)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.CriterionNotFound,
                    $"Criterion '{criterionId}' was not found in task '{task.Id}'.",
                    task.Id,
                    criterionId: criterionId),
                before,
                validation: validation);
        }

        if (criterion.IsSatisfied == satisfied)
        {
            return TaskOperationResult.Succeeded(Array.Empty<TaskItem>(), before, before, validation);
        }

        criterion.IsSatisfied = satisfied;

        IReadOnlyList<TaskItem> changedTasks;
        TaskOperationReadResult afterRead;
        try
        {
            var manager = CreateManager(author);
            changedTasks = await manager.UpdateTask(change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return CreateOutcomeUnknownResult(
                ex,
                task.Id,
                requestedStatus: null,
                criterionId,
                before,
                validation);
        }

        if (afterRead.Result != null)
        {
            return CreateOutcomeUnknownResult(
                new InvalidOperationException(afterRead.Result.DeniedReason?.Message ?? "Post-write graph read failed."),
                task.Id,
                requestedStatus: null,
                criterionId,
                before,
                validation);
        }

        var afterGraph = afterRead.Graph!;
        if (!afterGraph.TasksById.TryGetValue(task.Id, out var afterTask))
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.OutcomeUnknown,
                    $"Task '{task.Id}' was not found after criterion update.",
                    task.Id,
                    criterionId: criterionId),
                before,
                validation: validation);
        }

        var afterCriterion = afterTask.CompletionCriteria.FirstOrDefault(item =>
            string.Equals(item.Id, criterionId, StringComparison.Ordinal));
        if (afterCriterion?.IsSatisfied != satisfied)
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.OutcomeUnknown,
                    $"Criterion '{criterionId}' in task '{task.Id}' was not persisted with requested value.",
                    task.Id,
                    criterionId: criterionId),
                before,
                validation: validation);
        }

        var after = new TaskAvailabilityService(afterGraph.Tasks).Analyze(afterTask);
        return TaskOperationResult.Succeeded(changedTasks, before, after, validation);
    }

    private async Task<TaskOperationReadResult> ReadGraphForWriteAsync()
    {
        if (_storage is not ITaskGraphDiagnosticStorage diagnosticStorage)
        {
            return new TaskOperationReadResult(null, TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StorageFailed,
                    "Storage does not support diagnostic graph reads required for write commands.")));
        }

        try
        {
            return new TaskOperationReadResult(await diagnosticStorage.ReadGraphAsync(), null);
        }
        catch (Exception ex)
        {
            return new TaskOperationReadResult(null, TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StorageFailed,
                    ex.Message)));
        }
    }

    private async Task<TaskOperationResult> ExecuteWriteAsync(Func<Task<TaskOperationResult>> operation)
    {
        try
        {
            if (_storage is ITaskGraphWriteLock writeLock)
            {
                return await writeLock.WithWriteLockAsync(operation);
            }

            return await operation();
        }
        catch (Exception ex)
        {
            return CreateStorageFailedResult(
                ex,
                taskId: null,
                requestedStatus: null,
                criterionId: null,
                before: null,
                validation: null);
        }
    }

    private static TaskOperationResult CreateStorageFailedResult(
        Exception ex,
        string? taskId,
        DomainTaskStatus? requestedStatus,
        string? criterionId,
        TaskAvailabilityAnalysis? before,
        TaskGraphValidationReport? validation) =>
        TaskOperationResult.Denied(
            TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.StorageFailed,
                $"Task graph write failed: {ex.Message}",
                taskId,
                requestedStatus,
                criterionId),
            before,
            validation: validation);

    private static TaskOperationResult CreateOutcomeUnknownResult(
        Exception ex,
        string? taskId,
        DomainTaskStatus? requestedStatus,
        string? criterionId,
        TaskAvailabilityAnalysis? before,
        TaskGraphValidationReport? validation) =>
        TaskOperationResult.Denied(
            TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.OutcomeUnknown,
                $"Task graph write may have been persisted, but the final outcome could not be verified: {ex.Message}",
                taskId,
                requestedStatus,
                criterionId),
            before,
            validation: validation);

    private TaskTreeManager CreateManager(string? author) => new(_storage)
    {
        StatusAuthorProvider = task =>
            TaskItem.NormalizeAuthor(author ?? StatusAuthorProvider?.Invoke(task) ?? task.UserId ?? "local-user")
    };

    private static TaskItem CloneForUpdate(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory?.Select(CloneStatusHistoryEntry).ToList() ?? new List<TaskStatusHistoryEntry>(),
        CompletionCriteria = task.CompletionCriteria?.Select(CloneCriterion).ToList() ?? new List<TaskCompletionCriterion>(),
        ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
        ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
        BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
        BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>(),
        Repeater = CloneRepeater(task.Repeater)
    };

    private static TaskCompletionCriterion CloneCriterion(TaskCompletionCriterion criterion) => new()
    {
        Id = criterion.Id,
        Text = criterion.Text,
        IsSatisfied = criterion.IsSatisfied,
        ExtensionData = criterion.ExtensionData
    };

    private static TaskStatusHistoryEntry CloneStatusHistoryEntry(TaskStatusHistoryEntry entry) => new()
    {
        Status = entry.Status,
        ChangedAt = entry.ChangedAt,
        Author = entry.Author,
        ExtensionData = entry.ExtensionData
    };

    private static RepeaterPattern? CloneRepeater(RepeaterPattern? repeater) =>
        repeater == null
            ? null
            : new RepeaterPattern
            {
                Type = repeater.Type,
                Period = repeater.Period,
                AfterComplete = repeater.AfterComplete,
                Pattern = repeater.Pattern?.ToList()!,
                ExtensionData = repeater.ExtensionData?.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.DeepClone())
            };

    private sealed record TaskOperationReadResult(TaskGraphReadResult? Graph, TaskOperationResult? Result);
}
