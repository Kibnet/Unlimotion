namespace Unlimotion.ViewModel.Workspace;

public enum WorkspaceLocationKind
{
    Tasks,
    Task,
    Feed,
    Note
}

public enum WorkspaceOpenDisposition
{
    CurrentTab,
    NewTab,
    AdjacentPane
}

public sealed record WorkspaceLocation(
    WorkspaceLocationKind Kind,
    string Id,
    string Title,
    string? Anchor = null,
    string? StateKey = null,
    double? ScrollOffset = null)
{
    public string HistoryKey => string.Join("\n", Kind, Id, Anchor ?? string.Empty, StateKey ?? string.Empty);

    // A locator (day, block, filter, scroll) can change without opening a second object.
    public string ObjectKey => Kind switch
    {
        WorkspaceLocationKind.Tasks => $"tasks\n{(StateKey?.StartsWith("tasktab:", System.StringComparison.Ordinal) == true ? StateKey : "tasktab:0")}",
        WorkspaceLocationKind.Task => $"task\n{Id}",
        WorkspaceLocationKind.Feed => "feed",
        WorkspaceLocationKind.Note => $"note\n{NormalizeNoteIdentity(Id)}",
        _ => throw new System.ArgumentOutOfRangeException()
    };

    public WorkspaceMode Mode => Kind is WorkspaceLocationKind.Tasks or WorkspaceLocationKind.Task
        ? WorkspaceMode.Tasks
        : WorkspaceMode.Feed;

    public static WorkspaceLocation TasksRoot { get; } =
        new(WorkspaceLocationKind.Tasks, "tasks", "Задачи");

    public static WorkspaceLocation FeedRoot { get; } =
        new(WorkspaceLocationKind.Feed, "feed", "Лента");

    public static WorkspaceLocation ForTask(string id, string title) =>
        new(WorkspaceLocationKind.Task, id, title);

    public static WorkspaceLocation ForNote(string relativePath, string title, string? anchor = null) =>
        new(WorkspaceLocationKind.Note, relativePath.Replace('\\', '/'), title, anchor);

    public static WorkspaceLocation ForFeedDay(string relativePath, string title, string? anchor = null,
        string? areaFilterKey = null, double? scrollOffset = null) =>
        new(WorkspaceLocationKind.Feed, relativePath.Replace('\\', '/'), title, anchor, areaFilterKey, scrollOffset);

    private static string NormalizeNoteIdentity(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return System.OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }
}
