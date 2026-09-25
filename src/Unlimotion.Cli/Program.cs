using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.TasksPath))
            {
                throw new CliException("Missing required --tasks <path> option.");
            }

            if (!Directory.Exists(options.TasksPath))
            {
                throw new CliException($"Task directory '{options.TasksPath}' does not exist.");
            }

            var storage = new FileTaskStorage(new FileTaskStorageOptions
            {
                Path = options.TasksPath,
                UseDirectoryLock = true,
                PreserveUnknownJson = true
            });

            return options.Command switch
            {
                "status" => await RunReadCommand(options, storage, RunStatus),
                "unlocked" => await RunReadCommand(options, storage, RunUnlocked),
                "candidates" => await RunReadCommand(options, storage, RunCandidates),
                "apply" => await RunApply(options, storage),
                "claim" => await RunClaim(options, storage),
                "execution" => await RunExecution(options, storage),
                "release" => await RunRelease(options, storage),
                "create" => await RunCreate(options, storage),
                "task" => await RunReadCommand(options, storage, RunTask),
                "validate" => await RunReadCommand(options, storage, RunValidate),
                "set-status" => await RunSetStatus(options, storage),
                "complete" => await RunComplete(options, storage),
                "set-criterion" => await RunSetCriterion(options, storage),
                "satisfy-criterion" => await RunSatisfyCriterion(options, storage),
                _ => throw new CliException($"Unknown command '{options.Command}'.")
            };
        }
        catch (CliException ex)
        {
            WriteError(args, ex.Kind, ex.Message, ex.ExitCode);
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WriteError(args, "operationFailed", ex.Message, exitCode: 1);
            return 1;
        }
    }

    private static async Task<int> RunReadCommand(
        CliOptions options,
        FileTaskStorage storage,
        Func<CliOptions, FileTaskStorageDirectoryReadResult, TaskAvailabilityAnalyzer, int> command)
    {
        var loadResult = await storage.ReadDirectoryAsync();
        var analyzer = new TaskAvailabilityAnalyzer(loadResult.Tasks);
        return command(options, loadResult, analyzer);
    }

    private static int RunStatus(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var analyses = analyzer.AnalyzeAll();
        var output = new StatusOutput
        {
            TaskCount = analyses.Count,
            CountsByStatus = analyses
                .GroupBy(static analysis => analysis.Status.ToString())
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            StartableCount = analyses.Count(static analysis => analysis.CanStart),
            CompletableCount = analyses.Count(static analysis => analysis.CanComplete),
            CompletedCount = analyses.Count(static analysis => analysis.Status == DomainTaskStatus.Completed),
            ArchivedCount = analyses.Count(static analysis => analysis.Status == DomainTaskStatus.Archived)
        };

        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
            return 0;
        }

        Console.WriteLine($"Tasks: {output.TaskCount}");
        foreach (var item in output.CountsByStatus)
        {
            Console.WriteLine($"{item.Key}: {item.Value}");
        }

        Console.WriteLine($"Startable: {output.StartableCount}");
        Console.WriteLine($"Completable: {output.CompletableCount}");
        Console.WriteLine($"Completed: {output.CompletedCount}");
        Console.WriteLine($"Archived: {output.ArchivedCount}");
        return 0;
    }

    private static int RunUnlocked(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var scopedIds = ResolveUnlockedScope(options.RootIds, analyzer);
        var output = analyzer.AnalyzeAll()
            .Where(analysis => scopedIds == null || scopedIds.Contains(analysis.TaskId))
            .Where(static analysis => analysis.CanStart)
            .Select(static analysis => TaskSummary.FromAnalysis(analysis))
            .ToArray();

        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
            return 0;
        }

        if (output.Length == 0)
        {
            Console.WriteLine("No unlocked tasks.");
            return 0;
        }

        foreach (var task in output)
        {
            Console.WriteLine($"{task.Id}\t{task.Status}\t{task.Title}");
        }

        return 0;
    }

    private static IReadOnlySet<string>? ResolveUnlockedScope(
        IReadOnlyList<string> rootIds,
        TaskAvailabilityAnalyzer analyzer)
    {
        if (rootIds.Count == 0)
        {
            return null;
        }

        var scoped = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(rootIds.Reverse());
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!scoped.Add(id))
            {
                continue;
            }

            if (!analyzer.TryGetTask(id, out var task) || task == null)
            {
                throw new CliException($"Scope root task '{id}' was not found.", exitCode: 1, kind: "notFound");
            }

            foreach (var childId in task.ContainsTasks ?? [])
            {
                pending.Push(childId);
            }
        }

        return scoped;
    }

    private static int RunCandidates(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var limit = options.Limit ?? throw new CliException("Command 'candidates' requires --limit <1..100>.");
        var requestedStatus = options.Status ?? DomainTaskStatus.Prepared;
        var requestedStartable = options.Startable ?? true;
        var candidates = analyzer.AnalyzeAll()
            .Where(analysis => analysis.Status == requestedStatus && analysis.CanStart == requestedStartable)
            .Select(analysis =>
            {
                analyzer.TryGetTask(analysis.TaskId, out var task);
                return new CandidateOutput
                {
                    Id = analysis.TaskId,
                    Title = analysis.Title,
                    Status = analysis.Status,
                    Importance = task?.Importance ?? 0,
                    PlannedBeginDateTime = task?.PlannedBeginDateTime,
                    CreatedDateTime = task?.CreatedDateTime ?? DateTimeOffset.MinValue,
                    CanStart = analysis.CanStart,
                    ReasonCount = analysis.Reasons.Count
                };
            })
            .OrderByDescending(static task => task.Importance)
            .ThenBy(static task => task.PlannedBeginDateTime.HasValue ? 0 : 1)
            .ThenBy(static task => task.PlannedBeginDateTime)
            .ThenBy(static task => task.CreatedDateTime)
            .ThenBy(static task => task.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        if (options.Format == OutputFormat.Json)
        {
            WriteJson(candidates);
            return 0;
        }

        foreach (var candidate in candidates)
        {
            Console.WriteLine($"{candidate.Id}\t{candidate.Importance}\t{candidate.Title}");
        }

        return 0;
    }

    private static int RunTask(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var taskId = RequireTaskId(options, "task");
        if (!analyzer.TryGetTask(taskId, out var task))
        {
            throw new CliException($"Task '{taskId}' was not found.", exitCode: 1, kind: "notFound");
        }

        var analysis = analyzer.Analyze(taskId);
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(options.IncludeSections.Count == 0
                ? analysis
                : TaskSnapshotOutput.Create(task!, analysis, analyzer, options.IncludeSections));
            return 0;
        }

        WriteAnalysisText(analysis);
        return 0;
    }

    private static int RunValidate(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        var validation = analyzer.Validate();
        var output = ValidationOutput.From(loadResult, validation);

        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
            return output.IsValid ? 0 : 1;
        }

        Console.WriteLine(output.IsValid
            ? $"Validation OK: {output.TaskCount} tasks."
            : $"Validation failed: {output.TaskCount} tasks.");

        foreach (var error in output.LoadErrors)
        {
            Console.WriteLine($"- load error: {error.File}: {error.Message}");
        }

        foreach (var issue in output.ReferenceIssues)
        {
            Console.WriteLine($"- {issue.Kind}: {issue.Details}");
        }

        foreach (var mismatch in output.AvailabilityMismatches)
        {
            Console.WriteLine($"- availability mismatch: {mismatch.TaskId} stored={mismatch.StoredIsCanBeCompleted} computed={mismatch.ComputedIsCanBeCompleted}");
        }

        foreach (var duplicate in output.DuplicateIdIssues)
        {
            Console.WriteLine($"- duplicate id: {duplicate.TaskId} files={string.Join(", ", duplicate.Files)}");
        }

        return output.IsValid ? 0 : 1;
    }

    private static Task<int> RunSetStatus(CliOptions options, FileTaskStorage storage)
    {
        var taskId = RequireTaskId(options, "set-status");
        var status = options.Status ?? throw new CliException("Command 'set-status' requires --status <status>.");
        return ChangeStatus(options, storage, taskId, status);
    }

    private static Task<int> RunComplete(CliOptions options, FileTaskStorage storage) =>
        ChangeStatus(options, storage, RequireTaskId(options, "complete"), DomainTaskStatus.Completed);

    private static async Task<int> RunClaim(CliOptions options, FileTaskStorage storage)
    {
        var taskId = RequireTaskId(options, "claim");
        var agentId = RequireAgentId(options, "claim");
        var expectedStatus = options.ExpectedStatus ??
            throw new CliException("Command 'claim' requires --expected-status Prepared.");
        var service = CreateCommandService(storage, options);
        var result = await service.TryClaimAsync(taskId, agentId, expectedStatus);
        if (!result.Success)
        {
            WriteJsonOrTextDenied(options, result, taskId, "claim");
            return 1;
        }

        var task = result.AuthoritativeTask ?? throw new InvalidOperationException("Claim succeeded without an authoritative task.");
        var output = new ClaimOutput
        {
            Success = true,
            Task = TaskSummary.FromAnalysis(result.After ?? result.Before!),
            Execution = ExecutionOutput.From(task.AgentExecution!),
            ChangedTaskIds = ChangedIds(result.ChangedTasks),
            StorageRevision = result.StorageRevision,
            DidMutate = true,
            AuditTruncated = task.AgentExecution?.AuditTruncated == true
        };
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
        }
        else
        {
            Console.WriteLine($"Claimed: {task.Id} by {task.AgentExecution!.AgentId} ({task.AgentExecution.LeaseId})");
        }

        return 0;
    }

    private static async Task<int> RunExecution(CliOptions options, FileTaskStorage storage)
    {
        var action = options.ExecutionCommand ??
            throw new CliException("Command 'execution' requires question, answer, result, or complete.");
        var taskId = RequireTaskId(options, $"execution {action}");
        var agentId = RequireAgentId(options, $"execution {action}");
        var leaseId = RequireLeaseId(options, $"execution {action}");
        var service = CreateCommandService(storage, options);
        var result = action switch
        {
            "question" => await service.TryAddExecutionQuestionAsync(
                taskId, agentId, leaseId, RequireText(options, "execution question")),
            "answer" => await service.TryAnswerExecutionQuestionAsync(
                taskId, agentId, leaseId, RequireQuestionId(options), RequireText(options, "execution answer")),
            "result" => await service.TrySetExecutionResultAsync(
                taskId, agentId, leaseId, RequireSummary(options, "execution result"), options.Links),
            "complete" => await service.TryCompleteExecutionAsync(
                taskId, agentId, leaseId, RequireSummary(options, "execution complete"), options.Links),
            _ => throw new CliException($"Unknown execution command '{action}'.")
        };

        return RenderExecutionResult(options, result, taskId, $"execution {action}");
    }

    private static async Task<int> RunRelease(CliOptions options, FileTaskStorage storage)
    {
        var taskId = RequireTaskId(options, "release");
        var result = await CreateCommandService(storage, options).TryReleaseExecutionAsync(
            taskId,
            RequireAgentId(options, "release"),
            RequireLeaseId(options, "release"),
            string.IsNullOrWhiteSpace(options.Reason)
                ? throw new CliException("Command 'release' requires --reason <text>.")
                : options.Reason);
        return RenderExecutionResult(options, result, taskId, "release");
    }

    private static async Task<int> RunCreate(CliOptions options, FileTaskStorage storage)
    {
        var title = string.IsNullOrWhiteSpace(options.Title)
            ? throw new CliException("Command 'create' requires --title <text>.")
            : options.Title;
        var result = await CreateCommandService(storage, options).TryCreateTaskAsync(
            title,
            options.Description,
            options.ParentIds,
            options.Author);
        if (!result.Success)
        {
            WriteJsonOrTextDenied(options, result, result.DeniedReason?.TaskId ?? string.Empty, "create");
            return 1;
        }

        var task = result.AuthoritativeTask ?? throw new InvalidOperationException("Create succeeded without a task.");
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(new CreateTaskOutput
            {
                Success = true,
                Task = TaskSummary.FromAnalysis(result.After!),
                ChangedTaskIds = ChangedIds(result.ChangedTasks),
                StorageRevision = result.StorageRevision,
                DidMutate = true
            });
        }
        else
        {
            Console.WriteLine($"Created: {task.Id} {task.Title}");
        }

        return 0;
    }

    private static async Task<int> RunApply(CliOptions options, FileTaskStorage storage)
    {
        var requestPath = string.IsNullOrWhiteSpace(options.RequestPath)
            ? throw new CliException("Command 'apply' requires --request <path|->.")
            : options.RequestPath;
        var json = requestPath == "-"
            ? await Console.In.ReadToEndAsync()
            : await File.ReadAllTextAsync(requestPath);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024)
        {
            throw new CliException("Application request exceeds the 4 MiB limit.");
        }
        ApplicationRequestInput? input;
        try
        {
            input = JsonSerializer.Deserialize<ApplicationRequestInput>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CliException($"Application request JSON is invalid: {ex.Message}");
        }

        if (input == null)
        {
            throw new CliException("Application request JSON is empty.");
        }

        var request = input.ToDomain();
        var requestHash = RequestHash(json);
        TaskApplicationResult result;
        var receiptWritten = false;
        if (!options.DryRun)
        {
            var receipts = new TaskApplicationReceiptStore(storage.Path);
            var receipt = await receipts.ReadAsync(request.ApplicationId);
            if (receipt != null)
            {
                result = string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal)
                    ? new TaskApplicationResult
                    {
                        Success = true,
                        Mode = "alreadyApplied",
                        ChangedTaskIds = receipt.ChangedTaskIds,
                        CreatedTaskIds = receipt.CreatedTaskIds,
                        OperationResults = receipt.OperationIds.Select(id => new TaskApplicationOperationResult { OperationId = id, Outcome = "alreadyApplied" }).ToArray()
                    }
                    : new TaskApplicationResult
                    {
                        Success = false,
                        Error = new TaskApplicationError { Kind = TaskApplicationErrorKind.IdempotencyConflict, Message = "Application id already belongs to a different request." }
                    };
            }
            else
            {
                var service = new TaskApplicationCommandService(storage, TaskEtag.Create);
                result = await service.TryApplyAsync(request);
                if (result.Success)
                {
                    await receipts.WriteAsync(new TaskApplicationReceipt
                    {
                        ApplicationId = request.ApplicationId,
                        RequestHash = requestHash,
                        AppliedAt = DateTimeOffset.UtcNow,
                        ProposalRefs = request.ProposalRefs.Select(reference => new ApplicationReceiptProposalReference
                        {
                            Id = reference.Id,
                            Revision = reference.Revision
                        }).ToArray(),
                        ChangedTaskIds = result.ChangedTaskIds,
                        CreatedTaskIds = result.CreatedTaskIds,
                        OperationIds = request.Operations.Select(static operation => operation.OperationId).ToArray()
                    });
                    receiptWritten = true;
                }
            }
        }
        else
        {
            result = await new TaskApplicationCommandService(storage, TaskEtag.Create).PreviewAsync(request);
        }
        var output = ApplicationCommandOutput.From(request.ApplicationId, requestHash, result, receiptWritten);
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
        }
        else if (result.Success)
        {
            Console.WriteLine($"{output.Mode}: {string.Join(", ", output.ChangedTaskIds)}");
        }
        else
        {
            Console.Error.WriteLine(output.Error?.Message ?? "Application request failed.");
        }

        return result.Success ? 0 : 1;
    }

    private static string RequestHash(string json) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static int RenderExecutionResult(
        CliOptions options,
        TaskOperationResult result,
        string taskId,
        string action)
    {
        if (!result.Success)
        {
            if (options.Format == OutputFormat.Json)
            {
                WriteJson(ExecutionErrorOutput.Create(result));
            }
            else
            {
                Console.Error.WriteLine(result.DeniedReason?.Message ?? $"Command '{action}' was denied for '{taskId}'.");
            }

            return 1;
        }

        var task = result.AuthoritativeTask ?? throw new InvalidOperationException("Execution write succeeded without a task.");
        var output = new ExecutionMutationOutput
        {
            Success = true,
            Task = TaskSummary.FromAnalysis(result.After ?? result.Before!),
            Execution = ExecutionDetailsOutput.From(task.AgentExecution!),
            ChangedTaskIds = ChangedIds(result.ChangedTasks),
            StorageRevision = result.StorageRevision,
            DidMutate = true,
            AuditTruncated = task.AgentExecution?.AuditTruncated == true
        };
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
        }
        else
        {
            Console.WriteLine($"OK: {action} for {task.Id}; execution={task.AgentExecution!.State}");
        }

        return 0;
    }

    private static Task<int> RunSetCriterion(CliOptions options, FileTaskStorage storage)
    {
        var taskId = RequireTaskId(options, "set-criterion");
        var criterionId = RequireCriterionId(options, "set-criterion");
        if (!options.Satisfied.HasValue)
        {
            throw new CliException("Command 'set-criterion' requires --satisfied true|false.");
        }

        return ChangeCriterion(options, storage, taskId, criterionId, options.Satisfied.Value);
    }

    private static Task<int> RunSatisfyCriterion(CliOptions options, FileTaskStorage storage) =>
        ChangeCriterion(options, storage, RequireTaskId(options, "satisfy-criterion"), RequireCriterionId(options, "satisfy-criterion"), satisfied: true);

    private static async Task<int> ChangeStatus(
        CliOptions options,
        FileTaskStorage storage,
        string taskId,
        DomainTaskStatus requestedStatus)
    {
        var commandService = CreateCommandService(storage, options);
        var result = await commandService.TrySetStatusAsync(taskId, requestedStatus, options.Author);
        WriteCommandResult(options, result, taskId, $"status={requestedStatus}");
        return result.Success ? 0 : 1;
    }

    private static async Task<int> ChangeCriterion(
        CliOptions options,
        FileTaskStorage storage,
        string taskId,
        string criterionId,
        bool satisfied)
    {
        var commandService = CreateCommandService(storage, options);
        var result = await commandService.TrySetCriterionAsync(taskId, criterionId, satisfied, options.Author);
        WriteCommandResult(options, result, taskId, $"criterion={criterionId};satisfied={satisfied}");
        return result.Success ? 0 : 1;
    }

    private static TaskGraphCommandService CreateCommandService(FileTaskStorage storage, CliOptions options) => new(storage)
    {
        StatusAuthorProvider = _ => options.Author ?? "unlimotion-cli"
    };

    private static IReadOnlyList<string> ChangedIds(IEnumerable<TaskItem> changedTasks) =>
        changedTasks
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .Select(static task => task.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static taskId => taskId, StringComparer.Ordinal)
            .ToArray();

    private static string RequireTaskId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.TaskId)
            ? throw new CliException($"Command '{command}' requires --id <task-id>.")
            : options.TaskId;

    private static string RequireCriterionId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.CriterionId)
            ? throw new CliException($"Command '{command}' requires --criterion <criterion-id>.")
            : options.CriterionId;

    private static string RequireAgentId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.AgentId)
            ? throw new CliException($"Command '{command}' requires --agent <agent-id>.")
            : options.AgentId;

    private static string RequireLeaseId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.LeaseId)
            ? throw new CliException($"Command '{command}' requires --lease <lease-id>.")
            : options.LeaseId;

    private static string RequireQuestionId(CliOptions options) =>
        string.IsNullOrWhiteSpace(options.QuestionId)
            ? throw new CliException("Command 'execution answer' requires --question-id <question-id>.")
            : options.QuestionId;

    private static string RequireText(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.Text)
            ? throw new CliException($"Command '{command}' requires --text <text>.")
            : options.Text;

    private static string RequireSummary(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.Summary)
            ? throw new CliException($"Command '{command}' requires --summary <text>.")
            : options.Summary;

    private static void WriteJsonOrTextDenied(
        CliOptions options,
        TaskOperationResult result,
        string taskId,
        string action)
    {
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(ErrorOutput.Create(
                MapDeniedKindForOutput(result.DeniedReason?.Kind ?? TaskOperationDeniedKind.StorageFailed),
                result.DeniedReason?.Message ?? $"Command '{action}' was denied."));
            return;
        }

        Console.Error.WriteLine(result.DeniedReason?.Message ?? $"Command '{action}' was denied for '{taskId}'.");
    }

    private static void WriteCommandResult(
        CliOptions options,
        TaskOperationResult result,
        string taskId,
        string action)
    {
        var analysis = result.After ?? result.Before;
        var title = analysis?.Title;
        var output = result.Success
            ? WriteCommandOutput.Succeeded(
                analysis?.TaskId ?? taskId,
                title,
                action,
                ChangedIds(result.ChangedTasks),
                analysis)
            : WriteCommandOutput.Denied(
                result.DeniedReason?.TaskId ?? analysis?.TaskId ?? taskId,
                title,
                action,
                result.DeniedReason?.Message ?? "Command was denied.",
                analysis,
                result.DeniedReason?.Kind ?? TaskOperationDeniedKind.StorageFailed);

        RenderWriteCommandOutput(options, output);
    }

    private static void RenderWriteCommandOutput(CliOptions options, WriteCommandOutput output)
    {
        if (options.Format == OutputFormat.Json)
        {
            if (!output.Success)
            {
                WriteJson(ErrorOutput.Create(
                    MapDeniedKindForOutput(output.DeniedKind ?? TaskOperationDeniedKind.StorageFailed),
                    output.Error ?? "Command was denied."));
                return;
            }

            WriteJson(output);
            return;
        }

        if (!output.Success)
        {
            Console.Error.WriteLine(output.Error);
        }
        else
        {
            Console.WriteLine($"OK: {output.Action} for {output.TaskId} {output.Title}");
            Console.WriteLine($"Changed tasks: {string.Join(", ", output.ChangedTaskIds)}");
        }

        if (output.Analysis != null)
        {
            WriteAnalysisText(output.Analysis);
        }
    }

    internal static string MapDeniedKindForOutput(TaskOperationDeniedKind deniedKind) => deniedKind switch
    {
        TaskOperationDeniedKind.ValidationFailed => "validationFailed",
        TaskOperationDeniedKind.TaskNotFound => "notFound",
        TaskOperationDeniedKind.CriterionNotFound => "notFound",
        TaskOperationDeniedKind.StatusTransitionDenied => "businessRuleDenied",
        TaskOperationDeniedKind.CompletedCriteriaImmutable => "businessRuleDenied",
        TaskOperationDeniedKind.StorageFailed => "operationFailed",
        TaskOperationDeniedKind.ClaimConflict => "claimConflict",
        TaskOperationDeniedKind.ExecutionStateDenied => "executionStateDenied",
        TaskOperationDeniedKind.LeaseMismatch => "leaseMismatch",
        TaskOperationDeniedKind.QuestionNotFound => "questionNotFound",
        TaskOperationDeniedKind.DescriptionMarkerConflict => "descriptionMarkerConflict",
        TaskOperationDeniedKind.InvalidArguments => "invalidArguments",
        TaskOperationDeniedKind.OutcomeUnknown => "outcomeUnknown",
        _ => "operationFailed"
    };

    private static void WriteAnalysisText(TaskAvailabilityAnalysis analysis)
    {
        Console.WriteLine($"{analysis.TaskId} {analysis.Status} {analysis.Title}");
        Console.WriteLine($"isCanBeCompleted: {analysis.IsCanBeCompleted} (stored: {analysis.StoredIsCanBeCompleted})");
        Console.WriteLine($"canStart: {analysis.CanStart}");
        Console.WriteLine($"canComplete: {analysis.CanComplete}");
        Console.WriteLine($"completionCriteriaSatisfied: {analysis.CompletionCriteriaSatisfied}");
        Console.WriteLine($"plannedBeginIsFuture: {analysis.PlannedBeginIsFuture}");
        if (analysis.Reasons.Count == 0)
        {
            Console.WriteLine("Reasons: none");
            return;
        }

        Console.WriteLine("Reasons:");
        foreach (var reason in analysis.Reasons)
        {
            Console.WriteLine($"- {reason.Kind}: {reason.Details} ({reason.SubjectId} {reason.SubjectTitle})");
        }
    }

    private static void WriteLoadErrors(CliOptions options, IReadOnlyList<FileTaskStorageLoadError> errors)
    {
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(ErrorOutput.Create(
                "loadFailed",
                "Task directory contains load errors: " +
                string.Join("; ", errors.Select(error => $"{error.File}: {error.Message}"))));
            return;
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"Load error: {error.File}: {error.Message}");
        }
    }

    private static void WriteJson(object output) =>
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));

    private static void WriteError(string[] args, string kind, string message, int exitCode)
    {
        if (WantsJson(args))
        {
            WriteJson(ErrorOutput.Create(kind, message));
            return;
        }

        Console.Error.WriteLine(message);
        if (exitCode == 2)
        {
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
        }
    }

    private static bool WantsJson(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--format", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[i + 1], "json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void PrintUsage(TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine("Usage:");
        writer.WriteLine("  unlimotion-cli status --tasks <path> [--format text|json]");
        writer.WriteLine("  Task directory: --tasks <path>, then UNLIMOTION_TASKS, then active local desktop settings.");
        writer.WriteLine("  unlimotion-cli unlocked --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli unlocked [--root <task-id>]... --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli candidates --tasks <path> --limit <1..100> [--status <status>] [--startable true|false] [--sort default] [--format text|json]");
        writer.WriteLine("  unlimotion-cli claim --tasks <path> --id <task-id> --agent <agent-id> --expected-status Prepared [--format text|json]");
        writer.WriteLine("  unlimotion-cli execution question --tasks <path> --id <task-id> --agent <agent-id> --lease <lease-id> --text <text> [--format text|json]");
        writer.WriteLine("  unlimotion-cli execution answer --tasks <path> --id <task-id> --agent <agent-id> --lease <lease-id> --question-id <question-id> --text <text> [--format text|json]");
        writer.WriteLine("  unlimotion-cli execution result|complete --tasks <path> --id <task-id> --agent <agent-id> --lease <lease-id> --summary <text> [--link <absolute-uri>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli release --tasks <path> --id <task-id> --agent <agent-id> --lease <lease-id> --reason <text> [--format text|json]");
        writer.WriteLine("  unlimotion-cli create --tasks <path> --title <text> [--description <text>] [--parent <task-id>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli apply --tasks <path> --request <path|-> [--dry-run] [--format text|json]");
        writer.WriteLine("  unlimotion-cli task --tasks <path> --id <task-id> [--include details,relations,criteria,history,execution] [--format text|json]");
        writer.WriteLine("  unlimotion-cli validate --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-status --tasks <path> --id <task-id> --status <status> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli complete --tasks <path> --id <task-id> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-criterion --tasks <path> --id <task-id> --criterion <criterion-id> --satisfied true|false [--format text|json]");
        writer.WriteLine("  unlimotion-cli satisfy-criterion --tasks <path> --id <task-id> --criterion <criterion-id> [--format text|json]");
    }
}

public sealed record CliOptions
{
    private static readonly HashSet<string> KnownCommands =
    [
        "status",
        "unlocked",
        "candidates",
        "apply",
        "claim",
        "execution",
        "release",
        "create",
        "task",
        "validate",
        "set-status",
        "complete",
        "set-criterion",
        "satisfy-criterion"
    ];

    public string Command { get; init; } = string.Empty;
    public string? ExecutionCommand { get; init; }
    public string? TasksPath { get; init; }
    public string? TaskId { get; init; }
    public string? CriterionId { get; init; }
    public string? AgentId { get; init; }
    public string? LeaseId { get; init; }
    public string? QuestionId { get; init; }
    public string? Text { get; init; }
    public string? Summary { get; init; }
    public string? Reason { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ParentIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RootIds { get; init; } = Array.Empty<string>();
    public string? RequestPath { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlySet<string> IncludeSections { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public DomainTaskStatus? ExpectedStatus { get; init; }
    public int? Limit { get; init; }
    public bool? Startable { get; init; }
    public string? Sort { get; init; }
    public DomainTaskStatus? Status { get; init; }
    public bool? Satisfied { get; init; }
    public string? Author { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Text;
    public bool ShowHelp { get; init; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            return new CliOptions { ShowHelp = true, Command = "help" };
        }

        var command = args[0].ToLowerInvariant();
        if (!KnownCommands.Contains(command))
        {
            throw new CliException($"Unknown command '{command}'.");
        }

        string? executionCommand = null;
        var firstOptionIndex = 1;
        if (command == "execution")
        {
            if (args.Length < 2 || args[1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new CliException("Command 'execution' requires question, answer, result, or complete.");
            }

            executionCommand = args[1].ToLowerInvariant();
            if (executionCommand is not ("question" or "answer" or "result" or "complete"))
            {
                throw new CliException($"Unknown execution command '{executionCommand}'.");
            }

            firstOptionIndex = 2;
        }

        string? tasksPath = null;
        string? taskId = null;
        string? criterionId = null;
        string? agentId = null;
        string? leaseId = null;
        string? questionId = null;
        string? text = null;
        string? summary = null;
        string? reason = null;
        string? title = null;
        string? description = null;
        var links = new List<string>();
        var parentIds = new List<string>();
        var rootIds = new List<string>();
        string? requestPath = null;
        var dryRun = false;
        var includeSections = new HashSet<string>(StringComparer.Ordinal);
        DomainTaskStatus? expectedStatus = null;
        int? limit = null;
        bool? startable = null;
        string? sort = null;
        DomainTaskStatus? status = null;
        bool? satisfied = null;
        string? author = null;
        var format = OutputFormat.Text;
        var suppliedOptions = new HashSet<string>(StringComparer.Ordinal);

        for (var i = firstOptionIndex; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--tasks":
                case "-t":
                    suppliedOptions.Add("--tasks");
                    tasksPath = RequireValue(args, ref i, arg);
                    break;
                case "--id":
                    suppliedOptions.Add(arg);
                    taskId = RequireValue(args, ref i, arg);
                    break;
                case "--criterion":
                    suppliedOptions.Add(arg);
                    criterionId = RequireValue(args, ref i, arg);
                    break;
                case "--limit":
                    suppliedOptions.Add(arg);
                    limit = ParseLimit(RequireValue(args, ref i, arg));
                    break;
                case "--agent":
                    suppliedOptions.Add(arg);
                    agentId = RequireValue(args, ref i, arg);
                    break;
                case "--lease":
                    suppliedOptions.Add(arg);
                    leaseId = RequireValue(args, ref i, arg);
                    break;
                case "--question-id":
                    suppliedOptions.Add(arg);
                    questionId = RequireValue(args, ref i, arg);
                    break;
                case "--text":
                    suppliedOptions.Add(arg);
                    text = RequireValue(args, ref i, arg);
                    break;
                case "--summary":
                    suppliedOptions.Add(arg);
                    summary = RequireValue(args, ref i, arg);
                    break;
                case "--reason":
                    suppliedOptions.Add(arg);
                    reason = RequireValue(args, ref i, arg);
                    break;
                case "--title":
                    suppliedOptions.Add(arg);
                    title = RequireValue(args, ref i, arg);
                    break;
                case "--description":
                    suppliedOptions.Add(arg);
                    description = RequireValue(args, ref i, arg);
                    break;
                case "--link":
                    suppliedOptions.Add(arg);
                    links.Add(RequireValue(args, ref i, arg));
                    break;
                case "--parent":
                    suppliedOptions.Add(arg);
                    parentIds.Add(RequireValue(args, ref i, arg));
                    break;
                case "--root":
                    suppliedOptions.Add(arg);
                    rootIds.Add(RequireValue(args, ref i, arg));
                    break;
                case "--request":
                    suppliedOptions.Add(arg);
                    requestPath = RequireValue(args, ref i, arg);
                    break;
                case "--dry-run":
                    suppliedOptions.Add(arg);
                    dryRun = true;
                    break;
                case "--include":
                    suppliedOptions.Add(arg);
                    AddIncludeSections(includeSections, RequireValue(args, ref i, arg));
                    break;
                case "--expected-status":
                    suppliedOptions.Add(arg);
                    expectedStatus = ParseStatus(RequireValue(args, ref i, arg));
                    break;
                case "--status":
                    suppliedOptions.Add(arg);
                    status = ParseStatus(RequireValue(args, ref i, arg));
                    break;
                case "--startable":
                    suppliedOptions.Add(arg);
                    startable = ParseBoolean(RequireValue(args, ref i, arg), arg);
                    break;
                case "--sort":
                    suppliedOptions.Add(arg);
                    sort = RequireValue(args, ref i, arg);
                    if (!string.Equals(sort, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CliException("--sort currently supports only 'default'.");
                    }
                    sort = "default";
                    break;
                case "--satisfied":
                    suppliedOptions.Add(arg);
                    satisfied = ParseBoolean(RequireValue(args, ref i, arg), arg);
                    break;
                case "--author":
                    suppliedOptions.Add(arg);
                    author = RequireValue(args, ref i, arg);
                    break;
                case "--format":
                    suppliedOptions.Add(arg);
                    format = ParseFormat(RequireValue(args, ref i, arg));
                    break;
                default:
                    throw new CliException($"Unknown option '{arg}'.");
            }
        }

        ValidateOptions(command, executionCommand, suppliedOptions);

        if (parentIds.Count != parentIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new CliException("--parent values must be unique.");
        }

        return new CliOptions
        {
            Command = command,
            ExecutionCommand = executionCommand,
            TasksPath = tasksPath ?? TaskDirectoryResolver.Resolve(null),
            TaskId = taskId,
            CriterionId = criterionId,
            AgentId = agentId,
            LeaseId = leaseId,
            QuestionId = questionId,
            Text = text,
            Summary = summary,
            Reason = reason,
            Title = title,
            Description = description,
            Links = links,
            ParentIds = parentIds,
            RootIds = rootIds,
            RequestPath = requestPath,
            DryRun = dryRun,
            IncludeSections = includeSections,
            ExpectedStatus = expectedStatus,
            Limit = limit,
            Startable = startable,
            Sort = sort,
            Status = status,
            Satisfied = satisfied,
            Author = author,
            Format = format
        };
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliException($"Option '{optionName}' requires a value.");
        }

        index++;
        return args[index];
    }

    private static OutputFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "text" => OutputFormat.Text,
        "json" => OutputFormat.Json,
        _ => throw new CliException("--format must be 'text' or 'json'.")
    };

    private static int ParseLimit(string value)
    {
        if (!int.TryParse(value, out var limit) || limit is < 1 or > 100)
        {
            throw new CliException("--limit must be an integer from 1 to 100.");
        }

        return limit;
    }

    private static DomainTaskStatus ParseStatus(string value)
    {
        var statusName = Enum.GetNames<DomainTaskStatus>()
            .FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
        return statusName != null
            ? Enum.Parse<DomainTaskStatus>(statusName)
            : throw new CliException("--status must be one of NotReady, Prepared, InProgress, Completed, Archived.");
    }

    private static void AddIncludeSections(ISet<string> target, string value)
    {
        foreach (var rawSection in value.Split(','))
        {
            var section = rawSection.Trim().ToLowerInvariant();
            if (section is not ("details" or "relations" or "criteria" or "history" or "execution"))
            {
                throw new CliException($"Unknown task include section '{rawSection}'.");
            }

            target.Add(section);
        }
    }

    private static void ValidateOptions(
        string command,
        string? executionCommand,
        IReadOnlySet<string> suppliedOptions)
    {
        var allowedOptions = (command, executionCommand) switch
        {
            ("status" or "validate", _) => new[] { "--tasks", "--format" },
            ("unlocked", _) => new[] { "--tasks", "--root", "--format" },
            ("apply", _) => new[] { "--tasks", "--request", "--dry-run", "--format" },
            ("candidates", _) => new[] { "--tasks", "--limit", "--status", "--startable", "--sort", "--format" },
            ("claim", _) => new[] { "--tasks", "--id", "--agent", "--expected-status", "--format" },
            ("task", _) => new[] { "--tasks", "--id", "--include", "--format" },
            ("execution", "question") => new[] { "--tasks", "--id", "--agent", "--lease", "--text", "--format" },
            ("execution", "answer") => new[] { "--tasks", "--id", "--agent", "--lease", "--question-id", "--text", "--format" },
            ("execution", "result" or "complete") => new[] { "--tasks", "--id", "--agent", "--lease", "--summary", "--link", "--format" },
            ("release", _) => new[] { "--tasks", "--id", "--agent", "--lease", "--reason", "--format" },
            ("create", _) => new[] { "--tasks", "--title", "--description", "--parent", "--author", "--format" },
            ("set-status", _) => new[] { "--tasks", "--id", "--status", "--author", "--format" },
            ("complete", _) => new[] { "--tasks", "--id", "--author", "--format" },
            ("set-criterion", _) => new[] { "--tasks", "--id", "--criterion", "--satisfied", "--format" },
            ("satisfy-criterion", _) => new[] { "--tasks", "--id", "--criterion", "--format" },
            _ => Array.Empty<string>()
        };
        var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
        var invalid = suppliedOptions.FirstOrDefault(option => !allowed.Contains(option));
        if (invalid != null)
        {
            throw new CliException($"Option '{invalid}' is not valid for command '{command}'.");
        }
    }

    private static bool ParseBoolean(string value, string optionName) => value.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => throw new CliException($"{optionName} must be true or false.")
    };
}

public enum OutputFormat
{
    Text,
    Json
}

public sealed class CliException : Exception
{
    public CliException(string message, int exitCode = 2, string kind = "invalidArguments")
        : base(message)
    {
        ExitCode = exitCode;
        Kind = kind;
    }

    public int ExitCode { get; }
    public string Kind { get; }
}

public sealed record ErrorOutput
{
    public bool Success { get; init; }
    public ErrorDetails Error { get; init; } = new();

    public static ErrorOutput Create(string kind, string message) => new()
    {
        Success = false,
        Error = new ErrorDetails
        {
            Kind = kind,
            Message = message
        }
    };
}

public sealed record ErrorDetails
{
    public string Kind { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record StatusOutput
{
    public int TaskCount { get; init; }
    public IReadOnlyDictionary<string, int> CountsByStatus { get; init; } = new Dictionary<string, int>();
    public int StartableCount { get; init; }
    public int CompletableCount { get; init; }
    public int CompletedCount { get; init; }
    public int ArchivedCount { get; init; }
}

public sealed record TaskSummary
{
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public DomainTaskStatus Status { get; init; }
    public bool IsCanBeCompleted { get; init; }
    public bool CanStart { get; init; }
    public bool CanComplete { get; init; }
    public int ReasonCount { get; init; }

    public static TaskSummary FromAnalysis(TaskAvailabilityAnalysis analysis) => new()
    {
        Id = analysis.TaskId,
        Title = analysis.Title,
        Status = analysis.Status,
        IsCanBeCompleted = analysis.IsCanBeCompleted,
        CanStart = analysis.CanStart,
        CanComplete = analysis.CanComplete,
        ReasonCount = analysis.Reasons.Count
    };
}

public sealed record CandidateOutput
{
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public DomainTaskStatus Status { get; init; }
    public int Importance { get; init; }
    public DateTimeOffset? PlannedBeginDateTime { get; init; }
    public DateTimeOffset CreatedDateTime { get; init; }
    public bool CanStart { get; init; }
    public int ReasonCount { get; init; }
}

public sealed record ExecutionOutput
{
    public string AgentId { get; init; } = string.Empty;
    public string LeaseId { get; init; } = string.Empty;
    public AgentExecutionState State { get; init; }
    public DateTimeOffset ClaimedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static ExecutionOutput From(AgentExecutionRecord execution) => new()
    {
        AgentId = execution.AgentId,
        LeaseId = execution.LeaseId,
        State = execution.State,
        ClaimedAt = execution.ClaimedAt,
        UpdatedAt = execution.UpdatedAt
    };
}

public sealed record ClaimOutput
{
    public bool Success { get; init; }
    public TaskSummary Task { get; init; } = new();
    public ExecutionOutput Execution { get; init; } = new();
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public long StorageRevision { get; init; }
    public bool DidMutate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AuditTruncated { get; init; }
}

public sealed record ExecutionDetailsOutput
{
    public string AgentId { get; init; } = string.Empty;
    public string LeaseId { get; init; } = string.Empty;
    public AgentExecutionState State { get; init; }
    public DateTimeOffset ClaimedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<ExecutionQuestionOutput> Questions { get; init; } = Array.Empty<ExecutionQuestionOutput>();
    public ExecutionResultOutput? Result { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
    public string? ReleaseReason { get; init; }
    public IReadOnlyList<ExecutionAttemptOutput> PreviousAttempts { get; init; } = Array.Empty<ExecutionAttemptOutput>();
    public bool AuditTruncated { get; init; }

    public static ExecutionDetailsOutput From(AgentExecutionRecord execution) => new()
    {
        AgentId = execution.AgentId,
        LeaseId = execution.LeaseId,
        State = execution.State,
        ClaimedAt = execution.ClaimedAt,
        UpdatedAt = execution.UpdatedAt,
        Questions = (execution.Questions ?? [])
            .Select(static question => new ExecutionQuestionOutput
            {
                Id = question.Id,
                Text = question.Text,
                AskedAt = question.AskedAt,
                Answer = question.Answer,
                AnsweredAt = question.AnsweredAt
            })
            .OrderBy(static question => question.AskedAt)
            .ThenBy(static question => question.Id, StringComparer.Ordinal)
            .ToArray(),
        Result = execution.Result == null
            ? null
            : new ExecutionResultOutput
            {
                Summary = execution.Result.Summary,
                Links = (execution.Result.Links ?? []).ToArray(),
                RecordedAt = execution.Result.RecordedAt
            },
        ReleasedAt = execution.ReleasedAt,
        ReleaseReason = execution.ReleaseReason,
        PreviousAttempts = (execution.PreviousAttempts ?? [])
            .Select(static attempt => new ExecutionAttemptOutput
            {
                AgentId = attempt.AgentId,
                LeaseId = attempt.LeaseId,
                State = attempt.State,
                ClaimedAt = attempt.ClaimedAt,
                UpdatedAt = attempt.UpdatedAt,
                ReleasedAt = attempt.ReleasedAt,
                ReleaseReason = attempt.ReleaseReason,
                CompletedAt = attempt.CompletedAt
            })
            .ToArray(),
        AuditTruncated = execution.AuditTruncated
    };
}

public sealed record ExecutionQuestionOutput
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public DateTimeOffset AskedAt { get; init; }
    public string? Answer { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }
}

public sealed record ExecutionResultOutput
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
    public DateTimeOffset RecordedAt { get; init; }
}

public sealed record ExecutionAttemptOutput
{
    public string AgentId { get; init; } = string.Empty;
    public string LeaseId { get; init; } = string.Empty;
    public AgentExecutionState State { get; init; }
    public DateTimeOffset ClaimedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
    public string? ReleaseReason { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ExecutionMutationOutput
{
    public bool Success { get; init; }
    public TaskSummary Task { get; init; } = new();
    public ExecutionDetailsOutput Execution { get; init; } = new();
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public long StorageRevision { get; init; }
    public bool DidMutate { get; init; }
    public bool AuditTruncated { get; init; }
}

public sealed record CreateTaskOutput
{
    public bool Success { get; init; }
    public TaskSummary Task { get; init; } = new();
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public long StorageRevision { get; init; }
    public bool DidMutate { get; init; }
}

public sealed record ExecutionErrorOutput
{
    public bool Success { get; init; }
    public ErrorDetails Error { get; init; } = new();
    public AuthoritativeTaskOutput? AuthoritativeTask { get; init; }

    public static ExecutionErrorOutput Create(TaskOperationResult result) => new()
    {
        Success = false,
        Error = new ErrorDetails
        {
            Kind = Program.MapDeniedKindForOutput(result.DeniedReason?.Kind ?? TaskOperationDeniedKind.StorageFailed),
            Message = result.DeniedReason?.Message ?? "Execution command was denied."
        },
        AuthoritativeTask = result.AuthoritativeTask == null
            ? result.Before == null ? null : AuthoritativeTaskOutput.From(result.Before)
            : AuthoritativeTaskOutput.From(result.AuthoritativeTask)
    };
}

public sealed record AuthoritativeTaskOutput
{
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public DomainTaskStatus Status { get; init; }
    public string? ExecutionAgentId { get; init; }
    public AgentExecutionState? ExecutionState { get; init; }

    public static AuthoritativeTaskOutput From(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Status = task.Status,
        ExecutionAgentId = task.AgentExecution?.AgentId,
        ExecutionState = task.AgentExecution?.State
    };

    public static AuthoritativeTaskOutput From(TaskAvailabilityAnalysis analysis) => new()
    {
        Id = analysis.TaskId,
        Title = analysis.Title,
        Status = analysis.Status
    };
}

public sealed record TaskSnapshotOutput
{
    public string Etag { get; init; } = string.Empty;
    public TaskSummary Task { get; init; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskDetailsOutput? Details { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TaskRelationOutput>? Relations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TaskCriterionOutput>? Criteria { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TaskHistoryOutput>? History { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionDetailsOutput? Execution { get; init; }

    public static TaskSnapshotOutput Create(
        TaskItem task,
        TaskAvailabilityAnalysis analysis,
        TaskAvailabilityAnalyzer analyzer,
        IReadOnlySet<string> include)
    {
        return new TaskSnapshotOutput
        {
            Etag = TaskEtag.Create(task),
            Task = TaskSummary.FromAnalysis(analysis),
            Details = include.Contains("details") ? TaskDetailsOutput.From(task) : null,
            Relations = include.Contains("relations") ? BuildRelations(task, analyzer) : null,
            Criteria = include.Contains("criteria")
                ? (task.CompletionCriteria ?? [])
                    .Where(static criterion => criterion != null)
                    .OrderBy(static criterion => criterion.Id, StringComparer.Ordinal)
                    .Select(static criterion => new TaskCriterionOutput
                    {
                        Id = criterion.Id,
                        Text = criterion.Text,
                        IsSatisfied = criterion.IsSatisfied
                    }).ToArray()
                : null,
            History = include.Contains("history")
                ? (task.StatusHistory ?? [])
                    .Where(static entry => entry != null)
                    .OrderBy(static entry => entry.ChangedAt)
                    .ThenBy(static entry => entry.Status)
                    .Select(static entry => new TaskHistoryOutput
                    {
                        Status = entry.Status,
                        ChangedAt = entry.ChangedAt,
                        Author = entry.Author
                    }).ToArray()
                : null,
            Execution = include.Contains("execution") && task.AgentExecution != null
                ? ExecutionDetailsOutput.From(task.AgentExecution)
                : null
        };
    }

    private static IReadOnlyList<TaskRelationOutput> BuildRelations(
        TaskItem task,
        TaskAvailabilityAnalyzer analyzer)
    {
        var relations = new List<TaskRelationOutput>();
        AddRelations(relations, task.ContainsTasks, nameof(TaskItem.ContainsTasks), analyzer);
        AddRelations(relations, task.ParentTasks, nameof(TaskItem.ParentTasks), analyzer);
        AddRelations(relations, task.BlocksTasks, nameof(TaskItem.BlocksTasks), analyzer);
        AddRelations(relations, task.BlockedByTasks, nameof(TaskItem.BlockedByTasks), analyzer);
        return relations
            .OrderBy(static relation => relation.Type, StringComparer.Ordinal)
            .ThenBy(static relation => relation.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddRelations(
        ICollection<TaskRelationOutput> target,
        IEnumerable<string>? ids,
        string type,
        TaskAvailabilityAnalyzer analyzer)
    {
        foreach (var id in (ids ?? []).Distinct(StringComparer.Ordinal))
        {
            analyzer.TryGetTask(id, out var related);
            target.Add(new TaskRelationOutput
            {
                Type = type,
                Id = id,
                Title = related?.Title,
                Status = related?.Status,
                Exists = related != null
            });
        }
    }
}

public sealed record TaskDetailsOutput
{
    public string Description { get; init; } = string.Empty;
    public string DescriptionUserText { get; init; } = string.Empty;
    public int Importance { get; init; }
    public bool Wanted { get; init; }
    public DateTimeOffset CreatedDateTime { get; init; }
    public DateTimeOffset? UpdatedDateTime { get; init; }
    public DateTimeOffset? PlannedBeginDateTime { get; init; }
    public DateTimeOffset? PlannedEndDateTime { get; init; }
    public TimeSpan? PlannedDuration { get; init; }

    public static TaskDetailsOutput From(TaskItem task)
    {
        var markerState = AgentExecutionDescriptionRenderer.TryRemove(task.Description, out var userText, out _);
        return new TaskDetailsOutput
        {
        Description = task.Description,
        DescriptionUserText = markerState ? userText : task.Description,
        Importance = task.Importance,
        Wanted = task.Wanted,
        CreatedDateTime = task.CreatedDateTime,
        UpdatedDateTime = task.UpdatedDateTime,
        PlannedBeginDateTime = task.PlannedBeginDateTime,
        PlannedEndDateTime = task.PlannedEndDateTime,
        PlannedDuration = task.PlannedDuration
        };
    }
}

public sealed record ApplicationCommandOutput
{
    public bool Success { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string ApplicationId { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public bool DidMutate { get; init; }
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CreatedTaskIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TaskApplicationOperationResult> OperationResults { get; init; } = Array.Empty<TaskApplicationOperationResult>();
    public bool ReceiptWritten { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TaskGraphValidationReport? Validation { get; init; }
    public IReadOnlyList<ApplicationAuthoritativeTaskOutput> AuthoritativeTasks { get; init; } = Array.Empty<ApplicationAuthoritativeTaskOutput>();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApplicationCommandError? Error { get; init; }

    public static ApplicationCommandOutput From(string applicationId, string requestHash, TaskApplicationResult result, bool receiptWritten) => new()
    {
        Success = result.Success,
        Mode = result.Mode,
        ApplicationId = applicationId,
        RequestHash = requestHash,
        DidMutate = result.DidMutate,
        ChangedTaskIds = result.ChangedTaskIds,
        CreatedTaskIds = result.CreatedTaskIds,
        OperationResults = result.OperationResults,
        ReceiptWritten = receiptWritten,
        Validation = result.Validation,
        AuthoritativeTasks = result.AuthoritativeTasks.Select(ApplicationAuthoritativeTaskOutput.From).ToArray(),
        Error = result.Error == null ? null : new ApplicationCommandError
        {
            Kind = Map(result.Error.Kind),
            Message = result.Error.Message,
            OperationId = result.Error.OperationId,
            TaskId = result.Error.TaskId,
            ExpectedEtag = result.Error.ExpectedEtag,
            ActualEtag = result.Error.ActualEtag
        }
    };

    private static string Map(TaskApplicationErrorKind kind) => kind switch
    {
        TaskApplicationErrorKind.InvalidArguments => "invalidArguments",
        TaskApplicationErrorKind.NotFound => "notFound",
        TaskApplicationErrorKind.PreconditionFailed => "preconditionFailed",
        TaskApplicationErrorKind.ConflictingOperations => "conflictingOperations",
        TaskApplicationErrorKind.DescriptionMarkerConflict => "descriptionMarkerConflict",
        TaskApplicationErrorKind.BusinessRuleDenied => "businessRuleDenied",
        TaskApplicationErrorKind.ValidationFailed => "validationFailed",
        TaskApplicationErrorKind.IdempotencyConflict => "idempotencyConflict",
        TaskApplicationErrorKind.ReconciliationRequired => "reconciliationRequired",
        TaskApplicationErrorKind.OutcomeUnknown => "outcomeUnknown",
        _ => "operationFailed"
    };
}

public sealed record ApplicationAuthoritativeTaskOutput
{
    public string Id { get; init; } = string.Empty;
    public string Etag { get; init; } = string.Empty;
    public DomainTaskStatus Status { get; init; }
    public TaskDetailsOutput Details { get; init; } = new();
    public IReadOnlyList<TaskCriterionOutput> Criteria { get; init; } = Array.Empty<TaskCriterionOutput>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RelationIds { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public static ApplicationAuthoritativeTaskOutput From(TaskItem task) => new()
    {
        Id = task.Id,
        Etag = TaskEtag.Create(task),
        Status = task.Status,
        Details = TaskDetailsOutput.From(task),
        Criteria = task.CompletionCriteria.OrderBy(static item => item.Id, StringComparer.Ordinal)
            .Select(static item => new TaskCriterionOutput { Id = item.Id, Text = item.Text, IsSatisfied = item.IsSatisfied }).ToArray(),
        RelationIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["contains"] = task.ContainsTasks.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            ["parents"] = task.ParentTasks.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            ["blocks"] = task.BlocksTasks.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            ["blockedBy"] = task.BlockedByTasks.OrderBy(static id => id, StringComparer.Ordinal).ToArray()
        }
    };
}

public sealed record ApplicationCommandError
{
    public string Kind { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OperationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaskId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExpectedEtag { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ActualEtag { get; init; }
}

public sealed record TaskRelationOutput
{
    public string Type { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public DomainTaskStatus? Status { get; init; }
    public bool Exists { get; init; }
}

public sealed record TaskCriterionOutput
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool IsSatisfied { get; init; }
}

public sealed record TaskHistoryOutput
{
    public DomainTaskStatus Status { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
    public string Author { get; init; } = string.Empty;
}

public sealed record ValidationOutput
{
    public int TaskCount { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyList<FileTaskStorageLoadError> LoadErrors { get; init; } = Array.Empty<FileTaskStorageLoadError>();
    public IReadOnlyList<TaskGraphReferenceIssue> ReferenceIssues { get; init; } = Array.Empty<TaskGraphReferenceIssue>();
    public IReadOnlyList<TaskAvailabilityMismatch> AvailabilityMismatches { get; init; } = Array.Empty<TaskAvailabilityMismatch>();
    public IReadOnlyList<FileTaskStorageDuplicateIdIssue> DuplicateIdIssues { get; init; } = Array.Empty<FileTaskStorageDuplicateIdIssue>();

    public static ValidationOutput From(FileTaskStorageDirectoryReadResult loadResult, TaskGraphValidationResult validation) => new()
    {
        TaskCount = validation.TaskCount,
        IsValid = loadResult.LoadErrors.Count == 0 &&
                  loadResult.DuplicateIdIssues.Count == 0 &&
                  validation.ReferenceIssues.Count == 0 &&
                  validation.DuplicateIdIssues.Count == 0 &&
                  validation.AvailabilityMismatches.Count == 0,
        LoadErrors = loadResult.LoadErrors,
        ReferenceIssues = validation.ReferenceIssues,
        AvailabilityMismatches = validation.AvailabilityMismatches,
        DuplicateIdIssues = loadResult.DuplicateIdIssues
    };
}

public sealed record WriteCommandOutput
{
    public bool Success { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Action { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskOperationDeniedKind? DeniedKind { get; init; }
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public TaskAvailabilityAnalysis? Analysis { get; init; }

    public static WriteCommandOutput Succeeded(
        string taskId,
        string? title,
        string action,
        IReadOnlyList<string> changedTaskIds,
        TaskAvailabilityAnalysis? analysis) => new()
        {
            Success = true,
            TaskId = taskId,
            Title = title,
            Action = action,
            ChangedTaskIds = changedTaskIds,
            Analysis = analysis
        };

    public static WriteCommandOutput Denied(
        string taskId,
        string? title,
        string action,
        string error,
        TaskAvailabilityAnalysis? analysis,
        TaskOperationDeniedKind deniedKind) => new()
        {
            Success = false,
            TaskId = taskId,
            Title = title,
            Action = action,
            Error = error,
            Analysis = analysis,
            DeniedKind = deniedKind
        };
}
