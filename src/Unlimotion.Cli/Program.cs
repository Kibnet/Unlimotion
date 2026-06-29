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

        var output = analyzer.AnalyzeAll()
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

    private static int RunTask(CliOptions options, FileTaskStorageDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var taskId = RequireTaskId(options, "task");
        if (!analyzer.TryGetTask(taskId, out _))
        {
            throw new CliException($"Task '{taskId}' was not found.", exitCode: 1, kind: "notFound");
        }

        var analysis = analyzer.Analyze(taskId);
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(analysis);
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
                WriteJson(ErrorOutput.Create(MapDeniedKind(output.DeniedKind), output.Error ?? "Command was denied."));
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

    private static string MapDeniedKind(TaskOperationDeniedKind deniedKind) => deniedKind switch
    {
        TaskOperationDeniedKind.ValidationFailed => "validationFailed",
        TaskOperationDeniedKind.TaskNotFound => "notFound",
        TaskOperationDeniedKind.CriterionNotFound => "notFound",
        TaskOperationDeniedKind.StatusTransitionDenied => "businessRuleDenied",
        TaskOperationDeniedKind.CompletedCriteriaImmutable => "businessRuleDenied",
        TaskOperationDeniedKind.StorageFailed => "operationFailed",
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
        writer.WriteLine("  unlimotion-cli unlocked --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli task --tasks <path> --id <task-id> [--format text|json]");
        writer.WriteLine("  unlimotion-cli validate --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-status --tasks <path> --id <task-id> --status <status> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli complete --tasks <path> --id <task-id> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-criterion --tasks <path> --id <task-id> --criterion <criterion-id> --satisfied true|false [--format text|json]");
        writer.WriteLine("  unlimotion-cli satisfy-criterion --tasks <path> --id <task-id> --criterion <criterion-id> [--format text|json]");
    }
}

public sealed record CliOptions
{
    public string Command { get; init; } = string.Empty;
    public string? TasksPath { get; init; }
    public string? TaskId { get; init; }
    public string? CriterionId { get; init; }
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
        string? tasksPath = null;
        string? taskId = null;
        string? criterionId = null;
        DomainTaskStatus? status = null;
        bool? satisfied = null;
        string? author = null;
        var format = OutputFormat.Text;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--tasks":
                case "-t":
                    tasksPath = RequireValue(args, ref i, arg);
                    break;
                case "--id":
                    taskId = RequireValue(args, ref i, arg);
                    break;
                case "--criterion":
                    criterionId = RequireValue(args, ref i, arg);
                    break;
                case "--status":
                    status = ParseStatus(RequireValue(args, ref i, arg));
                    break;
                case "--satisfied":
                    satisfied = ParseBoolean(RequireValue(args, ref i, arg), arg);
                    break;
                case "--author":
                    author = RequireValue(args, ref i, arg);
                    break;
                case "--format":
                    format = ParseFormat(RequireValue(args, ref i, arg));
                    break;
                case "--explain":
                    break;
                default:
                    throw new CliException($"Unknown option '{arg}'.");
            }
        }

        return new CliOptions
        {
            Command = command,
            TasksPath = tasksPath,
            TaskId = taskId,
            CriterionId = criterionId,
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

    private static DomainTaskStatus ParseStatus(string value) =>
        Enum.TryParse<DomainTaskStatus>(value, ignoreCase: true, out var status)
            ? status
            : throw new CliException("--status must be one of NotReady, Prepared, InProgress, Completed, Archived.");

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
    public string? Error { get; init; }
    public TaskOperationDeniedKind DeniedKind { get; init; } = TaskOperationDeniedKind.StorageFailed;
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
