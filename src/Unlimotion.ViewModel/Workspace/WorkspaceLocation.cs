using System;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Workspace;

public enum TaskListKind
{
    AllTasks = 0,
    LastCreated = 1,
    LastUpdated = 2,
    Unlocked = 3,
    InProgress = 4,
    Completed = 5,
    Archived = 6,
    LastOpened = 7,
    Roadmap = 8
}

public enum WorkspaceNavigationIntent
{
    FocusObject,
    TargetLocator,
    TraverseHistory
}

public enum WorkspaceLocationKind
{
    Tasks,
    Task,
    Feed,
    Note,
    Review
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
    double? ScrollOffset = null,
    string? ScopeKey = null)
{
    public string HistoryKey => string.Join("\n", ScopeKey ?? string.Empty, Kind, Id, Anchor ?? string.Empty, StateKey ?? string.Empty);

    public string LocatorKey => string.Join("\n", ObjectKey,
        Kind == WorkspaceLocationKind.Feed ? NormalizeNoteIdentity(Id) : string.Empty,
        Anchor ?? string.Empty);

    public TaskListKind TaskListKind
    {
        get
        {
            // Legacy indices are decoded only at this compatibility boundary.
            if (StateKey?.StartsWith("tasktab:", StringComparison.Ordinal) == true
                && int.TryParse(StateKey[8..], out var index) && index is >= 0 and <= 8)
                return (TaskListKind)index;
            return Id.StartsWith("tasks:", StringComparison.Ordinal)
                   && Enum.TryParse<TaskListKind>(Id[6..], out var kind) && Enum.IsDefined(kind)
                ? kind : TaskListKind.AllTasks;
        }
    }

    public int LegacyTaskTabIndex => (int)TaskListKind;

    public bool HasExplicitLocator => Anchor is not null
        || Kind == WorkspaceLocationKind.Feed && Id != "feed";

    // A locator (day, block, filter, scroll) can change without opening a second object.
    public string ObjectKey => (ScopeKey is null ? string.Empty : ScopeKey + "\n") + (Kind switch
    {
        WorkspaceLocationKind.Tasks => $"tasks\n{TaskListKind}",
        WorkspaceLocationKind.Task => $"task\n{Id}",
        WorkspaceLocationKind.Feed => "feed",
        WorkspaceLocationKind.Note => $"note\n{NormalizeNoteIdentity(Id)}",
        WorkspaceLocationKind.Review => "review",
        _ => throw new System.ArgumentOutOfRangeException()
    });

    public WorkspaceMode Mode => Kind is WorkspaceLocationKind.Tasks or WorkspaceLocationKind.Task
        ? WorkspaceMode.Tasks
        : WorkspaceMode.Feed;

    public static WorkspaceLocation TasksRoot => ForTaskList(TaskListKind.AllTasks);

    public static WorkspaceLocation ForTaskList(TaskListKind kind, string? title = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return new WorkspaceLocation(WorkspaceLocationKind.Tasks, $"tasks:{kind}",
            title ?? L10n.Get(kind.ToString()));
    }

    public static WorkspaceLocation FeedRoot { get; } =
        new(WorkspaceLocationKind.Feed, "feed", "Лента");

    public static WorkspaceLocation ReviewRoot { get; } =
        new(WorkspaceLocationKind.Review, "review", "Разбор");

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
