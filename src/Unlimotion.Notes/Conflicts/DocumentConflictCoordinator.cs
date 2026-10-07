using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;

namespace Unlimotion.Notes.Conflicts;

public sealed record DirtyDocumentBuffer(
    string RelativePath,
    string EditorDocumentText,
    string DirtyBlockMarkdown,
    int BlockIndex,
    string? BaseRevision,
    bool HasUtf8Bom);

public interface IDirtyDocumentRegistry
{
    bool TryGet(string relativePath, out DirtyDocumentBuffer? buffer);

    void Set(DirtyDocumentBuffer buffer);

    void Clear(string relativePath);
}

public sealed class InMemoryDirtyDocumentRegistry : IDirtyDocumentRegistry
{
    private readonly ConcurrentDictionary<string, DirtyDocumentBuffer> buffers = new(PathComparer);

    public bool TryGet(string relativePath, out DirtyDocumentBuffer? buffer) =>
        buffers.TryGetValue(Normalize(relativePath), out buffer);

    public void Set(DirtyDocumentBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.BlockIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer), "A dirty block index cannot be negative.");
        }

        var relativePath = Normalize(buffer.RelativePath);
        buffers[relativePath] = buffer with { RelativePath = relativePath };
    }

    public void Clear(string relativePath) => buffers.TryRemove(Normalize(relativePath), out _);

    private static string Normalize(string relativePath) => OwnWriteRegistry.NormalizeRelativePath(relativePath);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed record DocumentReloadSignal(VaultWatchChange Change, VaultDocument? Document);

public sealed record DocumentConflictState(
    string ConflictId,
    string EditorRelativePath,
    string DiskRelativePath,
    string EditorDocumentText,
    string DirtyBlockMarkdown,
    int BlockIndex,
    string? BaseRevision,
    string EditorRevision,
    bool EditorHasUtf8Bom,
    VaultDocument? DiskDocument,
    VaultWatchChangeKind ExternalChangeKind,
    DateTimeOffset DetectedAt,
    string RecoveryBundlePath);

public interface IDocumentExternalChangeSink
{
    ValueTask ReloadAsync(DocumentReloadSignal signal, CancellationToken cancellationToken);

    ValueTask ConflictAsync(DocumentConflictState conflict, CancellationToken cancellationToken);
}

public enum DocumentConflictResolution
{
    UseEditor,
    UseDisk,
    SaveBoth
}

public sealed record DocumentConflictResolutionResult(
    string ConflictId,
    DocumentConflictResolution Resolution,
    string RelativePath,
    string? ConflictCopyRelativePath,
    string? ResultRevision,
    string RecoveryBundlePath,
    string? DraftCleanupWarning = null);

public sealed class DocumentConflictCoordinator : IVaultChangeSink, IAsyncDisposable
{
    private readonly string vaultId;
    private readonly INoteVault vault;
    private readonly IDirtyDocumentRegistry dirtyDocuments;
    private readonly IDocumentExternalChangeSink sink;
    private readonly IFeedDraftStore drafts;
    private readonly IRevisionStore revisions;
    private readonly IDocumentConflictStore conflictBundles;
    private readonly OwnWriteRegistry ownWrites;
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<string, DocumentConflictState> conflicts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> conflictByEditorPath = new(PathComparer);
    private readonly SemaphoreSlim resolutionGate = new(1, 1);
    private int disposed;

    public DocumentConflictCoordinator(
        string vaultId,
        INoteVault vault,
        IDirtyDocumentRegistry dirtyDocuments,
        IDocumentExternalChangeSink sink,
        IFeedDraftStore drafts,
        IRevisionStore revisions,
        IDocumentConflictStore conflictBundles,
        OwnWriteRegistry ownWrites,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(dirtyDocuments);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(conflictBundles);
        ArgumentNullException.ThrowIfNull(ownWrites);
        this.vaultId = vaultId;
        this.vault = vault;
        this.dirtyDocuments = dirtyDocuments;
        this.sink = sink;
        this.drafts = drafts;
        this.revisions = revisions;
        this.conflictBundles = conflictBundles;
        this.ownWrites = ownWrites;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyCollection<DocumentConflictState> ActiveConflicts => conflicts.Values.ToArray();

    public IFeedDraftStore Drafts => drafts;

    public IDocumentConflictStore ConflictBundles => conflictBundles;

    public async Task<DocumentConflictState?> RestoreAsync(
        DocumentConflictBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(bundle);
        if (!string.Equals(bundle.VaultId, vaultId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The document conflict belongs to another vault identity.");
        }

        var computedEditorRevision = VaultRevision.Compute(
            VaultRevision.Encode(bundle.EditorMarkdown, bundle.EditorHasUtf8Bom));
        if (!string.Equals(computedEditorRevision, bundle.EditorRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The preserved editor version does not match its revision.");
        }

        var currentDisk = await vault.ReadAsync(bundle.DiskRelativePath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(currentDisk?.Revision, bundle.EditorRevision, StringComparison.Ordinal))
        {
            dirtyDocuments.Clear(bundle.EditorRelativePath);
            await conflictBundles.AcknowledgeAsync(vaultId, bundle.ConflictId, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        var detectedAt = timeProvider.GetUtcNow();
        var effectiveBundle = bundle;
        var supersedesStoredBundle = false;
        if (!string.Equals(currentDisk?.Revision, bundle.DiskRevision, StringComparison.Ordinal))
        {
            var nextConflictId = CreateConflictId(
                bundle.EditorRelativePath,
                bundle.DiskRelativePath,
                bundle.BaseRevision,
                bundle.EditorRevision,
                currentDisk?.Revision);
            effectiveBundle = bundle with
            {
                ConflictId = nextConflictId,
                DiskRevision = currentDisk?.Revision,
                DiskMarkdown = currentDisk?.Text,
                DiskHasUtf8Bom = currentDisk?.HasUtf8Bom ?? false,
                CreatedAt = detectedAt
            };
            supersedesStoredBundle = true;
        }

        var recoveryBundlePath = await conflictBundles.PreserveAsync(effectiveBundle, cancellationToken)
            .ConfigureAwait(false);
        if (supersedesStoredBundle)
        {
            await conflictBundles.AcknowledgeAsync(vaultId, bundle.ConflictId, cancellationToken)
                .ConfigureAwait(false);
        }

        dirtyDocuments.Set(new DirtyDocumentBuffer(
            effectiveBundle.EditorRelativePath,
            effectiveBundle.EditorMarkdown,
            effectiveBundle.EditorMarkdown,
            effectiveBundle.BlockIndex,
            effectiveBundle.BaseRevision,
            effectiveBundle.EditorHasUtf8Bom));
        var conflict = new DocumentConflictState(
            effectiveBundle.ConflictId,
            effectiveBundle.EditorRelativePath,
            effectiveBundle.DiskRelativePath,
            effectiveBundle.EditorMarkdown,
            effectiveBundle.EditorMarkdown,
            effectiveBundle.BlockIndex,
            effectiveBundle.BaseRevision,
            effectiveBundle.EditorRevision,
            effectiveBundle.EditorHasUtf8Bom,
            currentDisk,
            currentDisk is null ? VaultWatchChangeKind.Deleted : VaultWatchChangeKind.Changed,
            detectedAt,
            recoveryBundlePath);
        conflicts[conflict.ConflictId] = conflict;
        conflictByEditorPath[conflict.EditorRelativePath] = conflict.ConflictId;
        return conflict;
    }

    public async ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (change.Scope != VaultWatchScope.Markdown)
        {
            return;
        }

        if (change.Kind == VaultWatchChangeKind.RescanRequired)
        {
            await sink.ReloadAsync(new DocumentReloadSignal(change, null), cancellationToken).ConfigureAwait(false);
            return;
        }

        var diskPath = change.RelativePath
            ?? throw new InvalidDataException("A document change must identify its vault path.");
        var editorPath = change.Kind == VaultWatchChangeKind.Renamed && change.OldRelativePath is not null
            ? change.OldRelativePath
            : diskPath;
        if (!dirtyDocuments.TryGet(editorPath, out var dirty) || dirty is null)
        {
            var cleanDocument = change.Kind == VaultWatchChangeKind.Deleted
                ? null
                : await vault.ReadAsync(diskPath, cancellationToken).ConfigureAwait(false);
            await sink.ReloadAsync(new DocumentReloadSignal(change, cleanDocument), cancellationToken).ConfigureAwait(false);
            return;
        }

        var diskDocument = change.Kind == VaultWatchChangeKind.Deleted
            ? null
            : await vault.ReadAsync(diskPath, cancellationToken).ConfigureAwait(false);
        if (change.Kind != VaultWatchChangeKind.Renamed
            && string.Equals(diskDocument?.Revision, dirty.BaseRevision, StringComparison.Ordinal))
        {
            return;
        }

        var editorRevision = VaultRevision.Compute(VaultRevision.Encode(dirty.EditorDocumentText, dirty.HasUtf8Bom));
        if (string.Equals(diskDocument?.Revision, editorRevision, StringComparison.Ordinal))
        {
            await drafts.DeleteAsync(vaultId, editorPath, dirty.BlockIndex, cancellationToken)
                .ConfigureAwait(false);
            dirtyDocuments.Clear(editorPath);
            await sink.ReloadAsync(new DocumentReloadSignal(change, diskDocument), cancellationToken).ConfigureAwait(false);
            return;
        }

        var conflictId = CreateConflictId(editorPath, diskPath, dirty.BaseRevision, editorRevision, diskDocument?.Revision);
        var detectedAt = timeProvider.GetUtcNow();
        await drafts.SaveAsync(new FeedDraft(
                1,
                vaultId,
                editorPath,
                dirty.BlockIndex,
                dirty.BaseRevision ?? string.Empty,
                dirty.DirtyBlockMarkdown,
                detectedAt,
                dirty.EditorDocumentText,
                dirty.HasUtf8Bom), cancellationToken)
            .ConfigureAwait(false);
        var recoveryBundlePath = await conflictBundles.PreserveAsync(new DocumentConflictBundle(
                1,
                vaultId,
                conflictId,
                editorPath,
                diskPath,
                dirty.BlockIndex,
                dirty.BaseRevision,
                editorRevision,
                dirty.EditorDocumentText,
                dirty.HasUtf8Bom,
                diskDocument?.Revision,
                diskDocument?.Text,
                diskDocument?.HasUtf8Bom ?? false,
                detectedAt), cancellationToken)
            .ConfigureAwait(false);
        var conflict = new DocumentConflictState(
            conflictId,
            editorPath,
            diskPath,
            dirty.EditorDocumentText,
            dirty.DirtyBlockMarkdown,
            dirty.BlockIndex,
            dirty.BaseRevision,
            editorRevision,
            dirty.HasUtf8Bom,
            diskDocument,
            change.Kind,
            detectedAt,
            recoveryBundlePath);

        if (conflictByEditorPath.TryGetValue(editorPath, out var previousConflictId))
        {
            conflicts.TryRemove(previousConflictId, out _);
        }

        conflicts[conflictId] = conflict;
        conflictByEditorPath[editorPath] = conflictId;
        await sink.ConflictAsync(conflict, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentConflictResolutionResult> ResolveAsync(
        string conflictId,
        DocumentConflictResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictId);
        await resolutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!conflicts.TryGetValue(conflictId, out var conflict))
            {
                throw new KeyNotFoundException($"Document conflict '{conflictId}' is no longer active.");
            }

            var currentDisk = await vault.ReadAsync(conflict.DiskRelativePath, cancellationToken).ConfigureAwait(false);
            EnsureDiskRevision(conflict, currentDisk);

            string? copyPath = null;
            string? resultRevision;
            switch (resolution)
            {
                case DocumentConflictResolution.UseEditor:
                    if (currentDisk is not null)
                    {
                        await revisions.SaveAsync(vaultId, currentDisk, cancellationToken).ConfigureAwait(false);
                    }

                    using (var ownWrite = ownWrites.RegisterRevision(
                               $"conflict-{conflict.ConflictId}",
                               conflict.DiskRelativePath,
                               conflict.EditorRevision))
                    {
                        var result = currentDisk is null
                            ? await vault.CreateAsync(
                                conflict.DiskRelativePath,
                                conflict.EditorDocumentText,
                                conflict.EditorHasUtf8Bom,
                                cancellationToken).ConfigureAwait(false)
                            : await vault.WriteAsync(
                                conflict.DiskRelativePath,
                                conflict.EditorDocumentText,
                                currentDisk.Revision,
                                conflict.EditorHasUtf8Bom,
                                cancellationToken).ConfigureAwait(false);
                        ownWrite.Commit();
                        resultRevision = result.Revision;
                    }

                    break;

                case DocumentConflictResolution.UseDisk:
                    await PreserveEditorDraftAsync(conflict, cancellationToken).ConfigureAwait(false);
                    resultRevision = currentDisk?.Revision;
                    break;

                case DocumentConflictResolution.SaveBoth:
                    copyPath = CreateConflictCopyPath(conflict.DiskRelativePath, conflict.EditorRevision);
                    var existingCopy = await vault.ReadAsync(copyPath, cancellationToken).ConfigureAwait(false);
                    if (existingCopy is not null)
                    {
                        if (!string.Equals(existingCopy.Revision, conflict.EditorRevision, StringComparison.Ordinal))
                        {
                            throw new IOException($"The deterministic conflict copy '{copyPath}' already contains different data.");
                        }

                        resultRevision = existingCopy.Revision;
                        break;
                    }

                    using (var ownWrite = ownWrites.RegisterRevision(
                               $"conflict-copy-{conflict.ConflictId}",
                               copyPath,
                               conflict.EditorRevision))
                    {
                        var result = await vault.CreateAsync(
                            copyPath,
                            conflict.EditorDocumentText,
                            conflict.EditorHasUtf8Bom,
                            cancellationToken).ConfigureAwait(false);
                        ownWrite.Commit();
                        resultRevision = result.Revision;
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(resolution));
            }

            string? draftCleanupWarning = null;
            if (resolution != DocumentConflictResolution.UseDisk)
            {
                try
                {
                    await drafts.DeleteAsync(
                            vaultId,
                            conflict.EditorRelativePath,
                            conflict.BlockIndex,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    draftCleanupWarning = exception.Message;
                }
            }

            dirtyDocuments.Clear(conflict.EditorRelativePath);
            conflicts.TryRemove(conflict.ConflictId, out _);
            conflictByEditorPath.TryRemove(conflict.EditorRelativePath, out _);
            await conflictBundles.AcknowledgeAsync(vaultId, conflict.ConflictId, cancellationToken)
                .ConfigureAwait(false);
            return new DocumentConflictResolutionResult(
                conflict.ConflictId,
                resolution,
                conflict.DiskRelativePath,
                copyPath,
                resultRevision,
                conflict.RecoveryBundlePath,
                draftCleanupWarning);
        }
        finally
        {
            resolutionGate.Release();
        }
    }

    private Task PreserveEditorDraftAsync(
        DocumentConflictState conflict,
        CancellationToken cancellationToken) =>
        drafts.SaveAsync(new FeedDraft(
            1,
            vaultId,
            conflict.EditorRelativePath,
            conflict.BlockIndex,
            conflict.BaseRevision ?? string.Empty,
            conflict.DirtyBlockMarkdown,
            timeProvider.GetUtcNow(),
            conflict.EditorDocumentText,
            conflict.EditorHasUtf8Bom), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await resolutionGate.WaitAsync().ConfigureAwait(false);
        conflicts.Clear();
        conflictByEditorPath.Clear();
        resolutionGate.Release();
        resolutionGate.Dispose();
    }

    public static string CreateConflictCopyPath(string relativePath, string editorRevision)
    {
        var normalized = OwnWriteRegistry.NormalizeRelativePath(relativePath);
        if (editorRevision.Length < 16 || editorRevision.Any(static value => !Uri.IsHexDigit(value)))
        {
            throw new ArgumentException("A conflict revision must be a SHA-256 hex value.", nameof(editorRevision));
        }

        var directory = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
        var extension = Path.GetExtension(normalized);
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var conflictName = $"{fileName} (Unlimotion conflict {editorRevision[..16].ToLowerInvariant()}){extension}";
        return string.IsNullOrEmpty(directory)
            ? conflictName
            : Path.Combine(directory, conflictName).Replace('\\', '/');
    }

    private static void EnsureDiskRevision(DocumentConflictState conflict, VaultDocument? currentDisk)
    {
        if (!string.Equals(currentDisk?.Revision, conflict.DiskDocument?.Revision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(
                conflict.DiskRelativePath,
                conflict.DiskDocument?.Revision,
                currentDisk?.Revision);
        }
    }

    private static string CreateConflictId(
        string editorPath,
        string diskPath,
        string? baseRevision,
        string editorRevision,
        string? diskRevision)
    {
        var key = string.Join("\n", editorPath, diskPath, baseRevision ?? "<new>", editorRevision, diskRevision ?? "<missing>");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
