using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

public sealed class TaskGraphCommandService
{
    private readonly IStorage _storage;
    private long _lastReadRevision;
    private ITaskGraphWriteScope? _activeWriteScope;

    public TaskGraphCommandService(IStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public Func<TaskItem, string>? StatusAuthorProvider { get; set; }

    public Task<TaskOperationResult> TryClaimAsync(
        string taskId,
        string agentId,
        DomainTaskStatus expectedStatus) =>
        ExecuteWriteAsync(() => TryClaimCoreAsync(taskId, agentId, expectedStatus));

    public Task<TaskOperationResult> TryAddExecutionQuestionAsync(
        string taskId,
        string agentId,
        string leaseId,
        string text) =>
        ExecuteWriteAsync(() => TryMutateExecutionCoreAsync(
            taskId,
            agentId,
            leaseId,
            AgentExecutionMutation.Question(text)));

    public Task<TaskOperationResult> TryAnswerExecutionQuestionAsync(
        string taskId,
        string agentId,
        string leaseId,
        string questionId,
        string text) =>
        ExecuteWriteAsync(() => TryMutateExecutionCoreAsync(
            taskId,
            agentId,
            leaseId,
            AgentExecutionMutation.Answer(questionId, text)));

    public Task<TaskOperationResult> TrySetExecutionResultAsync(
        string taskId,
        string agentId,
        string leaseId,
        string summary,
        IReadOnlyList<string> links) =>
        ExecuteWriteAsync(() => TryMutateExecutionCoreAsync(
            taskId,
            agentId,
            leaseId,
            AgentExecutionMutation.Result(summary, links)));

    public Task<TaskOperationResult> TryCompleteExecutionAsync(
        string taskId,
        string agentId,
        string leaseId,
        string summary,
        IReadOnlyList<string> links) =>
        ExecuteWriteAsync(() => TryMutateExecutionCoreAsync(
            taskId,
            agentId,
            leaseId,
            AgentExecutionMutation.Complete(summary, links)));

    public Task<TaskOperationResult> TryReleaseExecutionAsync(
        string taskId,
        string agentId,
        string leaseId,
        string reason) =>
        ExecuteWriteAsync(() => TryMutateExecutionCoreAsync(
            taskId,
            agentId,
            leaseId,
            AgentExecutionMutation.Release(reason)));

    public Task<TaskOperationResult> TryCreateTaskAsync(
        string title,
        string? description,
        IReadOnlyList<string> parentIds,
        string? author = null) =>
        ExecuteWriteAsync(() => TryCreateTaskCoreAsync(title, description, parentIds, author));

    public Task<TaskOperationResult> TrySetStatusAsync(
        string taskId,
        DomainTaskStatus requestedStatus,
        string? author = null) =>
        ExecuteWriteAsync(() => TrySetStatusCoreAsync(
            taskId,
            requestedStatus,
            isUnarchive: false,
            author));

    private async Task<TaskOperationResult> TryClaimCoreAsync(
        string taskId,
        string agentId,
        DomainTaskStatus expectedStatus)
    {
        var normalizedAgentId = agentId.Trim();
        if (!IsValidExecutionText(normalizedAgentId, 200))
        {
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.InvalidArguments,
                "Agent id is invalid for agent execution.",
                taskId));
        }

        var readResult = await ReadGraphForWriteAsync();
        if (readResult.Result != null)
        {
            return readResult.Result;
        }

        var graph = readResult.Graph!;
        var validation = TaskGraphValidationReport.From(graph);
        if (!validation.IsWriteSafe)
        {
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.ValidationFailed,
                validation.BuildWriteSafetyMessage(),
                taskId,
                expectedStatus), validation: validation);
        }

        if (!graph.TasksById.TryGetValue(taskId, out var task))
        {
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.TaskNotFound,
                $"Task '{taskId}' was not found.",
                taskId,
                expectedStatus), validation: validation);
        }

        var rules = new TaskAvailabilityService(graph.Tasks);
        var before = rules.Analyze(task);
        if (task.Status != expectedStatus || expectedStatus != DomainTaskStatus.Prepared || !before.CanStart ||
            task.AgentExecution is { State: not AgentExecutionState.Released })
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.ClaimConflict,
                    $"Task '{task.Id}' cannot be claimed from its authoritative state.",
                    task.Id,
                    expectedStatus),
                CloneForUpdate(task),
                before,
                validation: validation);
        }

        var previousAttempts = task.AgentExecution?.PreviousAttempts?.ToList() ?? [];
        var auditTruncated = task.AgentExecution?.AuditTruncated == true;
        var markerState = AgentExecutionDescriptionRenderer.Inspect(task.Description, out var markerInspectionError);
        var expectedMarkerState = task.AgentExecution?.State == AgentExecutionState.Released
            ? AgentExecutionMarkerState.Single
            : AgentExecutionMarkerState.None;
        if (markerState != expectedMarkerState)
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.DescriptionMarkerConflict,
                    markerInspectionError ?? $"Task '{task.Id}' has an unexpected agent execution marker state.",
                    task.Id),
                CloneForUpdate(task),
                before,
                validation: validation);
        }

        if (task.AgentExecution?.State == AgentExecutionState.Released)
        {
            previousAttempts.Add(ToAttempt(task.AgentExecution));
            if (previousAttempts.Count > 20)
            {
                previousAttempts = previousAttempts[^20..];
                auditTruncated = true;
            }
        }

        var now = CurrentPersistableTimestamp();
        var change = CloneForUpdate(task);
        change.SetStatus(DomainTaskStatus.InProgress, now, normalizedAgentId);
        change.AgentExecution = new AgentExecutionRecord
        {
            AgentId = normalizedAgentId,
            LeaseId = Guid.NewGuid().ToString("D"),
            State = AgentExecutionState.Active,
            ClaimedAt = now,
            UpdatedAt = now,
            PreviousAttempts = previousAttempts,
            AuditTruncated = auditTruncated
        };
        if (!AgentExecutionDescriptionRenderer.TryRender(
                change.Description,
                change.AgentExecution,
                out var renderedDescription,
                out var markerError))
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.DescriptionMarkerConflict,
                    markerError ?? $"Task '{task.Id}' has a conflicting agent execution marker in Description.",
                    task.Id),
                CloneForUpdate(task),
                before,
                validation: validation);
        }

        change.Description = renderedDescription;

        IReadOnlyList<TaskItem> changedTasks;
        TaskOperationReadResult afterRead;
        try
        {
            var manager = CreateManager(normalizedAgentId);
            changedTasks = await UpdateTaskWithinCommandBoundaryAsync(manager, change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return await CreateOutcomeUnknownResultAsync(ex, task.Id, DomainTaskStatus.InProgress, null, before, validation);
        }

        if (afterRead.Result != null || !afterRead.Graph!.TasksById.TryGetValue(task.Id, out var afterTask) ||
            afterTask.Status != DomainTaskStatus.InProgress || afterTask.AgentExecution?.LeaseId != change.AgentExecution.LeaseId)
        {
            return await CreateOutcomeUnknownResultAsync(
                new InvalidOperationException("Claim write could not be authoritatively verified."),
                task.Id,
                DomainTaskStatus.InProgress,
                null,
                before,
                validation);
        }

        var after = new TaskAvailabilityService(afterRead.Graph.Tasks).Analyze(afterTask);
        return TaskOperationResult.Succeeded(
            BuildConfirmedChanges(changedTasks, afterRead.Graph),
            before,
            after,
            validation,
            CloneForUpdate(afterTask));
    }

    public Task<TaskOperationResult> TryUnarchiveAsync(
        string taskId,
        string? author = null) =>
        ExecuteWriteAsync(() => TrySetStatusCoreAsync(
            taskId,
            requestedStatusHint: null,
            isUnarchive: true,
            author));

    public Task<TaskOperationResult> TrySetCriterionAsync(
        string taskId,
        string criterionId,
        bool satisfied,
        string? author = null) =>
        ExecuteWriteAsync(() => TrySetCriterionCoreAsync(taskId, criterionId, satisfied, author));

    private async Task<TaskOperationResult> TryMutateExecutionCoreAsync(
        string taskId,
        string agentId,
        string leaseId,
        AgentExecutionMutation mutation)
    {
        var normalizedAgentId = agentId.Trim();
        var normalizedLeaseId = leaseId.Trim();
        var inputError = ValidateExecutionMutationInput(normalizedAgentId, normalizedLeaseId, mutation);
        if (inputError != null)
        {
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.InvalidArguments,
                inputError,
                taskId));
        }

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
                    taskId),
                validation: validation);
        }

        if (!graph.TasksById.TryGetValue(taskId, out var task))
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.TaskNotFound,
                    $"Task '{taskId}' was not found.",
                    taskId),
                validation: validation);
        }

        var rules = new TaskAvailabilityService(graph.Tasks);
        var before = rules.Analyze(task);
        var execution = task.AgentExecution;
        if (execution == null ||
            !string.Equals(execution.AgentId, normalizedAgentId, StringComparison.Ordinal) ||
            !string.Equals(execution.LeaseId, normalizedLeaseId, StringComparison.Ordinal))
        {
            return DeniedExecution(
                task,
                before,
                validation,
                TaskOperationDeniedKind.LeaseMismatch,
                $"Task '{task.Id}' is not owned by the supplied agent and lease.");
        }

        var change = CloneForUpdate(task);
        var current = change.AgentExecution!;
        var now = CurrentPersistableTimestamp();
        var markerState = AgentExecutionDescriptionRenderer.Inspect(task.Description, out var markerInspectionError);
        if (markerState != AgentExecutionMarkerState.Single)
        {
            return DeniedExecution(
                task,
                before,
                validation,
                TaskOperationDeniedKind.DescriptionMarkerConflict,
                markerInspectionError ?? $"Task '{task.Id}' does not contain exactly one agent execution marker block.");
        }


        if (mutation.Kind == AgentExecutionMutationKind.Complete &&
            TryFindInvalidRepeaterMarker(task, graph, out var invalidMarkerTaskId, out var invalidMarkerError))
        {
            return DeniedExecution(
                task,
                before,
                validation,
                TaskOperationDeniedKind.DescriptionMarkerConflict,
                $"Repeating subtree task '{invalidMarkerTaskId}' has an invalid agent execution marker: {invalidMarkerError}");
        }

        TaskOperationResult? denied = mutation.Kind switch
        {
            AgentExecutionMutationKind.Question => ApplyQuestion(change, current, mutation, now, before, validation),
            AgentExecutionMutationKind.Answer => ApplyAnswer(change, current, mutation, now, before, validation),
            AgentExecutionMutationKind.Result => ApplyResult(change, current, mutation, now, before, validation),
            AgentExecutionMutationKind.Complete => ApplyComplete(change, current, mutation, now, before, validation),
            AgentExecutionMutationKind.Release => ApplyRelease(change, current, mutation, now, before, validation),
            _ => TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.InvalidArguments,
                "Unknown agent execution mutation.",
                task.Id), before, validation: validation)
        };
        if (denied != null)
        {
            return denied;
        }

        if (!AgentExecutionDescriptionRenderer.TryRender(
                change.Description,
                current,
                out var renderedDescription,
                out var markerError))
        {
            return DeniedExecution(
                task,
                before,
                validation,
                TaskOperationDeniedKind.DescriptionMarkerConflict,
                markerError ?? $"Task '{task.Id}' has a conflicting agent execution marker in Description.");
        }

        change.Description = renderedDescription;
        IReadOnlyList<TaskItem> changedTasks;
        TaskOperationReadResult afterRead;
        try
        {
            var manager = CreateManager(normalizedAgentId);
            changedTasks = mutation.Kind is AgentExecutionMutationKind.Question or
                AgentExecutionMutationKind.Answer or AgentExecutionMutationKind.Result
                ? await UpdateExecutionWithinCommandBoundaryAsync(manager, change)
                : await UpdateTaskWithinCommandBoundaryAsync(manager, change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return await CreateOutcomeUnknownResultAsync(
                ex,
                task.Id,
                change.Status,
                null,
                before,
                validation);
        }

        if (afterRead.Result != null || !afterRead.Graph!.TasksById.TryGetValue(task.Id, out var afterTask) ||
            afterTask.AgentExecution?.LeaseId != normalizedLeaseId ||
            afterTask.AgentExecution.UpdatedAt != now ||
            afterTask.Status != change.Status)
        {
            return await CreateOutcomeUnknownResultAsync(
                new InvalidOperationException("Agent execution write could not be authoritatively verified."),
                task.Id,
                change.Status,
                null,
                before,
                validation);
        }

        var afterGraph = afterRead.Graph;
        var after = new TaskAvailabilityService(afterGraph.Tasks).Analyze(afterTask);
        return TaskOperationResult.Succeeded(
            BuildConfirmedChanges(changedTasks, afterGraph),
            before,
            after,
            validation,
            CloneForUpdate(afterTask));
    }

    private async Task<TaskOperationResult> TryCreateTaskCoreAsync(
        string title,
        string? description,
        IReadOnlyList<string> parentIds,
        string? author)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedDescription = description ?? string.Empty;
        var normalizedParents = (parentIds ?? Array.Empty<string>())
            .Select(static id => id.Trim())
            .ToArray();
        if (normalizedTitle.Length is 0 or > 4000 || normalizedTitle.Any(char.IsControl) ||
            AgentExecutionDescriptionRenderer.ContainsReservedMarker(normalizedTitle) ||
            normalizedDescription.Length > 100_000 || normalizedDescription.Any(char.IsControl) ||
            AgentExecutionDescriptionRenderer.ContainsReservedMarker(normalizedDescription) ||
            normalizedParents.Any(string.IsNullOrWhiteSpace) ||
            normalizedParents.Distinct(StringComparer.Ordinal).Count() != normalizedParents.Length)
        {
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.InvalidArguments,
                "Create arguments are invalid: title, description, and parent ids must satisfy the CLI contract."));
        }

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
                    validation.BuildWriteSafetyMessage()),
                validation: validation);
        }

        foreach (var parentId in normalizedParents)
        {
            if (!graph.TasksById.ContainsKey(parentId))
            {
                return TaskOperationResult.Denied(
                    TaskOperationDeniedReason.Create(
                        TaskOperationDeniedKind.TaskNotFound,
                        $"Parent task '{parentId}' was not found.",
                        parentId),
                    validation: validation);
            }
        }

        var now = CurrentPersistableTimestamp();
        var normalizedAuthor = TaskItem.NormalizeAuthor(author ?? "unlimotion-cli");
        var child = new TaskItem
        {
            Id = Guid.NewGuid().ToString("D"),
            UserId = normalizedAuthor,
            Title = normalizedTitle,
            Description = normalizedDescription,
            Status = DomainTaskStatus.Prepared,
            CreatedDateTime = now,
            UpdatedDateTime = now,
            ParentTasks = normalizedParents.ToList(),
            Version = 1
        };
        child.EnsureStatusHistory(normalizedAuthor);

        var changed = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        try
        {
            await _storage.Save(child);
            changed[child.Id] = child;

            var manager = CreateManager(normalizedAuthor);
            foreach (var parentId in normalizedParents)
            {
                var parent = CloneForUpdate(graph.TasksById[parentId]);
                parent.ContainsTasks.Add(child.Id);
                parent.UpdatedDateTime = now;
                await _storage.Save(parent);
                changed[parent.Id] = parent;
                foreach (var affected in await manager.CalculateAndUpdateAvailability(parent))
                {
                    changed[affected.Id] = affected;
                }
            }

            foreach (var affected in await manager.CalculateAndUpdateAvailability(child))
            {
                changed[affected.Id] = affected;
            }

            var afterRead = await ReadGraphForWriteAsync();
            if (afterRead.Result != null ||
                !afterRead.Graph!.TasksById.TryGetValue(child.Id, out var afterChild) ||
                afterChild.ParentTasks.Count != normalizedParents.Length ||
                normalizedParents.Any(parentId => !afterChild.ParentTasks.Contains(parentId, StringComparer.Ordinal)) ||
                normalizedParents.Any(parentId =>
                    !afterRead.Graph.TasksById.TryGetValue(parentId, out var parent) ||
                    !parent.ContainsTasks.Contains(child.Id, StringComparer.Ordinal)))
            {
                return await CreateOutcomeUnknownResultAsync(
                    new InvalidOperationException("Created task and parent relations could not be authoritatively verified."),
                    child.Id,
                    DomainTaskStatus.Prepared,
                    null,
                    before: null,
                    validation);
            }

            var afterValidation = TaskGraphValidationReport.From(afterRead.Graph);
            if (!afterValidation.IsValid)
            {
                return await CreateOutcomeUnknownResultAsync(
                    new InvalidOperationException(afterValidation.BuildWriteSafetyMessage()),
                    child.Id,
                    DomainTaskStatus.Prepared,
                    null,
                    before: null,
                    validation);
            }

            var after = new TaskAvailabilityService(afterRead.Graph.Tasks).Analyze(afterChild);
            return TaskOperationResult.Succeeded(
                BuildConfirmedChanges(changed.Values.ToArray(), afterRead.Graph),
                before: null,
                after,
                validation,
                CloneForUpdate(afterChild));
        }
        catch (Exception ex)
        {
            return await CreateOutcomeUnknownResultAsync(
                ex,
                child.Id,
                DomainTaskStatus.Prepared,
                null,
                before: null,
                validation);
        }
    }

    private static TaskOperationResult? ApplyQuestion(
        TaskItem task,
        AgentExecutionRecord execution,
        AgentExecutionMutation mutation,
        DateTimeOffset now,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation)
    {
        if (task.Status != DomainTaskStatus.InProgress || execution.State != AgentExecutionState.Active)
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.ExecutionStateDenied,
                $"Task '{task.Id}' cannot accept a question in its current execution state.");
        }

        execution.Questions.Add(new AgentExecutionQuestion
        {
            Id = Guid.NewGuid().ToString("D"),
            Text = mutation.Text!,
            AskedAt = now
        });
        execution.State = AgentExecutionState.AwaitingInput;
        execution.UpdatedAt = now;
        return null;
    }

    private static TaskOperationResult? ApplyAnswer(
        TaskItem task,
        AgentExecutionRecord execution,
        AgentExecutionMutation mutation,
        DateTimeOffset now,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation)
    {
        if (task.Status != DomainTaskStatus.InProgress || execution.State != AgentExecutionState.AwaitingInput)
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.ExecutionStateDenied,
                $"Task '{task.Id}' cannot accept an answer in its current execution state.");
        }

        var question = execution.Questions.FirstOrDefault(item =>
            string.Equals(item.Id, mutation.QuestionId, StringComparison.Ordinal));
        if (question == null || question.Answer != null)
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.QuestionNotFound,
                $"Question '{mutation.QuestionId}' is not available for an answer in task '{task.Id}'.");
        }

        question.Answer = mutation.Text;
        question.AnsweredAt = now;
        execution.State = execution.Questions.Any(static item => item.Answer == null)
            ? AgentExecutionState.AwaitingInput
            : AgentExecutionState.Active;
        execution.UpdatedAt = now;
        return null;
    }

    private static TaskOperationResult? ApplyResult(
        TaskItem task,
        AgentExecutionRecord execution,
        AgentExecutionMutation mutation,
        DateTimeOffset now,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation)
    {
        if (task.Status != DomainTaskStatus.InProgress || execution.State != AgentExecutionState.Active)
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.ExecutionStateDenied,
                $"Task '{task.Id}' cannot record a result in its current execution state.");
        }

        execution.Result = new AgentExecutionResult
        {
            Summary = mutation.Text!,
            Links = mutation.Links.ToList(),
            RecordedAt = now
        };
        execution.UpdatedAt = now;
        return null;
    }

    private static TaskOperationResult? ApplyComplete(
        TaskItem task,
        AgentExecutionRecord execution,
        AgentExecutionMutation mutation,
        DateTimeOffset now,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation)
    {
        if (task.Status != DomainTaskStatus.InProgress || execution.State != AgentExecutionState.Active ||
            execution.Questions.Any(static question => question.Answer == null))
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.ExecutionStateDenied,
                $"Task '{task.Id}' cannot be completed in its current execution state.");
        }

        if (!before.CanComplete)
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.StatusTransitionDenied,
                $"Task '{task.Id}' does not satisfy its completion rules.");
        }

        execution.Result = new AgentExecutionResult
        {
            Summary = mutation.Text!,
            Links = mutation.Links.ToList(),
            RecordedAt = now
        };
        execution.State = AgentExecutionState.Completed;
        execution.UpdatedAt = now;
        task.Status = DomainTaskStatus.Completed;
        return null;
    }

    private static TaskOperationResult? ApplyRelease(
        TaskItem task,
        AgentExecutionRecord execution,
        AgentExecutionMutation mutation,
        DateTimeOffset now,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation)
    {
        if (task.Status != DomainTaskStatus.InProgress ||
            execution.State is not (AgentExecutionState.Active or AgentExecutionState.AwaitingInput))
        {
            return DeniedExecution(task, before, validation, TaskOperationDeniedKind.ExecutionStateDenied,
                $"Task '{task.Id}' cannot be released in its current execution state.");
        }

        execution.State = AgentExecutionState.Released;
        execution.ReleasedAt = now;
        execution.ReleaseReason = mutation.Text;
        execution.UpdatedAt = now;
        task.Status = DomainTaskStatus.Prepared;
        return null;
    }

    private static TaskOperationResult DeniedExecution(
        TaskItem task,
        TaskAvailabilityAnalysis before,
        TaskGraphValidationReport validation,
        TaskOperationDeniedKind kind,
        string message) =>
        TaskOperationResult.DeniedWithAuthoritativeTask(
            TaskOperationDeniedReason.Create(kind, message, task.Id),
            CloneForUpdate(task),
            before,
            validation: validation);

    private async Task<TaskOperationResult> TrySetStatusCoreAsync(
        string taskId,
        DomainTaskStatus? requestedStatusHint,
        bool isUnarchive,
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
                    requestedStatusHint),
                validation: validation);
        }

        if (!graph.TasksById.TryGetValue(taskId, out var task))
        {
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.TaskNotFound,
                    $"Task '{taskId}' was not found.",
                    taskId,
                    requestedStatusHint),
                validation: validation);
        }

        var rules = new TaskAvailabilityService(graph.Tasks);
        var before = rules.Analyze(task);
        if (!isUnarchive && requestedStatusHint != task.Status &&
            task.AgentExecution?.State is AgentExecutionState.Active or AgentExecutionState.AwaitingInput)
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.ExecutionStateDenied,
                    $"Task '{task.Id}' has an active agent execution and its status can only change through the matching lease.",
                    task.Id,
                    requestedStatusHint),
                CloneForUpdate(task),
                before,
                validation: validation);
        }

        if (isUnarchive && task.Status != DomainTaskStatus.Archived)
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StatusPreconditionFailed,
                    $"Task '{task.Id}' cannot be unarchived because its authoritative status is {task.Status}.",
                    task.Id),
                authoritativeTask: CloneForUpdate(task),
                before: before,
                validation: validation);
        }

        var requestedStatus = isUnarchive
            ? task.GetRestoreStatusAfterArchive(DateTimeOffset.UtcNow)
            : requestedStatusHint!.Value;
        if (!Enum.IsDefined(requestedStatus))
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.CreateWithStatusTransition(
                    TaskOperationDeniedKind.StatusTransitionDenied,
                    $"Task '{task.Id}' cannot move to invalid status value {(int)requestedStatus}.",
                    statusTransitionReason: TaskStatusTransitionDenialReason.InvalidTargetStatus,
                    taskId: task.Id,
                    requestedStatus: requestedStatus),
                authoritativeTask: CloneForUpdate(task),
                before: before,
                validation: validation);
        }

        if (task.Status == requestedStatus)
        {
            return TaskOperationResult.Succeeded(
                Array.Empty<TaskItem>(),
                before,
                before,
                validation,
                authoritativeTask: CloneForUpdate(task));
        }

        var transition = rules.EvaluateStatusTransition(task, requestedStatus);
        if (!transition.Allowed)
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.CreateWithStatusTransition(
                    TaskOperationDeniedKind.StatusTransitionDenied,
                    transition.DenialMessage ?? $"Task '{task.Id}' cannot move to {requestedStatus}.",
                    statusTransitionReason: transition.Evaluation.Reason,
                    taskId: task.Id,
                    requestedStatus: requestedStatus),
                authoritativeTask: CloneForUpdate(task),
                before: before,
                validation: validation);
        }


        if (requestedStatus == DomainTaskStatus.Completed &&
            TryFindInvalidRepeaterMarker(task, graph, out var invalidMarkerTaskId, out var invalidMarkerError))
        {
            return TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.DescriptionMarkerConflict,
                    $"Repeating subtree task '{invalidMarkerTaskId}' has an invalid agent execution marker: {invalidMarkerError}",
                    task.Id,
                    requestedStatus),
                CloneForUpdate(task),
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
            changedTasks = await UpdateTaskWithinCommandBoundaryAsync(manager, change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return await CreateOutcomeUnknownResultAsync(
                ex,
                task.Id,
                requestedStatus,
                criterionId: null,
                before,
                validation);
        }

        if (afterRead.Result != null)
        {
            return await CreateOutcomeUnknownResultAsync(
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
        return TaskOperationResult.Succeeded(
            BuildConfirmedChanges(changedTasks, afterGraph),
            before,
            after,
            validation,
            authoritativeTask: CloneForUpdate(afterTask));
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
            changedTasks = await UpdateTaskWithinCommandBoundaryAsync(manager, change);
            afterRead = await ReadGraphForWriteAsync();
        }
        catch (Exception ex)
        {
            return await CreateOutcomeUnknownResultAsync(
                ex,
                task.Id,
                requestedStatus: null,
                criterionId,
                before,
                validation);
        }

        if (afterRead.Result != null)
        {
            return await CreateOutcomeUnknownResultAsync(
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
        return TaskOperationResult.Succeeded(BuildConfirmedChanges(changedTasks, afterGraph), before, after, validation);
    }

    private static bool TryFindInvalidRepeaterMarker(
        TaskItem root,
        TaskGraphReadResult graph,
        out string taskId,
        out string error)
    {
        taskId = string.Empty;
        error = string.Empty;
        if (root.Repeater == null || root.Repeater.Type == RepeaterType.None ||
            !root.PlannedBeginDateTime.HasValue)
        {
            return false;
        }

        var pending = new Stack<TaskItem>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            if (AgentExecutionDescriptionRenderer.Inspect(current.Description, out var markerError) ==
                AgentExecutionMarkerState.Invalid)
            {
                taskId = current.Id;
                error = markerError ?? "unknown marker error";
                return true;
            }

            foreach (var childId in current.ContainsTasks ?? [])
            {
                if (graph.TasksById.TryGetValue(childId, out var child))
                {
                    pending.Push(child);
                }
            }
        }

        return false;
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
            var graph = await diagnosticStorage.ReadGraphAsync();
            _lastReadRevision = graph.Revision;
            return new TaskOperationReadResult(graph, null);
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
        using var writeScope = (_storage as ITaskGraphWriteScopeStorage)?.BeginWriteScope();
        _activeWriteScope = writeScope;
        TaskOperationResult? operationResult = null;
        try
        {
            TaskOperationResult result;
            if (_storage is ITaskGraphWriteLock writeLock)
            {
                result = await writeLock.WithWriteLockAsync(async () =>
                {
                    var lockedResult = await operation();
                    operationResult = lockedResult;
                    await FinalizeRecoverableScopeAsync(writeScope, lockedResult.Success);
                    return await EnrichOutcomeUnknownAfterFinalizationAsync(lockedResult, writeScope);
                });
            }
            else
            {
                result = await operation();
                operationResult = result;
                await FinalizeRecoverableScopeAsync(writeScope, result.Success);
                result = await EnrichOutcomeUnknownAfterFinalizationAsync(result, writeScope);
            }

            return result with { StorageRevision = _lastReadRevision };
        }
        catch (Exception ex)
        {
            if (writeScope is IRecoverableTaskGraphWriteScope recoverableScope)
            {
                try
                {
                    if (_storage is ITaskGraphWriteLock writeLock)
                    {
                        await writeLock.WithWriteLockAsync(async () =>
                        {
                            await recoverableScope.RollbackAsync();
                            return true;
                        });
                    }
                    else
                    {
                        await recoverableScope.RollbackAsync();
                    }
                }
                catch
                {
                    // The durable journal is intentionally left for recovery on the next locked access.
                }
            }

            if (writeScope?.AttemptedTaskIds.Count > 0)
            {
                var primaryTaskId = operationResult?.AuthoritativeTask?.Id ??
                                    operationResult?.DeniedReason?.TaskId ??
                                    writeScope.AttemptedTaskIds.LastOrDefault();
                var failure = CreateOutcomeUnknownResult(
                    ex,
                    taskId: primaryTaskId,
                    requestedStatus: operationResult?.After?.Status ?? operationResult?.DeniedReason?.RequestedStatus,
                    criterionId: operationResult?.DeniedReason?.CriterionId,
                    before: operationResult?.Before,
                    validation: operationResult?.Validation);
                return await EnrichOutcomeUnknownAfterFinalizationAsync(failure, writeScope);
            }

            return CreateStorageFailedResult(
                ex,
                taskId: null,
                requestedStatus: null,
                criterionId: null,
                before: null,
                validation: null);
        }
        finally
        {
            _activeWriteScope = null;
        }
    }

    private static async Task FinalizeRecoverableScopeAsync(ITaskGraphWriteScope? scope, bool success)
    {
        if (scope is not IRecoverableTaskGraphWriteScope recoverableScope)
        {
            return;
        }

        if (success)
        {
            await recoverableScope.CommitAsync();
        }
        else
        {
            await recoverableScope.RollbackAsync();
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

    private async Task<TaskOperationResult> CreateOutcomeUnknownResultAsync(
        Exception ex,
        string? taskId,
        DomainTaskStatus? requestedStatus,
        string? criterionId,
        TaskAvailabilityAnalysis? before,
        TaskGraphValidationReport? validation)
    {
        var result = CreateOutcomeUnknownResult(
            ex,
            taskId,
            requestedStatus,
            criterionId,
            before,
            validation);

        // A recoverable scope must first roll back or roll forward its journal.
        // Returning a read-back from the partial pre-finalization graph would falsely
        // label an intermediate state as authoritative.
        if (_activeWriteScope is IRecoverableTaskGraphWriteScope)
        {
            return result;
        }

        return await EnrichOutcomeUnknownAfterFinalizationAsync(result, _activeWriteScope);
    }

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

    private async Task<TaskOperationResult> EnrichOutcomeUnknownAfterFinalizationAsync(
        TaskOperationResult result,
        ITaskGraphWriteScope? scope)
    {
        if (result.Success || result.DeniedReason?.Kind != TaskOperationDeniedKind.OutcomeUnknown)
        {
            return result;
        }

        var attemptedTaskIds = scope?.AttemptedTaskIds ?? Array.Empty<string>();
        if (attemptedTaskIds.Count == 0 || _storage is not ITaskGraphDiagnosticStorage diagnosticStorage)
        {
            return result;
        }

        try
        {
            var confirmedGraph = _storage is ITaskGraphWriteScopeStorage scopedStorage && scope != null
                ? await scopedStorage.RefreshAttemptedWritesAsync(scope)
                : await diagnosticStorage.ReadGraphAsync();
            _lastReadRevision = confirmedGraph.Revision;
            var confirmedTasks = attemptedTaskIds
                .Select(id => confirmedGraph.TasksById.GetValueOrDefault(id))
                .Where(static task => task != null)
                .Select(static task => TaskItemSnapshot.Clone(task!))
                .ToArray();
            var authoritativeTask = result.DeniedReason.TaskId != null
                ? confirmedGraph.TasksById.GetValueOrDefault(result.DeniedReason.TaskId)
                : null;
            return result with
            {
                ChangedTasks = confirmedTasks,
                AuthoritativeTask = authoritativeTask == null
                    ? null
                    : TaskItemSnapshot.Clone(authoritativeTask),
                StorageRevision = confirmedGraph.Revision
            };
        }
        catch
        {
            return result;
        }
    }

    private TaskTreeManager CreateManager(string? author) => new(_storage)
    {
        StatusAuthorProvider = task =>
            TaskItem.NormalizeAuthor(author ?? StatusAuthorProvider?.Invoke(task) ?? task.UserId ?? "local-user")
    };

    private Task<List<TaskItem>> UpdateTaskWithinCommandBoundaryAsync(
        TaskTreeManager manager,
        TaskItem change) =>
        _storage is ITaskGraphWriteLock
            ? manager.UpdateTaskWithinExistingMutationLockAsync(change)
            : manager.UpdateTask(change);

    private Task<List<TaskItem>> UpdateExecutionWithinCommandBoundaryAsync(
        TaskTreeManager manager,
        TaskItem change) =>
        _storage is ITaskGraphWriteLock
            ? manager.UpdateExecutionWithinExistingMutationLockAsync(change)
            : manager.UpdateTask(change);

    private static TaskItem CloneForUpdate(TaskItem task) => TaskItemSnapshot.Clone(task);

    private static IReadOnlyList<TaskItem> BuildConfirmedChanges(
        IReadOnlyList<TaskItem> changedTasks,
        TaskGraphReadResult confirmedGraph) => changedTasks
        .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
        .Select(task => confirmedGraph.TasksById.GetValueOrDefault(task.Id))
        .Where(static task => task != null)
        .Select(static task => TaskItemSnapshot.Clone(task!))
        .ToArray();

    private static bool IsValidExecutionText(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength &&
        !value.Any(char.IsControl) &&
        !AgentExecutionDescriptionRenderer.ContainsReservedMarker(value);

    private static string? ValidateExecutionMutationInput(
        string agentId,
        string leaseId,
        AgentExecutionMutation mutation)
    {
        if (!IsValidExecutionText(agentId, 200))
        {
            return "Agent id is invalid for agent execution.";
        }

        if (!Guid.TryParseExact(leaseId, "D", out _))
        {
            return "Lease id must be a GUID in D format.";
        }

        var maximumLength = mutation.Kind is AgentExecutionMutationKind.Result or AgentExecutionMutationKind.Complete
            ? 16000
            : 4000;
        if (!IsValidExecutionText(mutation.Text ?? string.Empty, maximumLength))
        {
            return "Execution text is empty, too long, contains control characters, or contains a reserved marker.";
        }

        if (mutation.Kind == AgentExecutionMutationKind.Answer &&
            !Guid.TryParseExact(mutation.QuestionId, "D", out _))
        {
            return "Question id must be a GUID in D format.";
        }

        if (mutation.Links.Count > 20)
        {
            return "Execution result cannot contain more than 20 links.";
        }

        foreach (var link in mutation.Links)
        {
            if (!IsValidExecutionText(link, 4000) ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https" or "file"))
            {
                return $"Execution result link '{link}' is not an absolute http, https, or file URI.";
            }
        }

        return null;
    }

    private static AgentExecutionAttempt ToAttempt(AgentExecutionRecord execution) => new()
    {
        AgentId = execution.AgentId,
        LeaseId = execution.LeaseId,
        State = execution.State,
        ClaimedAt = execution.ClaimedAt,
        UpdatedAt = execution.UpdatedAt,
        ReleasedAt = execution.ReleasedAt,
        ReleaseReason = execution.ReleaseReason,
        CompletedAt = execution.State == AgentExecutionState.Completed ? execution.UpdatedAt : null
    };

    private static DateTimeOffset CurrentPersistableTimestamp() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private enum AgentExecutionMutationKind
    {
        Question,
        Answer,
        Result,
        Complete,
        Release
    }

    private sealed record AgentExecutionMutation(
        AgentExecutionMutationKind Kind,
        string? Text,
        string? QuestionId,
        IReadOnlyList<string> Links)
    {
        public static AgentExecutionMutation Question(string text) =>
            new(AgentExecutionMutationKind.Question, text, null, Array.Empty<string>());

        public static AgentExecutionMutation Answer(string questionId, string text) =>
            new(AgentExecutionMutationKind.Answer, text, questionId, Array.Empty<string>());

        public static AgentExecutionMutation Result(string summary, IReadOnlyList<string> links) =>
            new(AgentExecutionMutationKind.Result, summary, null, links);

        public static AgentExecutionMutation Complete(string summary, IReadOnlyList<string> links) =>
            new(AgentExecutionMutationKind.Complete, summary, null, links);

        public static AgentExecutionMutation Release(string reason) =>
            new(AgentExecutionMutationKind.Release, reason, null, Array.Empty<string>());
    }

    private sealed record TaskOperationReadResult(TaskGraphReadResult? Graph, TaskOperationResult? Result);
}
