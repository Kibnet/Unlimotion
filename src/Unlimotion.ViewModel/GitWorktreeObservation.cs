using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public enum GitObservationKind { Known, Unknown, NotApplicable }
public enum GitIndicatorState { NotApplicable, Attention, Syncing, Unknown, Dirty, Idle }
public enum BackupOperationResult { Success, Failed, Cancelled, SkippedUnknown }

// Runtime token distinguishes a completed settings projection from a partial reload of another source.
public sealed record GitSettingsProjection(string SourceId, string RepositoryPath, object SourceVersion);

// Capture values, never a runner lease or a mutable settings object.
public sealed record GitObservationRequest(string SourceId, string RepositoryPath, long Generation,
    bool Applicable = true, string? RemoteName = null, string? BranchName = null)
{
    public static string CanonicalPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed record GitWorktreeObservation(GitObservationRequest Request, DateTimeOffset ObservedAt,
    GitObservationKind Kind, int? DirtyFileCount = null, bool? HasConflicts = null,
    int? Ahead = null, int? Behind = null, string? ErrorCode = null);

public interface IGitWorktreeObserver
{
    Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken cancellationToken);
}

public sealed record BackupOperationDiagnostic(string SourceId, string RepositoryPath, Guid OperationId,
    bool Started, BackupOperationResult? Result = null, string? ErrorCode = null, object? SourceVersion = null);

public interface IBackupOperationDiagnostics
{
    event Action<BackupOperationDiagnostic>? OperationDiagnostic;
}
