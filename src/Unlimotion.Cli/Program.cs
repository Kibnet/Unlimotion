using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using NewtonsoftJsonException = Newtonsoft.Json.JsonException;

namespace Unlimotion.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static int Main(string[] args)
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

            var loadResult = TaskDirectoryReader.Read(options.TasksPath);
            var analyzer = new TaskAvailabilityAnalyzer(loadResult.Tasks);

            return options.Command switch
            {
                "status" => RunStatus(options, loadResult, analyzer),
                "unlocked" => RunUnlocked(options, loadResult, analyzer),
                "task" => RunTask(options, loadResult, analyzer),
                "validate" => RunValidate(options, loadResult, analyzer),
                "set-status" => RunSetStatus(options, loadResult, analyzer),
                "complete" => RunComplete(options, loadResult, analyzer),
                "set-criterion" => RunSetCriterion(options, loadResult),
                "satisfy-criterion" => RunSatisfyCriterion(options, loadResult),
                _ => throw new CliException($"Unknown command '{options.Command}'.")
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 2;
        }
    }

    private static int RunStatus(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
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

    private static int RunUnlocked(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
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

    private static int RunTask(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var taskId = RequireTaskId(options, "task");
        if (!analyzer.TryGetTask(taskId, out _))
        {
            throw new CliException($"Task '{taskId}' was not found.");
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

    private static int RunValidate(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        var validation = analyzer.Validate();
        var output = new ValidationOutput
        {
            TaskCount = validation.TaskCount,
            IsValid = loadResult.LoadErrors.Count == 0 && validation.IsValid,
            LoadErrors = loadResult.LoadErrors,
            ReferenceIssues = validation.ReferenceIssues,
            AvailabilityMismatches = validation.AvailabilityMismatches,
            DuplicateIdIssues = validation.DuplicateIdIssues
        };

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
            Console.WriteLine($"- duplicate id: {duplicate.TaskId} count={duplicate.Count}");
        }

        return output.IsValid ? 0 : 1;
    }

    private static int RunSetStatus(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var task = RequireTask(loadResult, RequireTaskId(options, "set-status"));
        var status = options.Status ?? throw new CliException("Command 'set-status' requires --status <status>.");
        return ChangeStatus(options, loadResult, analyzer, task, status);
    }

    private static int RunComplete(CliOptions options, TaskDirectoryReadResult loadResult, TaskAvailabilityAnalyzer analyzer)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var task = RequireTask(loadResult, RequireTaskId(options, "complete"));
        return ChangeStatus(options, loadResult, analyzer, task, DomainTaskStatus.Completed);
    }

    private static int RunSetCriterion(CliOptions options, TaskDirectoryReadResult loadResult)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var task = RequireTask(loadResult, RequireTaskId(options, "set-criterion"));
        var criterionId = RequireCriterionId(options, "set-criterion");
        if (!options.Satisfied.HasValue)
        {
            throw new CliException("Command 'set-criterion' requires --satisfied true|false.");
        }

        return ChangeCriterion(options, loadResult, task, criterionId, options.Satisfied.Value);
    }

    private static int RunSatisfyCriterion(CliOptions options, TaskDirectoryReadResult loadResult)
    {
        if (loadResult.LoadErrors.Count > 0)
        {
            WriteLoadErrors(options, loadResult.LoadErrors);
            return 1;
        }

        var task = RequireTask(loadResult, RequireTaskId(options, "satisfy-criterion"));
        return ChangeCriterion(options, loadResult, task, RequireCriterionId(options, "satisfy-criterion"), satisfied: true);
    }

    private static int ChangeStatus(
        CliOptions options,
        TaskDirectoryReadResult loadResult,
        TaskAvailabilityAnalyzer analyzer,
        TaskItem task,
        DomainTaskStatus requestedStatus)
    {
        var before = analyzer.Analyze(task.Id);
        if (!CanChangeStatus(task, requestedStatus, before, out var denialReason))
        {
            var deniedOutput = WriteCommandOutput.Denied(task.Id, task.Title, requestedStatus.ToString(), denialReason, before);
            if (options.Format == OutputFormat.Json)
            {
                WriteJson(deniedOutput);
            }
            else
            {
                Console.Error.WriteLine(denialReason);
                WriteAnalysisText(before);
            }

            return 1;
        }

        var now = DateTimeOffset.UtcNow;
        task.SetStatus(requestedStatus, now, options.Author ?? "unlimotion-cli");
        task.UpdatedDateTime = now;

        var changedTaskIds = RecalculateAvailability(loadResult, [task.Id]);
        TaskDirectoryWriter.Write(loadResult, changedTaskIds);

        var afterAnalyzer = new TaskAvailabilityAnalyzer(loadResult.Tasks);
        var after = afterAnalyzer.Analyze(task.Id);
        WriteCommandResult(options, WriteCommandOutput.Succeeded(task.Id, task.Title, $"status={requestedStatus}", changedTaskIds, after));
        return 0;
    }

    private static int ChangeCriterion(
        CliOptions options,
        TaskDirectoryReadResult loadResult,
        TaskItem task,
        string criterionId,
        bool satisfied)
    {
        var criterion = task.CompletionCriteria?.FirstOrDefault(criterion => string.Equals(criterion.Id, criterionId, StringComparison.Ordinal));
        if (criterion == null)
        {
            throw new CliException($"Criterion '{criterionId}' was not found in task '{task.Id}'.");
        }

        criterion.IsSatisfied = satisfied;
        task.UpdatedDateTime = DateTimeOffset.UtcNow;

        var changedTaskIds = RecalculateAvailability(loadResult, [task.Id]);
        TaskDirectoryWriter.Write(loadResult, changedTaskIds);

        var afterAnalyzer = new TaskAvailabilityAnalyzer(loadResult.Tasks);
        var after = afterAnalyzer.Analyze(task.Id);
        WriteCommandResult(options, WriteCommandOutput.Succeeded(task.Id, task.Title, $"criterion={criterionId};satisfied={satisfied}", changedTaskIds, after));
        return 0;
    }

    private static bool CanChangeStatus(
        TaskItem task,
        DomainTaskStatus requestedStatus,
        TaskAvailabilityAnalysis analysis,
        out string denialReason)
    {
        denialReason = string.Empty;
        switch (requestedStatus)
        {
            case DomainTaskStatus.NotReady:
            case DomainTaskStatus.Prepared:
                return true;
            case DomainTaskStatus.InProgress:
                if (analysis.CanStart)
                {
                    return true;
                }

                denialReason = $"Task '{task.Id}' cannot move to InProgress because it is not startable.";
                return false;
            case DomainTaskStatus.Completed:
                if (analysis.CanComplete)
                {
                    return true;
                }

                denialReason = $"Task '{task.Id}' cannot move to Completed because it is not completable.";
                return false;
            case DomainTaskStatus.Archived:
                if (task.Status != DomainTaskStatus.Completed)
                {
                    return true;
                }

                denialReason = $"Task '{task.Id}' cannot move from Completed to Archived.";
                return false;
            default:
                denialReason = $"Unsupported status '{requestedStatus}'.";
                return false;
        }
    }

    private static IReadOnlyList<string> RecalculateAvailability(TaskDirectoryReadResult loadResult, IEnumerable<string> changedTaskIds)
    {
        var changed = new HashSet<string>(changedTaskIds, StringComparer.Ordinal);
        var analyzer = new TaskAvailabilityAnalyzer(loadResult.Tasks);
        foreach (var task in loadResult.Tasks)
        {
            var computed = analyzer.Analyze(task).IsCanBeCompleted;
            if (task.IsCanBeCompleted == computed)
            {
                continue;
            }

            task.IsCanBeCompleted = computed;
            changed.Add(task.Id);
        }

        return changed.OrderBy(static taskId => taskId, StringComparer.Ordinal).ToArray();
    }

    private static void WriteCommandResult(CliOptions options, WriteCommandOutput output)
    {
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(output);
            return;
        }

        Console.WriteLine($"OK: {output.Action} for {output.TaskId} {output.Title}");
        Console.WriteLine($"Changed tasks: {string.Join(", ", output.ChangedTaskIds)}");
        if (output.Analysis != null)
        {
            WriteAnalysisText(output.Analysis);
        }
    }

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

    private static TaskItem RequireTask(TaskDirectoryReadResult loadResult, string taskId)
    {
        if (!loadResult.TasksById.TryGetValue(taskId, out var task))
        {
            throw new CliException($"Task '{taskId}' was not found.");
        }

        return task;
    }

    private static string RequireTaskId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.TaskId)
            ? throw new CliException($"Command '{command}' requires --id <task-id>.")
            : options.TaskId;

    private static string RequireCriterionId(CliOptions options, string command) =>
        string.IsNullOrWhiteSpace(options.CriterionId)
            ? throw new CliException($"Command '{command}' requires --criterion <criterion-id>.")
            : options.CriterionId;

    private static void WriteLoadErrors(CliOptions options, IReadOnlyList<TaskLoadError> errors)
    {
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(new { loadErrors = errors });
            return;
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"Load error: {error.File}: {error.Message}");
        }
    }

    private static void WriteJson(object output) =>
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, JsonOptions));

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
        writer.WriteLine("  unlimotion-cli task --tasks <path> --id <task-id> [--explain] [--format text|json]");
        writer.WriteLine("  unlimotion-cli validate --tasks <path> [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-status --tasks <path> --id <task-id> --status <status> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli complete --tasks <path> --id <task-id> [--author <name>] [--format text|json]");
        writer.WriteLine("  unlimotion-cli set-criterion --tasks <path> --id <task-id> --criterion <criterion-id> --satisfied true|false [--format text|json]");
        writer.WriteLine("  unlimotion-cli satisfy-criterion --tasks <path> --id <task-id> --criterion <criterion-id> [--format text|json]");
    }
}

internal sealed record CliOptions
{
    public string Command { get; init; } = string.Empty;
    public string? TasksPath { get; init; }
    public string? TaskId { get; init; }
    public string? CriterionId { get; init; }
    public DomainTaskStatus? Status { get; init; }
    public bool? Satisfied { get; init; }
    public string? Author { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Text;
    public bool Explain { get; init; }
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
        var explain = false;

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
                    explain = true;
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
            Format = format,
            Explain = explain
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

internal enum OutputFormat
{
    Text,
    Json
}

internal sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }
}

internal static class TaskDirectoryReader
{
    public static TaskDirectoryReadResult Read(string directory)
    {
        var tasks = new List<TaskItem>();
        var filesByTaskId = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<TaskLoadError>();

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            if (ShouldSkip(file))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(file);
                var task = JsonConvert.DeserializeObject<TaskItem>(json, TaskDirectoryJson.CreateConverters());
                if (task == null || string.IsNullOrWhiteSpace(task.Id))
                {
                    errors.Add(new TaskLoadError(file, "File does not contain a task with non-empty Id."));
                    continue;
                }

                tasks.Add(task);
                filesByTaskId[task.Id] = file;
            }
            catch (Exception ex) when (ex is NewtonsoftJsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add(new TaskLoadError(file, ex.Message));
            }
        }

        return new TaskDirectoryReadResult(tasks, filesByTaskId, errors);
    }

    private static bool ShouldSkip(string file)
    {
        var fileName = Path.GetFileName(file);
        return fileName.StartsWith(".", StringComparison.Ordinal) ||
               fileName.EndsWith(".report", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class TaskDirectoryWriter
{
    public static void Write(TaskDirectoryReadResult loadResult, IEnumerable<string> taskIds)
    {
        foreach (var taskId in taskIds.Distinct(StringComparer.Ordinal))
        {
            if (!loadResult.TasksById.TryGetValue(taskId, out var task) || !loadResult.FilesByTaskId.TryGetValue(taskId, out var file))
            {
                throw new CliException($"Cannot save task '{taskId}' because its source file was not found.");
            }

            var json = JsonConvert.SerializeObject(task, Formatting.Indented, TaskDirectoryJson.CreateConverters());
            File.WriteAllText(file, json + Environment.NewLine);
        }
    }
}


internal static class TaskDirectoryJson
{
    public static Newtonsoft.Json.JsonConverter[] CreateConverters() =>
    [
        new IsoDateTimeConverter
        {
            DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
            Culture = CultureInfo.InvariantCulture,
            DateTimeStyles = DateTimeStyles.None
        },
        new StringEnumConverter()
    ];
}

internal sealed record TaskDirectoryReadResult(
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyDictionary<string, string> FilesByTaskId,
    IReadOnlyList<TaskLoadError> LoadErrors)
{
    public IReadOnlyDictionary<string, TaskItem> TasksById { get; } = Tasks
        .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
        .GroupBy(static task => task.Id, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
}

internal sealed record TaskLoadError(string File, string Message);

internal sealed record StatusOutput
{
    public int TaskCount { get; init; }
    public IReadOnlyDictionary<string, int> CountsByStatus { get; init; } = new Dictionary<string, int>();
    public int StartableCount { get; init; }
    public int CompletableCount { get; init; }
    public int CompletedCount { get; init; }
    public int ArchivedCount { get; init; }
}

internal sealed record TaskSummary
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

internal sealed record ValidationOutput
{
    public int TaskCount { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyList<TaskLoadError> LoadErrors { get; init; } = Array.Empty<TaskLoadError>();
    public IReadOnlyList<TaskGraphReferenceIssue> ReferenceIssues { get; init; } = Array.Empty<TaskGraphReferenceIssue>();
    public IReadOnlyList<TaskAvailabilityMismatch> AvailabilityMismatches { get; init; } = Array.Empty<TaskAvailabilityMismatch>();
    public IReadOnlyList<TaskDuplicateIdIssue> DuplicateIdIssues { get; init; } = Array.Empty<TaskDuplicateIdIssue>();
}

internal sealed record WriteCommandOutput
{
    public bool Success { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? Error { get; init; }
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public TaskAvailabilityAnalysis? Analysis { get; init; }

    public static WriteCommandOutput Succeeded(
        string taskId,
        string? title,
        string action,
        IReadOnlyList<string> changedTaskIds,
        TaskAvailabilityAnalysis analysis) => new()
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
        TaskAvailabilityAnalysis analysis) => new()
        {
            Success = false,
            TaskId = taskId,
            Title = title,
            Action = action,
            Error = error,
            Analysis = analysis
        };
}
