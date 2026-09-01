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
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Recovery;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed record MarkdownLiveDocumentSnapshot(
    string Raw,
    string ExpectedRevisionHash,
    bool HasUtf8Bom,
    string RelativePath);

public sealed record MarkdownBlockTextRange(int Start, int Length);

public sealed record FeedAreaFilterSelection(string Identity, string? AreaName);

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

public sealed record MarkdownBlocksMoveRequest(
    MarkdownLiveDocumentSnapshot Snapshot,
    IReadOnlyList<int> SelectedBlockIndices,
    int? InsertBeforeBlockIndex,
    AreaReference? DestinationArea = null);

public sealed record MarkdownBlocksMoveResult(
    bool IsAccepted,
    MarkdownLiveDocumentSnapshot? Snapshot = null,
    IReadOnlyList<int>? OutputBlockIndices = null,
    string? ErrorMessage = null)
{
    public static MarkdownBlocksMoveResult Accepted(
        MarkdownLiveDocumentSnapshot snapshot,
        IReadOnlyList<int> outputBlockIndices) => new(true, snapshot, outputBlockIndices);

    public static MarkdownBlocksMoveResult Rejected(string errorMessage) =>
        new(false, ErrorMessage: errorMessage);
}

public sealed record MarkdownBlockMergeResult(
    bool IsApplicable,
    bool IsMerged,
    MarkdownLiveBlockViewModel? TargetBlock = null,
    int CaretIndex = 0);

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

public enum MarkdownMoveDropTarget
{
    None,
    Before,
    After
}

public enum MarkdownBlockListStyle
{
    Bulleted,
    Numbered,
    Checklist
}

public enum MarkdownSelectionSemanticAction
{
    Task,
    Note,
    Area,
    MoveToday,
    ConvertHeadingToArea
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
    private readonly FeedMarkdownBlockMergeService mergeService;
    private readonly TimeSpan autosaveDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> autosaveDelayAsync;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly SemaphoreSlim commitGate = new(1, 1);
    private readonly Dictionary<string, FeedTaskReferenceViewModel> taskReferences =
        new(StringComparer.Ordinal);
    private readonly HashSet<int> reviewSelectionIndices = [];
    private readonly HashSet<int> moveSelectionIndices = [];
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
    private Func<MarkdownBlocksMoveRequest, CancellationToken, Task<MarkdownBlocksMoveResult>>? moveBlocksAsync;
    private Func<MarkdownLivePreviewEditorViewModel, IReadOnlyList<int>, MarkdownSelectionSemanticAction, CancellationToken, Task>? selectionActionAsync;
    private IFeedDraftStore? draftStore;
    private string? draftVaultId;
    private TimeProvider draftTimeProvider = TimeProvider.System;
    private string? draftPersistenceError;
    private int? reviewAnchorBlockIndex;
    private int? moveSelectionAnchorBlockIndex;
    private string? reviewSelectionAutomationName;
    private long autosaveGeneration;
    private bool hasDeferredCommitNotification;
    private bool isMoveInProgress;
    private string? moveErrorMessage;
    private bool isDisposed;

    public MarkdownLivePreviewEditorViewModel(
        IMarkdownDocumentParser? parser = null,
        string automationIdPrefix = "MarkdownLivePreview",
        TimeSpan? autosaveDelay = null,
        Func<TimeSpan, CancellationToken, Task>? autosaveDelayAsync = null)
    {
        this.parser = parser ?? new MarkdownDocumentParser();
        mergeService = new FeedMarkdownBlockMergeService(this.parser);
        this.autosaveDelay = autosaveDelay ?? DefaultAutosaveDelay;
        if (this.autosaveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(autosaveDelay));
        }

        this.autosaveDelayAsync = autosaveDelayAsync ?? Task.Delay;
        synchronizationContext = SynchronizationContext.Current;
        AutomationIdPrefix = NormalizeAutomationIdPrefix(automationIdPrefix);
        RestoreRecoveryDraftCommand = ReactiveCommand.Create(RestoreRecoveryDraft);
        DiscardRecoveryDraftCommand = ReactiveCommand.CreateFromTask(DiscardRecoveryDraftAsync);
    }

    public ObservableCollection<MarkdownLiveBlockViewModel> Blocks { get; } = new();

    public bool ApplyAreaFilter(string? areaIdentity, string? areaName)
    {
        return areaIdentity is null
            ? ApplyAreaFilter([], showAll: true)
            : ApplyAreaFilter([new FeedAreaFilterSelection(areaIdentity, areaName)], showAll: false);
    }

    public bool ApplyAreaFilter(
        IReadOnlyCollection<FeedAreaFilterSelection> selectedAreas,
        bool showAll)
    {
        ArgumentNullException.ThrowIfNull(selectedAreas);
        var hasVisibleContent = false;
        foreach (var block in Blocks)
        {
            var belongsToArea = showAll || selectedAreas.Any(selection => MatchesArea(
                block.Block,
                selection.Identity,
                selection.AreaName));
            var isVisible = showAll
                || block.Block.IsContent && belongsToArea
                || block.Block.Kind == MarkdownBlockKind.AreaHeading && belongsToArea;
            block.SetFeedFilterVisible(isVisible);
            hasVisibleContent |= block.Block.IsContent && isVisible;
        }

        return showAll ? Blocks.Any(static block => block.Block.IsContent) : hasVisibleContent;
    }

    private static bool MatchesArea(MarkdownBlock block, string? areaIdentity, string? areaName)
    {
        if (string.IsNullOrEmpty(areaIdentity))
        {
            return string.IsNullOrWhiteSpace(block.AreaId) && string.IsNullOrWhiteSpace(block.AreaName);
        }

        return string.Equals(block.AreaId, areaIdentity, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(block.AreaId)
            && string.Equals(block.AreaName, areaName, StringComparison.CurrentCultureIgnoreCase);
    }

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

    public Func<MarkdownBlocksMoveRequest, CancellationToken, Task<MarkdownBlocksMoveResult>>? MoveBlocksAsync
    {
        get => moveBlocksAsync;
        set
        {
            this.RaiseAndSetIfChanged(ref moveBlocksAsync, value);
            RaiseMoveStateChanged();
        }
    }

    public int SelectedMoveBlockCount => moveSelectionIndices.Count;

    public bool HasMoveSelection => SelectedMoveBlockCount > 0;

    public bool IsMoveInProgress
    {
        get => isMoveInProgress;
        private set
        {
            this.RaiseAndSetIfChanged(ref isMoveInProgress, value);
            RaiseMoveStateChanged();
        }
    }

    public string? MoveErrorMessage
    {
        get => moveErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref moveErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasMoveError));
        }
    }

    public bool HasMoveError => !string.IsNullOrWhiteSpace(MoveErrorMessage);

    public bool CanMoveSelection => HasMoveSelection
        && MoveBlocksAsync is not null
        && ActiveBlock is null
        && !IsMoveInProgress;

    public bool CanMoveSelectionUp => CanMoveSelection && FindAdjacentMoveTarget(-1) is not null;

    public bool CanMoveSelectionDown => CanMoveSelection && FindAdjacentMoveTarget(1) is not null;

    public bool CanTransformSelection => HasMoveSelection
        && ActiveBlock is null
        && !IsMoveInProgress
        && SelectedMoveBlocks().All(static block => block.Kind is MarkdownBlockKind.Paragraph
            or MarkdownBlockKind.ListItem
            or MarkdownBlockKind.TaskListItem);

    public bool CanOpenSelectionActions => HasMoveSelection
        && ActiveBlock is null
        && !IsMoveInProgress
        && SelectionActionAsync is not null;

    public bool CanConvertSelectionToArea => CanOpenSelectionActions
        && SelectedMoveBlockCount == 1
        && SelectedMoveBlocks().Single().Kind == MarkdownBlockKind.Heading;

    internal bool CanMoveBlockFromToolbar(MarkdownLiveBlockViewModel block, int delta)
    {
        if (block.IsMoveSelected)
        {
            return delta < 0 ? CanMoveSelectionUp : CanMoveSelectionDown;
        }

        if (!block.IsMovable
            || MoveBlocksAsync is null
            || ActiveBlock is not null
            || IsMoveInProgress)
        {
            return false;
        }

        var movable = Blocks.Where(static candidate => candidate.IsMovable);
        return delta < 0
            ? movable.Any(candidate => candidate.Index < block.Index)
            : movable.Any(candidate => candidate.Index > block.Index);
    }

    internal bool CanTransformBlockFromToolbar(MarkdownLiveBlockViewModel block) =>
        block.IsMoveSelected
            ? CanTransformSelection
            : ActiveBlock is null
              && !IsMoveInProgress
              && block.Kind is MarkdownBlockKind.Paragraph
                  or MarkdownBlockKind.ListItem
                  or MarkdownBlockKind.TaskListItem;

    internal bool CanOpenBlockActionsFromToolbar(MarkdownLiveBlockViewModel block) =>
        block.IsMoveSelected
            ? CanOpenSelectionActions
            : block.IsMovable
              && ActiveBlock is null
              && !IsMoveInProgress
              && SelectionActionAsync is not null;

    internal bool CanConvertBlockToAreaFromToolbar(MarkdownLiveBlockViewModel block) =>
        block.IsMoveSelected
            ? CanConvertSelectionToArea
            : CanOpenBlockActionsFromToolbar(block) && block.Kind == MarkdownBlockKind.Heading;

    public Func<MarkdownLivePreviewEditorViewModel, IReadOnlyList<int>, MarkdownSelectionSemanticAction, CancellationToken, Task>? SelectionActionAsync
    {
        get => selectionActionAsync;
        set
        {
            this.RaiseAndSetIfChanged(ref selectionActionAsync, value);
            RaiseMoveStateChanged();
        }
    }

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
            .ConfigureAwait(true);
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
        moveSelectionIndices.Clear();
        moveSelectionAnchorBlockIndex = null;

        var document = parser.Parse(documentSnapshot.Raw);
        foreach (var block in document.Blocks)
        {
            Blocks.Add(new MarkdownLiveBlockViewModel(this, block, document.NewLine));
        }

        ApplyReviewSelection();
        ApplyMoveSelection();

        this.RaisePropertyChanged(nameof(HasDocument));
        this.RaisePropertyChanged(nameof(CanEdit));
        this.RaisePropertyChanged(nameof(CanRestoreRecoveryDraft));
        this.RaisePropertyChanged(nameof(IsRecoveryDraftStale));
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        RaiseMoveStateChanged();
    }

    public bool SelectMoveBlock(MarkdownLiveBlockViewModel block, bool toggle, bool extendRange)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(block);
        if (!ReferenceEquals(block.Owner, this) || !block.IsMovable || IsMoveInProgress)
        {
            return false;
        }

        MoveErrorMessage = null;
        if (extendRange && moveSelectionAnchorBlockIndex is { } anchor)
        {
            if (!toggle)
            {
                moveSelectionIndices.Clear();
            }

            var start = Math.Min(anchor, block.Index);
            var end = Math.Max(anchor, block.Index);
            foreach (var candidate in Blocks.Where(candidate => candidate.IsMovable
                         && candidate.Index >= start
                         && candidate.Index <= end))
            {
                moveSelectionIndices.Add(candidate.Index);
            }
        }
        else if (toggle)
        {
            if (!moveSelectionIndices.Remove(block.Index))
            {
                moveSelectionIndices.Add(block.Index);
            }

            moveSelectionAnchorBlockIndex = block.Index;
        }
        else
        {
            moveSelectionIndices.Clear();
            moveSelectionIndices.Add(block.Index);
            moveSelectionAnchorBlockIndex = block.Index;
        }

        NormalizeAreaSectionSelection();
        ApplyMoveSelection();
        RaiseMoveStateChanged();
        return true;
    }

    public void SetPointerOverBlock(MarkdownLiveBlockViewModel block, bool value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(block);
        if (ReferenceEquals(block.Owner, this))
        {
            block.SetPointerOver(value);
        }
    }

    public void SetToolbarFlyoutOpen(MarkdownLiveBlockViewModel block, bool value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(block);
        if (!ReferenceEquals(block.Owner, this))
        {
            return;
        }

        block.SetToolbarFlyoutOpen(value);
    }

    public void ClearMoveSelection()
    {
        moveSelectionIndices.Clear();
        moveSelectionAnchorBlockIndex = null;
        foreach (var block in Blocks)
        {
            block.SetMoveSelected(false);
            block.SetMoveDropTarget(MarkdownMoveDropTarget.None);
        }

        RaiseMoveStateChanged();
    }

    public void SetMoveDropTarget(MarkdownLiveBlockViewModel? target, bool after)
    {
        foreach (var block in Blocks)
        {
            block.SetMoveDropTarget(ReferenceEquals(block, target) && !moveSelectionIndices.Contains(block.Index)
                ? after ? MarkdownMoveDropTarget.After : MarkdownMoveDropTarget.Before
                : MarkdownMoveDropTarget.None);
        }
    }

    public Task<bool> MoveSelectionToTargetAsync(
        MarkdownLiveBlockViewModel target,
        bool after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsMovable || moveSelectionIndices.Contains(target.Index))
        {
            return Task.FromResult(false);
        }

        var insertBefore = after
            ? Blocks.Where(candidate => candidate.IsMovable
                        && candidate.Index > target.Index
                        && !moveSelectionIndices.Contains(candidate.Index))
                .Select(static candidate => (int?)candidate.Index)
                .FirstOrDefault()
            : target.Index;
        return ExecuteMoveAsync(insertBefore, destinationArea: null, cancellationToken);
    }

    public Task<bool> MoveSelectionByOffsetAsync(int delta, CancellationToken cancellationToken = default)
    {
        var target = FindAdjacentMoveTarget(delta);
        if (target is null)
        {
            return Task.FromResult(false);
        }

        return MoveSelectionToTargetAsync(target, after: delta > 0, cancellationToken);
    }

    public Task<bool> MoveSelectionToAreaAsync(
        AreaReference? destinationArea,
        CancellationToken cancellationToken = default) =>
        ExecuteMoveAsync(insertBeforeBlockIndex: null, destinationArea, cancellationToken);

    public async Task<bool> TransformSelectionAsync(
        MarkdownBlockListStyle style,
        CancellationToken cancellationToken = default)
    {
        var currentSnapshot = Snapshot;
        var callback = CommitBlockAsync;
        var selected = SelectedMoveBlocks().OrderBy(static block => block.Index).ToArray();
        if (!CanTransformSelection || currentSnapshot is null || callback is null || selected.Length == 0)
        {
            return false;
        }

        IsMoveInProgress = true;
        MoveErrorMessage = null;
        await commitGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var updated = currentSnapshot.Raw;
            for (var ordinal = selected.Length - 1; ordinal >= 0; ordinal--)
            {
                var block = selected[ordinal];
                var replacement = TransformListPrefix(block, style, ordinal + 1);
                updated = updated[..block.Block.Start]
                    + replacement
                    + updated[(block.Block.Start + block.Block.Length)..];
            }

            if (string.Equals(updated, currentSnapshot.Raw, StringComparison.Ordinal))
            {
                return true;
            }

            var selectedIndices = selected.Select(static block => block.Index).ToArray();
            var patch = new MarkdownBlockPatch(
                currentSnapshot.RelativePath,
                currentSnapshot.ExpectedRevisionHash,
                currentSnapshot.HasUtf8Bom,
                selectedIndices[0],
                new MarkdownBlockTextRange(0, currentSnapshot.Raw.Length),
                currentSnapshot.Raw,
                updated,
                updated);
            var result = await callback(patch, cancellationToken).ConfigureAwait(true);
            if (!result.IsAccepted
                || result.Snapshot is not { } accepted
                || !IsValidAcknowledgement(currentSnapshot, patch, accepted))
            {
                MoveErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L10n.Get("MarkdownBlockSaveFailed")
                    : result.ErrorMessage;
                return false;
            }

            Load(accepted);
            foreach (var index in selectedIndices.Where(index => index < Blocks.Count))
            {
                moveSelectionIndices.Add(index);
            }

            moveSelectionAnchorBlockIndex = selectedIndices[0];
            ApplyMoveSelection();
            RaiseMoveStateChanged();
            CommitAccepted?.Invoke(accepted);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            MoveErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            commitGate.Release();
            IsMoveInProgress = false;
        }
    }

    public async Task InvokeSelectionActionAsync(
        MarkdownSelectionSemanticAction action,
        CancellationToken cancellationToken = default)
    {
        var callback = SelectionActionAsync;
        var indices = SelectedMoveBlocks().OrderBy(static block => block.Index).Select(static block => block.Index).ToArray();
        if (callback is null
            || indices.Length == 0
            || action == MarkdownSelectionSemanticAction.ConvertHeadingToArea && !CanConvertSelectionToArea)
        {
            return;
        }

        MoveErrorMessage = null;
        IsMoveInProgress = true;
        try
        {
            await callback(this, indices, action, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            MoveErrorMessage = exception.Message;
        }
        finally
        {
            IsMoveInProgress = false;
        }
    }

    private IEnumerable<MarkdownLiveBlockViewModel> SelectedMoveBlocks() =>
        Blocks.Where(block => moveSelectionIndices.Contains(block.Index));

    private static string TransformListPrefix(
        MarkdownLiveBlockViewModel block,
        MarkdownBlockListStyle style,
        int ordinal)
    {
        var raw = block.Block.Raw;
        var lineEnd = raw.IndexOfAny(['\r', '\n']);
        var firstLine = lineEnd < 0 ? raw : raw[..lineEnd];
        var suffix = lineEnd < 0 ? string.Empty : raw[lineEnd..];
        var match = Regex.Match(
            firstLine,
            @"^(?<indent>[ \t]*)(?:(?:[-+*]|\d+[.)])\s+(?:\[(?<state>[ xX])\]\s+)?)?(?<text>.*)$",
            RegexOptions.CultureInvariant);
        var indent = match.Groups["indent"].Value;
        var text = match.Groups["text"].Value;
        var completed = block.Kind == MarkdownBlockKind.TaskListItem && block.IsTaskCompleted;
        var prefix = style switch
        {
            MarkdownBlockListStyle.Bulleted => "- ",
            MarkdownBlockListStyle.Numbered => $"{ordinal}. ",
            MarkdownBlockListStyle.Checklist => completed ? "- [x] " : "- [ ] ",
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
        };
        return indent + prefix + text + suffix;
    }

    private async Task<bool> ExecuteMoveAsync(
        int? insertBeforeBlockIndex,
        AreaReference? destinationArea,
        CancellationToken cancellationToken)
    {
        var currentSnapshot = Snapshot;
        var callback = MoveBlocksAsync;
        if (!CanMoveSelection || currentSnapshot is null || callback is null)
        {
            return false;
        }

        IsMoveInProgress = true;
        MoveErrorMessage = null;
        try
        {
            var selected = Blocks
                .Where(block => moveSelectionIndices.Contains(block.Index))
                .OrderBy(static block => block.Index)
                .Select(static block => block.Index)
                .ToArray();
            var result = await callback(
                new MarkdownBlocksMoveRequest(currentSnapshot, selected, insertBeforeBlockIndex, destinationArea),
                cancellationToken).ConfigureAwait(true);
            if (!result.IsAccepted || result.Snapshot is null || result.OutputBlockIndices is null)
            {
                MoveErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L10n.Get("FeedBlockMoveFailed")
                    : result.ErrorMessage;
                return false;
            }

            Load(result.Snapshot);
            foreach (var blockIndex in result.OutputBlockIndices)
            {
                moveSelectionIndices.Add(blockIndex);
            }

            moveSelectionAnchorBlockIndex = result.OutputBlockIndices.FirstOrDefault();
            ApplyMoveSelection();
            RaiseMoveStateChanged();
            CommitAccepted?.Invoke(result.Snapshot);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            MoveErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            IsMoveInProgress = false;
            SetMoveDropTarget(null, after: false);
        }
    }

    private MarkdownLiveBlockViewModel? FindAdjacentMoveTarget(int delta)
    {
        if (moveSelectionIndices.Count == 0 || delta == 0)
        {
            return null;
        }

        var movable = Blocks.Where(static block => block.IsMovable).ToArray();
        return delta < 0
            ? movable.LastOrDefault(block => block.Index < moveSelectionIndices.Min()
                && !moveSelectionIndices.Contains(block.Index))
            : movable.FirstOrDefault(block => block.Index > moveSelectionIndices.Max()
                && !moveSelectionIndices.Contains(block.Index));
    }

    private void ApplyMoveSelection()
    {
        foreach (var block in Blocks)
        {
            block.SetMoveSelected(moveSelectionIndices.Contains(block.Index));
        }
    }

    private void NormalizeAreaSectionSelection()
    {
        foreach (var headingIndex in moveSelectionIndices
                     .Where(index => Blocks.FirstOrDefault(block => block.Index == index)?.Kind == MarkdownBlockKind.AreaHeading)
                     .ToArray())
        {
            foreach (var candidate in Blocks.Where(candidate => candidate.Index > headingIndex))
            {
                if (candidate.Kind == MarkdownBlockKind.AreaHeading)
                {
                    break;
                }

                if (candidate.IsMovable)
                {
                    moveSelectionIndices.Add(candidate.Index);
                }
            }
        }
    }

    private void RaiseMoveStateChanged()
    {
        this.RaisePropertyChanged(nameof(SelectedMoveBlockCount));
        this.RaisePropertyChanged(nameof(HasMoveSelection));
        this.RaisePropertyChanged(nameof(CanMoveSelection));
        this.RaisePropertyChanged(nameof(CanMoveSelectionUp));
        this.RaisePropertyChanged(nameof(CanMoveSelectionDown));
        this.RaisePropertyChanged(nameof(CanTransformSelection));
        this.RaisePropertyChanged(nameof(CanOpenSelectionActions));
        this.RaisePropertyChanged(nameof(CanConvertSelectionToArea));
        foreach (var block in Blocks)
        {
            block.RaiseToolbarStateChanged();
        }
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
        RaiseMoveStateChanged();
        return true;
    }

    public MarkdownLiveBlockViewModel? BeginSessionBlockAfter(int sourceBlockIndex)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var sourcePosition = Blocks
            .Select((block, position) => (block, position))
            .FirstOrDefault(candidate => candidate.block.Index == sourceBlockIndex)
            .position;
        if (sourcePosition < 0
            || sourcePosition >= Blocks.Count
            || Blocks[sourcePosition].Index != sourceBlockIndex)
        {
            return null;
        }

        var source = Blocks[sourcePosition];
        MarkdownLiveBlockViewModel sessionBlock;
        if (sourcePosition + 1 < Blocks.Count
            && Blocks[sourcePosition + 1].Kind == MarkdownBlockKind.Blank)
        {
            var blank = Blocks[sourcePosition + 1];
            sessionBlock = new MarkdownLiveBlockViewModel(
                this,
                blank.Block,
                blank.DocumentNewLine,
                isSessionBlock: true,
                sessionInsertionPrefix: blank.Block.Raw,
                sessionInsertionSuffix: blank.DocumentNewLine);
            Blocks[sourcePosition + 1] = sessionBlock;
        }
        else
        {
            var synthetic = new MarkdownBlock(
                Blocks.Max(static block => block.Index) + 1,
                MarkdownBlockKind.Paragraph,
                string.Empty,
                source.Block.Start + source.Block.Length,
                0,
                source.Block.LineNumber + 1);
            sessionBlock = new MarkdownLiveBlockViewModel(
                this,
                synthetic,
                source.DocumentNewLine,
                isSessionBlock: true,
                sessionInsertionPrefix: source.DocumentNewLine,
                sessionInsertionSuffix: source.DocumentNewLine);
            Blocks.Insert(sourcePosition + 1, sessionBlock);
        }

        return BeginEdit(sessionBlock) ? sessionBlock : null;
    }

    public async Task<bool> ToggleTaskCompletionAsync(
        MarkdownLiveBlockViewModel requestedBlock,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(requestedBlock);
        if (!ReferenceEquals(requestedBlock.Owner, this)
            || requestedBlock.Kind != MarkdownBlockKind.TaskListItem
            || !CanEdit)
        {
            return false;
        }

        var requestedIndex = requestedBlock.Index;
        if (ActiveBlock is not null && !ReferenceEquals(ActiveBlock, requestedBlock))
        {
            if (!await CommitActiveAsync(cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            requestedBlock = Blocks.FirstOrDefault(candidate => candidate.Index == requestedIndex)
                ?? requestedBlock;
        }

        if (!BeginEdit(requestedBlock))
        {
            return false;
        }

        var replacementState = requestedBlock.IsTaskCompleted ? " " : "x";
        requestedBlock.EditorText = Regex.Replace(
            requestedBlock.EditorText,
            @"^(?<prefix>[ \t]*[-+*]\s+\[)[ xX](?<suffix>\])",
            match => match.Groups["prefix"].Value + replacementState + match.Groups["suffix"].Value,
            RegexOptions.CultureInvariant);
        return await CommitActiveAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<MarkdownBlockMergeResult> MergeActiveWithPreviousAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        CancelPendingAutosave();
        var block = ActiveBlock;
        var currentSnapshot = Snapshot;
        if (block is null || currentSnapshot is null || block.IsSessionBlock)
        {
            return new MarkdownBlockMergeResult(false, false);
        }

        var plan = mergeService.CreatePlan(currentSnapshot.Raw, block.Index, block.EditorText);
        if (plan is null)
        {
            return new MarkdownBlockMergeResult(false, false);
        }

        var callback = CommitBlockAsync;
        if (callback is null)
        {
            block.ErrorMessage = L10n.Get("MarkdownEditingUnavailable");
            return new MarkdownBlockMergeResult(true, false);
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            sessionCancellation.Token);
        await commitGate.WaitAsync(linkedCancellation.Token);
        try
        {
            if (!ReferenceEquals(block, ActiveBlock) || !ReferenceEquals(currentSnapshot, Snapshot))
            {
                return new MarkdownBlockMergeResult(true, false);
            }

            block.IsCommitInProgress = true;
            block.ErrorMessage = null;
            var patch = new MarkdownBlockPatch(
                currentSnapshot.RelativePath,
                currentSnapshot.ExpectedRevisionHash,
                currentSnapshot.HasUtf8Bom,
                plan.TargetBlockIndex,
                new MarkdownBlockTextRange(plan.SelectionStart, plan.SelectionLength),
                plan.OriginalRaw,
                plan.ReplacementRaw,
                plan.UpdatedDocumentRaw);
            var result = await callback(patch, linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsAccepted || result.Snapshot is not { } acceptedSnapshot)
            {
                block.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L10n.Get("MarkdownBlockSaveFailed")
                    : result.ErrorMessage;
                return new MarkdownBlockMergeResult(true, false);
            }

            if (!IsValidAcknowledgement(currentSnapshot, patch, acceptedSnapshot))
            {
                block.ErrorMessage = L10n.Get("MarkdownPatchAcknowledgementInvalid");
                return new MarkdownBlockMergeResult(true, false);
            }

            var removedBlockIndex = block.Index;
            hasDeferredCommitNotification = false;
            Load(acceptedSnapshot);
            await QueueDeleteDraftAsync(currentSnapshot.RelativePath, removedBlockIndex).ConfigureAwait(true);
            var target = Blocks.FirstOrDefault(candidate =>
                candidate.Index == plan.TargetBlockIndex && candidate.IsEditable);
            if (target is null || !BeginEdit(target))
            {
                CommitAccepted?.Invoke(acceptedSnapshot);
                return new MarkdownBlockMergeResult(true, true);
            }

            CommitAccepted?.Invoke(acceptedSnapshot);
            return new MarkdownBlockMergeResult(true, true, target, plan.CaretIndex);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            return new MarkdownBlockMergeResult(true, false);
        }
        catch (Exception exception)
        {
            block.ErrorMessage = exception.Message;
            return new MarkdownBlockMergeResult(true, false);
        }
        finally
        {
            block.IsCommitInProgress = false;
            commitGate.Release();
            RaiseMoveStateChanged();
        }
    }

    public bool CanMergeActiveWithPrevious()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var block = ActiveBlock;
        var currentSnapshot = Snapshot;
        return block is not null
            && currentSnapshot is not null
            && !block.IsSessionBlock
            && mergeService.CreatePlan(currentSnapshot.Raw, block.Index, block.EditorText) is not null;
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
                await QueueDeleteDraftAsync(currentSnapshot.RelativePath, closedBlockIndex).ConfigureAwait(true);
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
            await QueueDeleteDraftAsync(currentSnapshot.RelativePath, committedBlockIndex).ConfigureAwait(true);
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
            RaiseMoveStateChanged();
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
            if (notifyCommitAccepted || cancelledBlock.IsSessionBlock)
            {
                Load(currentSnapshot);
                if (notifyCommitAccepted)
                {
                    CommitAccepted?.Invoke(currentSnapshot);
                }
            }
        }

        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        RaiseMoveStateChanged();
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
            await RunOnSynchronizationContextAsync(() =>
            {
                DraftPersistenceError = null;
                onSuccess?.Invoke();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RunOnSynchronizationContextAsync(
                    () => DraftPersistenceError = exception.Message)
                .ConfigureAwait(false);
        }
    }

    private Task RunOnSynchronizationContextAsync(Action action)
    {
        var context = synchronizationContext;
        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            _ =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            null);
        return completion.Task;
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
    private readonly string sessionInsertionPrefix;
    private readonly string sessionInsertionSuffix;
    private string editorText;
    private bool isEditing;
    private bool isCommitInProgress;
    private bool isReviewHighlighted;
    private bool isReviewAnchor;
    private bool isFeedFilterVisible = true;
    private bool isMoveSelected;
    private bool isPointerOverBlock;
    private bool isToolbarFlyoutOpen;
    private MarkdownMoveDropTarget moveDropTarget;
    private string? errorMessage;
    private string? reviewSelectionAutomationName;

    internal MarkdownLiveBlockViewModel(
        MarkdownLivePreviewEditorViewModel owner,
        MarkdownBlock block,
        string newLine,
        bool isSessionBlock = false,
        string sessionInsertionPrefix = "",
        string sessionInsertionSuffix = "")
    {
        Owner = owner;
        Block = block;
        this.newLine = newLine;
        this.sessionInsertionPrefix = sessionInsertionPrefix;
        this.sessionInsertionSuffix = sessionInsertionSuffix;
        IsSessionBlock = isSessionBlock;
        editorText = StripStructuralLineEnding(block.Raw);
        RenderKind = isSessionBlock ? MarkdownLiveBlockRenderKind.Paragraph : Classify(block);
        PreviewText = CreatePreviewText(block, RenderKind);
        InlineTokens = TokenizeInline(PreviewText);
    }

    public MarkdownLivePreviewEditorViewModel Owner { get; }

    public MarkdownBlock Block { get; private set; }

    public bool IsSessionBlock { get; }

    public int Index => Block.Index;

    public string DocumentNewLine => newLine;

    public string BlockAutomationId => $"{Owner.AutomationIdPrefix}Block-{Index}";

    public string PreviewAutomationId => $"{Owner.AutomationIdPrefix}BlockPreview-{Index}";

    public string EditorAutomationId => $"{Owner.AutomationIdPrefix}BlockEditor-{Index}";

    public string RawFallbackAutomationId => $"{Owner.AutomationIdPrefix}RawFallback-{Index}";

    public string ErrorAutomationId => $"{Owner.AutomationIdPrefix}BlockError-{Index}";

    public string SavingAutomationId => $"{Owner.AutomationIdPrefix}BlockSaving-{Index}";

    public string MoveHandleAutomationId => $"{Owner.AutomationIdPrefix}BlockMoveHandle-{Index}";

    public string ContextToolbarAutomationId => $"{Owner.AutomationIdPrefix}BlockToolbar-{Index}";

    public string TaskCheckboxAutomationId => $"{Owner.AutomationIdPrefix}TaskCheckbox-{Index}";

    public string LinkAutomationId(int linkIndex) => $"{Owner.AutomationIdPrefix}Link-{Index}-{linkIndex}";

    public string BlockedLinkAutomationId(int linkIndex) => $"{Owner.AutomationIdPrefix}BlockedLink-{Index}-{linkIndex}";

    public string AutomationName => L10n.Format("MarkdownBlockAutomationName", Index + 1, PreviewText);

    public bool IsReviewHighlighted => isReviewHighlighted;

    public bool IsReviewAnchor => isReviewAnchor;

    public bool IsFeedFilterVisible => isFeedFilterVisible;

    internal void SetFeedFilterVisible(bool value)
    {
        this.RaiseAndSetIfChanged(ref isFeedFilterVisible, value, nameof(IsFeedFilterVisible));
    }

    public bool IsMoveSelected => isMoveSelected;

    public bool IsPointerOverBlock => isPointerOverBlock;

    public bool IsToolbarFlyoutOpen => isToolbarFlyoutOpen;

    public bool IsContextToolbarVisible => !IsEditing
        && (IsPointerOverBlock || IsMoveSelected || IsToolbarFlyoutOpen);

    public bool CanMoveUpFromToolbar => Owner.CanMoveBlockFromToolbar(this, -1);

    public bool CanMoveDownFromToolbar => Owner.CanMoveBlockFromToolbar(this, 1);

    public bool CanTransformFromToolbar => Owner.CanTransformBlockFromToolbar(this);

    public bool CanOpenActionsFromToolbar => Owner.CanOpenBlockActionsFromToolbar(this);

    public bool CanConvertToAreaFromToolbar => Owner.CanConvertBlockToAreaFromToolbar(this);

    public bool IsMoveDropBefore => moveDropTarget == MarkdownMoveDropTarget.Before;

    public bool IsMoveDropAfter => moveDropTarget == MarkdownMoveDropTarget.After;

    public string MoveDropAutomationStatus => moveDropTarget.ToString();

    internal void SetMoveSelected(bool value)
    {
        this.RaiseAndSetIfChanged(ref isMoveSelected, value, nameof(IsMoveSelected));
        this.RaisePropertyChanged(nameof(MoveHandleIdleOpacity));
        this.RaisePropertyChanged(nameof(IsContextToolbarVisible));
        RaiseToolbarStateChanged();
    }

    internal void SetPointerOver(bool value)
    {
        this.RaiseAndSetIfChanged(ref isPointerOverBlock, value, nameof(IsPointerOverBlock));
        this.RaisePropertyChanged(nameof(MoveHandleIdleOpacity));
        this.RaisePropertyChanged(nameof(IsContextToolbarVisible));
    }

    internal void SetToolbarFlyoutOpen(bool value)
    {
        this.RaiseAndSetIfChanged(ref isToolbarFlyoutOpen, value, nameof(IsToolbarFlyoutOpen));
        this.RaisePropertyChanged(nameof(MoveHandleIdleOpacity));
        this.RaisePropertyChanged(nameof(IsContextToolbarVisible));
    }

    internal void RaiseToolbarStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanMoveUpFromToolbar));
        this.RaisePropertyChanged(nameof(CanMoveDownFromToolbar));
        this.RaisePropertyChanged(nameof(CanTransformFromToolbar));
        this.RaisePropertyChanged(nameof(CanOpenActionsFromToolbar));
        this.RaisePropertyChanged(nameof(CanConvertToAreaFromToolbar));
    }

    internal void SetMoveDropTarget(MarkdownMoveDropTarget value)
    {
        if (moveDropTarget == value)
        {
            return;
        }

        moveDropTarget = value;
        this.RaisePropertyChanged(nameof(IsMoveDropBefore));
        this.RaisePropertyChanged(nameof(IsMoveDropAfter));
        this.RaisePropertyChanged(nameof(MoveDropAutomationStatus));
    }

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

    public bool IsMovable => (Block.Kind is MarkdownBlockKind.Heading
            or MarkdownBlockKind.AreaHeading
            or MarkdownBlockKind.Paragraph
            or MarkdownBlockKind.ListItem
            or MarkdownBlockKind.TaskListItem
            or MarkdownBlockKind.BlockQuote
            or MarkdownBlockKind.FencedCode
            or MarkdownBlockKind.HorizontalRule)
        && Block.Kind is not MarkdownBlockKind.Raw
        && !Block.Raw.Contains("unlimotion://task/", StringComparison.Ordinal)
        && !Block.Raw.Contains("<!-- unlimotion-note:", StringComparison.Ordinal)
        && !Block.Raw.Contains("<!-- unlimotion-recovery:", StringComparison.Ordinal)
        && !Block.Raw.Contains("#^", StringComparison.Ordinal);

    public bool CanStartEdit => IsEditable && Owner.CanEdit;

    public bool IsPreviewVisible => !IsEditing;

    public bool IsRawFallback => RenderKind == MarkdownLiveBlockRenderKind.RawFallback;

    public bool IsTaskCompleted => Block.IsTaskCompleted == true;

    public bool IsAreaHeading => Block.Kind == MarkdownBlockKind.AreaHeading;

    public string MoveHandleContent => IsAreaHeading ? "◈" : "⋮⋮";

    public double MoveHandleIdleOpacity =>
        IsAreaHeading || IsMoveSelected || IsPointerOverBlock || IsToolbarFlyoutOpen ? 1 : 0;

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
            this.RaisePropertyChanged(nameof(IsContextToolbarVisible));
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
        var replacement = IsSessionBlock
            ? sessionInsertionPrefix + NormalizeNewLines(EditorText, newLine) + sessionInsertionSuffix
            : NormalizeNewLines(EditorText, newLine) + GetStructuralLineEnding(Block.Raw);
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
