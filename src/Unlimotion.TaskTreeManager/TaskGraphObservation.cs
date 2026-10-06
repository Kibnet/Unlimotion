namespace Unlimotion.TaskTree;

public interface ITaskGraphObservationStorage
{
    Task<TaskGraphObservation> ReadObservationAsync(CancellationToken cancellationToken = default);
}

public interface ITaskGraphObservedWriteGuardStorage
{
    /// <summary>Protects writes based on an observation made by this storage, within its held lock and recoverable scope.</summary>
    IDisposable BeginObservedWriteGuard(TaskGraphObservation observation);
}

/// <summary>A verified read of the source namespace; it is not an atomic snapshot of uncooperative writers.</summary>
public sealed record TaskGraphObservation(
    TaskGraphReadResult Graph,
    string SourceManifestHash,
    IReadOnlySet<string> UnstableTaskIds,
    DateTimeOffset StartedAt,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset CompletedAt,
    int Attempts,
    int TaskParseCount,
    int VerifiedFileCount,
    long SourceBytes);

public sealed class TaskGraphObservationException : IOException
{
    public TaskGraphObservationException(string kind, string message, Exception? innerException = null)
        : base(message, innerException) => Kind = kind;

    public string Kind { get; }
    public string? Limit { get; init; }
    public long? Observed { get; init; }
    public long? Maximum { get; init; }
    public int Attempts { get; init; }
    public TaskGraphReadResult? Graph { get; init; }
}
