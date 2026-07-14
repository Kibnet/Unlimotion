using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public interface ITaskGraphDiagnosticStorage
{
    Task<TaskGraphReadResult> ReadGraphAsync();
}

public interface ITaskGraphWriteLock
{
    Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation);
}

public sealed record TaskGraphReadResult(
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyDictionary<string, string> FilesByTaskId,
    IReadOnlyList<TaskGraphLoadError> LoadErrors,
    IReadOnlyList<TaskGraphDuplicateIdIssue> DuplicateIdIssues)
{
    public IReadOnlyDictionary<string, TaskItem> TasksById { get; } = Tasks
        .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
        .GroupBy(static task => task.Id, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
}

public sealed record TaskGraphLoadError(string File, string Message);

public sealed record TaskGraphDuplicateIdIssue(string TaskId, IReadOnlyList<string> Files);

public sealed record TaskGraphValidationReport
{
    public int TaskCount { get; init; }
    public bool IsWriteSafe => LoadErrors.Count == 0 &&
                               DuplicateIdIssues.Count == 0 &&
                               ReferenceIssues.Count == 0;
    public bool IsValid => IsWriteSafe && AvailabilityMismatches.Count == 0;
    public IReadOnlyList<TaskGraphLoadError> LoadErrors { get; init; } = Array.Empty<TaskGraphLoadError>();
    public IReadOnlyList<TaskGraphDuplicateIdIssue> DuplicateIdIssues { get; init; } = Array.Empty<TaskGraphDuplicateIdIssue>();
    public IReadOnlyList<TaskGraphReferenceIssue> ReferenceIssues { get; init; } = Array.Empty<TaskGraphReferenceIssue>();
    public IReadOnlyList<TaskAvailabilityMismatch> AvailabilityMismatches { get; init; } = Array.Empty<TaskAvailabilityMismatch>();

    public static TaskGraphValidationReport From(TaskGraphReadResult readResult)
    {
        ArgumentNullException.ThrowIfNull(readResult);

        var validation = new TaskAvailabilityService(readResult.Tasks).Validate();
        return new TaskGraphValidationReport
        {
            TaskCount = validation.TaskCount,
            LoadErrors = readResult.LoadErrors,
            DuplicateIdIssues = BuildDuplicateIdIssues(readResult, validation.DuplicateIdIssues),
            ReferenceIssues = validation.ReferenceIssues,
            AvailabilityMismatches = validation.AvailabilityMismatches
        };
    }

    public string BuildWriteSafetyMessage()
    {
        var messages = new List<string>();
        messages.AddRange(LoadErrors.Select(error => $"load error: {error.File}: {error.Message}"));
        messages.AddRange(DuplicateIdIssues.Select(issue => $"duplicate id: {issue.TaskId} files={string.Join(", ", issue.Files)}"));
        messages.AddRange(ReferenceIssues.Select(issue => $"{issue.Kind}: {issue.Details}"));

        return messages.Count == 0
            ? "Task graph is safe for write commands."
            : "Task graph is not safe for write commands: " + string.Join("; ", messages);
    }

    private static IReadOnlyList<TaskGraphDuplicateIdIssue> BuildDuplicateIdIssues(
        TaskGraphReadResult readResult,
        IReadOnlyList<TaskDuplicateIdIssue> fallbackIssues)
    {
        var issues = readResult.DuplicateIdIssues.ToList();
        var reportedIds = issues
            .Select(static issue => issue.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var issue in fallbackIssues)
        {
            if (!reportedIds.Add(issue.TaskId))
            {
                continue;
            }

            var locations = readResult.Tasks
                .Where(task => string.Equals(task.Id, issue.TaskId, StringComparison.Ordinal))
                .Select((task, index) =>
                    index == 0 && readResult.FilesByTaskId.TryGetValue(task.Id, out var file)
                        ? file
                        : $"<diagnostic-duplicate:{index + 1}>")
                .ToArray();

            issues.Add(new TaskGraphDuplicateIdIssue(
                issue.TaskId,
                locations.Length == 0 ? [$"<diagnostic-duplicate-count:{issue.Count}>"] : locations));
        }

        return issues;
    }
}
