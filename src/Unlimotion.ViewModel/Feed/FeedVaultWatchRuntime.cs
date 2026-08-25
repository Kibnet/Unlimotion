using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;

namespace Unlimotion.ViewModel.Feed;

public sealed record FeedVaultIdentityFreezeSignal(
    VaultWatchChange Change,
    string Reason);

public interface IFeedVaultWatchRuntimeSink
{
    ValueTask ReloadMarkdownAsync(DocumentReloadSignal signal, CancellationToken cancellationToken);

    ValueTask ShowMarkdownConflictAsync(DocumentConflictState conflict, CancellationToken cancellationToken);

    ValueTask RefreshAreasAsync(VaultWatchChange change, CancellationToken cancellationToken);

    ValueTask RefreshReviewAsync(VaultWatchChange change, CancellationToken cancellationToken);

    ValueTask ReloadDailyNoteSettingsAsync(VaultWatchChange change, CancellationToken cancellationToken);

    ValueTask FreezeForIdentityChangeAsync(
        FeedVaultIdentityFreezeSignal signal,
        CancellationToken cancellationToken);
}

/// <summary>
/// Bridges the reusable vault watchers to one Feed session. Start it before the bootstrap snapshot,
/// then call <see cref="ActivateAsync"/> after bootstrap so changes observed during that window are
/// replayed before live callbacks begin.
/// </summary>
public sealed class FeedVaultWatchRuntime : IAsyncDisposable
{
    private enum RuntimePhase
    {
        Created,
        Buffering,
        Activating,
        Active,
        Disposed
    }

    private const string IdentityChangedReason = "The portable vault identity changed and must be revalidated before Feed writes continue.";
    private const string SidecarRescanReason = "The sidecar watcher requested a rescan, so the vault identity must be revalidated before Feed writes continue.";

    private readonly object lifecycleSync = new();
    private readonly Queue<VaultWatchChange> bufferedChanges = new();
    private readonly string vaultId;
    private readonly INoteVault vault;
    private readonly IFeedVaultWatchRuntimeSink sink;
    private readonly MarkdownVaultWatcher markdownWatcher;
    private readonly SidecarVaultWatcher sidecarWatcher;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim routeGate = new(1, 1);
    private RuntimePhase phase;
    private bool identityFrozen;

    public FeedVaultWatchRuntime(
        string vaultId,
        INoteVault vault,
        OwnWriteRegistry ownWrites,
        IDirtyDocumentRegistry dirtyDocuments,
        string appLocalRecoveryRoot,
        IFeedVaultWatchRuntimeSink sink,
        IVaultWatchSource? markdownSource = null,
        IVaultWatchSource? sidecarSource = null,
        TimeSpan? coalesceDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(ownWrites);
        ArgumentNullException.ThrowIfNull(dirtyDocuments);
        ArgumentException.ThrowIfNullOrWhiteSpace(appLocalRecoveryRoot);
        ArgumentNullException.ThrowIfNull(sink);

        this.vaultId = vaultId;
        this.vault = vault;
        this.sink = sink;
        OwnWrites = ownWrites;
        DirtyDocuments = dirtyDocuments;
        Drafts = new FileFeedDraftStore(appLocalRecoveryRoot);
        ConflictBundles = new FileDocumentConflictStore(appLocalRecoveryRoot);
        ConflictCoordinator = new DocumentConflictCoordinator(
            vaultId,
            vault,
            dirtyDocuments,
            new RuntimeDocumentExternalChangeSink(this),
            Drafts,
            new BoundedRevisionStore(appLocalRecoveryRoot),
            ConflictBundles,
            ownWrites);

        var markdownSink = new DelegateVaultChangeSink(HandleWatcherChangeAsync);
        var sidecarSink = new DelegateVaultChangeSink(HandleWatcherChangeAsync);
        markdownWatcher = markdownSource is null
            ? new MarkdownVaultWatcher(vault.RootPath, ownWrites, markdownSink, coalesceDelay)
            : new MarkdownVaultWatcher(vault.RootPath, markdownSource, ownWrites, markdownSink, coalesceDelay);
        sidecarWatcher = sidecarSource is null
            ? new SidecarVaultWatcher(vault.RootPath, ownWrites, sidecarSink, coalesceDelay)
            : new SidecarVaultWatcher(vault.RootPath, sidecarSource, ownWrites, sidecarSink, coalesceDelay);
    }

    public OwnWriteRegistry OwnWrites { get; }

    public string VaultId => vaultId;

    public IDirtyDocumentRegistry DirtyDocuments { get; }

    public IFeedDraftStore Drafts { get; }

    public IDocumentConflictStore ConflictBundles { get; }

    public DocumentConflictCoordinator ConflictCoordinator { get; }

    public bool IsIdentityFrozen
    {
        get
        {
            lock (lifecycleSync)
            {
                return identityFrozen;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (lifecycleSync)
            {
                return phase == RuntimePhase.Active;
            }
        }
    }

    public int BufferedChangeCount
    {
        get
        {
            lock (lifecycleSync)
            {
                return bufferedChanges.Count;
            }
        }
    }

    public void Start()
    {
        lock (lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(phase == RuntimePhase.Disposed, this);
            if (phase != RuntimePhase.Created)
            {
                throw new InvalidOperationException("The Feed vault watch runtime has already been started.");
            }

            phase = RuntimePhase.Buffering;
        }

        try
        {
            markdownWatcher.Start();
            sidecarWatcher.Start();
        }
        catch
        {
            lifetimeCancellation.Cancel();
            throw;
        }
    }

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(phase == RuntimePhase.Disposed, this);
            if (phase == RuntimePhase.Created)
            {
                throw new InvalidOperationException("Start the Feed vault watch runtime before activation.");
            }

            if (phase == RuntimePhase.Active)
            {
                return;
            }

            if (phase == RuntimePhase.Activating)
            {
                throw new InvalidOperationException("The Feed vault watch runtime is already activating.");
            }

            phase = RuntimePhase.Activating;
        }

        try
        {
            while (true)
            {
                VaultWatchChange? change;
                lock (lifecycleSync)
                {
                    ObjectDisposedException.ThrowIf(phase == RuntimePhase.Disposed, this);
                    if (bufferedChanges.Count == 0)
                    {
                        phase = RuntimePhase.Active;
                        return;
                    }

                    change = bufferedChanges.Dequeue();
                }

                await RouteAsync(change, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (lifecycleSync)
            {
                if (phase != RuntimePhase.Disposed)
                {
                    phase = RuntimePhase.Buffering;
                }
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (lifecycleSync)
        {
            if (phase == RuntimePhase.Disposed)
            {
                return;
            }

            phase = RuntimePhase.Disposed;
            bufferedChanges.Clear();
            lifetimeCancellation.Cancel();
        }

        Exception? watcherFailure = null;
        try
        {
            await Task.WhenAll(
                    markdownWatcher.DisposeAsync().AsTask(),
                    sidecarWatcher.DisposeAsync().AsTask())
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            watcherFailure = exception;
        }

        await routeGate.WaitAsync().ConfigureAwait(false);
        routeGate.Release();
        await ConflictCoordinator.DisposeAsync().ConfigureAwait(false);
        routeGate.Dispose();
        lifetimeCancellation.Dispose();

        if (watcherFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(watcherFailure).Throw();
        }
    }

    private ValueTask HandleWatcherChangeAsync(
        VaultWatchChange change,
        CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (phase == RuntimePhase.Disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (phase is RuntimePhase.Buffering or RuntimePhase.Activating)
            {
                bufferedChanges.Enqueue(change);
                return ValueTask.CompletedTask;
            }

            if (phase != RuntimePhase.Active)
            {
                throw new InvalidOperationException("The Feed vault watch runtime received an event before Start.");
            }
        }

        return new ValueTask(RouteAsync(change, cancellationToken));
    }

    private async Task RouteAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetimeCancellation.Token);
        var token = linkedCancellation.Token;
        await routeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            lock (lifecycleSync)
            {
                if (phase == RuntimePhase.Disposed || identityFrozen)
                {
                    return;
                }
            }

            if (change.Scope == VaultWatchScope.Markdown)
            {
                await ConflictCoordinator.HandleAsync(change, token).ConfigureAwait(false);
                return;
            }

            await RouteSidecarAsync(change, token).ConfigureAwait(false);
        }
        finally
        {
            routeGate.Release();
        }
    }

    private async ValueTask RouteSidecarAsync(
        VaultWatchChange change,
        CancellationToken cancellationToken)
    {
        if (change.Kind == VaultWatchChangeKind.RescanRequired)
        {
            if (!await HasExpectedIdentityAsync(cancellationToken).ConfigureAwait(false))
            {
                await FreezeForIdentityChangeAsync(change, SidecarRescanReason, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await sink.RefreshAreasAsync(change, cancellationToken).ConfigureAwait(false);
            await sink.RefreshReviewAsync(change, cancellationToken).ConfigureAwait(false);
            await sink.ReloadDailyNoteSettingsAsync(change, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (change.SidecarArtifact == SidecarArtifactKind.VaultIdentity)
        {
            if (await HasExpectedIdentityAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await FreezeForIdentityChangeAsync(change, IdentityChangedReason, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        switch (change.SidecarArtifact)
        {
            case SidecarArtifactKind.Areas:
                await sink.RefreshAreasAsync(change, cancellationToken).ConfigureAwait(false);
                break;
            case SidecarArtifactKind.Review:
                await sink.RefreshReviewAsync(change, cancellationToken).ConfigureAwait(false);
                break;
            case SidecarArtifactKind.DailyNoteSettings:
                await sink.ReloadDailyNoteSettingsAsync(change, cancellationToken).ConfigureAwait(false);
                break;
            case SidecarArtifactKind.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.SidecarArtifact, "Unsupported Feed sidecar artifact.");
        }
    }

    private async Task<bool> HasExpectedIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = await vault.ReadAsync(VaultIdentityService.ManifestPath, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return false;
            }

            var identity = VaultIdentityService.Parse(document.Text);
            return string.Equals(identity.VaultId, vaultId, StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async ValueTask FreezeForIdentityChangeAsync(
        VaultWatchChange change,
        string reason,
        CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (phase == RuntimePhase.Disposed || identityFrozen)
            {
                return;
            }

            identityFrozen = true;
        }

        await sink.FreezeForIdentityChangeAsync(
                new FeedVaultIdentityFreezeSignal(change, reason),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask ReloadMarkdownAsync(
        DocumentReloadSignal signal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycleSync)
        {
            if (phase == RuntimePhase.Disposed || identityFrozen)
            {
                return;
            }
        }

        await sink.ReloadMarkdownAsync(signal, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ShowMarkdownConflictAsync(
        DocumentConflictState conflict,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycleSync)
        {
            if (phase == RuntimePhase.Disposed || identityFrozen)
            {
                return;
            }
        }

        await sink.ShowMarkdownConflictAsync(conflict, cancellationToken).ConfigureAwait(false);
    }

    private sealed class RuntimeDocumentExternalChangeSink(FeedVaultWatchRuntime owner)
        : IDocumentExternalChangeSink
    {
        public ValueTask ReloadAsync(DocumentReloadSignal signal, CancellationToken cancellationToken) =>
            owner.ReloadMarkdownAsync(signal, cancellationToken);

        public ValueTask ConflictAsync(DocumentConflictState conflict, CancellationToken cancellationToken) =>
            owner.ShowMarkdownConflictAsync(conflict, cancellationToken);
    }
}
