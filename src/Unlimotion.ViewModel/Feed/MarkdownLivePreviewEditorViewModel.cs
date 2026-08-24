using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Recovery;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed record MarkdownLiveDocumentSnapshot(
    string Raw,
    string ExpectedRevisionHash,
    bool HasUtf8Bom,
    string RelativePath);

public sealed record MarkdownBlockTextRange(int Start, int Length);

public sealed record MarkdownBlockPatch(
    string RelativePath,
    string ExpectedRevisionHash,
    bool HasUtf8Bom,
    int BlockIndex,
    MarkdownBlockTextRange Selection,
    string OriginalRaw,
    string ReplacementRaw,
    string PatchedDocumentRaw)
{
    public string ApplyTo(string currentRaw)
    {
        ArgumentNullException.ThrowIfNull(currentRaw);
        if (Selection.Start < 0
            || Selection.Length < 0
            || Selection.Start + Selection.Length > currentRaw.Length
            || !currentRaw.AsSpan(Selection.Start, Selection.Length).SequenceEqual(OriginalRaw.AsSpan()))
        {
            throw new InvalidOperationException("The Markdown block no longer matches the patch selection.");
        }

        return currentRaw[..Selection.Start] + ReplacementRaw + currentRaw[(Selection.Start + Selection.Length)..];
    }
}

public sealed record MarkdownBlockCommitResult(
    bool IsAccepted,
    MarkdownLiveDocumentSnapshot? Snapshot = null,
    string? ErrorMessage = null)
{
    public static MarkdownBlockCommitResult Accepted(MarkdownLiveDocumentSnapshot snapshot) => new(true, snapshot);

    public static MarkdownBlockCommitResult Rejected(string errorMessage) => new(false, null, errorMessage);
}

public enum MarkdownLiveBlockRenderKind
{
    Blank,
    Heading,
    Paragraph,
    ListItem,
    TaskListItem,
    BlockQuote,
    FencedCode,
    HorizontalRule,
    RawFallback
}

public enum MarkdownInlineTokenKind
{
    Text,
    Emphasis,
    Strong,
    Link,
    WikiLink
}

public sealed record MarkdownInlineToken(
    MarkdownInlineTokenKind Kind,
    string Text,
    string? Target = null,
    bool IsSafeLink = false);

public static class MarkdownUriPolicy
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        "unlimotion"
    };

    public static bool IsSafe(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || target.Any(static character => char.IsControl(character))
            || target.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            return AllowedSchemes.Contains(absolute.Scheme);
        }

        return !target.Contains(':')
            && !Path.IsPathRooted(target);
    }
}

public sealed class MarkdownLivePreviewEditorViewModel : ReactiveObject, IDisposable
{
    private const string TaskUriPrefix = "unlimotion://task/";
    private static readonly TimeSpan DefaultAutosaveDelay = TimeSpan.FromSeconds(2);
    private readonly IMarkdownDocumentParser parser;
    private readonly TimeSpan autosaveDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> autosaveDelayAsync;
    private readonly SemaphoreSlim commitGate = new(1, 1);
    private readonly Dictionary<string, FeedTaskReferenceViewModel> taskReferences =
        new(StringComparer.Ordinal);
    private readonly HashSet<int> reviewSelectionIndices = [];
    private readonly object autosaveSync = new();
    private readonly object draftPersistenceSync = new();
    private readonly HashSet<int> persistedDraftBlocks = [];
    private CancellationTokenSource sessionCancellation = new();
    private CancellationTokenSource? autosaveDebounceCancellation;
    private Task autosaveTask = Task.CompletedTask;
    private Task draftPersistenceTail = Task.CompletedTask;
    private MarkdownLiveDocumentSnapshot? snapshot;
    private MarkdownLiveBlockViewModel? activeBlock;
    private FeedDraft? recoveryDraft;
    private Func<MarkdownBlockPatch, CancellationToken, Task<MarkdownBlockCommitResult>>? commitBlockAsync;
    private IFeedDraftStore? draftStore;
    private string? draftVaultId;
    private TimeProvider draftTimeProvider = TimeProvider.System;
    private string? draftPersistenceError;
    private int? reviewAnchorBlockIndex;
    private string? reviewSelectionAutomationName;
    private long autosaveGeneration;
    private bool hasDeferredCommitNotification;
    private bool isDisposed;

    public MarkdownLivePreviewEditorViewModel(
        IMarkdownDocumentParser? parser = null,
        string automationIdPrefix = "MarkdownLivePreview",
        TimeSpan? autosaveDelay = null,
        Func<TimeSpan, CancellationToken, Task>? autosaveDelayAsync = null)
    {
        this.parser = parser ?? new MarkdownDocumentParser();
        this.autosaveDelay = autosaveDelay ?? DefaultAutosaveDelay;
        if (this.autosaveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(autosaveDelay));
        }

        this.autosaveDelayAsync = autosaveDelayAsync ?? Task.Delay;
        AutomationIdPrefix = NormalizeAutomationIdPrefix(automationIdPrefix);
        RestoreRecoveryDraftCommand = ReactiveCommand.Create(RestoreRecoveryDraft);
        DiscardRecoveryDraftCommand = ReactiveCommand.CreateFromTask(DiscardRecoveryDraftAsync);
    }

    public ObservableCollection<MarkdownLiveBlockViewModel> Blocks { get; } = new();

    public event EventHandler? DirtyStateChanged;

    public string AutomationIdPrefix { get; }

    public string RootAutomationId => $"{AutomationIdPrefix}Root";

    public string BlocksAutomationId => $"{AutomationIdPrefix}Blocks";

    public string RecoveryRootAutomationId => $"{AutomationIdPrefix}DraftRecovery";

    public string RecoveryRestoreAutomationId => $"{AutomationIdPrefix}DraftRestore";

    public string RecoveryDiscardAutomationId => $"{AutomationIdPrefix}DraftDiscard";

    public string RecoveryContentAutomationId => $"{AutomationIdPrefix}DraftContent";

    public string DraftPersistenceErrorAutomationId => $"{AutomationIdPrefix}DraftPersistenceError";

    public MarkdownLiveDocumentSnapshot? Snapshot
    {
        get => snapshot;
        private set => this.RaiseAndSetIfChanged(ref snapshot, value);
    }

    public MarkdownLiveBlockViewModel? ActiveBlock
    {
        get => activeBlock;
        private set => this.RaiseAndSetIfChanged(ref activeBlock, value);
    }

    public bool HasDocument => Snapshot is not null;

    public bool CanEdit => CommitBlockAsync is not null && !isDisposed;

    public FeedDraft? RecoveryDraft
    {
        get => recoveryDraft;
        private set
        {
            this.RaiseAndSetIfChanged(ref recoveryDraft, value);
            this.RaisePropertyChanged(nameof(HasRecoveryDraft));
            this.RaisePropertyChanged(nameof(CanRestoreRecoveryDraft));
            this.RaisePropertyChanged(nameof(IsRecoveryDraftStale));
        }
    }

    public bool HasRecoveryDraft => RecoveryDraft is not null;

    public bool CanRestoreRecoveryDraft => RecoveryDraft is not null
        && Snapshot is not null
        && string.Equals(RecoveryDraft.BaseRevision, Snapshot.ExpectedRevisionHash, StringComparison.Ordinal)
        && string.Equals(
            NormalizeRelativePath(RecoveryDraft.RelativePath),
            NormalizeRelativePath(Snapshot.RelativePath),
            PathComparison)
        && Blocks.Any(block => block.Index == RecoveryDraft.BlockIndex && block.IsEditable);

    public bool IsRecoveryDraftStale => HasRecoveryDraft && !CanRestoreRecoveryDraft;

    public string? DraftPersistenceError
    {
        get => draftPersistenceError;
        private set
        {
            this.RaiseAndSetIfChanged(ref draftPersistenceError, value);
            this.RaisePropertyChanged(nameof(HasDraftPersistenceError));
        }
    }

    public bool HasDraftPersistenceError => !string.IsNullOrWhiteSpace(DraftPersistenceError);

    public ReactiveCommand<Unit, bool> RestoreRecoveryDraftCommand { get; }

    public ReactiveCommand<Unit, Unit> DiscardRecoveryDraftCommand { get; }

    public Func<MarkdownBlockPatch, CancellationToken, Task<MarkdownBlockCommitResult>>? CommitBlockAsync
    {
        get => commitBlockAsync;
        set
        {
            this.RaiseAndSetIfChanged(ref commitBlockAsync, value);
            this.RaisePropertyChanged(nameof(CanEdit));
            foreach (var block in Blocks)
            {
                block.RaiseCanStartEditChanged();
            }
        }
    }

    public Action<MarkdownLiveDocumentSnapshot>? CommitAccepted { get; set; }

    public void ConfigureDraftPersistence(
        string vaultId,
        IFeedDraftStore store,
        TimeProvider? timeProvider = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId);
        ArgumentNullException.ThrowIfNull(store);
        lock (draftPersistenceSync)
        {
            if (draftStore is not null
                && (!ReferenceEquals(draftStore, store)
                    || !string.Equals(draftVaultId, vaultId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Draft persistence is already configured for another vault session.");
            }

            draftVaultId = vaultId;
            draftStore = store;
            draftTimeProvider = timeProvider ?? TimeProvider.System;
        }
    }

    public void OfferRecoveryDraft(FeedDraft draft)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.SchemaVersion != 1 || draft.BlockIndex < 0)
        {
            throw new ArgumentException("Unsupported recovery draft.", nameof(draft));
        }

        if (draftStore is null
            || !string.Equals(draft.VaultId, draftVaultId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Configure matching draft persistence before offering recovery.");
        }

        var currentSnapshot = Snapshot
            ?? throw new InvalidOperationException("Load the Markdown document before offering recovery.");
        if (!string.Equals(
                NormalizeRelativePath(draft.RelativePath),
                NormalizeRelativePath(currentSnapshot.RelativePath),
                PathComparison))
        {
            throw new ArgumentException("The recovery draft belongs to another Markdown document.", nameof(draft));
        }

        RecoveryDraft = draft;
        DraftPersistenceError = null;
    }

    public bool RestoreRecoveryDraft()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var draft = RecoveryDraft;
        if (draft is null)
        {
            return false;
        }

        if (!CanRestoreRecoveryDraft)
        {
            DraftPersistenceError = L10n.Get("FeedDraftRecoveryStale");
            return false;
        }

        var block = Blocks.Single(candidate => candidate.Index == draft.BlockIndex);
        if (!BeginEdit(block))
        {
            return false;
        }

        RecoveryDraft = null;
        block.EditorText = draft.RawMarkdown;
        return true;
    }

    public async Task DiscardRecoveryDraftAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var draft = RecoveryDraft;
        if (draft is null)
        {
            return;
        }

        await QueueDeleteDraftAsync(
                draft.RelativePath,
                draft.BlockIndex,
                () => RecoveryDraft = null)
            .ConfigureAwait(false);
    }

    public Task FlushDraftPersistenceAsync()
    {
        lock (draftPersistenceSync)
        {
            return draftPersistenceTail;
        }
    }

    public Task FlushAutosaveAsync()
    {
        lock (autosaveSync)
        {
            return autosaveTask;
        }
    }

    public void SetTaskReferences(IEnumerable<FeedTaskReferenceViewModel> references)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(references);

        taskReferences.Clear();
        foreach (var reference in references)
        {
            if (!string.IsNullOrWhiteSpace(reference.TaskId))
            {
                taskReferences[reference.TaskId] = reference;
            }
        }

        foreach (var block in Blocks)
        {
            block.RaiseTaskReferencesChanged();
        }
    }

    internal FeedTaskReferenceViewModel? ResolveTaskReference(string target)
    {
        if (!target.StartsWith(TaskUriPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var taskId = target[TaskUriPrefix.Length..];
        return taskReferences.TryGetValue(taskId, out var reference) ? reference : null;
    }

    public void Load(MarkdownLiveDocumentSnapshot documentSnapshot)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(documentSnapshot);
        ArgumentNullException.ThrowIfNull(documentSnapshot.Raw);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentSnapshot.ExpectedRevisionHash);
        ArgumentNullException.ThrowIfNull(documentSnapshot.RelativePath);

        ReplaceSession();
        hasDeferredCommitNotification = false;
        ActiveBlock?.CancelEdit();
        ActiveBlock = null;
        Snapshot = documentSnapshot;
        Blocks.Clear();

        var document = parser.Parse(documentSnapshot.Raw);
        foreach (var block in document.Blocks)
        {
            Blocks.Add(new MarkdownLiveBlockViewModel(this, block, document.NewLine));
        }

        ApplyReviewSelection();

        this.RaisePropertyChanged(nameof(HasDocument));
        this.RaisePropertyChanged(nameof(CanEdit));
        this.RaisePropertyChanged(nameof(CanRestoreRecoveryDraft));
        this.RaisePropertyChanged(nameof(IsRecoveryDraftStale));
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetReviewSelection(
        IEnumerable<int> selectedBlockIndices,
        int anchorBlockIndex,
        string selectedMarkdown)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(selectedBlockIndices);

        reviewSelectionIndices.Clear();
        foreach (var blockIndex in selectedBlockIndices)
        {
            reviewSelectionIndices.Add(blockIndex);
        }

        reviewAnchorBlockIndex = anchorBlockIndex;
        reviewSelectionAutomationName = selectedMarkdown;
        ApplyReviewSelection();
    }

    public void ClearReviewSelection()
    {
        reviewSelectionIndices.Clear();
        reviewAnchorBlockIndex = null;
        reviewSelectionAutomationName = null;
        ApplyReviewSelection();
    }

    private void ApplyReviewSelection()
    {
        foreach (var block in Blocks)
        {
            var isHighlighted = reviewSelectionIndices.Contains(block.Index);
            var isAnchor = isHighlighted && reviewAnchorBlockIndex == block.Index;
            block.SetReviewHighlight(
                isHighlighted,
                isAnchor,
                isAnchor ? reviewSelectionAutomationName : null);
        }
    }

    public bool BeginEdit(MarkdownLiveBlockViewModel block)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(block);
        if (!CanEdit || !ReferenceEquals(block.Owner, this) || !block.IsEditable)
        {
            return false;
        }

        if (ReferenceEquals(ActiveBlock, block))
        {
            return true;
        }

        if (ActiveBlock is not null && commitGate.CurrentCount == 0)
        {
            return false;
        }

        CancelPendingAutosave();
        if (hasDeferredCommitNotification && Snapshot is not null)
        {
            hasDeferredCommitNotification = false;
            CommitAccepted?.Invoke(Snapshot);
        }

        ActiveBlock?.CancelEdit();
        ActiveBlock = block;
        block.BeginEdit();
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> CommitActiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        CancelPendingAutosave();
        var block = ActiveBlock;
        if (block is null)
        {
            return true;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            sessionCancellation.Token);
        await commitGate.WaitAsync(linkedCancellation.Token);
        try
        {
            if (!ReferenceEquals(block, ActiveBlock))
            {
                return false;
            }

            var currentSnapshot = Snapshot;
            if (currentSnapshot is null)
            {
                return true;
            }

            if (!block.IsDirty)
            {
                var closedBlockIndex = block.Index;
                var notifyCommitAccepted = hasDeferredCommitNotification;
                hasDeferredCommitNotification = false;
                block.AcceptCommit();
                ActiveBlock = null;
                Load(currentSnapshot);
                await QueueDeleteDraftAsync(currentSnapshot.RelativePath, closedBlockIndex).ConfigureAwait(false);
                if (notifyCommitAccepted)
                {
                    CommitAccepted?.Invoke(currentSnapshot);
                }

                return true;
            }

            var callback = CommitBlockAsync;
            if (callback is null)
            {
                block.ErrorMessage = L10n.Get("MarkdownEditingUnavailable");
                return false;
            }

            block.IsCommitInProgress = true;
            block.ErrorMessage = null;
            var patch = block.CreatePatch(currentSnapshot);
            var result = await callback(patch, linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();

            if (!result.IsAccepted)
            {
                block.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L10n.Get("MarkdownBlockSaveFailed")
                    : result.ErrorMessage;
                return false;
            }

            var acceptedSnapshot = result.Snapshot;
            if (acceptedSnapshot is null
                || !IsValidAcknowledgement(currentSnapshot, patch, acceptedSnapshot))
            {
                block.ErrorMessage = L10n.Get("MarkdownPatchAcknowledgementInvalid");
                return false;
            }

            var committedBlockIndex = block.Index;
            hasDeferredCommitNotification = false;
            Load(acceptedSnapshot);
            await QueueDeleteDraftAsync(currentSnapshot.RelativePath, committedBlockIndex).ConfigureAwait(false);
            CommitAccepted?.Invoke(acceptedSnapshot);
            return true;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            block.ErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            block.IsCommitInProgress = false;
            commitGate.Release();
        }
    }

    public void CancelActiveEdit()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        CancelPendingAutosave();
        var currentSnapshot = Snapshot;
        var cancelledBlock = ActiveBlock;
        var notifyCommitAccepted = hasDeferredCommitNotification;
        hasDeferredCommitNotification = false;
        cancelledBlock?.CancelEdit();
        ActiveBlock = null;
        if (currentSnapshot is not null && cancelledBlock is not null)
        {
            _ = QueueDeleteDraftAsync(currentSnapshot.RelativePath, cancelledBlock.Index);
            if (notifyCommitAccepted)
            {
                Load(currentSnapshot);
                CommitAccepted?.Invoke(currentSnapshot);
            }
        }

        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifyBlockStateChanged(MarkdownLiveBlockViewModel block)
    {
        if (!isDisposed && ReferenceEquals(ActiveBlock, block))
        {
            if (block.IsDirty)
            {
                QueueSaveDraft(block);
                ScheduleAutosave(block);
            }
            else
            {
                CancelPendingAutosave();
            }

            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleAutosave(MarkdownLiveBlockViewModel block)
    {
        if (isDisposed || CommitBlockAsync is null || Snapshot is null || !block.IsDirty)
        {
            CancelPendingAutosave();
            return;
        }

        CancellationTokenSource? previous;
        CancellationTokenSource next;
        CancellationToken sessionToken;
        long generation;
        lock (autosaveSync)
        {
            previous = autosaveDebounceCancellation;
            sessionToken = sessionCancellation.Token;
            next = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            autosaveDebounceCancellation = next;
            generation = ++autosaveGeneration;
        }

        previous?.Cancel();
        previous?.Dispose();
        var scheduled = RunAutosaveAfterDelayAsync(block, generation, next, sessionToken);
        lock (autosaveSync)
        {
            if (ReferenceEquals(autosaveDebounceCancellation, next))
            {
                autosaveTask = scheduled;
            }
        }
    }

    private async Task RunAutosaveAfterDelayAsync(
        MarkdownLiveBlockViewModel block,
        long generation,
        CancellationTokenSource debounceCancellation,
        CancellationToken sessionToken)
    {
        try
        {
            await autosaveDelayAsync(autosaveDelay, debounceCancellation.Token);
            debounceCancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAutosave(block, generation))
            {
                return;
            }

            await CommitAutosaveAsync(block, generation, sessionToken);
        }
        catch (OperationCanceledException) when (
            debounceCancellation.IsCancellationRequested
            || sessionToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (autosaveSync)
            {
                if (ReferenceEquals(autosaveDebounceCancellation, debounceCancellation))
                {
                    autosaveDebounceCancellation = null;
                }
            }

            debounceCancellation.Dispose();
        }
    }

    private async Task<bool> CommitAutosaveAsync(
        MarkdownLiveBlockViewModel expectedBlock,
        long generation,
        CancellationToken sessionToken)
    {
        await commitGate.WaitAsync(sessionToken);
        try
        {
            if (!IsCurrentAutosave(expectedBlock, generation))
            {
                return false;
            }

            var currentSnapshot = Snapshot;
            var callback = CommitBlockAsync;
            if (currentSnapshot is null || callback is null || !expectedBlock.IsDirty)
            {
                return false;
            }

            expectedBlock.ErrorMessage = null;
            var patch = expectedBlock.CreatePatch(currentSnapshot);
            MarkdownBlockCommitResult result;
            try
            {
                result = await callback(patch, sessionToken);
                sessionToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                if (sessionToken.IsCancellationRequested)
                {
                    return false;
                }

                expectedBlock.ErrorMessage = exception.Message;
                CancelPendingAutosave();
                QueueSaveDraft(expectedBlock);
                return false;
            }

            if (!result.IsAccepted)
            {
                expectedBlock.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L10n.Get("MarkdownBlockSaveFailed")
                    : result.ErrorMessage;
                CancelPendingAutosave();
                QueueSaveDraft(expectedBlock);
                return false;
            }

            var acceptedSnapshot = result.Snapshot;
            if (acceptedSnapshot is null
                || !IsValidAcknowledgement(currentSnapshot, patch, acceptedSnapshot))
            {
                expectedBlock.ErrorMessage = L10n.Get("MarkdownPatchAcknowledgementInvalid");
                CancelPendingAutosave();
                QueueSaveDraft(expectedBlock);
                return false;
            }

            if (!ReferenceEquals(ActiveBlock, expectedBlock) || !expectedBlock.IsEditing)
            {
                var completedBlockIndex = expectedBlock.Index;
                hasDeferredCommitNotification = false;
                Load(acceptedSnapshot);
                await QueueDeleteDraftAsync(currentSnapshot.RelativePath, completedBlockIndex);
                CommitAccepted?.Invoke(acceptedSnapshot);
                return true;
            }

            Snapshot = acceptedSnapshot;
            expectedBlock.AcceptAutosave(patch.ReplacementRaw);
            hasDeferredCommitNotification = true;
            if (expectedBlock.IsDirty)
            {
                QueueSaveDraft(expectedBlock);
            }
            else
            {
                await QueueDeleteDraftAsync(currentSnapshot.RelativePath, expectedBlock.Index);
            }

            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            commitGate.Release();
        }
    }

    private bool IsCurrentAutosave(MarkdownLiveBlockViewModel block, long generation)
    {
        lock (autosaveSync)
        {
            return !isDisposed
                && generation == autosaveGeneration
                && ReferenceEquals(ActiveBlock, block)
                && block.IsEditing
                && block.IsDirty;
        }
    }

    private void CancelPendingAutosave()
    {
        CancellationTokenSource? pending;
        lock (autosaveSync)
        {
            autosaveGeneration++;
            pending = autosaveDebounceCancellation;
            autosaveDebounceCancellation = null;
        }

        pending?.Cancel();
        pending?.Dispose();
    }

    private static bool IsValidAcknowledgement(
        MarkdownLiveDocumentSnapshot currentSnapshot,
        MarkdownBlockPatch patch,
        MarkdownLiveDocumentSnapshot acceptedSnapshot)
    {
        return string.Equals(acceptedSnapshot.RelativePath, currentSnapshot.RelativePath, StringComparison.Ordinal)
            && acceptedSnapshot.HasUtf8Bom == currentSnapshot.HasUtf8Bom
            && string.Equals(acceptedSnapshot.Raw, patch.PatchedDocumentRaw, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(acceptedSnapshot.ExpectedRevisionHash);
    }

    private void QueueSaveDraft(MarkdownLiveBlockViewModel block)
    {
        var currentSnapshot = Snapshot;
        IFeedDraftStore? store;
        string? vaultId;
        TimeProvider timeProvider;
        lock (draftPersistenceSync)
        {
            store = draftStore;
            vaultId = draftVaultId;
            timeProvider = draftTimeProvider;
        }

        if (currentSnapshot is null || store is null || string.IsNullOrWhiteSpace(vaultId))
        {
            return;
        }

        var patch = block.CreatePatch(currentSnapshot);
        var draft = new FeedDraft(
            1,
            vaultId,
            currentSnapshot.RelativePath,
            block.Index,
            currentSnapshot.ExpectedRevisionHash,
            block.EditorText,
            timeProvider.GetUtcNow(),
            patch.PatchedDocumentRaw,
            currentSnapshot.HasUtf8Bom);
        lock (draftPersistenceSync)
        {
            persistedDraftBlocks.Add(block.Index);
        }

        _ = QueueDraftOperationAsync(
            () => store.SaveAsync(draft, CancellationToken.None),
            onSuccess: null);
    }

    private Task QueueDeleteDraftAsync(
        string relativePath,
        int blockIndex,
        Action? onSuccess = null)
    {
        IFeedDraftStore? store;
        string? vaultId;
        lock (draftPersistenceSync)
        {
            store = draftStore;
            vaultId = draftVaultId;
        }

        if (store is null || string.IsNullOrWhiteSpace(vaultId))
        {
            onSuccess?.Invoke();
            return Task.CompletedTask;
        }

        return QueueDraftOperationAsync(
            () => store.DeleteAsync(vaultId, relativePath, blockIndex, CancellationToken.None),
            () =>
            {
                lock (draftPersistenceSync)
                {
                    persistedDraftBlocks.Remove(blockIndex);
                }

                onSuccess?.Invoke();
            });
    }

    private Task QueueDraftOperationAsync(Func<Task> operation, Action? onSuccess)
    {
        lock (draftPersistenceSync)
        {
            draftPersistenceTail = RunDraftOperationAsync(draftPersistenceTail, operation, onSuccess);
            return draftPersistenceTail;
        }
    }

    private async Task RunDraftOperationAsync(
        Task previous,
        Func<Task> operation,
        Action? onSuccess)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            DraftPersistenceError = null;
            onSuccess?.Invoke();
        }
        catch (Exception exception)
        {
            DraftPersistenceError = exception.Message;
        }
    }

    private void ReplaceSession()
    {
        CancelPendingAutosave();
        var previous = sessionCancellation;
        sessionCancellation = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    private static string NormalizeAutomationIdPrefix(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9_-]+", "-");
        return normalized.EndsWith("-", StringComparison.Ordinal) ? normalized : normalized + "-";
    }

    private static string NormalizeRelativePath(string value) => value.Replace('\\', '/');

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        CancelPendingAutosave();
        sessionCancellation.Cancel();
        sessionCancellation.Dispose();
        FlushDraftPersistenceAsync().GetAwaiter().GetResult();
        ActiveBlock?.CancelEdit();
        ActiveBlock = null;
        RecoveryDraft = null;
        Blocks.Clear();
        Snapshot = null;
        RestoreRecoveryDraftCommand.Dispose();
        DiscardRecoveryDraftCommand.Dispose();
        this.RaisePropertyChanged(nameof(HasDocument));
        this.RaisePropertyChanged(nameof(CanEdit));
        this.RaisePropertyChanged(nameof(CanRestoreRecoveryDraft));
        this.RaisePropertyChanged(nameof(IsRecoveryDraftStale));
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class MarkdownLiveBlockViewModel : ReactiveObject
{
    private static readonly Regex HtmlOrPluginRegex = new(
        @"(?im)^\s*(?:<[/!?A-Za-z]|%%|:::\s*|[A-Za-z0-9_-]+::\s)|<\s*script\b|\bon[a-z]+\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AreaMarkerRegex = new(
        @"\s*<!--\s*unlimotion-area:[A-Za-z0-9_-]+\s*-->\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TaskPrefixRegex = new(
        @"^[ \t]*[-+*]\s+\[[ xX]\](?:\s+|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ListPrefixRegex = new(
        @"^[ \t]*(?:[-+*]|\d+[.)])(?:\s+|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InlineRegex = new(
        @"(?<wiki>\[\[(?<wikiTarget>[^\]\r\n|]+?)(?:\|(?<wikiLabel>[^\]\r\n]+))?\]\])|(?<link>\[(?<linkLabel>[^\]\r\n]+)\]\((?<linkTarget>[^)\r\n]+)\))|(?<strong>\*\*(?<strongText>.+?)\*\*|__(?<strongTextAlt>.+?)__)|(?<emphasis>(?<!\*)\*(?<emphasisText>[^*\r\n]+)\*(?!\*)|(?<!_)_(?<emphasisTextAlt>[^_\r\n]+)_(?!_))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string newLine;
    private string editorText;
    private bool isEditing;
    private bool isCommitInProgress;
    private bool isReviewHighlighted;
    private bool isReviewAnchor;
    private string? errorMessage;
    private string? reviewSelectionAutomationName;

    internal MarkdownLiveBlockViewModel(
        MarkdownLivePreviewEditorViewModel owner,
        MarkdownBlock block,
        string newLine)
    {
        Owner = owner;
        Block = block;
        this.newLine = newLine;
        editorText = StripStructuralLineEnding(block.Raw);
        RenderKind = Classify(block);
        PreviewText = CreatePreviewText(block, RenderKind);
        InlineTokens = TokenizeInline(PreviewText);
    }

    public MarkdownLivePreviewEditorViewModel Owner { get; }

    public MarkdownBlock Block { get; private set; }

    public int Index => Block.Index;

    public string BlockAutomationId => $"{Owner.AutomationIdPrefix}Block-{Index}";

    public string PreviewAutomationId => $"{Owner.AutomationIdPrefix}BlockPreview-{Index}";

    public string EditorAutomationId => $"{Owner.AutomationIdPrefix}BlockEditor-{Index}";

    public string RawFallbackAutomationId => $"{Owner.AutomationIdPrefix}RawFallback-{Index}";

    public string ErrorAutomationId => $"{Owner.AutomationIdPrefix}BlockError-{Index}";

    public string SavingAutomationId => $"{Owner.AutomationIdPrefix}BlockSaving-{Index}";

    public string LinkAutomationId(int linkIndex) => $"{Owner.AutomationIdPrefix}Link-{Index}-{linkIndex}";

    public string BlockedLinkAutomationId(int linkIndex) => $"{Owner.AutomationIdPrefix}BlockedLink-{Index}-{linkIndex}";

    public string AutomationName => L10n.Format("MarkdownBlockAutomationName", Index + 1, PreviewText);

    public bool IsReviewHighlighted => isReviewHighlighted;

    public bool IsReviewAnchor => isReviewAnchor;

    public string? ReviewSelectionAutomationId => IsReviewAnchor
        ? "FeedReviewInlineAnchorText"
        : null;

    public string? ReviewSelectionAutomationName => reviewSelectionAutomationName;

    public string EditorAutomationName => L10n.Format("MarkdownBlockEditorAutomationName", Index + 1);

    public MarkdownBlockKind Kind => Block.Kind;

    public MarkdownLiveBlockRenderKind RenderKind { get; }

    public string PreviewText { get; }

    public IReadOnlyList<MarkdownInlineToken> InlineTokens { get; }

    public long TaskReferencesVersion { get; private set; }

    public bool IsEditable => RenderKind != MarkdownLiveBlockRenderKind.Blank;

    public bool CanStartEdit => IsEditable && Owner.CanEdit;

    public bool IsPreviewVisible => !IsEditing;

    public bool IsRawFallback => RenderKind == MarkdownLiveBlockRenderKind.RawFallback;

    public bool IsTaskCompleted => Block.IsTaskCompleted == true;

    public int HeadingLevel => Block.HeadingLevel;

    public int ListDepth => Block.ListDepth;

    public string ListMarker => CreateListMarker(Block);

    internal void SetReviewHighlight(
        bool highlighted,
        bool anchor,
        string? selectionAutomationName)
    {
        if (isReviewHighlighted == highlighted
            && isReviewAnchor == anchor
            && string.Equals(
                reviewSelectionAutomationName,
                selectionAutomationName,
                StringComparison.Ordinal))
        {
            return;
        }

        isReviewHighlighted = highlighted;
        isReviewAnchor = anchor;
        reviewSelectionAutomationName = selectionAutomationName;
        this.RaisePropertyChanged(nameof(IsReviewHighlighted));
        this.RaisePropertyChanged(nameof(IsReviewAnchor));
        this.RaisePropertyChanged(nameof(ReviewSelectionAutomationId));
        this.RaisePropertyChanged(nameof(ReviewSelectionAutomationName));
    }

    public string EditorText
    {
        get => editorText;
        set
        {
            this.RaiseAndSetIfChanged(ref editorText, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(IsDirty));
            Owner.NotifyBlockStateChanged(this);
        }
    }

    public FeedTaskReferenceViewModel? ResolveTaskReference(string target) =>
        Owner.ResolveTaskReference(target);

    internal void RaiseTaskReferencesChanged()
    {
        TaskReferencesVersion++;
        this.RaisePropertyChanged(nameof(TaskReferencesVersion));
    }

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            this.RaiseAndSetIfChanged(ref isEditing, value);
            this.RaisePropertyChanged(nameof(IsPreviewVisible));
        }
    }

    public bool IsCommitInProgress
    {
        get => isCommitInProgress;
        internal set => this.RaiseAndSetIfChanged(ref isCommitInProgress, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        internal set
        {
            this.RaiseAndSetIfChanged(ref errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsDirty => !string.Equals(EditorText, StripStructuralLineEnding(Block.Raw), StringComparison.Ordinal);

    internal void BeginEdit()
    {
        EditorText = StripStructuralLineEnding(Block.Raw);
        ErrorMessage = null;
        IsEditing = true;
    }

    internal void RaiseCanStartEditChanged() => this.RaisePropertyChanged(nameof(CanStartEdit));

    internal void CancelEdit()
    {
        EditorText = StripStructuralLineEnding(Block.Raw);
        ErrorMessage = null;
        IsEditing = false;
        Owner.NotifyBlockStateChanged(this);
    }

    internal void AcceptCommit()
    {
        ErrorMessage = null;
        IsEditing = false;
        Owner.NotifyBlockStateChanged(this);
    }

    internal void AcceptAutosave(string committedRaw)
    {
        Block = Block with
        {
            Raw = committedRaw,
            Length = committedRaw.Length
        };
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(IsDirty));
    }

    internal MarkdownBlockPatch CreatePatch(MarkdownLiveDocumentSnapshot snapshot)
    {
        var replacement = NormalizeNewLines(EditorText, newLine) + GetStructuralLineEnding(Block.Raw);
        var selection = new MarkdownBlockTextRange(Block.Start, Block.Length);
        var patch = new MarkdownBlockPatch(
            snapshot.RelativePath,
            snapshot.ExpectedRevisionHash,
            snapshot.HasUtf8Bom,
            Block.Index,
            selection,
            Block.Raw,
            replacement,
            string.Empty);
        return patch with { PatchedDocumentRaw = patch.ApplyTo(snapshot.Raw) };
    }

    private static MarkdownLiveBlockRenderKind Classify(MarkdownBlock block)
    {
        if (block.Kind == MarkdownBlockKind.Blank)
        {
            return MarkdownLiveBlockRenderKind.Blank;
        }

        if (block.Kind is MarkdownBlockKind.Raw or MarkdownBlockKind.FrontMatter
            || HtmlOrPluginRegex.IsMatch(block.Raw)
            || block.Kind == MarkdownBlockKind.BlockQuote && block.Raw.TrimStart().StartsWith("> [!", StringComparison.Ordinal))
        {
            return MarkdownLiveBlockRenderKind.RawFallback;
        }

        return block.Kind switch
        {
            MarkdownBlockKind.Heading or MarkdownBlockKind.AreaHeading => MarkdownLiveBlockRenderKind.Heading,
            MarkdownBlockKind.Paragraph => MarkdownLiveBlockRenderKind.Paragraph,
            MarkdownBlockKind.ListItem => MarkdownLiveBlockRenderKind.ListItem,
            MarkdownBlockKind.TaskListItem => MarkdownLiveBlockRenderKind.TaskListItem,
            MarkdownBlockKind.BlockQuote => MarkdownLiveBlockRenderKind.BlockQuote,
            MarkdownBlockKind.FencedCode => MarkdownLiveBlockRenderKind.FencedCode,
            MarkdownBlockKind.HorizontalRule => MarkdownLiveBlockRenderKind.HorizontalRule,
            _ => MarkdownLiveBlockRenderKind.RawFallback
        };
    }

    private static string CreatePreviewText(MarkdownBlock block, MarkdownLiveBlockRenderKind renderKind)
    {
        var text = StripStructuralLineEnding(block.Raw);
        return renderKind switch
        {
            MarkdownLiveBlockRenderKind.Heading => AreaMarkerRegex.Replace(
                text.TrimStart().TrimStart('#').TrimStart(),
                string.Empty),
            MarkdownLiveBlockRenderKind.TaskListItem => TaskPrefixRegex.Replace(text, string.Empty),
            MarkdownLiveBlockRenderKind.ListItem => ListPrefixRegex.Replace(text, string.Empty),
            MarkdownLiveBlockRenderKind.BlockQuote => string.Join(
                "\n",
                text.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(static line => line.TrimStart().TrimStart('>').TrimStart())),
            MarkdownLiveBlockRenderKind.FencedCode => ExtractFencedCode(text),
            _ => text
        };
    }

    private static string CreateListMarker(MarkdownBlock block)
    {
        if (block.Kind == MarkdownBlockKind.TaskListItem)
        {
            return block.IsTaskCompleted == true ? "☑" : "☐";
        }

        var firstLine = StripStructuralLineEnding(block.Raw)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')[0];
        var match = ListPrefixRegex.Match(firstLine);
        return !match.Success
            ? "•"
            : match.Value.Trim() is "-" or "+" or "*" ? "•" : match.Value.Trim();
    }

    private static IReadOnlyList<MarkdownInlineToken> TokenizeInline(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var tokens = new List<MarkdownInlineToken>();
        var position = 0;
        foreach (Match match in InlineRegex.Matches(value))
        {
            if (match.Index > position)
            {
                tokens.Add(new MarkdownInlineToken(
                    MarkdownInlineTokenKind.Text,
                    value[position..match.Index]));
            }

            if (match.Groups["wiki"].Success)
            {
                var target = match.Groups["wikiTarget"].Value;
                var label = match.Groups["wikiLabel"].Success ? match.Groups["wikiLabel"].Value : target;
                tokens.Add(new MarkdownInlineToken(MarkdownInlineTokenKind.WikiLink, label, target, IsSafeWikiTarget(target)));
            }
            else if (match.Groups["link"].Success)
            {
                var target = match.Groups["linkTarget"].Value.Trim();
                tokens.Add(new MarkdownInlineToken(
                    MarkdownInlineTokenKind.Link,
                    match.Groups["linkLabel"].Value,
                    target,
                    MarkdownUriPolicy.IsSafe(target)));
            }
            else if (match.Groups["strong"].Success)
            {
                var text = match.Groups["strongText"].Success
                    ? match.Groups["strongText"].Value
                    : match.Groups["strongTextAlt"].Value;
                tokens.Add(new MarkdownInlineToken(MarkdownInlineTokenKind.Strong, text));
            }
            else
            {
                var text = match.Groups["emphasisText"].Success
                    ? match.Groups["emphasisText"].Value
                    : match.Groups["emphasisTextAlt"].Value;
                tokens.Add(new MarkdownInlineToken(MarkdownInlineTokenKind.Emphasis, text));
            }

            position = match.Index + match.Length;
        }

        if (position < value.Length)
        {
            tokens.Add(new MarkdownInlineToken(MarkdownInlineTokenKind.Text, value[position..]));
        }

        return tokens;
    }

    private static bool IsSafeWikiTarget(string target)
    {
        return !string.IsNullOrWhiteSpace(target)
            && target.All(static character => !char.IsControl(character))
            && !target.Contains("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(target)
            && target.IndexOfAny(['[', ']', '|']) < 0;
    }

    private static string ExtractFencedCode(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 0)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && Regex.IsMatch(lines[^1], @"^\s*(?:`{3,}|~{3,})\s*$"))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines);
    }

    private static string StripStructuralLineEnding(string raw)
    {
        if (raw.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return raw[..^2];
        }

        return raw.EndsWith('\r') || raw.EndsWith('\n') ? raw[..^1] : raw;
    }

    private static string GetStructuralLineEnding(string raw)
    {
        if (raw.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (raw.EndsWith('\r'))
        {
            return "\r";
        }

        return raw.EndsWith('\n') ? "\n" : string.Empty;
    }

    private static string NormalizeNewLines(string value, string targetNewLine)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", targetNewLine, StringComparison.Ordinal);
    }
}
