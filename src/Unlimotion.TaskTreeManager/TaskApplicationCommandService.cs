using System.Xml;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TaskTree;

/// <summary>
/// Applies an already-approved, declarative task graph change as one recoverable write boundary.
/// Proposal approval itself deliberately stays outside the task store.
/// </summary>
public sealed class TaskApplicationCommandService
{
    private readonly IStorage _storage;

    public TaskApplicationCommandService(IStorage storage, Func<TaskItem, string> etagProvider)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        EtagProvider = etagProvider ?? throw new ArgumentNullException(nameof(etagProvider));
    }

    public Func<TaskItem, string> EtagProvider { get; }

    public Task<TaskApplicationResult> PreviewAsync(TaskApplicationRequest request) =>
        ExecuteAsync(request, dryRun: true);

    public Task<TaskApplicationResult> TryApplyAsync(TaskApplicationRequest request) =>
        ExecuteAsync(request, dryRun: false);

    private async Task<TaskApplicationResult> ExecuteAsync(TaskApplicationRequest request, bool dryRun)
    {
        if (_storage is not ITaskGraphDiagnosticStorage diagnostics)
        {
            return Failed(TaskApplicationErrorKind.OperationFailed,
                "Storage does not support diagnostic graph reads required for application requests.");
        }

        async Task<TaskApplicationResult> UnderLockAsync()
        {
            using var scope = dryRun ? null : (_storage as ITaskGraphWriteScopeStorage)?.BeginWriteScope();
            try
            {
                var graph = await diagnostics.ReadGraphAsync();
                var initialValidation = TaskGraphValidationReport.From(graph);
                if (!initialValidation.IsWriteSafe)
                {
                    return Failed(TaskApplicationErrorKind.ValidationFailed,
                        initialValidation.BuildWriteSafetyMessage(), validation: initialValidation);
                }

                var requestError = ValidateRequest(request);
                if (requestError != null)
                {
                    return requestError;
                }

                var original = graph.TasksById.ToDictionary(
                    static pair => pair.Key,
                    static pair => TaskItemSnapshot.Clone(pair.Value),
                    StringComparer.Ordinal);
                var staged = graph.TasksById.ToDictionary(
                    static pair => pair.Key,
                    static pair => TaskItemSnapshot.Clone(pair.Value),
                    StringComparer.Ordinal);

                // The receipt is deliberately written after the task transaction.  A process can
                // therefore fail in the small interval between a committed transaction and the
                // receipt write.  Reconciliation makes that retry safe instead of treating the
                // deterministic create id as a second, conflicting create.
                if (!dryRun)
                {
                    var reconciliation = ReconcilePreviouslyAppliedRequest(request, original);
                    if (reconciliation != null)
                    {
                        return reconciliation;
                    }
                }

                var checkedPreconditions = new HashSet<string>(StringComparer.Ordinal);
                var operationResults = new List<TaskApplicationOperationResult>();

                foreach (var operation in request.Operations)
                {
                    var error = ApplyOperation(request, operation, original, staged, checkedPreconditions);
                    if (error != null)
                    {
                        return error with
                        {
                            OperationResults = operationResults.ToArray(),
                            AuthoritativeTasks = ToAuthoritativeTasks(original, error.Error?.TaskId)
                        };
                    }

                    operationResults.Add(new TaskApplicationOperationResult
                    {
                        OperationId = operation.OperationId,
                        TaskId = operation.TaskId ?? operation.NewTaskId ?? operation.FromTaskId,
                        Outcome = "wouldApply"
                    });
                }

                var now = DateTimeOffset.UtcNow;
                NormalizeAvailability(staged.Values, now, request.Author);
                if (staged.Values.Any(task => task.PlannedBeginDateTime.HasValue && task.PlannedEndDateTime.HasValue &&
                                              task.PlannedEndDateTime < task.PlannedBeginDateTime))
                {
                    return Failed(TaskApplicationErrorKind.ValidationFailed,
                        "Planned end date cannot be earlier than planned begin date.", operationResults: operationResults);
                }
                var finalGraph = CreateGraph(staged.Values);
                var finalValidation = TaskGraphValidationReport.From(finalGraph);
                if (!finalValidation.IsValid)
                {
                    return Failed(TaskApplicationErrorKind.ValidationFailed,
                        finalValidation.BuildWriteSafetyMessage(), validation: finalValidation,
                        operationResults: operationResults);
                }

                var changedBeforeTimestamp = staged.Values
                    .Where(task => !original.TryGetValue(task.Id, out var before) ||
                                   !string.Equals(EtagProvider(before), EtagProvider(task), StringComparison.Ordinal))
                    .OrderBy(static task => task.Id, StringComparer.Ordinal)
                    .ToArray();
                foreach (var task in changedBeforeTimestamp)
                {
                    task.UpdatedDateTime = NextUpdated(task.UpdatedDateTime, now);
                }
                var changed = staged.Values
                    .Where(task => !original.TryGetValue(task.Id, out var before) ||
                                   !string.Equals(EtagProvider(before), EtagProvider(task), StringComparison.Ordinal))
                    .OrderBy(static task => task.Id, StringComparer.Ordinal)
                    .ToArray();
                var createdIds = staged.Keys
                    .Where(id => !original.ContainsKey(id))
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();

                if (dryRun)
                {
                    return new TaskApplicationResult
                    {
                        Success = true,
                        Mode = "preview",
                        ChangedTaskIds = changed.Select(static task => task.Id).ToArray(),
                        CreatedTaskIds = createdIds,
                        OperationResults = operationResults,
                        Validation = finalValidation
                    };
                }

                if (scope is not IRecoverableTaskGraphWriteScope recoverableScope)
                {
                    return Failed(TaskApplicationErrorKind.OperationFailed,
                        "Storage does not support recoverable write scopes required for application requests.");
                }

                foreach (var task in changed)
                {
                    await _storage.Save(task);
                }

                await recoverableScope.CommitAsync();
                var afterGraph = await diagnostics.ReadGraphAsync();
                var afterValidation = TaskGraphValidationReport.From(afterGraph);
                var verificationFailure = request.Operations.FirstOrDefault(operation =>
                    !IsOperationAlreadyApplied(operation, afterGraph.TasksById));
                if (!afterValidation.IsValid || verificationFailure != null)
                {
                    return Failed(TaskApplicationErrorKind.OutcomeUnknown,
                        verificationFailure == null
                            ? "Application writes were committed but the resulting graph is invalid."
                            : $"Application operation '{verificationFailure.OperationId}' was committed but its authoritative postcondition could not be verified.",
                        validation: afterValidation,
                        operationResults: operationResults,
                        authoritativeTasks: ToAuthoritativeTasks(afterGraph.TasksById, changed.Select(static task => task.Id)));
                }

                return new TaskApplicationResult
                {
                    Success = true,
                    Mode = "applied",
                    DidMutate = changed.Length > 0,
                    ChangedTaskIds = changed.Select(static task => task.Id).ToArray(),
                    CreatedTaskIds = createdIds,
                    OperationResults = operationResults.Select(result => result with { Outcome = "applied" }).ToArray(),
                    Validation = afterValidation,
                    AuthoritativeTasks = ToAuthoritativeTasks(afterGraph.TasksById, changed.Select(static task => task.Id))
                };
            }
            catch (Exception ex)
            {
                if (scope is IRecoverableTaskGraphWriteScope recoverableScope)
                {
                    try
                    {
                        await recoverableScope.RollbackAsync();
                    }
                    catch
                    {
                        // The FileTaskStorage journal is deliberately left for the next locked access.
                    }
                }

                return Failed(TaskApplicationErrorKind.OutcomeUnknown,
                    $"Application outcome could not be verified: {ex.Message}");
            }
        }

        return _storage is ITaskGraphWriteLock writeLock
            ? await writeLock.WithWriteLockAsync(UnderLockAsync)
            : await UnderLockAsync();
    }

    private TaskApplicationResult? ApplyOperation(
        TaskApplicationRequest request,
        TaskApplicationOperation operation,
        IReadOnlyDictionary<string, TaskItem> original,
        IDictionary<string, TaskItem> staged,
        ISet<string> checkedPreconditions)
    {
        TaskApplicationResult? RequireStaged(string? id, bool requireStatus = false)
        {
            if (string.IsNullOrWhiteSpace(id) || !staged.TryGetValue(id, out var task))
            {
                return Failed(TaskApplicationErrorKind.NotFound, $"Task '{id}' was not found.", operation, id);
            }

            // A task created by an earlier operation in this ordered request has no prior
            // persisted snapshot to guard. Its staged value is wholly controlled by this request.
            if (!original.TryGetValue(id, out var originalTask))
            {
                return null;
            }

            if (checkedPreconditions.Add(id))
            {
                var precondition = request.Preconditions.SingleOrDefault(item =>
                    string.Equals(item.TaskId, id, StringComparison.Ordinal));
                if (precondition == null)
                {
                    return Failed(TaskApplicationErrorKind.PreconditionFailed,
                        $"Task '{id}' is changed by the request but has no etag precondition.", operation, id);
                }

                var actualEtag = EtagProvider(originalTask);
                if (!string.Equals(precondition.Etag, actualEtag, StringComparison.Ordinal))
                {
                    return Failed(TaskApplicationErrorKind.PreconditionFailed,
                        $"Task '{id}' no longer matches the approved snapshot.", operation, id,
                        expectedEtag: precondition.Etag, actualEtag: actualEtag);
                }

                if (requireStatus && (!precondition.Status.HasValue || precondition.Status.Value != task.Status))
                {
                    return Failed(TaskApplicationErrorKind.PreconditionFailed,
                        $"Task '{id}' no longer has the approved status.", operation, id);
                }
            }

            return null;
        }

        switch (operation.Kind)
        {
            case TaskApplicationOperationKind.SetField:
            {
                var error = RequireStaged(operation.TaskId);
                if (error != null) return error;
                return SetField(staged[operation.TaskId!], operation, request.Author);
            }
            case TaskApplicationOperationKind.ClearField:
            {
                var error = RequireStaged(operation.TaskId);
                if (error != null) return error;
                return ClearField(staged[operation.TaskId!], operation);
            }
            case TaskApplicationOperationKind.AddCriterion:
            case TaskApplicationOperationKind.ReplaceCriterion:
            case TaskApplicationOperationKind.RemoveCriterion:
            case TaskApplicationOperationKind.SetCriterionSatisfied:
            {
                var error = RequireStaged(operation.TaskId);
                if (error != null) return error;
                return MutateCriterion(staged[operation.TaskId!], operation);
            }
            case TaskApplicationOperationKind.AddRelation:
            case TaskApplicationOperationKind.RemoveRelation:
            {
                var fromError = RequireStaged(operation.FromTaskId);
                if (fromError != null) return fromError;
                var toError = RequireStaged(operation.ToTaskId);
                if (toError != null) return toError;
                return MutateRelation(staged[operation.FromTaskId!], staged[operation.ToTaskId!], operation);
            }
            case TaskApplicationOperationKind.CreateTask:
                return CreateTask(request, operation, staged, RequireStaged);
            case TaskApplicationOperationKind.SetStatus:
            {
                var error = RequireStaged(operation.TaskId, requireStatus: true);
                if (error != null) return error;
                return SetStatus(new Dictionary<string, TaskItem>(staged, StringComparer.Ordinal), staged[operation.TaskId!], operation, request.Author);
            }
            default:
                return Failed(TaskApplicationErrorKind.InvalidArguments,
                    $"Operation '{operation.OperationId}' has an unsupported kind.", operation);
        }
    }

    private TaskApplicationResult? CreateTask(
        TaskApplicationRequest request,
        TaskApplicationOperation operation,
        IDictionary<string, TaskItem> staged,
        Func<string?, bool, TaskApplicationResult?> requireStaged)
    {
        var id = operation.NewTaskId;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(operation.Title) ||
            operation.Title.Length > 4000 || operation.Title.Any(char.IsControl) ||
            AgentExecutionDescriptionRenderer.ContainsReservedMarker(operation.Title))
        {
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Create task id or title is invalid.", operation, id);
        }

        if (staged.ContainsKey(id))
        {
            return Failed(TaskApplicationErrorKind.IdempotencyConflict,
                $"Task '{id}' already exists and cannot be created by this application.", operation, id);
        }

        var parents = operation.ParentIds ?? Array.Empty<string>();
        if (parents.Distinct(StringComparer.Ordinal).Count() != parents.Count || parents.Any(string.IsNullOrWhiteSpace))
        {
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Create task parent ids are invalid.", operation, id);
        }

        foreach (var parentId in parents)
        {
            var error = requireStaged(parentId, false);
            if (error != null) return error;
        }

        var description = operation.DescriptionUserText ?? string.Empty;
        if (description.Length > 100_000 || description.Any(char.IsControl) ||
            AgentExecutionDescriptionRenderer.ContainsReservedMarker(description))
        {
            return Failed(TaskApplicationErrorKind.DescriptionMarkerConflict,
                "Create task description contains a reserved agent execution marker.", operation, id);
        }

        var now = DateTimeOffset.UtcNow;
        var created = new TaskItem
        {
            Id = id,
            UserId = TaskItem.NormalizeAuthor(request.Author),
            Title = operation.Title.Trim(),
            Description = description,
            Status = DomainTaskStatus.Prepared,
            CreatedDateTime = now,
            UpdatedDateTime = now,
            PlannedDuration = operation.PlannedDuration,
            PlannedBeginDateTime = operation.PlannedBeginDateTime,
            PlannedEndDateTime = operation.PlannedEndDateTime,
            ParentTasks = parents.ToList(),
            CompletionCriteria = (operation.Criteria ?? Array.Empty<TaskApplicationCriterion>())
                .Select(criterion => new TaskCompletionCriterion
                {
                    Id = criterion.CriterionId,
                    Text = criterion.Text,
                    IsSatisfied = criterion.IsSatisfied
                }).ToList(),
            Version = 1
        };
        if (created.CompletionCriteria.Any(criterion => string.IsNullOrWhiteSpace(criterion.Id) ||
                                                     string.IsNullOrWhiteSpace(criterion.Text)) ||
            created.CompletionCriteria.Select(static criterion => criterion.Id).Distinct(StringComparer.Ordinal).Count() !=
            created.CompletionCriteria.Count)
        {
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Create task criteria are invalid.", operation, id);
        }

        created.EnsureStatusHistory(request.Author);
        staged.Add(id, created);
        foreach (var parentId in parents)
        {
            AddDistinct(staged[parentId].ContainsTasks, id);
        }

        return null;
    }

    private static TaskApplicationResult? SetField(TaskItem task, TaskApplicationOperation operation, string author)
    {
        switch (operation.Field)
        {
            case "title":
                if (string.IsNullOrWhiteSpace(operation.Value) || operation.Value.Length > 4000 ||
                    operation.Value.Any(char.IsControl) || AgentExecutionDescriptionRenderer.ContainsReservedMarker(operation.Value))
                {
                    return Failed(TaskApplicationErrorKind.InvalidArguments, "Title is invalid.", operation, task.Id);
                }
                task.Title = operation.Value.Trim();
                return null;
            case "descriptionUserText":
                string? markerError = null;
                if (operation.Value == null || operation.Value.Length > 100_000 || operation.Value.Any(char.IsControl) ||
                    AgentExecutionDescriptionRenderer.ContainsReservedMarker(operation.Value) ||
                    !AgentExecutionDescriptionRenderer.TryRemove(task.Description, out _, out markerError))
                {
                    return Failed(TaskApplicationErrorKind.DescriptionMarkerConflict,
                        markerError ?? "Description user text is invalid.", operation, task.Id);
                }

                if (task.AgentExecution == null)
                {
                    task.Description = operation.Value;
                    return null;
                }

                if (!AgentExecutionDescriptionRenderer.TryRender(operation.Value, task.AgentExecution, out var rendered, out markerError))
                {
                    return Failed(TaskApplicationErrorKind.DescriptionMarkerConflict,
                        markerError ?? "Description marker could not be rendered.", operation, task.Id);
                }

                task.Description = rendered;
                return null;
            case "plannedDuration":
                if (!TryParseDuration(operation.Value, out var duration))
                {
                    return Failed(TaskApplicationErrorKind.InvalidArguments, "Planned duration must be a positive ISO 8601 duration.", operation, task.Id);
                }
                task.PlannedDuration = duration;
                return null;
            case "plannedBeginDateTime":
                if (!TryParseDate(operation.Value, out var begin))
                {
                    return Failed(TaskApplicationErrorKind.InvalidArguments, "Planned begin date must be RFC 3339 with an offset.", operation, task.Id);
                }
                task.PlannedBeginDateTime = begin;
                return null;
            case "plannedEndDateTime":
                if (!TryParseDate(operation.Value, out var end))
                {
                    return Failed(TaskApplicationErrorKind.InvalidArguments, "Planned end date must be RFC 3339 with an offset.", operation, task.Id);
                }
                task.PlannedEndDateTime = end;
                return null;
            default:
                return Failed(TaskApplicationErrorKind.InvalidArguments, "Field is not writable by application requests.", operation, task.Id);
        }
    }

    private static TaskApplicationResult? ClearField(TaskItem task, TaskApplicationOperation operation) => operation.Field switch
    {
        "descriptionUserText" => SetField(task, operation with { Kind = TaskApplicationOperationKind.SetField, Value = string.Empty }, string.Empty),
        "plannedDuration" => ClearDuration(task),
        "plannedBeginDateTime" => ClearBegin(task),
        "plannedEndDateTime" => ClearEnd(task),
        _ => Failed(TaskApplicationErrorKind.InvalidArguments, "Field cannot be cleared by application requests.", operation, task.Id)
    };

    private static TaskApplicationResult? MutateCriterion(TaskItem task, TaskApplicationOperation operation)
    {
        if (task.Status is DomainTaskStatus.Completed or DomainTaskStatus.Archived)
        {
            return Failed(TaskApplicationErrorKind.BusinessRuleDenied, "Terminal task criteria are immutable.", operation, task.Id);
        }

        var criterion = task.CompletionCriteria.SingleOrDefault(item => string.Equals(item.Id, operation.CriterionId, StringComparison.Ordinal));
        switch (operation.Kind)
        {
            case TaskApplicationOperationKind.AddCriterion:
                if (criterion != null || string.IsNullOrWhiteSpace(operation.CriterionId) || string.IsNullOrWhiteSpace(operation.Text))
                    return Failed(TaskApplicationErrorKind.ConflictingOperations, "Criterion cannot be added.", operation, task.Id);
                task.CompletionCriteria.Add(new TaskCompletionCriterion { Id = operation.CriterionId, Text = operation.Text, IsSatisfied = operation.IsSatisfied ?? false });
                return null;
            case TaskApplicationOperationKind.ReplaceCriterion:
                if (criterion == null || string.IsNullOrWhiteSpace(operation.Text))
                    return Failed(TaskApplicationErrorKind.NotFound, "Criterion was not found or replacement text is empty.", operation, task.Id);
                criterion.Text = operation.Text;
                return null;
            case TaskApplicationOperationKind.RemoveCriterion:
                if (criterion == null) return Failed(TaskApplicationErrorKind.NotFound, "Criterion was not found.", operation, task.Id);
                task.CompletionCriteria.Remove(criterion);
                return null;
            case TaskApplicationOperationKind.SetCriterionSatisfied:
                if (criterion == null || !operation.IsSatisfied.HasValue)
                    return Failed(TaskApplicationErrorKind.InvalidArguments, "Criterion or satisfaction value is missing.", operation, task.Id);
                criterion.IsSatisfied = operation.IsSatisfied.Value;
                return null;
            default:
                return Failed(TaskApplicationErrorKind.InvalidArguments, "Unsupported criterion operation.", operation, task.Id);
        }
    }

    private static TaskApplicationResult? MutateRelation(TaskItem from, TaskItem to, TaskApplicationOperation operation)
    {
        if (string.Equals(from.Id, to.Id, StringComparison.Ordinal))
            return Failed(TaskApplicationErrorKind.ValidationFailed, "Task relation cannot target itself.", operation, from.Id);

        var add = operation.Kind == TaskApplicationOperationKind.AddRelation;
        switch (operation.Relation)
        {
            case "contains":
                SetMembership(from.ContainsTasks, to.Id, add);
                SetMembership(to.ParentTasks, from.Id, add);
                return null;
            case "blocks":
                SetMembership(from.BlocksTasks, to.Id, add);
                SetMembership(to.BlockedByTasks, from.Id, add);
                return null;
            default:
                return Failed(TaskApplicationErrorKind.InvalidArguments, "Relation must be 'contains' or 'blocks'.", operation, from.Id);
        }
    }

    private static TaskApplicationResult? SetStatus(
        IReadOnlyDictionary<string, TaskItem> staged,
        TaskItem task,
        TaskApplicationOperation operation,
        string author)
    {
        if (!operation.Status.HasValue || operation.Status == DomainTaskStatus.InProgress)
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Application status must be NotReady, Prepared, Completed, or Archived.", operation, task.Id);
        if ((operation.Status is DomainTaskStatus.Completed or DomainTaskStatus.Archived) &&
            (string.IsNullOrWhiteSpace(operation.Justification) || operation.EvidenceLinks is not { Count: > 0 } ||
             operation.EvidenceLinks.Any(link => !Uri.TryCreate(link, UriKind.Absolute, out _))))
        {
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Terminal status requires justification and absolute evidence links.", operation, task.Id);
        }
        if (task.AgentExecution?.State is AgentExecutionState.Active or AgentExecutionState.AwaitingInput && task.Status != operation.Status)
            return Failed(TaskApplicationErrorKind.BusinessRuleDenied, "Active agent execution owns task status.", operation, task.Id);

        var rules = new TaskAvailabilityService(staged.Values);
        var transition = rules.EvaluateStatusTransition(task, operation.Status.Value);
        if (!transition.Allowed)
            return Failed(TaskApplicationErrorKind.BusinessRuleDenied, transition.DenialMessage ?? "Status transition is denied.", operation, task.Id);

        if (task.Status != operation.Status.Value)
            task.SetStatus(operation.Status.Value, DateTimeOffset.UtcNow, author);
        return null;
    }

    private static void NormalizeAvailability(IEnumerable<TaskItem> tasks, DateTimeOffset now, string author)
    {
        var list = tasks.ToArray();
        var rules = new TaskAvailabilityService(list);
        foreach (var task in list)
        {
            var analysis = rules.Analyze(task);
            task.IsCanBeCompleted = analysis.IsCanBeCompleted;
            task.UnlockedDateTime = analysis.IsCanBeCompleted ? task.UnlockedDateTime ?? now : null;
            if (task.Status == DomainTaskStatus.InProgress && !analysis.CanStart)
                task.SetStatus(DomainTaskStatus.Prepared, now, author);
        }
    }

    private static TaskGraphReadResult CreateGraph(IEnumerable<TaskItem> tasks) => new(
        tasks.Select(TaskItemSnapshot.Clone).ToArray(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<TaskGraphLoadError>(),
        Array.Empty<TaskGraphDuplicateIdIssue>());

    private static TaskApplicationResult? ValidateRequest(TaskApplicationRequest request)
    {
        if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.ApplicationId) ||
            request.ApplicationId.Length > 128 || string.IsNullOrWhiteSpace(request.Author) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Operations.Count is 0 or > 256 ||
            request.Preconditions.Count > 512 || request.Operations.Any(operation => string.IsNullOrWhiteSpace(operation.OperationId)) ||
            request.Operations.Select(static operation => operation.OperationId).Distinct(StringComparer.Ordinal).Count() != request.Operations.Count ||
            request.Preconditions.Select(static item => item.TaskId).Distinct(StringComparer.Ordinal).Count() != request.Preconditions.Count ||
            request.ProposalRefs.Count == 0 || request.ProposalRefs.Any(reference => string.IsNullOrWhiteSpace(reference.Id) || reference.Revision < 1) ||
            request.ProposalRefs.Select(static reference => reference.Id).Distinct(StringComparer.Ordinal).Count() != request.ProposalRefs.Count)
        {
            return Failed(TaskApplicationErrorKind.InvalidArguments, "Application request is invalid.");
        }

        var claimedTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in request.Operations)
        {
            var target = operation.Kind switch
            {
                TaskApplicationOperationKind.SetField or TaskApplicationOperationKind.ClearField => $"field:{operation.TaskId}:{operation.Field}",
                TaskApplicationOperationKind.AddCriterion or TaskApplicationOperationKind.ReplaceCriterion or
                    TaskApplicationOperationKind.RemoveCriterion or TaskApplicationOperationKind.SetCriterionSatisfied => $"criterion:{operation.TaskId}:{operation.CriterionId}",
                TaskApplicationOperationKind.AddRelation or TaskApplicationOperationKind.RemoveRelation => $"relation:{operation.Relation}:{operation.FromTaskId}:{operation.ToTaskId}",
                TaskApplicationOperationKind.CreateTask => $"create:{operation.NewTaskId}",
                TaskApplicationOperationKind.SetStatus => $"status:{operation.TaskId}",
                _ => $"unknown:{operation.OperationId}"
            };
            if (!claimedTargets.Add(target))
            {
                return Failed(TaskApplicationErrorKind.ConflictingOperations,
                    "Application request contains more than one operation for the same target.", operation);
            }
        }
        return null;
    }

    private static TaskApplicationResult? ReconcilePreviouslyAppliedRequest(
        TaskApplicationRequest request,
        IReadOnlyDictionary<string, TaskItem> tasks)
    {
        var appliedCount = request.Operations.Count(operation => IsOperationAlreadyApplied(operation, tasks));
        if (appliedCount == 0)
        {
            return null;
        }

        var taskIds = request.Operations.SelectMany(AffectedTaskIds).Distinct(StringComparer.Ordinal).ToArray();
        if (appliedCount != request.Operations.Count)
        {
            return Failed(TaskApplicationErrorKind.ReconciliationRequired,
                "Some operations already match the task graph but the application is incomplete; inspect authoritative state before retrying.",
                authoritativeTasks: ToAuthoritativeTasks(tasks, taskIds));
        }

        return new TaskApplicationResult
        {
            Success = true,
            Mode = "alreadyApplied",
            ChangedTaskIds = taskIds,
            CreatedTaskIds = request.Operations.Where(operation => operation.Kind == TaskApplicationOperationKind.CreateTask)
                .Select(static operation => operation.NewTaskId!)
                .ToArray(),
            OperationResults = request.Operations.Select(operation => new TaskApplicationOperationResult
            {
                OperationId = operation.OperationId,
                TaskId = operation.TaskId ?? operation.NewTaskId ?? operation.FromTaskId,
                Outcome = "alreadyApplied"
            }).ToArray(),
            AuthoritativeTasks = ToAuthoritativeTasks(tasks, taskIds)
        };
    }

    private static bool IsOperationAlreadyApplied(TaskApplicationOperation operation, IReadOnlyDictionary<string, TaskItem> tasks)
    {
        tasks.TryGetValue(operation.TaskId ?? operation.NewTaskId ?? operation.FromTaskId ?? string.Empty, out var task);
        return operation.Kind switch
        {
            TaskApplicationOperationKind.SetField when task != null => operation.Field switch
            {
                "title" => string.Equals(task.Title, operation.Value?.Trim(), StringComparison.Ordinal),
                "descriptionUserText" => AgentExecutionDescriptionRenderer.TryRemove(task.Description, out var userText, out _) &&
                                         string.Equals(userText, operation.Value, StringComparison.Ordinal),
                "plannedDuration" => TryParseDuration(operation.Value, out var duration) && task.PlannedDuration == duration,
                "plannedBeginDateTime" => TryParseDate(operation.Value, out var begin) && task.PlannedBeginDateTime == begin,
                "plannedEndDateTime" => TryParseDate(operation.Value, out var end) && task.PlannedEndDateTime == end,
                _ => false
            },
            TaskApplicationOperationKind.ClearField when task != null => operation.Field switch
            {
                "descriptionUserText" => AgentExecutionDescriptionRenderer.TryRemove(task.Description, out var userText, out _) && string.IsNullOrEmpty(userText),
                "plannedDuration" => task.PlannedDuration == null,
                "plannedBeginDateTime" => task.PlannedBeginDateTime == null,
                "plannedEndDateTime" => task.PlannedEndDateTime == null,
                _ => false
            },
            TaskApplicationOperationKind.AddCriterion when task != null => task.CompletionCriteria.Any(item =>
                item.Id == operation.CriterionId && item.Text == operation.Text && item.IsSatisfied == (operation.IsSatisfied ?? false)),
            TaskApplicationOperationKind.ReplaceCriterion when task != null => task.CompletionCriteria.Any(item => item.Id == operation.CriterionId && item.Text == operation.Text),
            TaskApplicationOperationKind.RemoveCriterion when task != null => task.CompletionCriteria.All(item => item.Id != operation.CriterionId),
            TaskApplicationOperationKind.SetCriterionSatisfied when task != null => task.CompletionCriteria.Any(item => item.Id == operation.CriterionId && item.IsSatisfied == operation.IsSatisfied),
            TaskApplicationOperationKind.AddRelation => RelationMatches(tasks, operation, expected: true),
            TaskApplicationOperationKind.RemoveRelation => RelationMatches(tasks, operation, expected: false),
            TaskApplicationOperationKind.CreateTask when task != null => task.Title == operation.Title?.Trim() &&
                task.Description == (operation.DescriptionUserText ?? string.Empty) &&
                task.PlannedDuration == operation.PlannedDuration &&
                task.PlannedBeginDateTime == operation.PlannedBeginDateTime &&
                task.PlannedEndDateTime == operation.PlannedEndDateTime &&
                (operation.ParentIds ?? Array.Empty<string>()).OrderBy(static id => id, StringComparer.Ordinal)
                .SequenceEqual(task.ParentTasks.OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal) &&
                CriteriaMatch(task.CompletionCriteria, operation.Criteria ?? Array.Empty<TaskApplicationCriterion>()),
            TaskApplicationOperationKind.SetStatus when task != null => task.Status == operation.Status,
            _ => false
        };
    }

    private static bool RelationMatches(IReadOnlyDictionary<string, TaskItem> tasks, TaskApplicationOperation operation, bool expected) =>
        tasks.TryGetValue(operation.FromTaskId ?? string.Empty, out var from) &&
        tasks.ContainsKey(operation.ToTaskId ?? string.Empty) &&
        operation.Relation switch
        {
            "contains" => from.ContainsTasks.Contains(operation.ToTaskId, StringComparer.Ordinal) == expected,
            "blocks" => from.BlocksTasks.Contains(operation.ToTaskId, StringComparer.Ordinal) == expected,
            _ => false
        };

    private static bool CriteriaMatch(IReadOnlyList<TaskCompletionCriterion> actual, IReadOnlyList<TaskApplicationCriterion> expected) =>
        actual.Count == expected.Count && expected.All(criterion => actual.Any(item =>
            item.Id == criterion.CriterionId && item.Text == criterion.Text && item.IsSatisfied == criterion.IsSatisfied));

    private static IEnumerable<string> AffectedTaskIds(TaskApplicationOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.TaskId)) yield return operation.TaskId;
        if (!string.IsNullOrWhiteSpace(operation.NewTaskId)) yield return operation.NewTaskId;
        if (!string.IsNullOrWhiteSpace(operation.FromTaskId)) yield return operation.FromTaskId;
        if (!string.IsNullOrWhiteSpace(operation.ToTaskId)) yield return operation.ToTaskId;
        if (operation.ParentIds != null)
            foreach (var parentId in operation.ParentIds.Where(static id => !string.IsNullOrWhiteSpace(id))) yield return parentId;
    }

    private static bool TryParseDuration(string? text, out TimeSpan value)
    {
        try
        {
            value = XmlConvert.ToTimeSpan(text ?? string.Empty);
            return value > TimeSpan.Zero && value <= TimeSpan.FromDays(3650);
        }
        catch (FormatException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryParseDate(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out value) &&
        text is not null && (text.EndsWith('Z') || text.LastIndexOf('+') > text.IndexOf('T') || text.LastIndexOf('-') > text.IndexOf('T'));

    private static TaskApplicationResult? ClearDuration(TaskItem task) { task.PlannedDuration = null; return null; }
    private static TaskApplicationResult? ClearBegin(TaskItem task) { task.PlannedBeginDateTime = null; return null; }
    private static TaskApplicationResult? ClearEnd(TaskItem task) { task.PlannedEndDateTime = null; return null; }
    private static DateTimeOffset NextUpdated(DateTimeOffset? previous, DateTimeOffset now) => previous.HasValue && previous.Value >= now ? previous.Value.AddSeconds(1) : now;
    private static void AddDistinct(ICollection<string> values, string id) { if (!values.Contains(id, StringComparer.Ordinal)) values.Add(id); }
    private static void SetMembership(ICollection<string> values, string id, bool add) { if (add) AddDistinct(values, id); else values.Remove(id); }
    private static IReadOnlyList<TaskItem> ToAuthoritativeTasks(IReadOnlyDictionary<string, TaskItem> tasks, IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal).Where(tasks.ContainsKey).Select(id => TaskItemSnapshot.Clone(tasks[id])).ToArray();
    private static IReadOnlyList<TaskItem> ToAuthoritativeTasks(IReadOnlyDictionary<string, TaskItem> tasks, string? id) => string.IsNullOrWhiteSpace(id) ? Array.Empty<TaskItem>() : ToAuthoritativeTasks(tasks, [id]);
    private static TaskApplicationResult Failed(TaskApplicationErrorKind kind, string message, TaskApplicationOperation? operation = null, string? taskId = null, string? expectedEtag = null, string? actualEtag = null, TaskGraphValidationReport? validation = null, IReadOnlyList<TaskApplicationOperationResult>? operationResults = null, IReadOnlyList<TaskItem>? authoritativeTasks = null) => new()
    {
        Success = false,
        Error = new TaskApplicationError { Kind = kind, Message = message, OperationId = operation?.OperationId, TaskId = taskId, ExpectedEtag = expectedEtag, ActualEtag = actualEtag },
        Validation = validation,
        OperationResults = operationResults ?? Array.Empty<TaskApplicationOperationResult>(),
        AuthoritativeTasks = authoritativeTasks ?? Array.Empty<TaskItem>()
    };
}

public sealed record TaskApplicationRequest
{
    public int SchemaVersion { get; init; }
    public string ApplicationId { get; init; } = string.Empty;
    public IReadOnlyList<TaskApplicationProposalReference> ProposalRefs { get; init; } = Array.Empty<TaskApplicationProposalReference>();
    public string Author { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<TaskApplicationPrecondition> Preconditions { get; init; } = Array.Empty<TaskApplicationPrecondition>();
    public IReadOnlyList<TaskApplicationOperation> Operations { get; init; } = Array.Empty<TaskApplicationOperation>();
}

public sealed record TaskApplicationProposalReference(string Id, int Revision);
public sealed record TaskApplicationPrecondition(string TaskId, string Etag, DomainTaskStatus? Status);
public sealed record TaskApplicationCriterion(string CriterionId, string Text, bool IsSatisfied);

public sealed record TaskApplicationOperation
{
    public string OperationId { get; init; } = string.Empty;
    public TaskApplicationOperationKind Kind { get; init; }
    public string? TaskId { get; init; }
    public string? Field { get; init; }
    public string? Value { get; init; }
    public string? CriterionId { get; init; }
    public string? Text { get; init; }
    public bool? IsSatisfied { get; init; }
    public string? Relation { get; init; }
    public string? FromTaskId { get; init; }
    public string? ToTaskId { get; init; }
    public string? NewTaskId { get; init; }
    public string? Title { get; init; }
    public string? DescriptionUserText { get; init; }
    public TimeSpan? PlannedDuration { get; init; }
    public DateTimeOffset? PlannedBeginDateTime { get; init; }
    public DateTimeOffset? PlannedEndDateTime { get; init; }
    public IReadOnlyList<string>? ParentIds { get; init; }
    public IReadOnlyList<TaskApplicationCriterion>? Criteria { get; init; }
    public DomainTaskStatus? Status { get; init; }
    public string? Justification { get; init; }
    public IReadOnlyList<string>? EvidenceLinks { get; init; }
}

public enum TaskApplicationOperationKind { SetField, ClearField, AddCriterion, ReplaceCriterion, RemoveCriterion, SetCriterionSatisfied, AddRelation, RemoveRelation, CreateTask, SetStatus }
public enum TaskApplicationErrorKind { InvalidArguments, NotFound, PreconditionFailed, ConflictingOperations, DescriptionMarkerConflict, BusinessRuleDenied, ValidationFailed, IdempotencyConflict, ReconciliationRequired, OutcomeUnknown, OperationFailed }
public sealed record TaskApplicationError { public TaskApplicationErrorKind Kind { get; init; } public string Message { get; init; } = string.Empty; public string? OperationId { get; init; } public string? TaskId { get; init; } public string? ExpectedEtag { get; init; } public string? ActualEtag { get; init; } }
public sealed record TaskApplicationOperationResult { public string OperationId { get; init; } = string.Empty; public string? TaskId { get; init; } public string Outcome { get; init; } = string.Empty; }
public sealed record TaskApplicationResult { public bool Success { get; init; } public string Mode { get; init; } = string.Empty; public bool DidMutate { get; init; } public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>(); public IReadOnlyList<string> CreatedTaskIds { get; init; } = Array.Empty<string>(); public IReadOnlyList<TaskApplicationOperationResult> OperationResults { get; init; } = Array.Empty<TaskApplicationOperationResult>(); public TaskApplicationError? Error { get; init; } public TaskGraphValidationReport? Validation { get; init; } public IReadOnlyList<TaskItem> AuthoritativeTasks { get; init; } = Array.Empty<TaskItem>(); }
