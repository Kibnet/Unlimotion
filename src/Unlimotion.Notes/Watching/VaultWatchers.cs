using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Watching;

public sealed record OwnWriteMatch(string OperationId, string RelativePath, string? Revision);

public sealed class OwnWriteRegistration : IDisposable
{
    private readonly OwnWriteRegistry owner;
    private readonly Guid registrationId;
    private int committed;
    private int disposed;

    internal OwnWriteRegistration(OwnWriteRegistry owner, Guid registrationId)
    {
        this.owner = owner;
        this.registrationId = registrationId;
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        Volatile.Write(ref committed, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && Volatile.Read(ref committed) == 0)
        {
            owner.Cancel(registrationId);
        }
    }
}

public sealed class OwnWriteRegistry
{
    private sealed record Entry(
        Guid RegistrationId,
        string OperationId,
        string RelativePath,
        string? Revision,
        DateTimeOffset ExpiresAt);

    private readonly object sync = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan retention;

    public OwnWriteRegistry(TimeProvider? timeProvider = null, TimeSpan? retention = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.retention = retention ?? TimeSpan.FromSeconds(10);
        if (this.retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }
    }

    public OwnWriteRegistration RegisterText(
        string operationId,
        string relativePath,
        string text,
        bool hasUtf8Bom = false) =>
        RegisterRevision(operationId, relativePath, VaultRevision.Compute(VaultRevision.Encode(text, hasUtf8Bom)));

    public OwnWriteRegistration RegisterBytes(string operationId, string relativePath, ReadOnlySpan<byte> bytes) =>
        RegisterRevision(operationId, relativePath, VaultRevision.Compute(bytes));

    public OwnWriteRegistration RegisterDeletion(string operationId, string relativePath) =>
        RegisterRevision(operationId, relativePath, revision: null);

    public OwnWriteRegistration RegisterRevision(string operationId, string relativePath, string? revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var normalized = NormalizeRelativePath(relativePath);
        if (revision is not null && (revision.Length != 64 || revision.Any(static value => !Uri.IsHexDigit(value))))
        {
            throw new ArgumentException("A write revision must be a SHA-256 hex value.", nameof(revision));
        }

        var entry = new Entry(Guid.NewGuid(), operationId, normalized, revision, timeProvider.GetUtcNow() + retention);
        lock (sync)
        {
            RemoveExpiredLocked();
            entries.Add(entry.RegistrationId, entry);
        }

        return new OwnWriteRegistration(this, entry.RegistrationId);
    }

    public bool TryMatch(string relativePath, string? revision, out OwnWriteMatch? match)
    {
        var normalized = NormalizeRelativePath(relativePath);
        lock (sync)
        {
            RemoveExpiredLocked();
            var entry = entries.Values
                .Where(value => PathComparer.Equals(value.RelativePath, normalized)
                    && string.Equals(value.Revision, revision, StringComparison.Ordinal))
                .OrderByDescending(static value => value.ExpiresAt)
                .FirstOrDefault();
            if (entry is null)
            {
                match = null;
                return false;
            }

            match = new OwnWriteMatch(entry.OperationId, entry.RelativePath, entry.Revision);
            return true;
        }
    }

    internal void Cancel(Guid registrationId)
    {
        lock (sync)
        {
            entries.Remove(registrationId);
        }
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("A watched path must be relative to the vault.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or ".."))
        {
            throw new ArgumentException("A watched path cannot escape the vault.", nameof(relativePath));
        }

        return normalized;
    }

    private void RemoveExpiredLocked()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var id in entries.Where(pair => pair.Value.ExpiresAt <= now).Select(static pair => pair.Key).ToArray())
        {
            entries.Remove(id);
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public abstract class CoalescingVaultWatcher : IAsyncDisposable
{
    private sealed record Candidate(
        VaultWatchChangeKind Kind,
        string? RelativePath,
        string? OldRelativePath,
        SidecarArtifactKind SidecarArtifact);

    private readonly string rootPath;
    private readonly string canonicalRootWithSeparator;
    private readonly IVaultWatchSource source;
    private readonly OwnWriteRegistry ownWrites;
    private readonly IVaultChangeSink sink;
    private readonly TimeSpan coalesceDelay;
    private readonly Channel<VaultRawChange> queue = Channel.CreateUnbounded<VaultRawChange>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource cancellation = new();
    private readonly object lifecycleSync = new();
    private Task? pump;
    private Exception? lastFailure;
    private bool started;
    private bool disposed;

    protected CoalescingVaultWatcher(
        string rootPath,
        IVaultWatchSource source,
        OwnWriteRegistry ownWrites,
        IVaultChangeSink sink,
        TimeSpan? coalesceDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        this.rootPath = Path.GetFullPath(rootPath);
        canonicalRootWithSeparator = this.rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        this.source = source;
        this.ownWrites = ownWrites;
        this.sink = sink;
        this.coalesceDelay = coalesceDelay ?? TimeSpan.FromMilliseconds(75);
        if (this.coalesceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(coalesceDelay));
        }
    }

    protected abstract VaultWatchScope Scope { get; }

    public Exception? LastFailure
    {
        get
        {
            lock (lifecycleSync)
            {
                return lastFailure;
            }
        }
    }

    public void Start()
    {
        lock (lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                throw new InvalidOperationException("The vault watcher has already been started.");
            }

            started = true;
            source.Change += OnRawChange;
            pump = PumpAsync(cancellation.Token);
            source.Start();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingPump;
        lock (lifecycleSync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            source.Change -= OnRawChange;
            cancellation.Cancel();
            queue.Writer.TryComplete();
            pendingPump = pump;
        }

        await source.DisposeAsync().ConfigureAwait(false);
        if (pendingPump is not null)
        {
            try
            {
                await pendingPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        cancellation.Dispose();
    }

    protected abstract bool IsIncluded(string relativePath);

    protected virtual SidecarArtifactKind GetSidecarArtifact(string relativePath) => SidecarArtifactKind.None;

    private void OnRawChange(VaultRawChange change)
    {
        if (!disposed)
        {
            queue.Writer.TryWrite(change);
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        while (await queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var candidates = new Dictionary<string, Candidate>(PathComparer);
            while (queue.Reader.TryRead(out var initial))
            {
                AddRawChangeOrRequestRescan(candidates, initial);
            }

            if (coalesceDelay > TimeSpan.Zero)
            {
                await Task.Delay(coalesceDelay, cancellationToken).ConfigureAwait(false);
                while (queue.Reader.TryRead(out var trailing))
                {
                    AddRawChangeOrRequestRescan(candidates, trailing);
                }
            }

            foreach (var candidate in candidates.Values.OrderBy(static value => value.RelativePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var change = await MaterializeAsync(candidate, cancellationToken).ConfigureAwait(false);
                    if (change is null || Volatile.Read(ref disposed))
                    {
                        continue;
                    }

                    if (change.RelativePath is not null
                        && ownWrites.TryMatch(change.RelativePath, change.Revision, out _))
                    {
                        continue;
                    }

                    await sink.HandleAsync(change, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    if (candidate.Kind != VaultWatchChangeKind.RescanRequired)
                    {
                        await TrySignalRescanRequiredAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private void AddRawChangeOrRequestRescan(
        Dictionary<string, Candidate> candidates,
        VaultRawChange raw)
    {
        try
        {
            AddRawChange(candidates, raw);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            candidates["\0rescan"] = new Candidate(
                VaultWatchChangeKind.RescanRequired,
                null,
                null,
                SidecarArtifactKind.None);
        }
    }

    private async ValueTask TrySignalRescanRequiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await sink.HandleAsync(
                    new VaultWatchChange(
                        Scope,
                        VaultWatchChangeKind.RescanRequired,
                        null,
                        null,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (lifecycleSync)
        {
            lastFailure = exception;
        }
    }

    private void AddRawChange(Dictionary<string, Candidate> candidates, VaultRawChange raw)
    {
        if (raw.Kind == VaultRawChangeKind.RescanRequired)
        {
            candidates["\0rescan"] = new Candidate(
                VaultWatchChangeKind.RescanRequired,
                null,
                null,
                SidecarArtifactKind.None);
            return;
        }

        // FileSystemWatcher reports ordinary directory creation/change while our own atomic and
        // bootstrap writes create parent folders. The concrete file notifications carry those
        // changes. Directory rename/delete is different: a whole Markdown subtree may move
        // without reliable child events, so it must still request a rescan.
        if (raw.IsDirectory
            && raw.Kind is VaultRawChangeKind.Created or VaultRawChangeKind.Changed)
        {
            return;
        }

        if (raw.IsDirectory)
        {
            candidates["\0rescan"] = new Candidate(
                VaultWatchChangeKind.RescanRequired,
                null,
                null,
                SidecarArtifactKind.None);
            return;
        }

        var current = TryGetRelativePath(raw.FullPath);
        var previous = raw.OldFullPath is null ? null : TryGetRelativePath(raw.OldFullPath);
        if (raw.Kind == VaultRawChangeKind.Renamed)
        {
            var includesCurrent = current is not null && IsIncluded(current);
            var includesPrevious = previous is not null && IsIncluded(previous);
            if (!includesCurrent && !includesPrevious)
            {
                return;
            }

            if (!includesCurrent)
            {
                Merge(candidates, new Candidate(
                    VaultWatchChangeKind.Deleted,
                    previous,
                    null,
                    GetSidecarArtifact(previous!)));
            }
            else if (!includesPrevious)
            {
                Merge(candidates, new Candidate(
                    VaultWatchChangeKind.Created,
                    current,
                    null,
                    GetSidecarArtifact(current!)));
            }
            else
            {
                var currentArtifact = GetSidecarArtifact(current!);
                var previousArtifact = GetSidecarArtifact(previous!);
                if (currentArtifact != previousArtifact)
                {
                    // A rename between two known sidecars changes two independent contracts.
                    // Model it as delete/create so the old owner (for example daily settings)
                    // can reload its missing-file default instead of observing only the target.
                    Merge(candidates, new Candidate(
                        VaultWatchChangeKind.Deleted,
                        previous,
                        null,
                        previousArtifact));
                    Merge(candidates, new Candidate(
                        VaultWatchChangeKind.Created,
                        current,
                        null,
                        currentArtifact));
                }
                else
                {
                    Merge(candidates, new Candidate(
                        VaultWatchChangeKind.Renamed,
                        current,
                        previous,
                        currentArtifact));
                }
            }

            return;
        }

        if (current is null || !IsIncluded(current))
        {
            return;
        }

        Merge(candidates, new Candidate(
            raw.Kind switch
            {
                VaultRawChangeKind.Created => VaultWatchChangeKind.Created,
                VaultRawChangeKind.Changed => VaultWatchChangeKind.Changed,
                VaultRawChangeKind.Deleted => VaultWatchChangeKind.Deleted,
                _ => throw new ArgumentOutOfRangeException(nameof(raw))
            },
            current,
            null,
            GetSidecarArtifact(current)));
    }

    private static void Merge(Dictionary<string, Candidate> candidates, Candidate incoming)
    {
        var key = incoming.RelativePath ?? "\0rescan";
        if (!candidates.TryGetValue(key, out var existing))
        {
            if (incoming.Kind == VaultWatchChangeKind.Renamed
                && incoming.OldRelativePath is not null
                && candidates.Remove(incoming.OldRelativePath, out var atOldPath)
                && atOldPath.Kind == VaultWatchChangeKind.Created)
            {
                candidates[key] = incoming with { Kind = VaultWatchChangeKind.Created, OldRelativePath = null };
                return;
            }

            candidates[key] = incoming;
            return;
        }

        candidates[key] = (existing.Kind, incoming.Kind) switch
        {
            (VaultWatchChangeKind.Created, VaultWatchChangeKind.Changed) => existing,
            (VaultWatchChangeKind.Renamed, VaultWatchChangeKind.Changed) => existing,
            (VaultWatchChangeKind.Deleted, VaultWatchChangeKind.Created) => incoming with { Kind = VaultWatchChangeKind.Changed },
            (_, VaultWatchChangeKind.Deleted) => incoming,
            _ => incoming
        };
    }

    private async Task<VaultWatchChange?> MaterializeAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Kind == VaultWatchChangeKind.RescanRequired)
        {
            return new VaultWatchChange(Scope, candidate.Kind, null, null, null);
        }

        string? revision = null;
        if (candidate.Kind is not VaultWatchChangeKind.Deleted && candidate.RelativePath is not null)
        {
            var fullPath = ResolveFullPath(candidate.RelativePath);
            revision = await ReadRevisionWithRetryAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (revision is null && !File.Exists(fullPath))
            {
                return new VaultWatchChange(
                    Scope,
                    VaultWatchChangeKind.Deleted,
                    candidate.OldRelativePath ?? candidate.RelativePath,
                    null,
                    null,
                    candidate.SidecarArtifact);
            }
        }

        return new VaultWatchChange(
            Scope,
            candidate.Kind,
            candidate.RelativePath,
            candidate.OldRelativePath,
            revision,
            candidate.SidecarArtifact);
    }

    private async Task<string?> ReadRevisionWithRetryAsync(string fullPath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(fullPath))
                {
                    return null;
                }

                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private string? TryGetRelativePath(string fullPath)
    {
        var canonical = Path.GetFullPath(fullPath);
        if (!canonical.StartsWith(canonicalRootWithSeparator, PathComparison)
            && !string.Equals(canonical, rootPath, PathComparison))
        {
            return null;
        }

        var relative = Path.GetRelativePath(rootPath, canonical).Replace('\\', '/');
        return relative is "." or "" ? null : relative;
    }

    private string ResolveFullPath(string relativePath) => Path.GetFullPath(Path.Combine(rootPath, relativePath));

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed class MarkdownVaultWatcher : CoalescingVaultWatcher
{
    public MarkdownVaultWatcher(
        string rootPath,
        IVaultWatchSource source,
        OwnWriteRegistry ownWrites,
        IVaultChangeSink sink,
        TimeSpan? coalesceDelay = null)
        : base(rootPath, source, ownWrites, sink, coalesceDelay)
    {
    }

    public MarkdownVaultWatcher(
        string rootPath,
        OwnWriteRegistry ownWrites,
        IVaultChangeSink sink,
        TimeSpan? coalesceDelay = null)
        : this(rootPath, new FileSystemVaultWatchSource(rootPath), ownWrites, sink, coalesceDelay)
    {
    }

    protected override VaultWatchScope Scope => VaultWatchScope.Markdown;

    protected override bool IsIncluded(string relativePath) =>
        relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && !relativePath.Split('/').Contains(".unlimotion", StringComparer.OrdinalIgnoreCase);
}

public sealed class SidecarVaultWatcher : CoalescingVaultWatcher
{
    public SidecarVaultWatcher(
        string rootPath,
        IVaultWatchSource source,
        OwnWriteRegistry ownWrites,
        IVaultChangeSink sink,
        TimeSpan? coalesceDelay = null)
        : base(rootPath, source, ownWrites, sink, coalesceDelay)
    {
    }

    public SidecarVaultWatcher(
        string rootPath,
        OwnWriteRegistry ownWrites,
        IVaultChangeSink sink,
        TimeSpan? coalesceDelay = null)
        : this(rootPath, new FileSystemVaultWatchSource(rootPath), ownWrites, sink, coalesceDelay)
    {
    }

    protected override VaultWatchScope Scope => VaultWatchScope.Sidecar;

    protected override bool IsIncluded(string relativePath) => GetSidecarArtifact(relativePath) != SidecarArtifactKind.None;

    protected override SidecarArtifactKind GetSidecarArtifact(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return SidecarArtifactKind.None;
        }

        if (string.Equals(normalized, ".unlimotion/vault.json", StringComparison.OrdinalIgnoreCase))
        {
            return SidecarArtifactKind.VaultIdentity;
        }

        if (string.Equals(normalized, ".unlimotion/areas.json", StringComparison.OrdinalIgnoreCase))
        {
            return SidecarArtifactKind.Areas;
        }

        if (string.Equals(normalized, ".unlimotion/daily-note-settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return SidecarArtifactKind.DailyNoteSettings;
        }

        return normalized.StartsWith(".unlimotion/review/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, ".unlimotion/review", StringComparison.OrdinalIgnoreCase)
                ? SidecarArtifactKind.Review
                : SidecarArtifactKind.None;
    }
}
