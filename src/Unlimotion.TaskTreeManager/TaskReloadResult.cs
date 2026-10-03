using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public interface ITaskReloadReader
{
    Task<TaskReloadResult> ReloadTaskAsync(string taskId);
}

public enum TaskReloadOutcome { Loaded, Missing, Failed }
public enum TaskReloadFailure { None, Unsupported, ReadFailed, ChangedDuringRead, SourceUnavailable }

public sealed record TaskReloadResult(
    TaskReloadOutcome Outcome,
    TaskItem? Snapshot = null,
    long StorageRevision = 0,
    TaskReloadFailure Failure = TaskReloadFailure.None,
    bool AppliedToCache = false)
{
    public static TaskReloadResult Loaded(TaskItem task, long revision = 0) =>
        new(TaskReloadOutcome.Loaded, TaskItemSnapshot.Clone(task), revision);
    public static TaskReloadResult Missing(long revision = 0) => new(TaskReloadOutcome.Missing, StorageRevision: revision);
    public static TaskReloadResult Failed(TaskReloadFailure failure = TaskReloadFailure.ReadFailed) =>
        new(TaskReloadOutcome.Failed, Failure: failure);
}
