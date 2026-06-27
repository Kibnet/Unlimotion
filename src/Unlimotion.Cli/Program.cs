using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using Unlimotion.TaskTree;

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

        if (string.IsNullOrWhiteSpace(options.TaskId))
        {
            throw new CliException("Command 'task' requires --id <task-id>.");
        }

        if (!analyzer.TryGetTask(options.TaskId, out _))
        {
            throw new CliException($"Task '{options.TaskId}' was not found.");
        }

        var analysis = analyzer.Analyze(options.TaskId);
        if (options.Format == OutputFormat.Json)
        {
            WriteJson(analysis);
            return 0;
        }

        Console.WriteLine($"{analysis.TaskId} {analysis.Status} {analysis.Title}");
        Console.WriteLine($"isCanBeCompleted: {analysis.IsCanBeCompleted} (stored: {analysis.StoredIsCanBeCompleted})");
        Console.WriteLine($"canStart: {analysis.CanStart}");
        Console.WriteLine($"canComplete: {analysis.CanComplete}");
        Console.WriteLine($"completionCriteriaSatisfied: {analysis.CompletionCriteriaSatisfied}");
        Console.WriteLine($"plannedBeginIsFuture: {analysis.PlannedBeginIsFuture}");
        if (analysis.Reasons.Count == 0)
        {
            Console.WriteLine("Reasons: none");
            return 0;
        }

        Console.WriteLine("Reasons:");
        foreach (var reason in analysis.Reasons)
        {
            Console.WriteLine($"- {reason.Kind}: {reason.Details} ({reason.SubjectId} {reason.SubjectTitle})");
        }

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
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));

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
    }
}

internal sealed record CliOptions
{
    public string Command { get; init; } = string.Empty;
    public string? TasksPath { get; init; }
    public string? TaskId { get; init; }
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
                var task = JsonSerializer.Deserialize<TaskItem>(json, ProgramJsonOptions.Value);
                if (task == null || string.IsNullOrWhiteSpace(task.Id))
                {
                    errors.Add(new TaskLoadError(file, "File does not contain a task with non-empty Id."));
                    continue;
                }

                tasks.Add(task);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add(new TaskLoadError(file, ex.Message));
            }
        }

        return new TaskDirectoryReadResult(tasks, errors);
    }

    private static bool ShouldSkip(string file)
    {
        var fileName = Path.GetFileName(file);
        return fileName.StartsWith("." , StringComparison.Ordinal) ||
               fileName.EndsWith(".report", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ProgramJsonOptions
{
    public static JsonSerializerOptions Value { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal sealed record TaskDirectoryReadResult(IReadOnlyList<TaskItem> Tasks, IReadOnlyList<TaskLoadError> LoadErrors);

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
