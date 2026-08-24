namespace Unlimotion.Notes.Watching;

public enum VaultRawChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
    RescanRequired
}

public sealed record VaultRawChange(
    VaultRawChangeKind Kind,
    string FullPath,
    string? OldFullPath = null,
    bool IsDirectory = false);

public interface IVaultWatchSource : IAsyncDisposable
{
    event Action<VaultRawChange>? Change;

    void Start();
}

public enum VaultWatchScope
{
    Markdown,
    Sidecar
}

public enum VaultWatchChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
    RescanRequired
}

public enum SidecarArtifactKind
{
    None,
    VaultIdentity,
    Areas,
    Review
}

public sealed record VaultWatchChange(
    VaultWatchScope Scope,
    VaultWatchChangeKind Kind,
    string? RelativePath,
    string? OldRelativePath,
    string? Revision,
    SidecarArtifactKind SidecarArtifact = SidecarArtifactKind.None);

public interface IVaultChangeSink
{
    ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken);
}

public sealed class DelegateVaultChangeSink(
    Func<VaultWatchChange, CancellationToken, ValueTask> handler) : IVaultChangeSink
{
    public ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken) =>
        handler(change, cancellationToken);
}
