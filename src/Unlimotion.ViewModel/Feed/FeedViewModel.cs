using PropertyChanged;
using ReactiveUI;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Search;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedViewModel : ReactiveObject, IDisposable
{
    private const int InitialDayPageSize = 14;
    private const int DayPageSize = 14;
    private static readonly VaultRootRegistry SharedVaultRootRegistry = new();
    private readonly CompositeDisposable disposables = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Func<DateOnly>? explicitTodayProvider;
    private readonly Func<DateTimeOffset> localNowProvider;
    private readonly Func<string, OwnWriteRegistry, INoteVault> vaultFactory;
    private readonly Func<string, IFeedOperationJournal> operationJournalFactory;
    private readonly Func<string, IFeedTaskConversionJournal> taskJournalFactory;
    private readonly Func<string, IRevisionStore> revisionStoreFactory;
    private readonly VaultRootRegistry vaultRootRegistry;
    private readonly string reviewDeviceId;
    private readonly object sessionLock = new();
    private readonly object reconfigureRequestLock = new();
    private readonly object vaultRootHandoffLock = new();
    private readonly object searchLock = new();
    private readonly object indexLock = new();
    private readonly object reviewQueueLock = new();
    private readonly Queue<DocumentConflictState> pendingDocumentConflicts = new();
    private Action<Action>? notificationDispatcher;
    private CancellationTokenSource? sessionCancellation;
    private CancellationTokenSource? rootReconfigureCancellation;
    private CancellationTokenSource? searchCancellation;
    private CancellationTokenSource? indexCancellation;
    private INoteVault? vault;
    private DailyNoteService? dailyNotes;
    private DailyNoteNaming dailyNoteNaming = DailyNoteNaming.Default;
    private DailyNoteSettingsSnapshot? dailyNoteSettingsSnapshot;
    private FeedSearchIndex? searchIndex;
    private IMarkdownDocumentParser? markdownParser;
    private FeedReviewSessionCoordinator? reviewCoordinator;
    private IRevisionStore? revisionStore;
    private FeedVaultWatchRuntime? watchRuntime;
    private FeedFilesDrawerViewModel? filesDrawer;
    private AreaManagementViewModel? areaManagement;
    private FeedDocumentConflictViewModel? documentConflict;
    private FeedVaultIdentityConflictViewModel? identityConflict;
    private FeedReviewRecoveryViewModel? reviewRecovery;
    private FeedThematicDocumentViewModel? openedThematicFile;
    private FeedTaskReferenceViewModel? createdTaskReference;
    private IDisposable? taskSearchSubscription;
    private ITaskStorage? indexedTaskStorage;
    private PropertyChangedEventHandler? taskOwnerPropertyChangedHandler;
    private NotifyCollectionChangedEventHandler? areaCatalogChangedHandler;
    private IReadOnlyList<FeedAreaOptionViewModel> snapshotAreas = [];
    private IReadOnlyList<FeedReviewDocument> reviewDocuments = [];
    private ReviewQueueSnapshot reviewQueueSnapshot = ReviewQueueSnapshot.Empty;
    private ReviewQueueBuildState? reviewQueueBuild;
    private string? vaultId;
    private string? attachedVaultId;
    private string? attachedVaultRootPath;
    private VaultRootHandoffLease? pendingVaultRootHandoff;
    private FeedReviewCandidate? currentCandidate;
    private MarkdownDocument? currentReviewDocument;
    private MarkdownBlockSelection? currentReviewSelection;
    private int currentReviewAnchorBlockIndex;
    private string? currentReviewOperationId;
    private Func<string, TaskItemViewModel?>? taskResolver;
    private MainWindowViewModel? taskOwner;
    private FeedDayViewModel? selectedDay;
    private FeedSearchAreaOptionViewModel? selectedSearchArea;
    private FeedSearchTypeOptionViewModel? selectedSearchType;
    private DateTimeOffset? searchFromDate;
    private DateTimeOffset? searchToDate;
    private string searchQuery = string.Empty;
    private string quickCaptureText = string.Empty;
    private TimeSpan dayBoundary;
    private long searchGeneration;
    private long indexGeneration;
    private long reviewQueueVersion;
    private long rootReconfigureGeneration;
    private long sessionGeneration;
    private int loadedDayCount = InitialDayPageSize;
    private int totalDayCount;
    private bool isIdentityFrozen;
    private bool isDisposed;

    public FeedViewModel(
        Func<DateOnly>? todayProvider = null,
        Func<string, INoteVault>? vaultFactory = null,
        Func<DateTimeOffset>? localNowProvider = null,
        Func<string, IFeedOperationJournal>? operationJournalFactory = null,
        Func<string, IFeedTaskConversionJournal>? taskJournalFactory = null,
        string? reviewDeviceId = null,
        bool isExternalVaultSupported = true,
        Func<string, IRevisionStore>? revisionStoreFactory = null,
        VaultRootRegistry? vaultRootRegistry = null)
    {
        explicitTodayProvider = todayProvider;
        this.localNowProvider = localNowProvider ?? (() => DateTimeOffset.Now);
        this.vaultFactory = vaultFactory is null
            ? static (rootPath, ownWrites) => new FileNoteVault(rootPath, ownWrites)
            : (rootPath, _) => vaultFactory(rootPath);
        this.operationJournalFactory = operationJournalFactory ??
            (id => new FileFeedOperationJournal(GetDefaultRecoveryRoot(id)));
        this.taskJournalFactory = taskJournalFactory ??
            (id => new FileFeedTaskConversionJournal(GetDefaultRecoveryRoot(id)));
        this.revisionStoreFactory = revisionStoreFactory ??
            (id => new BoundedRevisionStore(GetDefaultRecoveryRoot(id)));
        this.vaultRootRegistry = vaultRootRegistry ?? SharedVaultRootRegistry;
        this.reviewDeviceId = string.IsNullOrWhiteSpace(reviewDeviceId)
            ? CreateDefaultDeviceId()
            : reviewDeviceId;
        IsExternalVaultSupported = isExternalVaultSupported;
        Areas.Add(FeedAreaOptionViewModel.NoArea);
        SelectedArea = Areas[0];
        ReviewDestinationArea = Areas[0];
        SearchAreaOptions.Add(FeedSearchAreaOptionViewModel.All);
        SearchAreaOptions.Add(FeedSearchAreaOptionViewModel.NoArea);
        SelectedSearchArea = SearchAreaOptions[0];
        SearchTypeOptions.Add(FeedSearchTypeOptionViewModel.All);
        SearchTypeOptions.Add(new FeedSearchTypeOptionViewModel(
            FeedSearchDocumentType.Daily,
            "FeedSearchTypeDaily"));
        SearchTypeOptions.Add(new FeedSearchTypeOptionViewModel(
            FeedSearchDocumentType.Note,
            "FeedSearchTypeNote"));
        SearchTypeOptions.Add(new FeedSearchTypeOptionViewModel(
            FeedSearchDocumentType.Task,
            "FeedSearchTypeTask"));
        SelectedSearchType = SearchTypeOptions[0];

        var chooseVaultCommand = ReactiveCommand.CreateFromTask(ChooseVaultCoreAsync);
        ChooseVaultCommand = chooseVaultCommand;
        disposables.Add(chooseVaultCommand);

        var captureCommand = ReactiveCommand.CreateFromTask(CaptureCoreAsync);
        CaptureCommand = captureCommand;
        disposables.Add(captureCommand);

        var refreshCommand = ReactiveCommand.CreateFromTask(RefreshCoreAsync);
        RefreshCommand = refreshCommand;
        disposables.Add(refreshCommand);

        var loadOlderDaysCommand = ReactiveCommand.CreateFromTask(LoadOlderDaysCoreAsync);
        LoadOlderDaysCommand = loadOlderDaysCommand;
        disposables.Add(loadOlderDaysCommand);

        var startReviewCommand = ReactiveCommand.CreateFromTask(StartReviewCoreAsync);
        StartReviewCommand = startReviewCommand;
        disposables.Add(startReviewCommand);

        var finishReviewCommand = ReactiveCommand.CreateFromTask(FinishReviewCoreAsync);
        FinishReviewCommand = finishReviewCommand;
        disposables.Add(finishReviewCommand);

        var leaveReviewCommand = ReactiveCommand.CreateFromTask(() => CompleteReviewDecisionAsync(ReviewDecision.Kept));
        LeaveReviewCommand = leaveReviewCommand;
        disposables.Add(leaveReviewCommand);

        var skipReviewCommand = ReactiveCommand.CreateFromTask(() => CompleteReviewDecisionAsync(ReviewDecision.Deferred));
        SkipReviewCommand = skipReviewCommand;
        disposables.Add(skipReviewCommand);

        var assignAreaCommand = ReactiveCommand.CreateFromTask(AssignReviewAreaCoreAsync);
        AssignReviewAreaCommand = assignAreaCommand;
        disposables.Add(assignAreaCommand);

        var createTaskCommand = ReactiveCommand.CreateFromTask(CreateTaskCoreAsync);
        CreateTaskCommand = createTaskCommand;
        disposables.Add(createTaskCommand);

        var createNoteCommand = ReactiveCommand.CreateFromTask(CreateNoteCoreAsync);
        CreateNoteCommand = createNoteCommand;
        disposables.Add(createNoteCommand);

        var moveToTodayCommand = ReactiveCommand.CreateFromTask(MoveToTodayCoreAsync);
        MoveToTodayCommand = moveToTodayCommand;
        disposables.Add(moveToTodayCommand);

        var continueReviewCommand = ReactiveCommand.CreateFromTask(ContinueReviewCoreAsync);
        ContinueReviewCommand = continueReviewCommand;
        disposables.Add(continueReviewCommand);

        var openSearchResultCommand = ReactiveCommand.CreateFromTask<FeedSearchResultViewModel?>(OpenSearchResultCoreAsync);
        OpenSearchResultCommand = openSearchResultCommand;
        disposables.Add(openSearchResultCommand);

        ExpandSelectionUpCommand = new FeedActionCommand(_ => ExpandReviewSelection(up: true));
        ExpandSelectionDownCommand = new FeedActionCommand(_ => ExpandReviewSelection(up: false));
        ShrinkSelectionUpCommand = new FeedActionCommand(_ => ShrinkReviewSelection(fromTop: true));
        ShrinkSelectionDownCommand = new FeedActionCommand(_ => ShrinkReviewSelection(fromTop: false));
        NavigateTaskCommand = new FeedActionCommand(task => NavigateToTask(task as TaskItemViewModel));
        OpenFilesCommand = new FeedActionCommand(_ => OpenFilesDrawer());
        OpenAreasCommand = new FeedActionCommand(_ => OpenAreaManagement());
        CloseThematicFileCommand = new FeedActionCommand(_ => CloseThematicFile());
    }

    public ObservableCollection<FeedDayViewModel> Days { get; } = new();

    public ObservableCollection<FeedSearchResultViewModel> SearchResults { get; } = new();

    public ObservableCollection<FeedAreaOptionViewModel> Areas { get; } = new();

    public ObservableCollection<FeedSearchAreaOptionViewModel> SearchAreaOptions { get; } = new();

    public ObservableCollection<FeedSearchTypeOptionViewModel> SearchTypeOptions { get; } = new();

    public void SetNotificationDispatcher(Action<Action>? dispatcher)
    {
        ThrowIfDisposed();
        notificationDispatcher = dispatcher;
    }

    internal Func<CancellationToken, Task>? ReviewQueueBuildGateAsync { get; set; }

    /// <summary>
    /// Durable locators imported from the losing branch of a vault-identity conflict. They stay
    /// visible even when the referenced block is not present in the currently connected branch.
    /// </summary>
    public ObservableCollection<FeedSafePendingLocatorViewModel> IdentitySafePending { get; } = new();

    public bool HasIdentitySafePending { get; private set; }

    public ObservableCollection<FeedPendingRecoveryViewModel> PendingRecoveries { get; } = new();

    public bool HasPendingRecoveries { get; private set; }

    public ICommand ChooseVaultCommand { get; }

    public ICommand CaptureCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand LoadOlderDaysCommand { get; }

    public ICommand StartReviewCommand { get; }

    public ICommand FinishReviewCommand { get; }

    public ICommand LeaveReviewCommand { get; }

    public ICommand SkipReviewCommand { get; }

    public ICommand AssignReviewAreaCommand { get; }

    public ICommand CreateTaskCommand { get; }

    public ICommand CreateNoteCommand { get; }

    public ICommand MoveToTodayCommand { get; }

    public ICommand ContinueReviewCommand { get; }

    public ICommand OpenSearchResultCommand { get; }

    public ICommand ExpandSelectionUpCommand { get; }

    public ICommand ExpandSelectionDownCommand { get; }

    public ICommand ShrinkSelectionUpCommand { get; }

    public ICommand ShrinkSelectionDownCommand { get; }

    public ICommand NavigateTaskCommand { get; }

    public ICommand OpenFilesCommand { get; }

    public ICommand OpenAreasCommand { get; }

    public ICommand CloseThematicFileCommand { get; }

    public Func<Task>? ChooseVaultAsync { get; set; }

    [AlsoNotifyFor(nameof(CanCreateReviewTask), nameof(IsTaskConversionUnavailable))]
    public IFeedTaskCreationTarget? TaskCreationTarget { get; set; }

    public MainWindowViewModel? TaskOwner
    {
        get => taskOwner;
        set
        {
            if (ReferenceEquals(taskOwner, value))
            {
                return;
            }

            DetachTaskSearchTracking();
            taskOwner = value;
            AttachTaskSearchTracking();
            ScheduleSearchResultsRefresh();
        }
    }

    internal void OnTaskStorageChanged()
    {
        EnsureTaskSearchSubscription();
    }

    public Action<TaskItemViewModel>? NavigateToTaskRequested { get; set; }

    public Func<string, TaskItemViewModel?>? TaskResolver
    {
        get => taskResolver;
        set
        {
            taskResolver = value;
            RefreshTaskReferences();
            ScheduleSearchResultsRefresh();
        }
    }

    public event EventHandler? SearchNavigationStarting;

    public event EventHandler<FeedSearchNavigationRequestedEventArgs>? SearchNavigationRequested;

    public event EventHandler<FeedSearchNavigationRequestedEventArgs>? ReviewNavigationRequested;

    public FeedFilesDrawerViewModel? FilesDrawer
    {
        get => filesDrawer;
        private set => filesDrawer = value;
    }

    public AreaManagementViewModel? AreaManagement
    {
        get => areaManagement;
        private set => areaManagement = value;
    }

    public FeedDocumentConflictViewModel? DocumentConflict
    {
        get => documentConflict;
        private set => documentConflict = value;
    }

    public FeedVaultIdentityConflictViewModel? IdentityConflict
    {
        get => identityConflict;
        private set => identityConflict = value;
    }

    public FeedReviewRecoveryViewModel? ReviewRecovery
    {
        get => reviewRecovery;
        private set => reviewRecovery = value;
    }

    [AlsoNotifyFor(nameof(HasOpenedThematicFile))]
    public FeedThematicDocumentViewModel? OpenedThematicFile
    {
        get => openedThematicFile;
        private set => openedThematicFile = value;
    }

    public bool HasOpenedThematicFile => OpenedThematicFile is not null;

    [AlsoNotifyFor(nameof(CanCapture), nameof(CanStartReview))]
    public bool IsIdentityFrozen
    {
        get => isIdentityFrozen;
        private set => isIdentityFrozen = value;
    }

    [AlsoNotifyFor(nameof(IsVaultChoiceEnabled), nameof(IsVaultUnsupportedVisible))]
    public bool IsExternalVaultSupported { get; set; }

    public bool IsVaultChoiceEnabled => IsExternalVaultSupported && !IsBusy;

    public bool IsVaultUnsupportedVisible => !IsExternalVaultSupported;

    [AlsoNotifyFor(nameof(EffectiveToday), nameof(CanMoveReviewToToday))]
    public TimeSpan DayBoundary
    {
        get => dayBoundary;
        set
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The day boundary must be between 00:00 inclusive and 24:00 exclusive.");
            }

            dayBoundary = value;
        }
    }

    public DateOnly EffectiveToday => explicitTodayProvider?.Invoke()
        ?? DateOnly.FromDateTime(localNowProvider().DateTime.Subtract(DayBoundary));

    public string? VaultRootPath { get; private set; }

    /// <summary>
    /// Raised after the portable daily-note naming setting has been loaded or durably applied.
    /// The Settings surface owns draft handling; Feed only publishes the applied vault state.
    /// </summary>
    public event EventHandler<NoteDailyFileNameFormatState>? DailyNoteFileNameFormatChanged;

    public NoteDailyFileNameFormatValidation ValidateDailyNoteFileNameFormat(string format)
    {
        if (!DailyNoteNaming.TryCreate(format, out var naming, out var validationError))
        {
            return new NoteDailyFileNameFormatValidation(false, null, validationError);
        }

        return new NoteDailyFileNameFormatValidation(
            true,
            naming.GetRelativePath(EffectiveToday),
            null);
    }

    public async Task<NoteDailyFileNameFormatApplyResult> ApplyDailyNoteFileNameFormatAsync(string format)
    {
        ThrowIfDisposed();
        var validation = ValidateDailyNoteFileNameFormat(format);
        if (!validation.IsValid)
        {
            return new NoteDailyFileNameFormatApplyResult(
                false,
                ErrorMessage: validation.ErrorMessage);
        }

        if (!DailyNoteNaming.TryCreate(format, out var naming, out var validationError))
        {
            return new NoteDailyFileNameFormatApplyResult(
                false,
                ErrorMessage: validationError);
        }

        var rootPath = VaultRootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !IsVaultInitialized)
        {
            return new NoteDailyFileNameFormatApplyResult(
                false,
                ErrorMessage: "Connect a note vault before applying its daily filename format.");
        }

        var request = CaptureRootReconfigureRequest();
        var session = new VaultSessionExpectation(rootPath, GetSessionToken());
        var result = await RunVaultReconfigureAsync(
                rootPath,
                naming,
                dailyNoteSettingsSnapshot?.Revision,
                isExternalChange: false,
                request: request,
                expectedSession: session)
            .ConfigureAwait(true);
        return new NoteDailyFileNameFormatApplyResult(
            result.Succeeded,
            result.State,
            result.ErrorMessage,
            result.IsCancelled);
    }

    public async Task<NoteDailyFileNameFormatState> ReloadDailyNoteFileNameFormatAsync()
    {
        ThrowIfDisposed();
        var rootPath = VaultRootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !IsVaultInitialized)
        {
            return CreateDailyNoteFileNameFormatState();
        }

        var request = CaptureRootReconfigureRequest();
        var session = new VaultSessionExpectation(rootPath, GetSessionToken());
        var result = await RunVaultReconfigureAsync(
                rootPath,
                requestedNaming: null,
                expectedDailyNoteSettingsRevision: null,
                isExternalChange: true,
                request: request,
                expectedSession: session)
            .ConfigureAwait(true);
        return result.State ?? CreateDailyNoteFileNameFormatState(
            result.ErrorMessage,
            isExternalChange: true,
            requiresReload: !result.IsCancelled);
    }

    [AlsoNotifyFor(nameof(IsOnboardingVisible), nameof(IsContentVisible), nameof(CanCapture))]
    public bool IsVaultInitialized { get; private set; }

    public bool IsOnboardingVisible => !IsVaultInitialized;

    public bool IsContentVisible => IsVaultInitialized;

    [AlsoNotifyFor(nameof(CanCapture), nameof(CanStartReview), nameof(IsVaultChoiceEnabled))]
    public bool IsBusy { get; private set; }

    [AlsoNotifyFor(nameof(HasError))]
    public string? ErrorMessage { get; private set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [AlsoNotifyFor(nameof(IsSearchActive), nameof(IsChronologyVisible), nameof(IsSearchEmpty))]
    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(searchQuery, normalized, StringComparison.Ordinal))
            {
                return;
            }

            searchQuery = normalized;
            ScheduleSearchResultsRefresh();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool IsChronologyVisible => !IsSearchActive;

    public FeedSearchAreaOptionViewModel? SelectedSearchArea
    {
        get => selectedSearchArea;
        set
        {
            if (ReferenceEquals(selectedSearchArea, value))
            {
                return;
            }

            selectedSearchArea = value;
            ScheduleSearchResultsRefresh();
        }
    }

    public FeedSearchTypeOptionViewModel? SelectedSearchType
    {
        get => selectedSearchType;
        set
        {
            if (ReferenceEquals(selectedSearchType, value))
            {
                return;
            }

            selectedSearchType = value;
            ScheduleSearchResultsRefresh();
        }
    }

    public DateTimeOffset? SearchFromDate
    {
        get => searchFromDate;
        set
        {
            if (searchFromDate == value)
            {
                return;
            }

            searchFromDate = value;
            ScheduleSearchResultsRefresh();
        }
    }

    public DateTimeOffset? SearchToDate
    {
        get => searchToDate;
        set
        {
            if (searchToDate == value)
            {
                return;
            }

            searchToDate = value;
            ScheduleSearchResultsRefresh();
        }
    }

    [AlsoNotifyFor(nameof(IsChronologyEmpty))]
    public bool HasDays { get; private set; }

    public bool IsChronologyEmpty => !HasDays;

    public int LoadedDayCount { get; private set; }

    public int TotalDayCount
    {
        get => totalDayCount;
        private set => totalDayCount = value;
    }

    public bool HasMoreDays { get; private set; }

    public bool IsSearchIndexing { get; private set; }

    public int IndexedMarkdownFiles { get; private set; }

    public int TotalMarkdownFiles { get; private set; }

    public FeedDayViewModel? SelectedDay
    {
        get => selectedDay;
        set => selectedDay = value;
    }

    [AlsoNotifyFor(nameof(IsSearchEmpty))]
    public bool HasSearchResults { get; private set; }

    public bool IsSearchEmpty => IsSearchActive && !HasSearchResults;

    [AlsoNotifyFor(nameof(CanCapture))]
    public string QuickCaptureText
    {
        get => quickCaptureText;
        set => quickCaptureText = value ?? string.Empty;
    }

    [AlsoNotifyFor(nameof(CanCapture))]
    public FeedAreaOptionViewModel? SelectedArea { get; set; }

    public bool CanCapture => IsVaultInitialized
        && !IsBusy
        && !IsIdentityFrozen
        && !string.IsNullOrWhiteSpace(QuickCaptureText);

    [AlsoNotifyFor(nameof(HasBootstrapSummary))]
    public int BootstrapIndexedFiles { get; private set; }

    [AlsoNotifyFor(nameof(HasBootstrapSummary))]
    public int BootstrapPendingCheckboxes { get; private set; }

    public bool BootstrapWasReused { get; private set; }

    public bool HasBootstrapSummary => BootstrapIndexedFiles > 0 || BootstrapPendingCheckboxes > 0;

    [AlsoNotifyFor(nameof(HasPendingReview), nameof(CanStartReview), nameof(IsReviewBannerVisible))]
    public int PendingReviewBlocks { get; private set; }

    public int PendingReviewDays { get; private set; }

    public bool HasPendingReview => PendingReviewBlocks > 0;

    public bool IsReviewBannerVisible => HasPendingReview || IsReviewActive;

    public bool CanStartReview => IsVaultInitialized
        && HasPendingReview
        && !IsBusy
        && !IsReviewActive
        && !IsIdentityFrozen;

    [AlsoNotifyFor(
        nameof(CanStartReview),
        nameof(IsReviewSelectionVisible),
        nameof(IsReviewBannerVisible),
        nameof(CanCreateReviewTask),
        nameof(CanModifyReviewSource),
        nameof(IsTaskConversionUnavailable))]
    public bool IsReviewActive { get; private set; }

    public bool IsReviewSelectionVisible => IsReviewActive && CurrentReview is not null;

    [AlsoNotifyFor(
        nameof(IsReviewSelectionVisible),
        nameof(CanMoveReviewToToday),
        nameof(CanCreateReviewTask),
        nameof(CanModifyReviewSource),
        nameof(IsTaskConversionUnavailable))]
    public FeedReviewSelectionViewModel? CurrentReview { get; private set; }

    public FeedAreaOptionViewModel? ReviewDestinationArea { get; set; }

    public ObservableCollection<FeedTaskAreaOptionViewModel> ReviewTaskAreas { get; } = new();

    public bool ReviewTaskIsGoal { get; set; }

    public string ReviewNoteTitle { get; set; } = string.Empty;

    public string ReviewNoteFolder { get; set; } = string.Empty;

    [AlsoNotifyFor(
        nameof(HasCreatedTask),
        nameof(CanCreateReviewTask),
        nameof(CanMoveReviewToToday),
        nameof(CanModifyReviewSource))]
    public FeedTaskReferenceViewModel? CreatedTaskReference
    {
        get => createdTaskReference;
        private set
        {
            if (ReferenceEquals(createdTaskReference, value))
            {
                return;
            }

            createdTaskReference?.Dispose();
            createdTaskReference = value;
            this.RaisePropertyChanged(nameof(CreatedTaskReference));
            this.RaisePropertyChanged(nameof(HasCreatedTask));
            this.RaisePropertyChanged(nameof(CanCreateReviewTask));
            this.RaisePropertyChanged(nameof(CanMoveReviewToToday));
            this.RaisePropertyChanged(nameof(CanModifyReviewSource));
        }
    }

    public bool HasCreatedTask => CreatedTaskReference is not null;

    public bool CanCreateReviewTask => IsReviewSelectionVisible
        && !HasCreatedTask
        && TaskCreationTarget?.SupportsClassification == true;

    public bool CanModifyReviewSource => IsReviewSelectionVisible && !HasCreatedTask;

    public bool IsTaskConversionUnavailable => IsReviewSelectionVisible
        && TaskCreationTarget is not null
        && !TaskCreationTarget.SupportsClassification;

    public bool CanMoveReviewToToday => CanModifyReviewSource && CurrentReview?.Date < EffectiveToday;

    public async Task InitializeVaultAsync(string? rootPath)
    {
        var request = BeginRootReconfigureRequest();
        await RunVaultReconfigureAsync(
                rootPath,
                requestedNaming: null,
                expectedDailyNoteSettingsRevision: null,
                isExternalChange: false,
                request: request,
                expectedSession: null)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Serializes every vault session replacement, including a daily-layout write. The gate is
    /// acquired before the previous session is cancelled or disposed so a failed replacement
    /// keeps the last usable timeline connected.
    /// </summary>
    private async Task<VaultReconfigureResult> RunVaultReconfigureAsync(
        string? rootPath,
        DailyNoteNaming? requestedNaming,
        string? expectedDailyNoteSettingsRevision,
        bool isExternalChange,
        VaultReconfigureRequest request,
        VaultSessionExpectation? expectedSession)
    {
        ThrowIfDisposed();
        var gateAcquired = false;
        var cancellationToken = CancellationToken.None;
        FeedLoadResult? loaded = null;
        VaultRootHandoffLease? rootHandoff = null;
        INoteVault? settingsVaultForRollback = null;
        DailyNoteSettingsSnapshot? settingsBeforeApply = null;
        DailyNoteSettingsSnapshot? persistedApplySettings = null;
        var oldSessionTeardownStarted = false;
        var candidateOwnershipTransferred = false;
        var visibleCandidateInstalled = false;
        try
        {
            await operationGate.WaitAsync(request.CancellationToken).ConfigureAwait(true);
            gateAcquired = true;
            ThrowIfDisposed();
            if (!IsCurrentReconfigureRequest(request)
                || !IsCurrentVaultSession(expectedSession))
            {
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                cancellationToken = ReplaceSession(request.CancellationToken);
                oldSessionTeardownStarted = true;
                await DisposeVaultSessionAsync().ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested
                    || !IsCurrentReconfigureRequest(request))
                {
                    ResetVisibleState();
                    return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
                }

                ResetVisibleState();
                var cleared = CreateDailyNoteFileNameFormatState();
                if (!TryFinalizeVaultReconfigureRequest(request))
                {
                    ResetVisibleState();
                    return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
                }

                PublishDailyNoteFileNameFormatState(cleared);
                return VaultReconfigureResult.Success(cleared);
            }

            if (!IsExternalVaultSupported)
            {
                ErrorMessage = L10n.Get("FeedExternalVaultUnsupported");
                return VaultReconfigureResult.Failure(ErrorMessage);
            }

            // Loading/validating the portable setting (and the revision-checked write for Apply)
            // happens while the current session is still live. A corrupt external sidecar or an
            // optimistic-write conflict must not disconnect the user from the last known-good
            // daily layout.
            var ownWrites = watchRuntime?.OwnWrites ?? new OwnWriteRegistry();
            var preflightVault = vaultFactory(rootPath, ownWrites);
            settingsVaultForRollback = preflightVault;
            var preflightSettingsStore = new DailyNoteSettingsStore(preflightVault);
            var preflightSettings = await preflightSettingsStore.LoadAsync(request.CancellationToken)
                .ConfigureAwait(true);
            if (!IsCurrentReconfigureRequest(request)
                || !IsCurrentVaultSession(expectedSession))
            {
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            if (requestedNaming is not null)
            {
                settingsBeforeApply = preflightSettings;
                preflightSettings = await preflightSettingsStore.SaveAsync(
                        preflightSettings.Settings with
                        {
                            DailyFileNameFormat = requestedNaming.FileNameFormat
                        },
                        expectedDailyNoteSettingsRevision,
                        request.CancellationToken)
                    .ConfigureAwait(true);
                persistedApplySettings = preflightSettings;
                if (!IsCurrentReconfigureRequest(request)
                    || !IsCurrentVaultSession(expectedSession))
                {
                    ApplyRestoredDailyNoteSettingsIfCurrent(
                        await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                                settingsVaultForRollback,
                                settingsBeforeApply,
                                persistedApplySettings)
                            .ConfigureAwait(true),
                        expectedSession);
                    return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
                }
            }

            // Prepare the complete replacement while the current session remains usable. This
            // keeps a valid timeline connected if the new layout cannot be read or indexed.
            IsBusy = true;
            var currentAttachedVaultId = attachedVaultId;
            var currentAttachedVaultRootPath = attachedVaultRootPath;
            Func<DailyNoteSettingsSnapshot, Task<FeedLoadResult>> prepareCandidateAsync = candidateSettings =>
                Task.Run(async () =>
            {
                var nextVault = vaultFactory(rootPath, ownWrites);
                FeedVaultWatchRuntime? nextRuntime = null;
                FeedRuntimeSessionBinding? runtimeSession = null;
                string? nextVaultId = null;
                var vaultRootAttached = false;
                VaultRootHandoffLease? candidateRootHandoff = null;
                try
                {
                    var parser = new MarkdownDocumentParser();
                    var identity = await new VaultIdentityService(nextVault).GetOrCreateAsync(request.CancellationToken)
                        .ConfigureAwait(false);
                    nextVaultId = identity.VaultId;
                    var requiresVaultRootHandoff =
                        string.Equals(currentAttachedVaultId, nextVaultId, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(currentAttachedVaultRootPath) &&
                        !AreEquivalentVaultRoots(currentAttachedVaultRootPath, nextVault.RootPath);
                    if (requiresVaultRootHandoff)
                    {
                        // Keep A registered and freeze joins while B is prepared. The watcher for
                        // B may buffer bootstrap-window changes, but it cannot route them until
                        // this lease retires A and binds the new Feed session.
                        candidateRootHandoff = vaultRootRegistry.BeginHandoff(
                            nextVaultId,
                            currentAttachedVaultRootPath!,
                            nextVault.RootPath);
                        RegisterPendingVaultRootHandoff(candidateRootHandoff);
                    }
                    else
                    {
                        // Normal candidates reserve their root while they are prepared.
                        vaultRootRegistry.Attach(nextVaultId, nextVault.RootPath);
                        vaultRootAttached = true;
                    }
                    var nextRevisionStore = revisionStoreFactory(identity.VaultId);
                    if (nextVault is FileNoteVault)
                    {
                        runtimeSession = new FeedRuntimeSessionBinding();
                        nextRuntime = new FeedVaultWatchRuntime(
                            identity.VaultId,
                            nextVault,
                            ownWrites,
                            new InMemoryDirtyDocumentRegistry(),
                            GetDefaultRecoveryRoot(identity.VaultId),
                            new FeedWatchRuntimeSink(this, runtimeSession.GetToken));
                        nextRuntime.Start();
                    }

                    try
                    {
                        // The candidate watcher is running before this read. It closes the gap
                        // after an Apply write: a sidecar update that wins before the candidate
                        // starts is read here, while every later update is buffered by the
                        // candidate runtime until its session is committed.
                        candidateSettings = await new DailyNoteSettingsStore(nextVault)
                            .LoadAsync(request.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch when (requestedNaming is not null)
                    {
                        // A concurrent writer may have replaced the just-saved sidecar with an
                        // invalid value. Do not roll the local value back over that writer; keep
                        // the last good session and surface the explicit retry affordance.
                        settingsVaultForRollback = null;
                        settingsBeforeApply = null;
                        persistedApplySettings = null;
                        isExternalChange = true;
                        throw;
                    }

                    var nextDailyNotes = new DailyNoteService(
                        nextVault,
                        parser,
                        new MarkdownMutationService(parser),
                        candidateSettings.Naming);

                    var snapshot = await BuildSnapshotAsync(
                        nextVault,
                        nextDailyNotes,
                        parser,
                        taskResolver,
                        ScheduleRefreshAfterMarkdownCommit,
                        nextRuntime,
                        () => !IsIdentityFrozen,
                        loadedDayCount,
                        request.CancellationToken).ConfigureAwait(false);
                    var bootstrapService = new FirstConnectBootstrapService(
                        nextVault,
                        parser,
                        candidateSettings.Naming);
                    var bootstrap = await bootstrapService.FindSafeCompleteAsync(identity.VaultId, request.CancellationToken)
                        .ConfigureAwait(false);
                    if (bootstrap is null)
                    {
                        var bootstrapPaths = await nextDailyNotes.ListDayPathsAsync(request.CancellationToken)
                            .ConfigureAwait(false);
                        var bootstrapDocuments = new List<BootstrapStartDocument>(bootstrapPaths.Count);
                        foreach (var path in bootstrapPaths)
                        {
                            request.CancellationToken.ThrowIfCancellationRequested();
                            var document = await nextVault.ReadAsync(path.RelativePath, request.CancellationToken)
                                .ConfigureAwait(false);
                            if (document is not null)
                            {
                                bootstrapDocuments.Add(new BootstrapStartDocument(
                                    path.RelativePath,
                                    document.Revision,
                                    document.Text));
                            }
                        }

                        bootstrap = await bootstrapService.CreateOrResumeAsync(
                                identity.VaultId,
                                CreateBootstrapOperationId(
                                    identity.VaultId,
                                    reviewDeviceId,
                                    candidateSettings.Naming),
                                bootstrapDocuments,
                                request.CancellationToken)
                            .ConfigureAwait(false);
                        bootstrap = await bootstrapService.FindSafeCompleteAsync(identity.VaultId, request.CancellationToken)
                                .ConfigureAwait(false)
                            ?? bootstrap;
                    }

                    var state = new ReviewStateStore();
                    var coordinator = new FeedReviewSessionCoordinator(
                        identity.VaultId,
                        reviewDeviceId,
                        new PortableReviewEventStore(nextVault),
                        state);
                    await coordinator.InitializeAsync(request.CancellationToken).ConfigureAwait(false);
                    ApplyBootstrapBaseline(state, bootstrap, identity.VaultId);
                    return new FeedLoadResult(
                        nextVault,
                        nextDailyNotes,
                        candidateSettings,
                        parser,
                        identity.VaultId,
                        coordinator,
                        nextRevisionStore,
                        bootstrap,
                        snapshot,
                        nextRuntime,
                        runtimeSession,
                        vaultRootAttached,
                        candidateRootHandoff,
                        Auxiliary: null);
                }
                catch
                {
                    if (nextRuntime is not null)
                    {
                        try
                        {
                            await nextRuntime.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // Candidate cleanup must not strand its registry lease when a
                            // watcher is already faulting during startup.
                        }
                    }

                    if (vaultRootAttached && nextVaultId is not null)
                    {
                        vaultRootRegistry.Detach(nextVaultId, nextVault.RootPath);
                    }

                    ClearPendingVaultRootHandoff(candidateRootHandoff);
                    candidateRootHandoff?.Dispose();
                    throw;
                }
            }, request.CancellationToken);
            FeedLoadResult preparedLoad;
            for (var stabilizationAttempt = 0; ; stabilizationAttempt++)
            {
                var candidate = await prepareCandidateAsync(preflightSettings).ConfigureAwait(true);
                // Register the prepared candidate for outer cleanup before auxiliary view-model
                // construction: that stage may fail after a watcher and root attachment exist.
                loaded = candidate;
                var auxiliary = await PrepareAuxiliaryViewModelsAsync(
                        candidate.Vault,
                        candidate.DailyNotes.Naming,
                        request.CancellationToken)
                    .ConfigureAwait(true);
                candidate = candidate with { Auxiliary = auxiliary };
                if (!IsCurrentReconfigureRequest(request)
                    || !IsCurrentVaultSession(expectedSession))
                {
                    await DisposePreparedLoadAsync(candidate).ConfigureAwait(true);
                    loaded = null;
                    ApplyRestoredDailyNoteSettingsIfCurrent(
                        await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                                settingsVaultForRollback,
                                settingsBeforeApply,
                                persistedApplySettings)
                            .ConfigureAwait(true),
                        expectedSession);
                    return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
                }

                DailyNoteSettingsSnapshot committedSettings;
                try
                {
                    committedSettings = await new DailyNoteSettingsStore(candidate.Vault)
                        .LoadAsync(request.CancellationToken)
                        .ConfigureAwait(true);
                }
                catch when (requestedNaming is not null)
                {
                    await DisposePreparedLoadAsync(candidate).ConfigureAwait(true);
                    loaded = null;
                    settingsVaultForRollback = null;
                    settingsBeforeApply = null;
                    persistedApplySettings = null;
                    isExternalChange = true;
                    throw;
                }

                if (!DailyNoteSettingsSnapshotsMatch(candidate.DailyNoteSettings, preflightSettings))
                {
                    // The sidecar changed after the local Apply write but before the candidate
                    // began using it. The durable external value wins; never roll it back.
                    settingsVaultForRollback = null;
                    settingsBeforeApply = null;
                    persistedApplySettings = null;
                    isExternalChange = true;
                }

                if (DailyNoteSettingsSnapshotsMatch(candidate.DailyNoteSettings, committedSettings))
                {
                    preparedLoad = candidate;
                    loaded = candidate;
                    break;
                }

                await DisposePreparedLoadAsync(candidate).ConfigureAwait(true);
                loaded = null;
                settingsVaultForRollback = null;
                settingsBeforeApply = null;
                persistedApplySettings = null;
                isExternalChange = true;
                if (stabilizationAttempt >= 2)
                {
                    throw new InvalidDataException(
                        "The daily note filename setting changed repeatedly while the Feed session was being prepared.");
                }

                // Rebuild the candidate from the exact durable revision. A new candidate starts
                // its watcher before it reads the sidecar, closing the next observation window.
                preflightSettings = committedSettings;
            }

            // A root replacement can be requested while this session waits behind the operation
            // gate. Bind the watcher only after the request is still current, then switch the
            // in-memory session as one short, serialized commit.
            rootHandoff = preparedLoad.RootHandoff;
            cancellationToken = ReplaceSession(request.CancellationToken);
            loaded.RuntimeSession?.Bind(cancellationToken);
            oldSessionTeardownStarted = true;
            await DisposeVaultSessionAsync(rootHandoff).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested
                || !TryTransferPreparedVaultSessionOwnership(request, loaded, rootHandoff))
            {
                // The old session has already been detached, but this candidate no longer owns
                // the root request. Dispose its reservation/runtime before releasing the gate,
                // then revision-check the Apply rollback so a newer external sidecar still wins.
                await DisposePreparedLoadAsync(loaded).ConfigureAwait(true);
                loaded = null;
                rootHandoff = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            candidateOwnershipTransferred = true;
            rootHandoff = null;
            if (isDisposed || cancellationToken.IsCancellationRequested)
            {
                await DisposeTransferredCandidateAsync(loaded).ConfigureAwait(true);
                loaded = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            ResetVisibleState();
            if (isDisposed || cancellationToken.IsCancellationRequested)
            {
                await DisposeTransferredCandidateAsync(loaded).ConfigureAwait(true);
                loaded = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            IsBusy = true;
            vault = loaded.Vault;
            dailyNotes = loaded.DailyNotes;
            dailyNoteNaming = loaded.DailyNotes.Naming;
            dailyNoteSettingsSnapshot = loaded.DailyNoteSettings;
            markdownParser = loaded.Parser;
            vaultId = loaded.VaultId;
            reviewCoordinator = loaded.ReviewCoordinator;
            revisionStore = loaded.RevisionStore;
            searchIndex = loaded.Snapshot.SearchIndex;
            VaultRootPath = loaded.Vault.RootPath;
            BootstrapIndexedFiles = loaded.Bootstrap.IndexedFiles;
            BootstrapPendingCheckboxes = loaded.Bootstrap.PendingCheckboxes;
            BootstrapWasReused = loaded.Bootstrap.ReusedExisting;
            IsVaultInitialized = true;
            ApplySnapshot(loaded.Snapshot);
            RefreshIdentitySafePending();
            if (isDisposed || cancellationToken.IsCancellationRequested)
            {
                await DisposeTransferredCandidateAsync(loaded).ConfigureAwait(true);
                loaded = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            InstallPreparedAuxiliaryViewModels(loaded.Auxiliary
                ?? throw new InvalidOperationException("The candidate Feed auxiliary view models are missing."));
            visibleCandidateInstalled = true;
            if (!await CompleteCommittedVaultSessionAsync(cancellationToken).ConfigureAwait(true)
                || cancellationToken.IsCancellationRequested
                || !IsCurrentReconfigureRequest(request))
            {
                await DisposeTransferredCandidateAsync(loaded, auxiliaryInstalled: true).ConfigureAwait(true);
                loaded = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            var applied = CreateDailyNoteFileNameFormatState(isExternalChange: isExternalChange);
            if (!TryFinalizeVaultReconfigureRequest(request))
            {
                await DisposeTransferredCandidateAsync(loaded, auxiliaryInstalled: true).ConfigureAwait(true);
                loaded = null;
                await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                        settingsVaultForRollback,
                        settingsBeforeApply,
                        persistedApplySettings)
                    .ConfigureAwait(true);
                ResetVisibleState();
                return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
            }

            PublishDailyNoteFileNameFormatState(applied);
            return VaultReconfigureResult.Success(applied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
                                                 || request.CancellationToken.IsCancellationRequested)
        {
            ClearPendingVaultRootHandoff(rootHandoff);
            rootHandoff?.Dispose();
            if (loaded is not null && !candidateOwnershipTransferred)
            {
                await DisposePreparedLoadAsync(loaded).ConfigureAwait(true);
                loaded = null;
            }
            else if (loaded is not null && !visibleCandidateInstalled)
            {
                await DisposeTransferredCandidateAsync(loaded, auxiliaryInstalled: false).ConfigureAwait(true);
                loaded = null;
            }

            if (!visibleCandidateInstalled)
            {
                ApplyRestoredDailyNoteSettingsIfCurrent(
                    await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                            settingsVaultForRollback,
                            settingsBeforeApply,
                            persistedApplySettings)
                        .ConfigureAwait(true),
                        expectedSession);
            }

            if (oldSessionTeardownStarted && !visibleCandidateInstalled)
            {
                ResetVisibleState();
            }

            return VaultReconfigureResult.Cancelled(CreateDailyNoteFileNameFormatState());
        }
        catch (Exception exception)
        {
            ClearPendingVaultRootHandoff(rootHandoff);
            rootHandoff?.Dispose();
            if (loaded is not null && !candidateOwnershipTransferred)
            {
                await DisposePreparedLoadAsync(loaded).ConfigureAwait(true);
                loaded = null;
            }
            else if (loaded is not null && !visibleCandidateInstalled)
            {
                await DisposeTransferredCandidateAsync(loaded, auxiliaryInstalled: false).ConfigureAwait(true);
                loaded = null;
            }

            if (!visibleCandidateInstalled)
            {
                ApplyRestoredDailyNoteSettingsIfCurrent(
                    await RestoreDailyNoteSettingsAfterFailedApplyAsync(
                            settingsVaultForRollback,
                            settingsBeforeApply,
                            persistedApplySettings)
                        .ConfigureAwait(true),
                        expectedSession);
            }

            if (oldSessionTeardownStarted && !visibleCandidateInstalled)
            {
                ResetVisibleState();
            }

            ErrorMessage = exception.Message;
            return VaultReconfigureResult.Failure(
                exception.Message,
                CreateDailyNoteFileNameFormatState(
                    exception.Message,
                    isExternalChange: isExternalChange,
                    requiresReload: isExternalChange));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }

            if (gateAcquired)
            {
                operationGate.Release();
            }
        }
    }

    public Task CaptureAsync()
    {
        ThrowIfDisposed();
        return CaptureCoreAsync();
    }

    public Task RefreshAsync()
    {
        ThrowIfDisposed();
        return RefreshCoreAsync();
    }

    private async Task LoadOlderDaysCoreAsync()
    {
        if (!IsVaultInitialized || IsBusy || !HasMoreDays)
        {
            return;
        }

        loadedDayCount = Math.Min(TotalDayCount, loadedDayCount + DayPageSize);
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    public void OpenTaskReference(string taskId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        NavigateToTask(TaskResolver?.Invoke(taskId));
    }

    public async Task HandleBrokenTaskReferenceAsync(
        MarkdownLivePreviewEditorViewModel editor,
        int blockIndex,
        string taskId,
        FeedBrokenTaskReferenceAction action)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var target = $"unlimotion://task/{taskId}";
        var block = editor.Blocks.FirstOrDefault(candidate => candidate.Index == blockIndex);
        var reference = block?.ResolveTaskReference(target);
        if (block is null || reference is null || !reference.IsBroken)
        {
            return;
        }

        ErrorMessage = null;
        switch (action)
        {
            case FeedBrokenTaskReferenceAction.Find:
                SelectedSearchType = SearchTypeOptions.First(option =>
                    option.Type == FeedSearchDocumentType.Task);
                SearchQuery = reference.FallbackTitle;
                break;
            case FeedBrokenTaskReferenceAction.Unlink:
            {
                var replacement = TaskLinkRegex.Replace(
                    block.Block.Raw,
                    match => string.Equals(match.Groups["id"].Value, taskId, StringComparison.Ordinal)
                        ? reference.FallbackTitle
                        : match.Value);
                await CommitBrokenReferenceReplacementAsync(
                        editor,
                        blockIndex,
                        replacement.TrimEnd('\r', '\n'),
                        GetSessionToken())
                    .ConfigureAwait(true);
                break;
            }
            case FeedBrokenTaskReferenceAction.RestoreRevision:
                await RestoreBrokenTaskReferenceFromRevisionAsync(
                        editor,
                        blockIndex,
                        taskId,
                        reference.FallbackTitle,
                        GetSessionToken())
                    .ConfigureAwait(true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private async Task RestoreBrokenTaskReferenceFromRevisionAsync(
        MarkdownLivePreviewEditorViewModel editor,
        int blockIndex,
        string taskId,
        string fallbackTitle,
        CancellationToken cancellationToken)
    {
        if (revisionStore is null || vaultId is null || editor.Snapshot is not { } snapshot)
        {
            ErrorMessage = L10n.Get("FeedBrokenTaskRevisionUnavailable");
            return;
        }

        var taskTarget = $"unlimotion://task/{taskId}";
        var revisionPaths = await revisionStore.ListAsync(
                vaultId,
                snapshot.RelativePath,
                cancellationToken)
            .ConfigureAwait(true);
        foreach (var revisionPath in revisionPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(revisionPath))
            {
                continue;
            }

            string raw;
            try
            {
                raw = await File.ReadAllTextAsync(revisionPath, cancellationToken).ConfigureAwait(true);
            }
            catch (IOException)
            {
                continue;
            }

            var candidates = (markdownParser ?? new MarkdownDocumentParser())
                .Parse(raw)
                .Blocks
                .Where(candidate => candidate.IsContent
                    && !candidate.Raw.Contains(taskTarget, StringComparison.Ordinal)
                    && candidate.Raw.Contains(fallbackTitle, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(candidate => candidate.Index == blockIndex ? 0 : 1)
                .ThenBy(candidate => Math.Abs(candidate.Index - blockIndex))
                .ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            if (await CommitBrokenReferenceReplacementAsync(
                    editor,
                    blockIndex,
                    candidates[0].Raw.TrimEnd('\r', '\n'),
                    cancellationToken)
                .ConfigureAwait(true))
            {
                return;
            }
        }

        ErrorMessage = L10n.Get("FeedBrokenTaskRevisionUnavailable");
    }

    private async Task<bool> CommitBrokenReferenceReplacementAsync(
        MarkdownLivePreviewEditorViewModel editor,
        int blockIndex,
        string replacement,
        CancellationToken cancellationToken)
    {
        if (editor.ActiveBlock is not null
            && editor.ActiveBlock.Index != blockIndex
            && !await editor.CommitActiveAsync(cancellationToken).ConfigureAwait(true))
        {
            ErrorMessage = editor.ActiveBlock?.ErrorMessage ?? L10n.Get("FeedBrokenTaskActionFailed");
            return false;
        }

        var block = editor.Blocks.FirstOrDefault(candidate => candidate.Index == blockIndex);
        if (block is null || !editor.BeginEdit(block))
        {
            ErrorMessage = L10n.Get("FeedBrokenTaskActionFailed");
            return false;
        }

        block.EditorText = replacement;
        if (await editor.CommitActiveAsync(cancellationToken).ConfigureAwait(true))
        {
            return true;
        }

        ErrorMessage = block.ErrorMessage ?? L10n.Get("FeedBrokenTaskActionFailed");
        return false;
    }

    private async Task ChooseVaultCoreAsync()
    {
        if (!IsExternalVaultSupported)
        {
            ErrorMessage = L10n.Get("FeedExternalVaultUnsupported");
            return;
        }

        if (ChooseVaultAsync is null)
        {
            return;
        }

        try
        {
            ErrorMessage = null;
            await ChooseVaultAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task CaptureCoreAsync()
    {
        if (!CanCapture || dailyNotes is null || vault is null)
        {
            return;
        }

        var cancellationToken = GetSessionToken();
        var capture = QuickCaptureText;
        var area = SelectedArea?.Area;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                var today = EffectiveToday;
                var expectedRevision = Days.FirstOrDefault(day => day.Date == today)?.Revision;
                var sourceDailyNotes = dailyNotes;
                var sourceVault = vault;
                var snapshot = await Task.Run(async () =>
                {
                    await sourceDailyNotes.AppendCaptureAsync(
                            today,
                            capture,
                            area,
                            expectedRevision,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var parser = new MarkdownDocumentParser();
                    return await BuildSnapshotAsync(
                        sourceVault,
                        sourceDailyNotes,
                        parser,
                        taskResolver,
                        ScheduleRefreshAfterMarkdownCommit,
                        watchRuntime,
                        () => !IsIdentityFrozen,
                        loadedDayCount,
                        cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchNotificationAsync(() =>
                {
                    searchIndex = snapshot.SearchIndex;
                    QuickCaptureText = string.Empty;
                    ApplySnapshot(snapshot);
                }).ConfigureAwait(false);
                await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await DispatchNotificationAsync(() => IsBusy = false).ConfigureAwait(false);
            }
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (dailyNotes is null || vault is null || !IsVaultInitialized)
        {
            return;
        }

        var cancellationToken = GetSessionToken();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                var sourceDailyNotes = dailyNotes;
                var sourceVault = vault;
                var snapshot = await Task.Run(async () =>
                {
                    var parser = new MarkdownDocumentParser();
                    return await BuildSnapshotAsync(
                        sourceVault,
                        sourceDailyNotes,
                        parser,
                        taskResolver,
                        ScheduleRefreshAfterMarkdownCommit,
                        watchRuntime,
                        () => !IsIdentityFrozen,
                        loadedDayCount,
                        cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                searchIndex = snapshot.SearchIndex;
                ApplySnapshot(snapshot);
                await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private Task StartReviewCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (!IsVaultInitialized || !HasPendingReview || IsReviewActive || reviewCoordinator is null)
        {
            return;
        }

        try
        {
            await reviewCoordinator.OpenOrResumeAsync(cancellationToken);
            InvalidateReviewQueue();
        }
        catch (ForeignReviewSessionRequiresResolutionException exception)
        {
            ShowReviewRecovery(exception.Sessions[0]);
            return;
        }

        await ActivateOpenedReviewAsync(cancellationToken);
    });

    private async Task ActivateOpenedReviewAsync(CancellationToken cancellationToken)
    {
        if (reviewCoordinator is null)
        {
            return;
        }

        IsReviewActive = true;
        CreatedTaskReference = null;
        var snapshot = await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
        if (snapshot.Candidates.Count == 0)
        {
            await reviewCoordinator.CloseAsync(cancellationToken);
            InvalidateReviewQueue();
            ClearReviewSelection();
            await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        await SelectReviewCandidateAsync(snapshot.Candidates[0], cancellationToken);
    }

    private void ShowReviewRecovery(ForeignReviewSession session)
    {
        var coordinator = reviewCoordinator;
        if (coordinator is null)
        {
            return;
        }

        ReviewRecovery?.Dispose();
        var next = new FeedReviewRecoveryViewModel(coordinator, session);
        next.ResolvedCallbackAsync = takeOver => ExecuteReviewOperationAsync(async cancellationToken =>
        {
            if (takeOver)
            {
                InvalidateReviewQueue();
                await ActivateOpenedReviewAsync(cancellationToken);
            }
            else
            {
                InvalidateReviewQueue();
                ClearReviewSelection();
                await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            }
        });
        ReviewRecovery = next;
    }

    private Task FinishReviewCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (reviewCoordinator is not null)
        {
            await reviewCoordinator.CloseAsync(cancellationToken);
            InvalidateReviewQueue();
        }

        ClearReviewSelection();
        await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
    });

    private Task ContinueReviewCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        CreatedTaskReference = null;
        await AdvanceReviewAsync(cancellationToken);
    });

    private Task CompleteReviewDecisionAsync(ReviewDecision decision) => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (HasCreatedTask)
        {
            return;
        }

        var inputs = GetCurrentInputLocators();
        if (reviewCoordinator is null || inputs.Count == 0)
        {
            return;
        }

        currentReviewOperationId ??= Guid.NewGuid().ToString("N");
        await reviewCoordinator.ApplyDecisionAsync(
            inputs,
            decision,
            operationId: currentReviewOperationId,
            cancellationToken: cancellationToken);
        InvalidateReviewQueue();
        await AdvanceReviewAsync(cancellationToken);
    });

    private Task AssignReviewAreaCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (HasCreatedTask
            || vault is null
            || markdownParser is null
            || currentCandidate is null
            || currentReviewSelection is null
            || ReviewDestinationArea is null)
        {
            return;
        }

        var day = FindDay(currentCandidate.Locator.RelativePath);
        if (day is null)
        {
            throw new InvalidOperationException("The active review day is no longer available.");
        }

        var service = new FeedAreaAssignmentService(vault, markdownParser, new MarkdownMutationService(markdownParser));
        var result = await service.AssignAsync(
                new FeedAreaAssignmentRequest(
                    day.RelativePath,
                    day.Revision,
                    currentReviewSelection,
                    ReviewDestinationArea.Area),
                cancellationToken)
            ;
        await ReloadSnapshotAsync(cancellationToken);

        var updatedDay = FindDay(day.RelativePath)
            ?? throw new InvalidOperationException("The reassigned review day disappeared after refresh.");
        currentReviewDocument = markdownParser.Parse(updatedDay.Text);
        currentReviewSelection = result.OutputSelection;
        var anchorBlock = currentReviewSelection.Resolve(currentReviewDocument).First(static block => block.IsContent);
        currentReviewAnchorBlockIndex = anchorBlock.Index;
        var locator = result.OutputLocators.FirstOrDefault()
            ?? CreateLocator(updatedDay.RelativePath, currentReviewDocument, anchorBlock);
        currentCandidate = new FeedReviewCandidate(
            locator,
            anchorBlock,
            updatedDay.Date,
            FeedReviewPriority.Other,
            null);
        RebuildReviewTaskAreas(preserveExistingSelection: false);
        UpdateReviewSelectionViewModel();
    });

    private Task CreateTaskCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (HasCreatedTask
            || vault is null
            || markdownParser is null
            || reviewCoordinator is null
            || TaskCreationTarget is null
            || !TaskCreationTarget.SupportsClassification
            || currentCandidate is null
            || currentReviewSelection is null
            || vaultId is null)
        {
            return;
        }

        var day = FindDay(currentCandidate.Locator.RelativePath)
            ?? throw new InvalidOperationException("The active review day is no longer available.");
        var inputs = GetCurrentInputLocators();
        currentReviewOperationId ??= Guid.NewGuid().ToString("N");
        var selectedAreaIds = ReviewTaskAreas
            .Where(static area => area.IsSelected && area.Area.IsClassificationSelectable)
            .Select(static area => area.Area.StableAreaId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var taskJournal = taskJournalFactory(vaultId);
        var service = new FeedTaskConversionService(
            vault,
            markdownParser,
            new MarkdownMutationService(markdownParser),
            TaskCreationTarget,
            taskJournal,
            revisionStore);
        var result = await service.ConvertAsync(
                new FeedTaskConversionRequest(
                    vaultId,
                    currentReviewOperationId,
                    day.RelativePath,
                    day.Revision,
                    currentReviewSelection,
                    selectedAreaIds,
                    ReviewTaskIsGoal,
                    reviewCoordinator.CurrentSessionId),
                cancellationToken)
            ;
        await ReloadSnapshotAsync(cancellationToken);

        var updatedDay = FindDay(day.RelativePath)
            ?? throw new InvalidOperationException("The converted review day disappeared after refresh.");
        var output = FindOutputLocator(
            updatedDay,
            block => block.Raw.Contains($"(unlimotion://task/{result.TaskId})", StringComparison.Ordinal));
        await reviewCoordinator.ApplyDecisionAsync(
                inputs,
                ReviewDecision.Converted,
                [output],
                currentReviewOperationId,
                result.TaskId,
                cancellationToken)
            ;
        InvalidateReviewQueue();
        await service.MarkReviewAppliedAsync(vaultId, currentReviewOperationId, cancellationToken)
            .ConfigureAwait(true);

        var task = TaskResolver?.Invoke(result.TaskId);
        CreatedTaskReference = new FeedTaskReferenceViewModel(result.TaskId, result.Title, task);
        RebuildReviewTaskAreas(task);
        currentReviewDocument = markdownParser.Parse(updatedDay.Text);
        currentReviewSelection = new MarkdownBlockSelection(
            currentReviewDocument.Blocks.Single(block => block.ContentHash == output.ContentHash
                && block.Kind == output.BlockKind).Index,
            1);
        currentReviewAnchorBlockIndex = currentReviewSelection.StartBlockIndex;
        currentCandidate = new FeedReviewCandidate(
            output,
            currentReviewDocument.Blocks[currentReviewAnchorBlockIndex],
            updatedDay.Date,
            FeedReviewPriority.Other,
            null);
        UpdateReviewSelectionViewModel();
        await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
    });

    private Task CreateNoteCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (HasCreatedTask
            || vault is null
            || markdownParser is null
            || reviewCoordinator is null
            || currentCandidate is null
            || currentReviewSelection is null
            || vaultId is null
            || string.IsNullOrWhiteSpace(ReviewNoteTitle))
        {
            return;
        }

        var day = FindDay(currentCandidate.Locator.RelativePath)
            ?? throw new InvalidOperationException("The active review day is no longer available.");
        var inputs = GetCurrentInputLocators();
        currentReviewOperationId ??= Guid.NewGuid().ToString("N");
        var noteId = "note-" + currentReviewOperationId;
        var selectedAreaIds = ReviewTaskAreas
            .Where(static area => area.IsSelected && area.Area.IsClassificationSelectable)
            .Select(static area => area.Area.StableAreaId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var operationJournal = operationJournalFactory(vaultId);
        var service = new FeedNoteExtractionService(
            vault,
            markdownParser,
            new MarkdownMutationService(markdownParser),
            operationJournal,
            revisionStore);
        await service.ExtractAsync(
                new NoteExtractionRequest(
                    vaultId,
                    currentReviewOperationId,
                    day.RelativePath,
                    day.Revision,
                    currentReviewSelection,
                    ReviewNoteFolder,
                    ReviewNoteTitle,
                    noteId,
                    selectedAreaIds,
                    reviewCoordinator.CurrentSessionId),
                cancellationToken)
            ;
        await ReloadSnapshotAsync(cancellationToken);

        var updatedDay = FindDay(day.RelativePath)
            ?? throw new InvalidOperationException("The extracted review day disappeared after refresh.");
        var output = FindOutputLocator(
            updatedDay,
            block => block.Raw.Contains($"<!-- unlimotion-note:{noteId} -->", StringComparison.Ordinal));
        await reviewCoordinator.ApplyDecisionAsync(
                inputs,
                ReviewDecision.Converted,
                [output],
                currentReviewOperationId,
                noteId,
                cancellationToken)
            ;
        InvalidateReviewQueue();
        await service.MarkReviewAppliedAsync(vaultId, currentReviewOperationId, cancellationToken)
            .ConfigureAwait(true);
        await AdvanceReviewAsync(cancellationToken);
    });

    private Task MoveToTodayCoreAsync() => ExecuteReviewOperationAsync(async cancellationToken =>
    {
        if (HasCreatedTask
            || vault is null
            || markdownParser is null
            || reviewCoordinator is null
            || currentCandidate is null
            || currentReviewSelection is null
            || vaultId is null
            || currentCandidate.Day >= EffectiveToday
            || reviewCoordinator.CurrentSessionId is null)
        {
            return;
        }

        var sourceDay = FindDay(currentCandidate.Locator.RelativePath)
            ?? throw new InvalidOperationException("The active review day is no longer available.");
        var destinationDay = Days.FirstOrDefault(day => day.Date == EffectiveToday);
        var inputs = GetCurrentInputLocators();
        currentReviewOperationId ??= Guid.NewGuid().ToString("N");
        var destinationArea = ResolveCurrentReviewArea();
        var operationJournal = operationJournalFactory(vaultId);
        var service = new FeedMoveToTodayService(
            vault,
            markdownParser,
            new MarkdownMutationService(markdownParser),
            operationJournal,
            revisionStore,
            dailyNoteNaming);
        var result = await service.MoveAsync(
                new MoveToTodayRequest(
                    vaultId,
                    currentReviewOperationId,
                    sourceDay.RelativePath,
                    sourceDay.Revision,
                    currentReviewSelection,
                    EffectiveToday,
                    destinationArea,
                    destinationDay?.Revision,
                    reviewCoordinator.CurrentSessionId),
                cancellationToken)
            ;
        await ReloadSnapshotAsync(cancellationToken);

        var updatedSource = FindDay(sourceDay.RelativePath)
            ?? throw new InvalidOperationException("The move source disappeared after refresh.");
        var sourceOutput = FindOutputLocator(
            updatedSource,
            block => block.Raw.Contains($"#^{result.Anchor}", StringComparison.Ordinal));
        await reviewCoordinator.ApplyDecisionAsync(
                inputs,
                ReviewDecision.Moved,
                [sourceOutput],
                currentReviewOperationId,
                result.Anchor,
                cancellationToken)
            ;

        var updatedDestination = FindDay(result.DestinationPath)
            ?? throw new InvalidOperationException("The move destination disappeared after refresh.");
        var completedOperation = await operationJournal.LoadAsync(
                vaultId,
                currentReviewOperationId,
                cancellationToken)
            .ConfigureAwait(true);
        var destinationOutputs = completedOperation?.RecoveryDescriptor?.DestinationOutputLocators;
        if (destinationOutputs is null || destinationOutputs.Count == 0)
        {
            destinationOutputs =
            [
                FindOutputLocator(
                    updatedDestination,
                    block => block.Raw.Contains('^' + result.Anchor, StringComparison.Ordinal))
            ];
        }

        await reviewCoordinator.ApplyDecisionAsync(
                destinationOutputs,
                ReviewDecision.Deferred,
                operationId: currentReviewOperationId + "-destination",
                resultEntityId: result.Anchor,
                cancellationToken: cancellationToken)
            ;
        InvalidateReviewQueue();
        await operationJournal.MarkReviewAppliedAsync(vaultId, currentReviewOperationId, cancellationToken)
            .ConfigureAwait(true);
        await AdvanceReviewAsync(cancellationToken);
    });

    private async Task ExecuteReviewOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (isDisposed || IsBusy || IsIdentityFrozen)
        {
            return;
        }

        var cancellationToken = GetSessionToken();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private async Task AdvanceReviewAsync(CancellationToken cancellationToken)
    {
        currentReviewOperationId = null;
        CreatedTaskReference = null;
        var snapshot = await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
        if (snapshot.Candidates.Count == 0)
        {
            if (reviewCoordinator is not null)
            {
                await reviewCoordinator.CloseAsync(cancellationToken);
                InvalidateReviewQueue();
            }

            ClearReviewSelection();
            await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        await SelectReviewCandidateAsync(snapshot.Candidates[0], cancellationToken);
    }

    private async Task SelectReviewCandidateAsync(
        FeedReviewCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (markdownParser is null)
        {
            return;
        }

        await EnsureReviewDayLoadedAsync(candidate.Locator.RelativePath, cancellationToken)
            .ConfigureAwait(true);
        var day = FindDay(candidate.Locator.RelativePath)
            ?? throw new InvalidOperationException("The review candidate source is no longer available.");
        currentCandidate = candidate;
        currentReviewDocument = markdownParser.Parse(day.Text);
        var block = currentReviewDocument.Blocks.FirstOrDefault(value =>
                value.Index == candidate.Block.Index
                && value.Kind == candidate.Block.Kind
                && value.ContentHash == candidate.Block.ContentHash)
            ?? currentReviewDocument.Blocks.First(value =>
                value.Kind == candidate.Locator.BlockKind
                && value.ContentHash == candidate.Locator.ContentHash);
        currentReviewSelection = new MarkdownBlockSelection(block.Index, 1);
        currentReviewAnchorBlockIndex = block.Index;
        currentReviewOperationId = null;
        CreatedTaskReference = null;
        ReviewTaskIsGoal = false;
        ReviewNoteTitle = SuggestTitle(block.Raw);
        ReviewDestinationArea = ResolveDestinationAreaOption(candidate.Locator.AreaIdentity)
            ?? FeedAreaOptionViewModel.NoArea;
        ReviewNoteFolder = ReviewDestinationArea.Area is { Id: { } areaId }
            ? AreaManagement?.Areas
                    .FirstOrDefault(value => string.Equals(value.Id, areaId, StringComparison.Ordinal))
                    ?.DefaultNoteFolder
                ?? string.Empty
            : string.Empty;
        RebuildReviewTaskAreas(preserveExistingSelection: false);
        UpdateReviewSelectionViewModel();
        ReviewNavigationRequested?.Invoke(
            this,
            new FeedSearchNavigationRequestedEventArgs(
                day.RelativePath,
                day.MarkdownEditor,
                block.Index,
                day));
    }

    private void ExpandReviewSelection(bool up)
    {
        if (HasCreatedTask || currentReviewDocument is null || currentReviewSelection is null)
        {
            return;
        }

        var start = currentReviewSelection.StartBlockIndex;
        var count = currentReviewSelection.BlockCount;
        var candidateIndex = up ? start - 1 : start + count;
        if (candidateIndex < 0 || candidateIndex >= currentReviewDocument.Blocks.Count)
        {
            return;
        }

        var candidate = currentReviewDocument.Blocks[candidateIndex];
        if (candidate.Kind is MarkdownBlockKind.AreaHeading or MarkdownBlockKind.FrontMatter)
        {
            return;
        }

        currentReviewSelection = up
            ? new MarkdownBlockSelection(start - 1, count + 1)
            : new MarkdownBlockSelection(start, count + 1);
        UpdateReviewSelectionViewModel();
    }

    private void ShrinkReviewSelection(bool fromTop)
    {
        if (HasCreatedTask || currentReviewSelection is null || currentReviewSelection.BlockCount <= 1)
        {
            return;
        }

        var start = currentReviewSelection.StartBlockIndex;
        var end = start + currentReviewSelection.BlockCount - 1;
        if (fromTop && start < currentReviewAnchorBlockIndex)
        {
            currentReviewSelection = new MarkdownBlockSelection(start + 1, currentReviewSelection.BlockCount - 1);
        }
        else if (!fromTop && end > currentReviewAnchorBlockIndex)
        {
            currentReviewSelection = new MarkdownBlockSelection(start, currentReviewSelection.BlockCount - 1);
        }

        UpdateReviewSelectionViewModel();
    }

    private void UpdateReviewSelectionViewModel()
    {
        if (currentCandidate is null || currentReviewDocument is null || currentReviewSelection is null)
        {
            CurrentReview = null;
            return;
        }

        var selected = currentReviewSelection.Resolve(currentReviewDocument);
        var start = currentReviewSelection.StartBlockIndex;
        var end = start + currentReviewSelection.BlockCount - 1;
        var selectedMarkdown = string.Concat(selected.Select(static block => block.Raw))
            .TrimEnd('\r', '\n');
        CurrentReview = new FeedReviewSelectionViewModel(
            currentCandidate.Day,
            currentCandidate.Locator.RelativePath,
            selectedMarkdown,
            currentReviewSelection.BlockCount,
            start > 0 && currentReviewDocument.Blocks[start - 1].Kind is not MarkdownBlockKind.AreaHeading and not MarkdownBlockKind.FrontMatter,
            end + 1 < currentReviewDocument.Blocks.Count && currentReviewDocument.Blocks[end + 1].Kind is not MarkdownBlockKind.AreaHeading,
            start < currentReviewAnchorBlockIndex,
            end > currentReviewAnchorBlockIndex);
        ApplyReviewHighlight(
            currentCandidate.Locator.RelativePath,
            selected.Select(static block => block.Index),
            currentReviewAnchorBlockIndex,
            selectedMarkdown);
    }

    private IReadOnlyList<BlockLocator> GetCurrentInputLocators()
    {
        if (currentCandidate is null || currentReviewDocument is null || currentReviewSelection is null)
        {
            return [];
        }

        return FeedReviewQueue.CoveredLocators(
            currentCandidate.Locator.RelativePath,
            currentReviewDocument,
            currentReviewSelection);
    }

    private async Task<ReviewQueueSnapshot> RefreshReviewSummaryAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var snapshot = await GetReviewQueueSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var applied = false;
            await DispatchNotificationAsync(() =>
            {
                if (!IsReviewQueueSnapshotCurrent(snapshot))
                {
                    return;
                }

                PendingReviewBlocks = snapshot.Candidates.Count;
                PendingReviewDays = snapshot.PendingDays;
                applied = true;
            }).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (applied && IsReviewQueueSnapshotCurrent(snapshot))
            {
                return snapshot;
            }
        }
    }

    private async Task<ReviewQueueSnapshot> GetReviewQueueSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ReviewQueueBuildState? build;
            long version;
            lock (reviewQueueLock)
            {
                version = reviewQueueVersion;
                if (reviewQueueSnapshot.Version == version)
                {
                    return reviewQueueSnapshot;
                }

                build = reviewQueueBuild is { Version: var buildVersion } && buildVersion == version
                    ? reviewQueueBuild
                    : null;
            }

            if (build is null)
            {
                var request = await CaptureReviewQueueBuildRequestAsync(version, cancellationToken)
                    .ConfigureAwait(false);
                lock (reviewQueueLock)
                {
                    if (reviewQueueVersion != version)
                    {
                        continue;
                    }

                    if (reviewQueueSnapshot.Version == version)
                    {
                        return reviewQueueSnapshot;
                    }

                    build = reviewQueueBuild is { Version: var buildVersion } && buildVersion == version
                        ? reviewQueueBuild
                        : null;
                    if (build is null)
                    {
                        var buildCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetSessionToken());
                        var buildTask = Task.Run(
                            () => BuildReviewQueueSnapshotAsync(request, buildCancellation.Token),
                            CancellationToken.None);
                        build = new ReviewQueueBuildState(version, buildTask, buildCancellation);
                        reviewQueueBuild = build;
                    }
                }
            }

            ReviewQueueSnapshot built;
            try
            {
                built = await build.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                ClearReviewQueueBuild(build);
                continue;
            }
            catch
            {
                ClearReviewQueueBuild(build);
                throw;
            }

            var published = false;
            lock (reviewQueueLock)
            {
                if (ReferenceEquals(reviewQueueBuild, build))
                {
                    reviewQueueBuild = null;
                }

                if (reviewQueueVersion == built.Version)
                {
                    reviewQueueSnapshot = built;
                    published = true;
                }
            }

            build.Dispose();
            if (published)
            {
                return built;
            }
        }
    }

    private async Task<ReviewQueueBuildRequest> CaptureReviewQueueBuildRequestAsync(
        long version,
        CancellationToken cancellationToken)
    {
        ReviewQueueBuildRequest? request = null;
        await DispatchNotificationAsync(() =>
        {
            var coordinator = reviewCoordinator;
            if (coordinator is null)
            {
                request = ReviewQueueBuildRequest.Empty(version, ReviewQueueBuildGateAsync);
                return;
            }

            var state = new ReviewStateStore();
            foreach (var reviewEvent in coordinator.State.DecisionEvents.ToArray())
            {
                state.Add(reviewEvent);
            }

            foreach (var sessionEvent in coordinator.State.SessionEvents.ToArray())
            {
                state.Add(sessionEvent);
            }

            request = new ReviewQueueBuildRequest(
                version,
                Array.AsReadOnly(reviewDocuments.ToArray()),
                state,
                coordinator.CurrentObserver,
                dailyNoteNaming,
                ReviewQueueBuildGateAsync);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
        return request ?? ReviewQueueBuildRequest.Empty(version, ReviewQueueBuildGateAsync);
    }

    private static async Task<ReviewQueueSnapshot> BuildReviewQueueSnapshotAsync(
        ReviewQueueBuildRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.BuildGateAsync is not null)
        {
            await request.BuildGateAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = new FeedReviewQueue(
                new MarkdownDocumentParser(),
                request.State,
                request.Naming)
            .Build(
                request.Documents.Select(static document => (document.RelativePath, document.Text)),
                request.Observer)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new ReviewQueueSnapshot(
            request.Version,
            Array.AsReadOnly(candidates),
            candidates.Select(static candidate => candidate.Day).Distinct().Count());
    }

    private void InvalidateReviewQueue()
    {
        ReviewQueueBuildState? canceledBuild;
        lock (reviewQueueLock)
        {
            reviewQueueVersion++;
            reviewQueueSnapshot = ReviewQueueSnapshot.Empty;
            canceledBuild = reviewQueueBuild;
            reviewQueueBuild = null;
        }

        canceledBuild?.CancelAndDisposeWhenCompleted();
    }

    private void ClearReviewQueueBuild(ReviewQueueBuildState build)
    {
        lock (reviewQueueLock)
        {
            if (ReferenceEquals(reviewQueueBuild, build))
            {
                reviewQueueBuild = null;
            }
        }

        build.CancelAndDisposeWhenCompleted();
    }

    private bool IsReviewQueueSnapshotCurrent(ReviewQueueSnapshot snapshot)
    {
        lock (reviewQueueLock)
        {
            return snapshot.Version == reviewQueueVersion;
        }
    }

    private void ClearReviewSelection()
    {
        ClearReviewHighlight();
        IsReviewActive = false;
        CurrentReview = null;
        currentCandidate = null;
        currentReviewDocument = null;
        currentReviewSelection = null;
        currentReviewOperationId = null;
        CreatedTaskReference = null;
    }

    private void ApplyReviewHighlight(
        string relativePath,
        IEnumerable<int> selectedBlockIndices,
        int anchorBlockIndex,
        string selectedMarkdown)
    {
        var normalizedPath = NormalizePath(relativePath);
        foreach (var day in Days)
        {
            if (string.Equals(
                    NormalizePath(day.RelativePath),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                day.SetReviewTarget(selectedBlockIndices, anchorBlockIndex, selectedMarkdown);
                SelectedDay = day;
            }
            else
            {
                day.ClearReviewTarget();
            }
        }
    }

    private void ClearReviewHighlight()
    {
        foreach (var day in Days)
        {
            day.ClearReviewTarget();
        }
    }

    private async Task ReloadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (vault is null || dailyNotes is null || markdownParser is null)
        {
            return;
        }

        var sourceVault = vault;
        var sourceDailyNotes = dailyNotes;
        var parser = markdownParser;
        var snapshot = await Task.Run(
                () => BuildSnapshotAsync(
                    sourceVault,
                    sourceDailyNotes,
                    parser,
                    taskResolver,
                    ScheduleRefreshAfterMarkdownCommit,
                    watchRuntime,
                    () => !IsIdentityFrozen,
                    loadedDayCount,
                    cancellationToken),
                cancellationToken)
            ;
        searchIndex = snapshot.SearchIndex;
        ApplySnapshot(snapshot);
        await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
        ShowForeignReviewRecoveryIfNeeded();
    }

    private async Task<bool> RecoverPendingOperationsAsync(CancellationToken cancellationToken)
    {
        if (vault is null
            || markdownParser is null
            || reviewCoordinator is null
            || vaultId is null)
        {
            return false;
        }

        var operationJournal = operationJournalFactory(vaultId);
        var taskJournal = taskJournalFactory(vaultId);
        ClearPendingRecoveries();
        var pendingTasks = await taskJournal.ListPendingAsync(vaultId, cancellationToken)
            .ConfigureAwait(true);
        var pendingOperations = await operationJournal.ListPendingAsync(vaultId, cancellationToken)
            .ConfigureAwait(true);
        if (pendingTasks.Count == 0 && pendingOperations.Count == 0)
        {
            return false;
        }

        var failures = new List<string>();
        foreach (var pendingTask in pendingTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RecoverPendingTaskAsync(pendingTask, taskJournal, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add($"{pendingTask.OperationId}: {exception.Message}");
                AddPendingRecovery(new FeedPendingRecoveryViewModel(
                    pendingTask.OperationId,
                    FeedPendingRecoveryKind.TaskConversion,
                    pendingTask.SourcePath,
                    exception.Message,
                    pendingTask.State is FeedTaskConversionState.TaskCreated or FeedTaskConversionState.Completed,
                    FinishPendingRecoveryAsync,
                    KeepBothPendingRecoveryAsync));
            }
        }

        foreach (var pendingOperation in pendingOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RecoverPendingMarkdownOperationAsync(
                        pendingOperation,
                        operationJournal,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add($"{pendingOperation.OperationId}: {exception.Message}");
                AddPendingRecovery(new FeedPendingRecoveryViewModel(
                    pendingOperation.OperationId,
                    pendingOperation.Kind == FeedOperationKind.NoteExtraction
                        ? FeedPendingRecoveryKind.NoteExtraction
                        : FeedPendingRecoveryKind.MoveToToday,
                    pendingOperation.SourcePath,
                    exception.Message,
                    pendingOperation.State is FeedOperationState.DestinationCreated or FeedOperationState.Completed,
                    FinishPendingRecoveryAsync,
                    KeepBothPendingRecoveryAsync));
            }
        }

        if (failures.Count > 0)
        {
            ErrorMessage = string.Format(
                CultureInfo.CurrentCulture,
                L10n.Get("FeedPendingRecoveryFailed"),
                failures.Count,
                string.Join(Environment.NewLine, failures));
        }

        return true;
    }

    private Task FinishPendingRecoveryAsync(FeedPendingRecoveryViewModel item) =>
        ExecuteReviewOperationAsync(async cancellationToken =>
        {
            try
            {
                if (vaultId is null)
                {
                    throw new InvalidOperationException("Feed recovery is not initialized.");
                }

                if (item.Kind == FeedPendingRecoveryKind.TaskConversion)
                {
                    var journal = taskJournalFactory(vaultId);
                    var record = await journal.LoadAsync(vaultId, item.OperationId, cancellationToken)
                            .ConfigureAwait(true)
                        ?? throw new InvalidDataException("The task recovery journal no longer exists.");
                    await RecoverPendingTaskAsync(record, journal, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    var journal = operationJournalFactory(vaultId);
                    var record = await journal.LoadAsync(vaultId, item.OperationId, cancellationToken)
                            .ConfigureAwait(true)
                        ?? throw new InvalidDataException("The Markdown recovery journal no longer exists.");
                    await RecoverPendingMarkdownOperationAsync(record, journal, cancellationToken).ConfigureAwait(true);
                }

                RemovePendingRecovery(item);
                await ReloadSnapshotAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.Message = exception.Message;
                throw;
            }
        });

    private Task KeepBothPendingRecoveryAsync(FeedPendingRecoveryViewModel item) =>
        ExecuteReviewOperationAsync(async cancellationToken =>
        {
            try
            {
                if (vaultId is null || reviewCoordinator is null)
                {
                    throw new InvalidOperationException("Feed recovery is not initialized.");
                }

                if (item.Kind == FeedPendingRecoveryKind.TaskConversion)
                {
                    var journal = taskJournalFactory(vaultId);
                    await journal.ResolveKeepBothAsync(vaultId, item.OperationId, cancellationToken)
                        .ConfigureAwait(true);
                    var record = await journal.LoadAsync(vaultId, item.OperationId, cancellationToken)
                            .ConfigureAwait(true)
                        ?? throw new InvalidDataException("The task recovery journal no longer exists.");
                    var descriptor = record.RecoveryDescriptor
                        ?? throw new InvalidDataException("The task recovery descriptor is missing.");
                    await reviewCoordinator.ApplyRecoveredDecisionAsync(
                            RequireRecoveryReviewSessionId(descriptor.ReviewSessionId),
                            RequireRecoveryLocators(descriptor.InputLocators, "task inputs"),
                            ReviewDecision.Kept,
                            outputs: null,
                            record.OperationId + "-keep-both",
                            record.TaskId,
                            cancellationToken)
                        .ConfigureAwait(true);
                    await journal.MarkReviewAppliedAsync(vaultId, item.OperationId, cancellationToken)
                        .ConfigureAwait(true);
                }
                else
                {
                    var journal = operationJournalFactory(vaultId);
                    await journal.ResolveKeepBothAsync(vaultId, item.OperationId, cancellationToken)
                        .ConfigureAwait(true);
                    var record = await journal.LoadAsync(vaultId, item.OperationId, cancellationToken)
                            .ConfigureAwait(true)
                        ?? throw new InvalidDataException("The Markdown recovery journal no longer exists.");
                    var descriptor = record.RecoveryDescriptor
                        ?? throw new InvalidDataException("The Markdown recovery descriptor is missing.");
                    var reviewSessionId = RequireRecoveryReviewSessionId(descriptor.ReviewSessionId);
                    await reviewCoordinator.ApplyRecoveredDecisionAsync(
                            reviewSessionId,
                            RequireRecoveryLocators(descriptor.InputLocators, "operation inputs"),
                            ReviewDecision.Kept,
                            outputs: null,
                            record.OperationId + "-keep-both",
                            record.ResultId,
                            cancellationToken)
                        .ConfigureAwait(true);
                    if (record.Kind == FeedOperationKind.MoveToToday)
                    {
                        await reviewCoordinator.ApplyRecoveredDecisionAsync(
                                reviewSessionId,
                                RequireRecoveryLocators(
                                    descriptor.DestinationOutputLocators,
                                    "move destination output"),
                                ReviewDecision.Deferred,
                                outputs: null,
                                record.OperationId + "-destination",
                                record.ResultId,
                                cancellationToken)
                            .ConfigureAwait(true);
                    }

                    await journal.MarkReviewAppliedAsync(vaultId, item.OperationId, cancellationToken)
                        .ConfigureAwait(true);
                }

                RemovePendingRecovery(item);
                await ReloadSnapshotAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.Message = exception.Message;
                throw;
            }
        });

    private void AddPendingRecovery(FeedPendingRecoveryViewModel item)
    {
        var existing = PendingRecoveries.FirstOrDefault(value =>
            value.Kind == item.Kind
            && string.Equals(value.OperationId, item.OperationId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Message = item.Message;
            existing.CanKeepBoth = item.CanKeepBoth;
            item.Dispose();
        }
        else
        {
            PendingRecoveries.Add(item);
        }

        HasPendingRecoveries = PendingRecoveries.Count > 0;
    }

    private void RemovePendingRecovery(FeedPendingRecoveryViewModel item)
    {
        if (PendingRecoveries.Remove(item))
        {
            item.Dispose();
        }

        HasPendingRecoveries = PendingRecoveries.Count > 0;
    }

    private void ClearPendingRecoveries()
    {
        foreach (var recovery in PendingRecoveries)
        {
            recovery.Dispose();
        }

        PendingRecoveries.Clear();
        HasPendingRecoveries = false;
    }

    private async Task RecoverPendingTaskAsync(
        FeedTaskConversionRecord pending,
        IFeedTaskConversionJournal journal,
        CancellationToken cancellationToken)
    {
        if (vault is null || markdownParser is null || reviewCoordinator is null || vaultId is null)
        {
            throw new InvalidOperationException("Feed recovery is not initialized.");
        }

        if (pending.SchemaVersion != 2 || pending.RecoveryDescriptor is null)
        {
            throw new InvalidDataException("The task conversion journal requires explicit legacy recovery.");
        }

        var target = TaskCreationTarget;
        if (target?.SupportsClassification != true)
        {
            if (pending.State != FeedTaskConversionState.Completed)
            {
                throw new InvalidOperationException("Task storage cannot safely resume the pending conversion.");
            }

            target = RecoveryOnlyTaskCreationTarget.Instance;
        }

        var service = new FeedTaskConversionService(
            vault,
            markdownParser,
            new MarkdownMutationService(markdownParser),
            target,
            journal,
            revisionStore);
        await service.ResumeAsync(pending, cancellationToken).ConfigureAwait(true);
        pending = await journal.LoadAsync(vaultId, pending.OperationId, cancellationToken)
                .ConfigureAwait(true)
            ?? throw new InvalidDataException("The resumed task conversion journal disappeared.");

        var descriptor = pending.RecoveryDescriptor
            ?? throw new InvalidDataException("The task conversion journal lost its recovery descriptor.");
        var inputs = RequireRecoveryLocators(descriptor.InputLocators, "task inputs");
        var outputs = RequireRecoveryLocators(descriptor.SourceOutputLocators, "task output");
        await reviewCoordinator.ApplyRecoveredDecisionAsync(
                RequireRecoveryReviewSessionId(descriptor.ReviewSessionId),
                inputs,
                ReviewDecision.Converted,
                outputs,
                pending.OperationId,
                pending.TaskId,
                cancellationToken)
            .ConfigureAwait(true);

        await journal.MarkReviewAppliedAsync(vaultId, pending.OperationId, cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task RecoverPendingMarkdownOperationAsync(
        FeedOperationRecord pending,
        IFeedOperationJournal journal,
        CancellationToken cancellationToken)
    {
        if (vault is null || markdownParser is null || reviewCoordinator is null || vaultId is null)
        {
            throw new InvalidOperationException("Feed recovery is not initialized.");
        }

        if (pending.SchemaVersion != 2 || pending.RecoveryDescriptor is null)
        {
            throw new InvalidDataException("The Markdown operation journal requires explicit legacy recovery.");
        }

        switch (pending.Kind)
        {
            case FeedOperationKind.NoteExtraction:
                await new FeedNoteExtractionService(
                        vault,
                        markdownParser,
                        new MarkdownMutationService(markdownParser),
                        journal,
                        revisionStore)
                    .ResumeAsync(pending, cancellationToken)
                    .ConfigureAwait(true);
                break;
            case FeedOperationKind.MoveToToday:
                await new FeedMoveToTodayService(
                        vault,
                        markdownParser,
                        new MarkdownMutationService(markdownParser),
                        journal,
                        revisionStore,
                        dailyNoteNaming)
                    .ResumeAsync(pending, cancellationToken)
                    .ConfigureAwait(true);
                break;
            default:
                throw new InvalidDataException("The pending Markdown operation kind is unsupported.");
        }

        pending = await journal.LoadAsync(vaultId, pending.OperationId, cancellationToken)
                .ConfigureAwait(true)
            ?? throw new InvalidDataException("The resumed Markdown operation journal disappeared.");

        var descriptor = pending.RecoveryDescriptor
            ?? throw new InvalidDataException("The Markdown operation journal lost its recovery descriptor.");
        var inputs = RequireRecoveryLocators(descriptor.InputLocators, "operation inputs");
        var sourceOutputs = RequireRecoveryLocators(descriptor.SourceOutputLocators, "source output");

        switch (pending.Kind)
        {
            case FeedOperationKind.NoteExtraction:
                await reviewCoordinator.ApplyRecoveredDecisionAsync(
                        RequireRecoveryReviewSessionId(descriptor.ReviewSessionId),
                        inputs,
                        ReviewDecision.Converted,
                        sourceOutputs,
                        pending.OperationId,
                        pending.ResultId,
                        cancellationToken)
                    .ConfigureAwait(true);

                break;
            case FeedOperationKind.MoveToToday:
                var destinationOutputs = RequireRecoveryLocators(
                    descriptor.DestinationOutputLocators,
                    "move destination output");
                var destinationOperationId = pending.OperationId + "-destination";
                var reviewSessionId = RequireRecoveryReviewSessionId(descriptor.ReviewSessionId);
                await reviewCoordinator.ApplyRecoveredDecisionAsync(
                        reviewSessionId,
                        inputs,
                        ReviewDecision.Moved,
                        sourceOutputs,
                        pending.OperationId,
                        pending.ResultId,
                        cancellationToken)
                    .ConfigureAwait(true);
                await reviewCoordinator.ApplyRecoveredDecisionAsync(
                        reviewSessionId,
                        destinationOutputs,
                        ReviewDecision.Deferred,
                        outputs: null,
                        destinationOperationId,
                        pending.ResultId,
                        cancellationToken)
                    .ConfigureAwait(true);

                break;
            default:
                throw new InvalidDataException("The pending Markdown operation kind is unsupported.");
        }

        await journal.MarkReviewAppliedAsync(vaultId, pending.OperationId, cancellationToken)
            .ConfigureAwait(true);
    }

    private static string RequireRecoveryReviewSessionId(string? reviewSessionId)
    {
        if (string.IsNullOrWhiteSpace(reviewSessionId))
        {
            throw new InvalidDataException("The pending operation is not tied to a review session.");
        }

        return reviewSessionId;
    }

    private static IReadOnlyList<BlockLocator> RequireRecoveryLocators(
        IReadOnlyList<BlockLocator>? locators,
        string role)
    {
        if (locators is not { Count: > 0 })
        {
            throw new InvalidDataException($"The pending operation does not contain {role} locators.");
        }

        return locators;
    }

    private void ShowForeignReviewRecoveryIfNeeded()
    {
        if (IsReviewActive || reviewCoordinator?.CurrentSessionId is not null)
        {
            return;
        }

        var foreign = reviewCoordinator?.GetForeignOpenSessions().FirstOrDefault();
        if (foreign is not null)
        {
            ShowReviewRecovery(foreign);
        }
    }

    private BlockLocator FindOutputLocator(FeedDayViewModel day, Func<MarkdownBlock, bool> predicate)
    {
        if (markdownParser is null)
        {
            throw new InvalidOperationException("Markdown parser is unavailable.");
        }

        var document = markdownParser.Parse(day.Text);
        var block = document.Blocks.FirstOrDefault(value => value.IsContent && predicate(value))
            ?? throw new InvalidDataException("The operation output link could not be resolved in the daily note.");
        return CreateLocator(day.RelativePath, document, block);
    }

    private static BlockLocator CreateLocator(string relativePath, MarkdownDocument document, MarkdownBlock target)
    {
        var occurrence = 0;
        foreach (var block in document.Blocks.Where(static value => value.IsContent))
        {
            if (block.Index == target.Index)
            {
                break;
            }

            if (block.Kind == target.Kind
                && block.ContentHash == target.ContentHash
                && string.Equals(block.AreaId ?? block.AreaName, target.AreaId ?? target.AreaName, StringComparison.Ordinal))
            {
                occurrence++;
            }
        }

        return new BlockLocator(
            relativePath,
            target.AreaId ?? target.AreaName,
            target.Kind,
            target.ContentHash,
            occurrence);
    }

    private FeedDayViewModel? FindDay(string relativePath) => Days.FirstOrDefault(day =>
        string.Equals(NormalizePath(day.RelativePath), NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase));

    private AreaReference? ResolveCurrentReviewArea()
    {
        return ResolveDestinationAreaOption(currentCandidate?.Locator.AreaIdentity)?.Area;
    }

    private FeedAreaOptionViewModel? ResolveDestinationAreaOption(string? identityOrName)
    {
        if (string.IsNullOrWhiteSpace(identityOrName))
        {
            return null;
        }

        var identityMatch = Areas.FirstOrDefault(area =>
            area.HasStableAreaId
            && string.Equals(area.StableAreaId, identityOrName, StringComparison.Ordinal));
        if (identityMatch is not null)
        {
            return identityMatch;
        }

        var headingMatches = Areas.Where(area =>
                area.IsExistingHeadingDestination
                && string.Equals(area.DisplayName, identityOrName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (headingMatches.Length == 1)
        {
            return headingMatches[0];
        }

        return ResolveUniqueClassificationAreaOption(identityOrName);
    }

    private FeedAreaOptionViewModel? ResolveUniqueClassificationAreaOption(string? identityOrName)
    {
        if (string.IsNullOrWhiteSpace(identityOrName))
        {
            return null;
        }

        var identityMatch = Areas.FirstOrDefault(area =>
            area.IsClassificationSelectable
            && string.Equals(area.StableAreaId, identityOrName, StringComparison.Ordinal));
        if (identityMatch is not null)
        {
            return identityMatch;
        }

        var nameMatches = Areas.Where(area =>
                area.IsClassificationSelectable
                && string.Equals(area.DisplayName, identityOrName, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(static area => area.StableAreaId, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return nameMatches.Length == 1 ? nameMatches[0] : null;
    }

    private void RebuildReviewTaskAreas(
        TaskItemViewModel? task = null,
        bool preserveExistingSelection = true)
    {
        var selected = task is null && preserveExistingSelection
            ? ReviewTaskAreas.Where(static value => value.IsSelected)
                .Select(static value => value.Area.StableAreaId!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var primary = ResolveUniqueClassificationAreaOption(currentCandidate?.Locator.AreaIdentity)?.StableAreaId;
        ReviewTaskAreas.Clear();
        foreach (var area in Areas.Where(static value => value.IsClassificationSelectable))
        {
            var stableAreaId = area.StableAreaId!;
            var isSelected = task?.AreaIds.Contains(stableAreaId) == true
                || selected.Contains(stableAreaId)
                || string.Equals(stableAreaId, primary, StringComparison.Ordinal);
            ReviewTaskAreas.Add(new FeedTaskAreaOptionViewModel(
                area,
                isSelected,
                task is null
                    ? null
                    : (_, nextSelected) =>
                    {
                        if (nextSelected && !task.AreaIds.Contains(stableAreaId))
                        {
                            task.AreaIds.Add(stableAreaId);
                        }
                        else if (!nextSelected)
                        {
                            task.AreaIds.Remove(stableAreaId);
                        }
                    }));
        }
    }

    private void NavigateToTask(TaskItemViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        if (NavigateToTaskRequested is not null)
        {
            NavigateToTaskRequested(task);
            return;
        }

        if (TaskOwner is not null)
        {
            TaskOwner.CurrentTaskItem = task;
            TaskOwner.DetailsAreOpen = true;
            TaskOwner.SelectedWorkspaceMode = WorkspaceMode.Tasks;
        }
    }

    private void RefreshTaskReferences()
    {
        foreach (var day in Days)
        {
            day.ReplaceTaskReferences(ExtractTaskReferences(day.Text, taskResolver));
        }

        if (CreatedTaskReference is not null)
        {
            CreatedTaskReference = new FeedTaskReferenceViewModel(
                CreatedTaskReference.TaskId,
                CreatedTaskReference.FallbackTitle,
                taskResolver?.Invoke(CreatedTaskReference.TaskId));
        }
    }

    private static string SuggestTitle(string raw)
    {
        var line = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? L10n.Get("FeedReviewUntitledNote");
        return Regex.Replace(line, @"^\s*(?:[-*+]\s+)?(?:\[[ xX]\]\s*)?", string.Empty).Trim();
    }

    private static async Task<FeedSnapshot> BuildSnapshotAsync(
        INoteVault sourceVault,
        DailyNoteService sourceDailyNotes,
        IMarkdownDocumentParser parser,
        Func<string, TaskItemViewModel?>? taskResolver,
        Action? afterMarkdownCommit,
        FeedVaultWatchRuntime? runtime,
        Func<bool> canWrite,
        int dayLimit,
        CancellationToken cancellationToken)
    {
        var areaCatalog = await new AreaCatalogStore(sourceVault).LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        var activeCatalogAreas = areaCatalog.Catalog.Areas
            .Where(static area => !area.IsArchived)
            .ToArray();
        var activeCatalogAreaIds = activeCatalogAreas
            .Select(static area => area.Id)
            .ToHashSet(StringComparer.Ordinal);
        var resolveAreaName = CreateUniqueAreaNameResolver(activeCatalogAreas);
        var page = await sourceDailyNotes.ListDaysPageAsync(
                0,
                Math.Max(1, dayLimit),
                cancellationToken)
            .ConfigureAwait(false);
        var summaries = page.Days;
        var reviewSummaries = summaries.Count == page.TotalCount
            ? summaries
            : await sourceDailyNotes.ListDaysAsync(cancellationToken).ConfigureAwait(false);
        var days = new List<FeedDayViewModel>(summaries.Count);
        var areas = new Dictionary<string, FeedAreaOptionViewModel>(StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<string, VaultDocument>(StringComparer.OrdinalIgnoreCase);
        var latestRecoveryDrafts = runtime is null
            ? new Dictionary<string, FeedDraft>(StringComparer.OrdinalIgnoreCase)
            : (await runtime.Drafts.ListAsync(runtime.VaultId, cancellationToken).ConfigureAwait(false))
                .GroupBy(static value => NormalizePath(value.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderByDescending(static value => value.UpdatedAt).First(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = new VaultDocument(
                summary.RelativePath,
                summary.Text,
                summary.Revision,
                summary.HasUtf8Bom,
                summary.NewLine);

            documents[NormalizePath(summary.RelativePath)] = document;
            var taskReferences = ExtractTaskReferences(document.Text, taskResolver);
            var markdownEditor = CreateMarkdownEditor(
                sourceVault,
                document,
                summary.RelativePath,
                $"FeedDay-{summary.Date:yyyyMMdd}-Markdown",
                afterMarkdownCommit,
                runtime,
                canWrite);
            markdownEditor.SetTaskReferences(taskReferences);
            if (latestRecoveryDrafts.TryGetValue(NormalizePath(summary.RelativePath), out var recoveryDraft))
            {
                markdownEditor.OfferRecoveryDraft(recoveryDraft);
            }
            days.Add(new FeedDayViewModel(
                summary.Date,
                summary.RelativePath,
                document.Text,
                document.Revision,
                summary.ContentBlockCount,
                taskReferences,
                markdownEditor));

            foreach (var heading in parser.Parse(document.Text).Blocks.Where(static block => block.Kind == MarkdownBlockKind.AreaHeading))
            {
                var name = heading.AreaName?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var stableAreaId = string.IsNullOrWhiteSpace(heading.AreaId)
                    ? resolveAreaName(name)
                    : heading.AreaId;
                var identity = stableAreaId ?? CreateHeadingDestinationIdentity(name);
                var option = new FeedAreaOptionViewModel(
                    new AreaReference(stableAreaId, name)
                    {
                        MatchUnmarkedByName = string.IsNullOrWhiteSpace(heading.AreaId)
                    },
                    identity,
                    isExistingHeadingDestination: true,
                    isClassificationSelectable: stableAreaId is not null
                        && activeCatalogAreaIds.Contains(stableAreaId));
                areas.TryAdd(option.Identity, option);
            }
        }

        var nextSearchIndex = new FeedSearchIndex(parser, sourceDailyNotes.Naming);
        foreach (var document in documents.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(document.RelativePath);
            nextSearchIndex.IndexMarkdown(
                normalizedPath,
                document.Text,
                TryGetLastModifiedAt(sourceVault, normalizedPath));
        }

        return new FeedSnapshot(
            days.OrderByDescending(static day => day.Date).ToArray(),
            areas.Values.OrderBy(static area => area.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            nextSearchIndex,
            page.TotalCount,
            reviewSummaries
                .Select(static day => new FeedReviewDocument(day.RelativePath, day.Text))
                .ToArray());
    }

    private static DateTimeOffset? TryGetLastModifiedAt(INoteVault sourceVault, string relativePath)
    {
        try
        {
            var fullPath = sourceVault.ResolveSafePath(relativePath);
            return File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private void ApplySnapshot(FeedSnapshot snapshot)
    {
        var selectedAreaIdentity = SelectedArea?.Identity ?? FeedAreaOptionViewModel.NoArea.Identity;
        var reviewAreaIdentity = ReviewDestinationArea?.Identity ?? FeedAreaOptionViewModel.NoArea.Identity;
        var selectedDayPath = SelectedDay?.RelativePath;

        var previousDays = Days.ToDictionary(static day => NormalizePath(day.RelativePath), StringComparer.OrdinalIgnoreCase);
        var mergedDays = new List<FeedDayViewModel>(snapshot.Days.Count);
        foreach (var nextDay in snapshot.Days)
        {
            var key = NormalizePath(nextDay.RelativePath);
            if (previousDays.Remove(key, out var previousDay)
                && previousDay.MarkdownEditor.ActiveBlock?.IsDirty == true)
            {
                nextDay.Dispose();
                mergedDays.Add(previousDay);
            }
            else
            {
                if (previousDay is not null)
                {
                    nextDay.IsCollapsed = previousDay.IsCollapsed;
                }

                previousDay?.Dispose();
                mergedDays.Add(nextDay);
            }
        }

        foreach (var orphanedDay in previousDays.Values)
        {
            if (orphanedDay.MarkdownEditor.ActiveBlock?.IsDirty == true)
            {
                mergedDays.Add(orphanedDay);
            }
            else
            {
                orphanedDay.Dispose();
            }
        }

        Replace(Days, mergedDays.OrderByDescending(static day => day.Date));
        HasDays = Days.Count > 0;
        LoadedDayCount = Days.Count;
        TotalDayCount = snapshot.TotalDayCount;
        HasMoreDays = LoadedDayCount < TotalDayCount;
        reviewDocuments = snapshot.ReviewDocuments;
        InvalidateReviewQueue();
        SelectedDay = selectedDayPath is null
            ? SelectedDay
            : Days.FirstOrDefault(day => string.Equals(
                NormalizePath(day.RelativePath),
                NormalizePath(selectedDayPath),
                StringComparison.OrdinalIgnoreCase));
        snapshotAreas = snapshot.Areas;
        RebuildVisibleAreas(selectedAreaIdentity, reviewAreaIdentity);
        RebuildReviewTaskAreas();
        ScheduleSearchResultsRefresh();
        StartBackgroundSearchIndexing(
            snapshot.SearchIndex,
            snapshot.Days.Select(static day => day.RelativePath));
        if (currentCandidate is not null
            && currentReviewSelection is not null
            && CurrentReview is not null)
        {
            ApplyReviewHighlight(
                currentCandidate.Locator.RelativePath,
                Enumerable.Range(
                    currentReviewSelection.StartBlockIndex,
                    currentReviewSelection.BlockCount),
                currentReviewAnchorBlockIndex,
                CurrentReview.SelectedMarkdown);
        }
    }

    private void StartBackgroundSearchIndexing(
        FeedSearchIndex targetIndex,
        IEnumerable<string> alreadyIndexedPaths)
    {
        if (vault is not { } sourceVault || isDisposed)
        {
            return;
        }

        var loadedPaths = alreadyIndexedPaths
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetSessionToken());
        CancellationTokenSource? previousCancellation;
        long generation;
        lock (indexLock)
        {
            previousCancellation = indexCancellation;
            indexCancellation = nextCancellation;
            generation = ++indexGeneration;
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        IndexedMarkdownFiles = loadedPaths.Count;
        TotalMarkdownFiles = loadedPaths.Count;
        IsSearchIndexing = true;
        _ = IndexRemainingMarkdownAsync(
            sourceVault,
            targetIndex,
            loadedPaths,
            generation,
            nextCancellation);
    }

    private async Task IndexRemainingMarkdownAsync(
        INoteVault sourceVault,
        FeedSearchIndex targetIndex,
        IReadOnlySet<string> alreadyIndexedPaths,
        long generation,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var indexed = alreadyIndexedPaths.Count;
        var total = alreadyIndexedPaths.Count;
        try
        {
            var paths = (await sourceVault.ListMarkdownFilesAsync(token).ConfigureAwait(false))
                .Select(NormalizePath)
                .Where(static path => !path.StartsWith(".unlimotion/", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            indexed = paths.Count(alreadyIndexedPaths.Contains);
            total = paths.Length;
            PublishSearchIndexProgress(generation, targetIndex, indexed, total, completed: false);

            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (alreadyIndexedPaths.Contains(path))
                {
                    continue;
                }

                var document = await sourceVault.ReadAsync(path, token).ConfigureAwait(false);
                if (document is not null)
                {
                    targetIndex.IndexMarkdown(
                        path,
                        document.Text,
                        TryGetLastModifiedAt(sourceVault, path));
                }

                indexed++;
                if (indexed % 8 == 0)
                {
                    PublishSearchIndexProgress(generation, targetIndex, indexed, total, completed: false);
                }
            }

            PublishSearchIndexProgress(generation, targetIndex, indexed, total, completed: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishSearchIndexProgress(
                generation,
                targetIndex,
                indexed,
                total,
                completed: true,
                exception);
        }
        finally
        {
            lock (indexLock)
            {
                if (ReferenceEquals(indexCancellation, cancellation))
                {
                    indexCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void PublishSearchIndexProgress(
        long generation,
        FeedSearchIndex targetIndex,
        int indexed,
        int total,
        bool completed,
        Exception? exception = null)
    {
        void Apply()
        {
            lock (indexLock)
            {
                if (generation != indexGeneration || !ReferenceEquals(searchIndex, targetIndex))
                {
                    return;
                }
            }

            IndexedMarkdownFiles = indexed;
            TotalMarkdownFiles = total;
            IsSearchIndexing = !completed;
            if (exception is not null)
            {
                ErrorMessage = exception.Message;
            }

            if (IsSearchActive)
            {
                ScheduleSearchResultsRefresh();
            }
        }

        DispatchNotification(Apply);
    }

    private void CancelBackgroundSearchIndexing()
    {
        CancellationTokenSource? cancellation;
        lock (indexLock)
        {
            cancellation = indexCancellation;
            indexCancellation = null;
            indexGeneration++;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        IsSearchIndexing = false;
        IndexedMarkdownFiles = 0;
        TotalMarkdownFiles = 0;
    }

    private void RebuildVisibleAreas(string? selectedAreaIdentity = null, string? reviewAreaIdentity = null)
    {
        selectedAreaIdentity ??= SelectedArea?.Identity ?? FeedAreaOptionViewModel.NoArea.Identity;
        reviewAreaIdentity ??= ReviewDestinationArea?.Identity ?? FeedAreaOptionViewModel.NoArea.Identity;
        var searchAreaIdentity = SelectedSearchArea?.AreaIdentity;
        var searchAllAreas = SelectedSearchArea?.IsAll != false;
        var merged = new Dictionary<string, FeedAreaOptionViewModel>(StringComparer.OrdinalIgnoreCase);
        if (AreaManagement is not null)
        {
            var activeAreas = AreaManagement.ClassificationAreas
                .Where(static area => !area.IsArchived)
                .ToArray();
            var uniquelyNamedAreaIds = activeAreas
                .GroupBy(static area => area.Name, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Select(static area => area.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .Count() == 1)
                .SelectMany(static group => group.Select(static area => area.Id))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var area in activeAreas)
            {
                merged[area.Id] = new FeedAreaOptionViewModel(
                    new AreaReference(area.Id, area.Name)
                    {
                        MatchUnmarkedByName = uniquelyNamedAreaIds.Contains(area.Id)
                    },
                    isClassificationSelectable: true);
            }
        }

        foreach (var area in snapshotAreas)
        {
            merged[area.Identity] = area;
        }

        Areas.Clear();
        Areas.Add(FeedAreaOptionViewModel.NoArea);
        foreach (var area in merged.Values.OrderBy(static area => area.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Areas.Add(area);
        }

        SelectedArea = Areas.FirstOrDefault(area => string.Equals(area.Identity, selectedAreaIdentity, StringComparison.OrdinalIgnoreCase))
            ?? Areas[0];
        ReviewDestinationArea = Areas.FirstOrDefault(area => string.Equals(area.Identity, reviewAreaIdentity, StringComparison.OrdinalIgnoreCase))
            ?? Areas[0];
        RebuildSearchAreaOptions(searchAreaIdentity, searchAllAreas);
    }

    private void RebuildSearchAreaOptions(string? selectedIdentity, bool selectedAll)
    {
        SearchAreaOptions.Clear();
        SearchAreaOptions.Add(FeedSearchAreaOptionViewModel.All);
        SearchAreaOptions.Add(FeedSearchAreaOptionViewModel.NoArea);
        foreach (var area in Areas.Where(static option => option.IsClassificationSelectable))
        {
            SearchAreaOptions.Add(new FeedSearchAreaOptionViewModel(
                area.StableAreaId!,
                area.DisplayName));
        }

        SelectedSearchArea = selectedAll
            ? SearchAreaOptions[0]
            : SearchAreaOptions.FirstOrDefault(option =>
                string.Equals(option.AreaIdentity, selectedIdentity, StringComparison.OrdinalIgnoreCase))
              ?? SearchAreaOptions[0];
    }

    private async Task<FeedAuxiliaryViewModels> PrepareAuxiliaryViewModelsAsync(
        INoteVault sourceVault,
        DailyNoteNaming naming,
        CancellationToken cancellationToken)
    {
        var nextFiles = new FeedFilesDrawerViewModel(sourceVault, naming)
        {
            OpenFileCallbackAsync = OpenThematicFileAsync
        };
        var nextAreas = new AreaManagementViewModel(new AreaCatalogStore(sourceVault));
        try
        {
            await Task.WhenAll(
                    nextFiles.RefreshAsync(cancellationToken),
                    nextAreas.LoadAsync(cancellationToken))
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            nextFiles.Dispose();
            nextAreas.Dispose();
            throw;
        }

        return new FeedAuxiliaryViewModels(nextFiles, nextAreas);
    }

    private void InstallPreparedAuxiliaryViewModels(FeedAuxiliaryViewModels auxiliary)
    {
        FilesDrawer = auxiliary.FilesDrawer;
        AreaManagement = auxiliary.AreaManagement;
        areaCatalogChangedHandler = (_, _) =>
        {
            RebuildVisibleAreas();
            RebuildReviewTaskAreas();
        };
        auxiliary.AreaManagement.ClassificationAreas.CollectionChanged += areaCatalogChangedHandler;
        RebuildVisibleAreas();
        RebuildReviewTaskAreas();
    }

    private void OpenFilesDrawer()
    {
        if (FilesDrawer is null)
        {
            return;
        }

        FilesDrawer.IsOpen = true;
        _ = FilesDrawer.RefreshAsync();
    }

    private void OpenAreaManagement()
    {
        if (AreaManagement is null)
        {
            return;
        }

        AreaManagement.IsOpen = true;
        _ = AreaManagement.LoadAsync();
    }

    private async Task OpenThematicFileAsync(string relativePath)
    {
        var sourceVault = vault ?? throw new InvalidOperationException("The note vault is unavailable.");
        var cancellationToken = GetSessionToken();
        var document = await sourceVault.ReadAsync(relativePath, cancellationToken).ConfigureAwait(true)
            ?? throw new FileNotFoundException("The selected note no longer exists.", relativePath);
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizePath(relativePath))))
            .ToLowerInvariant()[..12];
        var editor = CreateMarkdownEditor(
            sourceVault,
            document,
            relativePath,
            $"FeedFile-{pathHash}-Markdown",
            ScheduleRefreshAfterMarkdownCommit,
            watchRuntime,
            () => !IsIdentityFrozen);
        if (watchRuntime is not null)
        {
            await OfferLatestRecoveryDraftAsync(editor, watchRuntime, relativePath, cancellationToken)
                .ConfigureAwait(true);
        }
        var previous = OpenedThematicFile;
        OpenedThematicFile = new FeedThematicDocumentViewModel(relativePath, editor);
        previous?.Dispose();
    }

    private void CloseThematicFile()
    {
        var previous = OpenedThematicFile;
        OpenedThematicFile = null;
        previous?.Dispose();
    }

    private void ScheduleRuntimeAction(
        CancellationToken expectedSession,
        Func<CancellationToken, Task> action)
    {
        if (isDisposed || expectedSession.IsCancellationRequested)
        {
            return;
        }

        void Start()
        {
            if (!isDisposed && !expectedSession.IsCancellationRequested)
            {
                _ = RunRuntimeActionAsync(action, expectedSession);
            }
        }

        DispatchNotification(Start);
    }

    private async Task RunRuntimeActionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!isDisposed && !cancellationToken.IsCancellationRequested)
            {
                ErrorMessage = exception.Message;
            }
        }
    }

    private async Task RefreshMarkdownFromWatcherAsync(CancellationToken cancellationToken)
    {
        await RefreshCoreAsync().ConfigureAwait(true);
        if (FilesDrawer is not null)
        {
            await FilesDrawer.RefreshAsync().ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RefreshAreasFromWatcherAsync(CancellationToken cancellationToken)
    {
        if (AreaManagement is null)
        {
            return;
        }

        await AreaManagement.LoadAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        RebuildVisibleAreas();
        RebuildReviewTaskAreas();
    }

    private async Task RefreshReviewFromWatcherAsync(CancellationToken cancellationToken)
    {
        if (reviewCoordinator is null || vault is null || markdownParser is null || vaultId is null)
        {
            return;
        }

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var coordinator = reviewCoordinator;
            var sourceVault = vault;
            var parser = markdownParser;
            var sourceVaultId = vaultId;
            if (coordinator is null || sourceVault is null || parser is null || sourceVaultId is null)
            {
                return;
            }

            await coordinator.InitializeAsync(cancellationToken).ConfigureAwait(true);
            var safeBootstrap = await new FirstConnectBootstrapService(
                    sourceVault,
                    parser,
                    dailyNoteNaming)
                .FindSafeCompleteAsync(sourceVaultId, cancellationToken)
                .ConfigureAwait(true);
            if (safeBootstrap is null)
            {
                coordinator.State.ReplaceBootstrapBaseline([]);
            }
            else
            {
                ApplyBootstrapBaseline(coordinator.State, safeBootstrap, sourceVaultId);
                BootstrapIndexedFiles = safeBootstrap.IndexedFiles;
                BootstrapPendingCheckboxes = safeBootstrap.PendingCheckboxes;
                BootstrapWasReused = true;
            }

            InvalidateReviewQueue();
            await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            ShowForeignReviewRecoveryIfNeeded();
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task ReloadDailyNoteSettingsFromWatcherAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootPath = VaultRootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !IsVaultInitialized)
        {
            return;
        }

        var request = CaptureRootReconfigureRequest();
        var session = new VaultSessionExpectation(rootPath, cancellationToken);
        var activeVault = vault;
        var activeSettings = dailyNoteSettingsSnapshot;
        if (activeVault is not null && activeSettings is not null)
        {
            try
            {
                var observedSettings = await new DailyNoteSettingsStore(activeVault)
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(true);
                if (!IsCurrentReconfigureRequest(request) || !IsCurrentVaultSession(session))
                {
                    return;
                }

                if (DailyNoteSettingsSnapshotsMatch(activeSettings, observedSettings))
                {
                    // A directory-level watcher rescan can reach this route even when the
                    // filename-format sidecar did not change. Do not replace the active
                    // session or present a false external-change decision in that case.
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Preserve the existing recovery behavior for a corrupt or transiently
                // unreadable sidecar: the full reconfigure below publishes its safe state.
            }
        }

        // This action was scheduled after the watcher released its route gate. Reconfiguring here
        // may dispose that watcher, so it must never run inline inside the watcher callback.
        var result = await RunVaultReconfigureAsync(
                rootPath,
                requestedNaming: null,
                expectedDailyNoteSettingsRevision: null,
                isExternalChange: true,
                request: request,
                expectedSession: session)
            .ConfigureAwait(true);
        if (!result.Succeeded &&
            !result.IsCancelled &&
            result.State is { } failedState &&
            IsCurrentReconfigureRequest(request) &&
            IsCurrentVaultSession(session))
        {
            // The invalid/corrupt sidecar remains on disk. Surface the failure through the
            // same Settings state channel as a normal external change so the user gets a
            // stable Reload affordance while the last valid Feed session stays usable.
            PublishDailyNoteFileNameFormatState(failedState);
        }
    }

    private async Task ReloadDailyNoteSettingsAfterWatcherRouteAsync(CancellationToken cancellationToken)
    {
        // ScheduleRuntimeAction can begin synchronously when Feed has no UI dispatcher. Yielding
        // guarantees that FeedVaultWatchRuntime.RouteAsync releases routeGate before the
        // reconfiguration disposes the old runtime and waits for that gate.
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        await ReloadDailyNoteSettingsFromWatcherAsync(cancellationToken).ConfigureAwait(true);
    }

    private Task ShowDocumentConflictAsync(
        DocumentConflictState conflict,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = watchRuntime;
        if (runtime is null)
        {
            return Task.CompletedTask;
        }

        if (DocumentConflict?.IsOpen == true)
        {
            pendingDocumentConflicts.Enqueue(conflict);
            return Task.CompletedTask;
        }

        DocumentConflict?.Dispose();
        var next = new FeedDocumentConflictViewModel(runtime.ConflictCoordinator, conflict);
        next.ResolvedCallbackAsync = async _ =>
        {
            CancelDirtyEditor(conflict.EditorRelativePath);
            await RefreshMarkdownFromWatcherAsync(GetSessionToken()).ConfigureAwait(true);
            ShowNextDocumentConflict();
        };
        DocumentConflict = next;
        return Task.CompletedTask;
    }

    private async Task RestorePendingDocumentConflictsAsync(CancellationToken cancellationToken)
    {
        var runtime = watchRuntime;
        if (runtime is null || vaultId is null)
        {
            return;
        }

        pendingDocumentConflicts.Clear();
        var bundles = await runtime.ConflictBundles.ListUnresolvedAsync(vaultId, cancellationToken)
            .ConfigureAwait(true);
        foreach (var bundle in bundles)
        {
            var restored = await runtime.ConflictCoordinator.RestoreAsync(bundle, cancellationToken)
                .ConfigureAwait(true);
            if (restored is not null)
            {
                pendingDocumentConflicts.Enqueue(restored);
            }
        }

        ShowNextDocumentConflict();
    }

    private void ShowNextDocumentConflict()
    {
        if (DocumentConflict is { IsOpen: true, IsResolved: false }
            || pendingDocumentConflicts.Count == 0)
        {
            return;
        }

        if (DocumentConflict?.IsResolved == true)
        {
            DocumentConflict.Dispose();
            DocumentConflict = null;
        }

        var next = pendingDocumentConflicts.Dequeue();
        _ = ShowDocumentConflictAsync(next, GetSessionToken());
    }

    private void CancelDirtyEditor(string relativePath)
    {
        var day = FindDay(relativePath);
        day?.MarkdownEditor.CancelActiveEdit();
        if (OpenedThematicFile is not null
            && string.Equals(
                NormalizePath(OpenedThematicFile.RelativePath),
                NormalizePath(relativePath),
                StringComparison.OrdinalIgnoreCase))
        {
            OpenedThematicFile.MarkdownEditor.CancelActiveEdit();
        }
    }

    private async Task FreezeForIdentityChangeAsync(
        FeedVaultIdentityFreezeSignal signal,
        CancellationToken cancellationToken)
    {
        IsIdentityFrozen = true;
        ErrorMessage = L10n.Get("FeedVaultIdentityFrozen");
        var currentVault = vault;
        var currentVaultId = vaultId;
        var coordinator = reviewCoordinator;
        if (currentVault is null || currentVaultId is null || coordinator is null)
        {
            return;
        }

        var decisionEvents = coordinator.State.DecisionEvents.ToArray();
        var sessionEvents = coordinator.State.SessionEvents.ToArray();
        var locators = decisionEvents
            .SelectMany(static value => new[] { value.Input }.Concat(value.Outputs ?? []))
            .DistinctBy(static value => value.SemanticKey)
            .ToArray();
        var acceptedBranch = new VaultIdentityBranchSnapshot(
            currentVaultId,
            JsonSerializer.Serialize(new VaultIdentityManifest(1, currentVaultId)) + "\n",
            IdentityRevision: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["in-memory/decisions.json"] = JsonSerializer.Serialize(decisionEvents),
                ["in-memory/sessions.json"] = JsonSerializer.Serialize(sessionEvents)
            },
            locators);
        var identityCoordinator = new VaultIdentityConflictCoordinator(
            currentVault,
            new FileVaultIdentityConflictStore(GetDefaultRecoveryRoot(currentVaultId)),
            new FeedJournalIdentityRecoveryGuard(
                operationJournalFactory(currentVaultId),
                taskJournalFactory(currentVaultId)));

        VaultIdentityConflictBundle? conflict;
        try
        {
            conflict = await identityCoordinator.DetectAndPreserveAsync(acceptedBranch, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            ErrorMessage = $"{L10n.Get("FeedVaultIdentityFrozen")} {exception.Message}";
            return;
        }

        if (conflict is null)
        {
            return;
        }

        IdentityConflict?.Dispose();
        var next = new FeedVaultIdentityConflictViewModel(identityCoordinator, conflict);
        next.ResolvedCallbackAsync = async result =>
        {
            var safePending = result.SafePendingLocators
                .DistinctBy(static value => value.SemanticKey)
                .ToArray();
            switch (result.Resolution)
            {
                case VaultIdentityConflictResolution.UseCurrentRootIdentity:
                    if (VaultRootPath is { } currentRoot)
                    {
                        await InitializeVaultAsync(currentRoot).ConfigureAwait(true);
                        await ImportIdentitySafePendingAsync(
                                result.ConflictId,
                                safePending,
                                GetSessionToken())
                            .ConfigureAwait(true);
                    }

                    break;
                case VaultIdentityConflictResolution.ReconnectAnotherRoot:
                    await ChooseVaultCoreAsync().ConfigureAwait(true);
                    if (IsVaultInitialized)
                    {
                        await ImportIdentitySafePendingAsync(
                                result.ConflictId,
                                safePending,
                                GetSessionToken())
                            .ConfigureAwait(true);
                    }

                    break;
                case VaultIdentityConflictResolution.StayReadOnly:
                    IsIdentityFrozen = true;
                    ShowIdentitySafePending(safePending);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        };
        IdentityConflict = next;
    }

    private async Task DisposeVaultSessionAsync(VaultRootHandoffLease? rootHandoff = null)
    {
        var previousAreaManagement = AreaManagement;
        if (previousAreaManagement is not null && areaCatalogChangedHandler is not null)
        {
            previousAreaManagement.ClassificationAreas.CollectionChanged -= areaCatalogChangedHandler;
        }

        areaCatalogChangedHandler = null;
        var previousDocumentConflict = DocumentConflict;
        DocumentConflict = null;
        DisposeVaultSessionResource(previousDocumentConflict);
        var previousIdentityConflict = IdentityConflict;
        IdentityConflict = null;
        DisposeVaultSessionResource(previousIdentityConflict);
        var previousReviewRecovery = ReviewRecovery;
        ReviewRecovery = null;
        DisposeVaultSessionResource(previousReviewRecovery);
        var previousThematicFile = OpenedThematicFile;
        OpenedThematicFile = null;
        DisposeVaultSessionResource(previousThematicFile);
        var previousFilesDrawer = FilesDrawer;
        FilesDrawer = null;
        DisposeVaultSessionResource(previousFilesDrawer);
        AreaManagement = null;
        DisposeVaultSessionResource(previousAreaManagement);
        var runtime = watchRuntime;
        watchRuntime = null;
        if (runtime is not null)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // A watcher can already be shutting down after an external failure. Its
                // resources have still been canceled by DisposeAsync; do not leave the old
                // registry attachment behind or abort the replacement session.
                ErrorMessage = exception.Message;
            }
        }


        if (attachedVaultId is { } registeredVaultId
            && attachedVaultRootPath is { } registeredRootPath)
        {
            if (rootHandoff is null)
            {
                vaultRootRegistry.Detach(registeredVaultId, registeredRootPath);
            }
            else
            {
                // The relocation lease is the only proof that this exact old Feed session has
                // reached its disposal point. A generic path-only Detach cannot authorize B.
                vaultRootRegistry.ConfirmHandoffOldAttachmentDetached(rootHandoff, registeredRootPath);
            }
        }

        attachedVaultId = null;
        attachedVaultRootPath = null;
    }

    private void DisposeVaultSessionResource(IDisposable? resource)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            // Reconfiguration must always finish releasing the old registry attachment. A
            // disposable UI surface cannot be allowed to strand a pending root handoff.
            ErrorMessage = exception.Message;
        }
    }

    private void AttachTaskSearchTracking()
    {
        if (taskOwner is null)
        {
            return;
        }

        taskOwnerPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName is null or nameof(MainWindowViewModel.IsInitialized))
            {
                EnsureTaskSearchSubscription();
            }
        };
        ((INotifyPropertyChanged)taskOwner).PropertyChanged += taskOwnerPropertyChangedHandler;
        EnsureTaskSearchSubscription();
    }

    private void EnsureTaskSearchSubscription()
    {
        var storage = taskOwner?.taskRepository;
        if (ReferenceEquals(indexedTaskStorage, storage))
        {
            return;
        }

        taskSearchSubscription?.Dispose();
        taskSearchSubscription = null;
        indexedTaskStorage = storage;
        if (storage is not null)
        {
            taskSearchSubscription = storage.Tasks
                .Connect()
                .Subscribe(_ => RequestTaskSearchRefresh());
        }

        RefreshTaskReferences();
        RequestTaskSearchRefresh();
    }

    private void DetachTaskSearchTracking()
    {
        if (taskOwner is not null && taskOwnerPropertyChangedHandler is not null)
        {
            ((INotifyPropertyChanged)taskOwner).PropertyChanged -= taskOwnerPropertyChangedHandler;
        }

        taskOwnerPropertyChangedHandler = null;
        taskSearchSubscription?.Dispose();
        taskSearchSubscription = null;
        indexedTaskStorage = null;
    }

    private void RequestTaskSearchRefresh()
    {
        if (isDisposed)
        {
            return;
        }

        DispatchNotification(ScheduleSearchResultsRefresh);
    }

    private void RefreshTaskSearchIndex(FeedSearchIndex index)
    {
        var tasks = taskOwner?.taskRepository?.Tasks.Items
            .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
            .Select(static task => new FeedSearchTaskDocument(
                task.Id,
                task.Title,
                task.Description,
                task.AreaIds.ToArray(),
                task.UpdatedDateTime))
            .ToArray()
            ?? [];
        index.ReplaceTasks(tasks);
    }

    private void ScheduleSearchResultsRefresh()
    {
        var generation = Interlocked.Increment(ref searchGeneration);
        var nextCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        lock (searchLock)
        {
            previousCancellation = searchCancellation;
            searchCancellation = nextCancellation;
        }

        previousCancellation?.Cancel();

        if (searchIndex is null || !IsSearchActive)
        {
            ApplySearchResults(generation, Array.Empty<FeedSearchEntry>());
            return;
        }

        var index = searchIndex;
        RefreshTaskSearchIndex(index);
        var query = CreateSearchQuery();
        var areaResolution = CreateSearchAreaResolution();
        ApplySearchResults(generation, Array.Empty<FeedSearchEntry>());
        _ = SearchAsync(index, query, areaResolution, generation, nextCancellation.Token);
    }

    private async Task SearchAsync(
        FeedSearchIndex index,
        FeedSearchQuery query,
        FeedSearchAreaResolution areaResolution,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
            var entries = await Task.Run(
                    () => index.Search(query)
                        .Select(areaResolution.Normalize)
                        .Where(areaResolution.Matches)
                        .ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            DispatchNotification(() => ApplySearchResults(generation, entries));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private FeedSearchQuery CreateSearchQuery() => new(
        SearchQuery,
        AreaIdentity: null,
        SearchFromDate is null ? null : DateOnly.FromDateTime(SearchFromDate.Value.LocalDateTime),
        SearchToDate is null ? null : DateOnly.FromDateTime(SearchToDate.Value.LocalDateTime),
        SelectedSearchType?.Type);

    private FeedSearchAreaResolution CreateSearchAreaResolution()
    {
        var uniqueNames = Areas
            .Where(static area => area.IsClassificationSelectable)
            .GroupBy(static area => area.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new
            {
                group.Key,
                Ids = group.Select(static area => area.StableAreaId!)
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .ToArray()
            })
            .Where(static group => group.Ids.Length == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Ids[0],
                StringComparer.OrdinalIgnoreCase);
        return new FeedSearchAreaResolution(SelectedSearchArea?.AreaIdentity, uniqueNames);
    }

    public Task OpenSearchResultAsync(FeedSearchResultViewModel result)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(result);
        return OpenSearchResultCoreAsync(result);
    }

    private async Task OpenSearchResultCoreAsync(FeedSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            if (result.Type == FeedSearchDocumentType.Task)
            {
                var task = TaskResolver?.Invoke(result.TaskId)
                    ?? (taskOwner?.taskRepository?.Tasks.Lookup(result.TaskId) is { HasValue: true } lookup
                        ? lookup.Value
                        : null);
                NavigateToTask(task);
                return;
            }

            var sourceVault = vault;
            var index = searchIndex;
            if (sourceVault is null || index is null)
            {
                return;
            }

            var cancellationToken = GetSessionToken();
            var document = await sourceVault.ReadAsync(result.RelativePath, cancellationToken).ConfigureAwait(true);
            if (document is null)
            {
                index.Remove(result.RelativePath);
                ScheduleSearchResultsRefresh();
                return;
            }

            index.IndexMarkdown(
                result.RelativePath,
                document.Text,
                TryGetLastModifiedAt(sourceVault, result.RelativePath));
            var query = CreateSearchQuery();
            var current = index.ResolveCurrentAnchor(result.Entry, query);
            if (current is null)
            {
                ScheduleSearchResultsRefresh();
                return;
            }

            if (current.Type == FeedSearchDocumentType.Daily)
            {
                var day = FindDay(current.RelativePath);
                if (day is null)
                {
                    await LoadThroughSearchDayAsync(current.RelativePath, cancellationToken).ConfigureAwait(true);
                    index = searchIndex;
                    current = index?.ResolveCurrentAnchor(result.Entry, query);
                    day = current is null ? null : FindDay(current.RelativePath);
                }
                else if (day.MarkdownEditor.Snapshot?.ExpectedRevisionHash != document.Revision)
                {
                    await RefreshCoreAsync().ConfigureAwait(true);
                    index = searchIndex;
                    current = index?.ResolveCurrentAnchor(result.Entry, query);
                    day = current is null ? null : FindDay(current.RelativePath);
                }

                if (day is null
                    || current is null
                    || !TryResolveSearchBlock(day.MarkdownEditor, current, out _))
                {
                    ScheduleSearchResultsRefresh();
                    return;
                }

                SearchNavigationStarting?.Invoke(this, EventArgs.Empty);
                SearchQuery = string.Empty;
                SelectedDay = day;
                SearchNavigationRequested?.Invoke(
                    this,
                    new FeedSearchNavigationRequestedEventArgs(
                        current.RelativePath,
                        day.MarkdownEditor,
                        current.BlockIndex,
                        day));
                return;
            }

            await OpenThematicFileAsync(current.RelativePath).ConfigureAwait(true);
            var thematic = OpenedThematicFile;
            if (thematic is null
                || !TryResolveSearchBlock(thematic.MarkdownEditor, current, out _))
            {
                CloseThematicFile();
                ScheduleSearchResultsRefresh();
                return;
            }

            SearchNavigationStarting?.Invoke(this, EventArgs.Empty);
            SearchQuery = string.Empty;
            SearchNavigationRequested?.Invoke(
                this,
                new FeedSearchNavigationRequestedEventArgs(
                    current.RelativePath,
                    thematic.MarkdownEditor,
                    current.BlockIndex,
                    null));
        }
        catch (OperationCanceledException) when (GetSessionToken().IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task LoadThroughSearchDayAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (dailyNotes is null)
        {
            return;
        }

        var paths = await dailyNotes.ListDayPathsAsync(cancellationToken).ConfigureAwait(true);
        var targetIndex = paths
            .Select((path, index) => (path, index))
            .FirstOrDefault(candidate => string.Equals(
                NormalizePath(candidate.path.RelativePath),
                NormalizePath(relativePath),
                StringComparison.OrdinalIgnoreCase))
            .index;
        if (targetIndex < 0
            || targetIndex >= paths.Count
            || !string.Equals(
                NormalizePath(paths[targetIndex].RelativePath),
                NormalizePath(relativePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedDayCount = Math.Max(loadedDayCount, targetIndex + 1);
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    private async Task EnsureReviewDayLoadedAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (FindDay(relativePath) is not null || dailyNotes is null)
        {
            return;
        }

        var paths = await dailyNotes.ListDayPathsAsync(cancellationToken).ConfigureAwait(true);
        var targetIndex = paths
            .Select((path, index) => (path, index))
            .FirstOrDefault(candidate => string.Equals(
                NormalizePath(candidate.path.RelativePath),
                NormalizePath(relativePath),
                StringComparison.OrdinalIgnoreCase))
            .index;
        if (targetIndex < 0
            || targetIndex >= paths.Count
            || !string.Equals(
                NormalizePath(paths[targetIndex].RelativePath),
                NormalizePath(relativePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedDayCount = Math.Max(loadedDayCount, targetIndex + 1);
        await ReloadSnapshotAsync(cancellationToken).ConfigureAwait(true);
    }

    private static bool TryResolveSearchBlock(
        MarkdownLivePreviewEditorViewModel editor,
        FeedSearchEntry entry,
        out MarkdownLiveBlockViewModel? block)
    {
        block = editor.Blocks.FirstOrDefault(candidate =>
            candidate.Index == entry.BlockIndex
            && string.Equals(candidate.Block.ContentHash, entry.ContentHash, StringComparison.Ordinal));
        return block is not null;
    }

    private void ApplySearchResults(long generation, IReadOnlyList<FeedSearchEntry> entries)
    {
        if (isDisposed || generation != Volatile.Read(ref searchGeneration))
        {
            return;
        }

        var areaNames = Areas
            .Where(static option => option.Area is not null)
            .ToDictionary(
                static option => option.Identity,
                static option => option.DisplayName,
                StringComparer.OrdinalIgnoreCase);
        SearchResults.Clear();
        foreach (var entry in entries)
        {
            SearchResults.Add(new FeedSearchResultViewModel(
                entry,
                areaIdentity => areaNames.TryGetValue(areaIdentity, out var areaName)
                    ? areaName
                    : areaIdentity));
        }
        HasSearchResults = SearchResults.Count > 0;
    }

    private CancellationToken ReplaceSession(CancellationToken rootReconfigureCancellation = default)
    {
        var next = rootReconfigureCancellation.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(rootReconfigureCancellation)
            : new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (sessionLock)
        {
            previous = sessionCancellation;
            sessionCancellation = next;
            sessionGeneration++;
        }

        previous?.Cancel();
        return next.Token;
    }

    private bool TryTransferPreparedVaultSessionOwnership(
        VaultReconfigureRequest request,
        FeedLoadResult preparedLoad,
        VaultRootHandoffLease? rootHandoff)
    {
        // BeginRootReconfigureRequest cancels the currently owned request under this same lock.
        // Keep the ownership check, irreversible registry handoff and the fields used by
        // DisposeVaultSessionAsync in one critical section. Dispose can therefore either win
        // before this transfer (the candidate remains locally disposable) or after it (the
        // candidate is already represented by the active session fields and is detached there).
        lock (reconfigureRequestLock)
        {
            if (isDisposed || !IsCurrentReconfigureRequest(request))
            {
                return false;
            }

            if (rootHandoff is not null)
            {
                vaultRootRegistry.CommitHandoff(rootHandoff);
                ClearPendingVaultRootHandoff(rootHandoff);
                rootHandoff.Dispose();
            }

            watchRuntime = preparedLoad.WatchRuntime;
            attachedVaultId = preparedLoad.VaultId;
            attachedVaultRootPath = preparedLoad.Vault.RootPath;

            return true;
        }
    }

    private bool TryFinalizeVaultReconfigureRequest(VaultReconfigureRequest request)
    {
        lock (reconfigureRequestLock)
        {
            return !isDisposed && IsCurrentReconfigureRequest(request);
        }
    }

    private CancellationToken GetSessionToken()
    {
        lock (sessionLock)
        {
            return sessionCancellation?.Token ?? CancellationToken.None;
        }
    }

    private long GetSessionGeneration()
    {
        lock (sessionLock)
        {
            return sessionGeneration;
        }
    }

    private VaultReconfigureRequest BeginRootReconfigureRequest()
    {
        var nextCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        long generation;
        lock (reconfigureRequestLock)
        {
            previousCancellation = rootReconfigureCancellation;
            rootReconfigureCancellation = nextCancellation;
            generation = ++rootReconfigureGeneration;
        }

        previousCancellation?.Cancel();
        return new VaultReconfigureRequest(generation, nextCancellation.Token);
    }

    private VaultReconfigureRequest CaptureRootReconfigureRequest()
    {
        lock (reconfigureRequestLock)
        {
            return new VaultReconfigureRequest(
                rootReconfigureGeneration,
                rootReconfigureCancellation?.Token ?? CancellationToken.None);
        }
    }

    private bool IsCurrentReconfigureRequest(VaultReconfigureRequest request)
    {
        lock (reconfigureRequestLock)
        {
            return request.Generation == rootReconfigureGeneration
                && request.CancellationToken == (rootReconfigureCancellation?.Token ?? CancellationToken.None)
                && !request.CancellationToken.IsCancellationRequested;
        }
    }

    private bool IsCurrentVaultSession(VaultSessionExpectation? expectedSession)
    {
        if (expectedSession is null)
        {
            return true;
        }

        return IsVaultInitialized
            && !expectedSession.SessionToken.IsCancellationRequested
            && expectedSession.SessionToken == GetSessionToken()
            && AreEquivalentVaultRoots(expectedSession.RootPath, VaultRootPath);
    }

    private static bool AreEquivalentVaultRoots(string expectedRootPath, string? currentRootPath)
    {
        if (string.IsNullOrWhiteSpace(expectedRootPath)
            || string.IsNullOrWhiteSpace(currentRootPath))
        {
            return false;
        }

        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(expectedRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(currentRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void RegisterPendingVaultRootHandoff(VaultRootHandoffLease handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        lock (vaultRootHandoffLock)
        {
            if (isDisposed)
            {
                handoff.Dispose();
                throw new ObjectDisposedException(nameof(FeedViewModel));
            }

            if (pendingVaultRootHandoff is not null)
            {
                handoff.Dispose();
                throw new InvalidOperationException("A vault root handoff is already pending.");
            }

            pendingVaultRootHandoff = handoff;
        }
    }

    private void ClearPendingVaultRootHandoff(VaultRootHandoffLease? handoff)
    {
        if (handoff is null)
        {
            return;
        }

        lock (vaultRootHandoffLock)
        {
            if (ReferenceEquals(pendingVaultRootHandoff, handoff))
            {
                pendingVaultRootHandoff = null;
            }
        }
    }

    private VaultRootHandoffLease? TakePendingVaultRootHandoff()
    {
        lock (vaultRootHandoffLock)
        {
            var handoff = pendingVaultRootHandoff;
            pendingVaultRootHandoff = null;
            return handoff;
        }
    }

    private async Task DisposePreparedLoadAsync(FeedLoadResult loaded)
    {
        loaded.Auxiliary?.Dispose();
        ClearPendingVaultRootHandoff(loaded.RootHandoff);
        loaded.RootHandoff?.Dispose();
        try
        {
            if (loaded.WatchRuntime is not null)
            {
                await loaded.WatchRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Candidate cleanup must not hide the original rebind/cancellation failure.
        }
        finally
        {
            if (loaded.VaultRootAttached)
            {
                vaultRootRegistry.Detach(loaded.VaultId, loaded.Vault.RootPath);
            }
        }
    }

    private async Task DisposeTransferredCandidateAsync(
        FeedLoadResult loaded,
        bool auxiliaryInstalled = false)
    {
        // The registry attachment and watcher ownership moved into the active Feed fields before
        // this method is called. Do not reuse DisposePreparedLoadAsync: a committed handoff is no
        // longer represented by that lease, while DisposeVaultSessionAsync can detach its new root.
        if (!auxiliaryInstalled)
        {
            loaded.Auxiliary?.Dispose();
        }

        await DisposeVaultSessionAsync().ConfigureAwait(false);
    }

    private async Task<bool> CompleteCommittedVaultSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RestorePendingDocumentConflictsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // Conflict recovery is auxiliary to a successfully validated timeline. Keep the
            // newly bound session usable and leave its durable bundles for the next recovery.
            ErrorMessage = exception.Message;
        }

        try
        {
            if (await RecoverPendingOperationsAsync(cancellationToken).ConfigureAwait(true))
            {
                await ReloadSnapshotAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
            }

            ShowForeignReviewRecoveryIfNeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // A recoverable journal/index problem must not turn a completed vault rebind into a
            // disconnected Feed. The candidate snapshot remains the visible, usable baseline.
            ErrorMessage = exception.Message;
        }

        var runtime = watchRuntime;
        if (runtime is null)
        {
            return true;
        }

        try
        {
            await runtime.ActivateAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // The watcher may retry on the next session; its activation is not a prerequisite
            // for displaying or editing the already prepared local snapshot.
            ErrorMessage = exception.Message;
            return true;
        }
    }

    private static async Task<DailyNoteSettingsSnapshot?> RestoreDailyNoteSettingsAfterFailedApplyAsync(
        INoteVault? vault,
        DailyNoteSettingsSnapshot? settingsBeforeApply,
        DailyNoteSettingsSnapshot? persistedApplySettings)
    {
        // The current session remains connected when candidate preparation fails. Restore the
        // sidecar with a revision check as well, so a later reconnect cannot silently activate a
        // format which this Apply never finished binding. A concurrent external write wins.
        if (vault is null || settingsBeforeApply is null || persistedApplySettings is null)
        {
            return null;
        }

        try
        {
            if (settingsBeforeApply.Revision is null)
            {
                var deleted = await vault.DeleteAsync(
                        DailyNoteSettingsStore.RelativePath,
                        persistedApplySettings.Revision,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return deleted
                    ? new DailyNoteSettingsSnapshot(settingsBeforeApply.Settings, Revision: null)
                    : null;
            }

            var settingsStore = new DailyNoteSettingsStore(vault);
            return await settingsStore.SaveAsync(
                    settingsBeforeApply.Settings,
                    persistedApplySettings.Revision,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (VaultRevisionConflictException)
        {
            // A remote writer updated the sidecar after the failed Apply. Never overwrite it.
            return null;
        }
        catch (Exception)
        {
            // Preserve the original candidate-preparation error; the current session remains
            // usable and a later reload surfaces the durable sidecar state.
            return null;
        }
    }

    private static bool DailyNoteSettingsSnapshotsMatch(
        DailyNoteSettingsSnapshot left,
        DailyNoteSettingsSnapshot right) =>
        string.Equals(left.Revision, right.Revision, StringComparison.Ordinal)
        && left.Settings.SchemaVersion == right.Settings.SchemaVersion
        && string.Equals(
            left.Settings.DailyFileNameFormat,
            right.Settings.DailyFileNameFormat,
            StringComparison.Ordinal);

    private void ApplyRestoredDailyNoteSettingsIfCurrent(
        DailyNoteSettingsSnapshot? restoredSettings,
        VaultSessionExpectation? expectedSession)
    {
        if (restoredSettings is not null && IsCurrentVaultSession(expectedSession))
        {
            dailyNoteSettingsSnapshot = restoredSettings;
        }
    }

    private void ResetVisibleState()
    {
        CancelBackgroundSearchIndexing();
        vault = null;
        dailyNotes = null;
        dailyNoteNaming = DailyNoteNaming.Default;
        dailyNoteSettingsSnapshot = null;
        searchIndex = null;
        markdownParser = null;
        reviewCoordinator = null;
        revisionStore = null;
        vaultId = null;
        snapshotAreas = [];
        reviewDocuments = [];
        InvalidateReviewQueue();
        VaultRootPath = null;
        IsVaultInitialized = false;
        IsIdentityFrozen = false;
        IsBusy = false;
        ErrorMessage = null;
        foreach (var day in Days)
        {
            day.Dispose();
        }
        Days.Clear();
        HasDays = false;
        loadedDayCount = InitialDayPageSize;
        LoadedDayCount = 0;
        TotalDayCount = 0;
        HasMoreDays = false;
        SelectedDay = null;
        SearchResults.Clear();
        HasSearchResults = false;
        BootstrapIndexedFiles = 0;
        BootstrapPendingCheckboxes = 0;
        BootstrapWasReused = false;
        PendingReviewBlocks = 0;
        PendingReviewDays = 0;
        ClearReviewSelection();
        ScheduleSearchResultsRefresh();
        Areas.Clear();
        Areas.Add(FeedAreaOptionViewModel.NoArea);
        SelectedArea = Areas[0];
        ReviewDestinationArea = Areas[0];
        RebuildSearchAreaOptions(null, selectedAll: true);
        ReviewTaskAreas.Clear();
        IdentitySafePending.Clear();
        HasIdentitySafePending = false;
        ClearPendingRecoveries();
        pendingDocumentConflicts.Clear();
    }

    private async Task ImportIdentitySafePendingAsync(
        string conflictId,
        IReadOnlyList<BlockLocator> locators,
        CancellationToken cancellationToken)
    {
        if (locators.Count == 0 || reviewCoordinator is null)
        {
            return;
        }

        await reviewCoordinator.MarkSafePendingAsync(
                locators,
                "identity-conflict-" + conflictId,
                cancellationToken)
            .ConfigureAwait(true);
        InvalidateReviewQueue();
        RefreshIdentitySafePending();
        await RefreshReviewSummaryAsync(cancellationToken).ConfigureAwait(true);
    }

    private void RefreshIdentitySafePending()
    {
        if (reviewCoordinator is null)
        {
            ShowIdentitySafePending([]);
            return;
        }

        var locators = reviewCoordinator.State.DecisionEvents
            .Where(static value => value.Decision == ReviewDecision.Deferred
                && value.OperationId is not null
                && value.OperationId.StartsWith("identity-conflict-", StringComparison.Ordinal))
            .Select(static value => value.Input)
            .DistinctBy(static value => value.SemanticKey)
            .Where(locator => !reviewCoordinator.State.Resolve(locator).IsTerminal)
            .ToArray();
        ShowIdentitySafePending(locators);
    }

    private void ShowIdentitySafePending(IReadOnlyList<BlockLocator> locators)
    {
        IdentitySafePending.Clear();
        foreach (var locator in locators.OrderBy(static value => value.RelativePath, StringComparer.Ordinal)
                     .ThenBy(static value => value.Occurrence))
        {
            IdentitySafePending.Add(new FeedSafePendingLocatorViewModel(
                locator.RelativePath,
                locator.AreaIdentity,
                locator.BlockKind.ToString(),
                locator.ContentHash,
                locator.Occurrence));
        }

        HasIdentitySafePending = IdentitySafePending.Count > 0;
    }

    private static void ApplyBootstrapBaseline(
        ReviewStateStore state,
        BootstrapResult bootstrap,
        string identity)
    {
        long sequence = 0;
        var baselineEvents = new List<ReviewDecisionEvent>();
        foreach (var fingerprint in bootstrap.Fingerprints.Where(static value => value.BaselineKept))
        {
            sequence++;
            baselineEvents.Add(new ReviewDecisionEvent(
                identity,
                $"{bootstrap.Manifest.OperationId}-{sequence:D20}",
                new CausalEnvelope("bootstrap", sequence, new Dictionary<string, long>()),
                bootstrap.Manifest.CompletedAt ?? bootstrap.Manifest.StartedAt,
                new BlockLocator(
                    fingerprint.RelativePath,
                    fingerprint.AreaIdentity,
                    fingerprint.BlockKind,
                    fingerprint.ContentHash,
                    fingerprint.Occurrence,
                    fingerprint.PreviousContentHash,
                    fingerprint.NextContentHash),
                ReviewDecision.BaselineKept,
                OperationId: bootstrap.Manifest.OperationId));
        }

        state.ReplaceBootstrapBaseline(baselineEvents);
    }

    private static IReadOnlyList<FeedTaskReferenceViewModel> ExtractTaskReferences(
        string markdown,
        Func<string, TaskItemViewModel?>? resolver)
    {
        var result = new List<FeedTaskReferenceViewModel>();
        foreach (Match match in TaskLinkRegex.Matches(markdown))
        {
            var taskId = match.Groups["id"].Value;
            var fallback = match.Groups["title"].Value
                .Replace("\\]", "]", StringComparison.Ordinal)
                .Replace("\\[", "[", StringComparison.Ordinal)
                .Replace("\\(", "(", StringComparison.Ordinal)
                .Replace("\\)", ")", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
            result.Add(new FeedTaskReferenceViewModel(taskId, fallback, resolver?.Invoke(taskId)));
        }

        return result;
    }

    private static MarkdownLivePreviewEditorViewModel CreateMarkdownEditor(
        INoteVault sourceVault,
        VaultDocument document,
        string relativePath,
        string automationIdPrefix,
        Action? afterMarkdownCommit,
        FeedVaultWatchRuntime? runtime,
        Func<bool> canWrite)
    {
        var editor = new MarkdownLivePreviewEditorViewModel(
            automationIdPrefix: automationIdPrefix);
        editor.CommitAccepted = _ => afterMarkdownCommit?.Invoke();
        editor.CommitBlockAsync = async (patch, cancellationToken) =>
        {
            if (!canWrite())
            {
                return MarkdownBlockCommitResult.Rejected(L10n.Get("FeedVaultIdentityFrozen"));
            }

            try
            {
                var write = await sourceVault.WriteAsync(
                        patch.RelativePath,
                        patch.PatchedDocumentRaw,
                        patch.ExpectedRevisionHash,
                        patch.HasUtf8Bom,
                        cancellationToken)
                    .ConfigureAwait(false);
                return MarkdownBlockCommitResult.Accepted(new MarkdownLiveDocumentSnapshot(
                    patch.PatchedDocumentRaw,
                    write.Revision,
                    patch.HasUtf8Bom,
                    patch.RelativePath));
            }
            catch (VaultRevisionConflictException exception)
            {
                if (runtime is not null)
                {
                    try
                    {
                        var current = await sourceVault.ReadAsync(relativePath, cancellationToken)
                            .ConfigureAwait(false);
                        await runtime.ConflictCoordinator.HandleAsync(
                                new VaultWatchChange(
                                    VaultWatchScope.Markdown,
                                    VaultWatchChangeKind.Changed,
                                    relativePath,
                                    OldRelativePath: null,
                                    current?.Revision),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception recoveryException)
                    {
                        return MarkdownBlockCommitResult.Rejected(
                            $"{exception.Message} {recoveryException.Message}");
                    }
                }

                return MarkdownBlockCommitResult.Rejected(exception.Message);
            }
        };
        if (runtime is not null)
        {
            editor.ConfigureDraftPersistence(runtime.VaultId, runtime.Drafts);
            editor.DirtyStateChanged += (_, _) =>
            {
                var snapshot = editor.Snapshot;
                var block = editor.ActiveBlock;
                if (snapshot is null || block?.IsDirty != true)
                {
                    runtime.DirtyDocuments.Clear(relativePath);
                    return;
                }

                var patch = block.CreatePatch(snapshot);
                runtime.DirtyDocuments.Set(new DirtyDocumentBuffer(
                    relativePath,
                    patch.PatchedDocumentRaw,
                    patch.ReplacementRaw,
                    patch.BlockIndex,
                    patch.ExpectedRevisionHash,
                    patch.HasUtf8Bom));
            };
        }

        editor.Load(new MarkdownLiveDocumentSnapshot(
            document.Text,
            document.Revision,
            document.HasUtf8Bom,
            relativePath));
        return editor;
    }

    private static async Task OfferLatestRecoveryDraftAsync(
        MarkdownLivePreviewEditorViewModel editor,
        FeedVaultWatchRuntime runtime,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var draft = (await runtime.Drafts.ListAsync(runtime.VaultId, cancellationToken).ConfigureAwait(false))
            .Where(value => string.Equals(
                NormalizePath(value.RelativePath),
                NormalizePath(relativePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(static value => value.UpdatedAt)
            .FirstOrDefault();
        if (draft is not null)
        {
            editor.OfferRecoveryDraft(draft);
        }
    }

    private void ScheduleRefreshAfterMarkdownCommit()
    {
        if (isDisposed)
        {
            return;
        }

        DispatchNotification(() => _ = RefreshAsync());
    }

    private void DispatchNotification(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (isDisposed)
        {
            return;
        }

        void Apply()
        {
            if (!isDisposed)
            {
                action();
            }
        }

        var dispatcher = notificationDispatcher;
        if (dispatcher is not null)
        {
            dispatcher(Apply);
            return;
        }

        RxSchedulers.MainThreadScheduler.Schedule(Apply);
    }

    private Task DispatchNotificationAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (isDisposed)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            DispatchNotification(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private static string CreateDefaultDeviceId()
    {
        var identity = Environment.MachineName + "|" + Environment.UserName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return "device-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string CreateBootstrapOperationId(
        string identity,
        string deviceId,
        DailyNoteNaming naming)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + deviceId));
        var legacy = "bootstrap-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        if (string.Equals(naming.FileNameFormat, DailyNoteNaming.DefaultFileNameFormat, StringComparison.Ordinal))
        {
            return legacy;
        }

        var layoutHash = SHA256.HashData(Encoding.UTF8.GetBytes(naming.FileNameFormat));
        return legacy + "-layout-" + Convert.ToHexString(layoutHash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private NoteDailyFileNameFormatState CreateDailyNoteFileNameFormatState(
        string? statusMessage = null,
        bool isExternalChange = false,
        bool requiresReload = false) => new(
        dailyNoteNaming.FileNameFormat,
        VaultRootPath,
        dailyNoteSettingsSnapshot?.Revision,
        statusMessage,
        isExternalChange,
        GetSessionGeneration(),
        requiresReload);

    private void PublishDailyNoteFileNameFormatState(NoteDailyFileNameFormatState state) =>
        DailyNoteFileNameFormatChanged?.Invoke(this, state);

    private static string GetDefaultRecoveryRoot(string _)
    {
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            localRoot = Path.GetTempPath();
        }

        return Path.Combine(localRoot, "Unlimotion", "feed-recovery");
    }

    private static readonly Regex TaskLinkRegex = new(
        @"\[(?<title>(?:\\.|[^\]])*)\]\(unlimotion://task/(?<id>[A-Za-z0-9_-]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static Func<string, string?> CreateUniqueAreaNameResolver(IEnumerable<AreaDefinition> areas)
    {
        var uniqueByName = areas
            .Where(static area => !string.IsNullOrWhiteSpace(area.Name))
            .GroupBy(static area => area.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Take(2).Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single().Id,
                StringComparer.OrdinalIgnoreCase);
        return name => string.IsNullOrWhiteSpace(name)
            ? null
            : uniqueByName.GetValueOrDefault(name.Trim());
    }

    private static string CreateHeadingDestinationIdentity(string name)
    {
        var normalized = name.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "heading:" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    public void Dispose()
    {
        CancellationTokenSource? rootReconfigure;
        lock (reconfigureRequestLock)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            rootReconfigure = rootReconfigureCancellation;
            rootReconfigureCancellation = null;
            rootReconfigureGeneration++;
        }

        notificationDispatcher = null;
        DetachTaskSearchTracking();
        CancelBackgroundSearchIndexing();
        CancellationTokenSource? cancellation;
        lock (sessionLock)
        {
            cancellation = sessionCancellation;
            sessionCancellation = null;
        }

        cancellation?.Cancel();

        rootReconfigure?.Cancel();

        CancellationTokenSource? pendingSearch;
        lock (searchLock)
        {
            pendingSearch = searchCancellation;
            searchCancellation = null;
        }

        pendingSearch?.Cancel();
        InvalidateReviewQueue();
        var pendingRootHandoff = TakePendingVaultRootHandoff();
        try
        {
            DisposeVaultSessionAsync(pendingRootHandoff).GetAwaiter().GetResult();
        }
        finally
        {
            pendingRootHandoff?.Dispose();
            foreach (var day in Days)
            {
                day.Dispose();
            }

            Days.Clear();
            disposables.Dispose();
        }
    }

    private sealed class FeedWatchRuntimeSink(
        FeedViewModel owner,
        Func<CancellationToken> expectedSessionProvider) : IFeedVaultWatchRuntimeSink
    {
        public ValueTask ReloadMarkdownAsync(
            DocumentReloadSignal signal,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(expectedSessionProvider(), owner.RefreshMarkdownFromWatcherAsync);
            return ValueTask.CompletedTask;
        }

        public ValueTask ShowMarkdownConflictAsync(
            DocumentConflictState conflict,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(
                expectedSessionProvider(),
                token => owner.ShowDocumentConflictAsync(conflict, token));
            return ValueTask.CompletedTask;
        }

        public ValueTask RefreshAreasAsync(
            VaultWatchChange change,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(expectedSessionProvider(), owner.RefreshAreasFromWatcherAsync);
            return ValueTask.CompletedTask;
        }

        public ValueTask RefreshReviewAsync(
            VaultWatchChange change,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(expectedSessionProvider(), owner.RefreshReviewFromWatcherAsync);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReloadDailyNoteSettingsAsync(
            VaultWatchChange change,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(expectedSessionProvider(), owner.ReloadDailyNoteSettingsAfterWatcherRouteAsync);
            return ValueTask.CompletedTask;
        }

        public ValueTask FreezeForIdentityChangeAsync(
            FeedVaultIdentityFreezeSignal signal,
            CancellationToken cancellationToken)
        {
            owner.ScheduleRuntimeAction(
                expectedSessionProvider(),
                token => owner.FreezeForIdentityChangeAsync(signal, token));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FeedJournalIdentityRecoveryGuard(
        IFeedOperationJournal operations,
        IFeedTaskConversionJournal tasks) : IVaultIdentityRecoveryGuard
    {
        public async Task<bool> HasPendingOperationsAsync(
            string vaultId,
            CancellationToken cancellationToken = default)
        {
            var pendingOperations = await operations.ListPendingAsync(vaultId, cancellationToken)
                .ConfigureAwait(false);
            if (pendingOperations.Count > 0)
            {
                return true;
            }

            var pendingTasks = await tasks.ListPendingAsync(vaultId, cancellationToken)
                .ConfigureAwait(false);
            return pendingTasks.Count > 0;
        }
    }

    private sealed class RecoveryOnlyTaskCreationTarget : IFeedTaskCreationTarget
    {
        public static RecoveryOnlyTaskCreationTarget Instance { get; } = new();

        private RecoveryOnlyTaskCreationTarget()
        {
        }

        public Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "A completed conversion must not recreate its task during recovery verification.");
    }

    private sealed record FeedLoadResult(
        INoteVault Vault,
        DailyNoteService DailyNotes,
        DailyNoteSettingsSnapshot DailyNoteSettings,
        IMarkdownDocumentParser Parser,
        string VaultId,
        FeedReviewSessionCoordinator ReviewCoordinator,
        IRevisionStore RevisionStore,
        BootstrapResult Bootstrap,
        FeedSnapshot Snapshot,
        FeedVaultWatchRuntime? WatchRuntime,
        FeedRuntimeSessionBinding? RuntimeSession,
        bool VaultRootAttached,
        VaultRootHandoffLease? RootHandoff,
        FeedAuxiliaryViewModels? Auxiliary);

    private sealed record FeedAuxiliaryViewModels(
        FeedFilesDrawerViewModel FilesDrawer,
        AreaManagementViewModel AreaManagement) : IDisposable
    {
        public void Dispose()
        {
            FilesDrawer.Dispose();
            AreaManagement.Dispose();
        }
    }

    private sealed record VaultReconfigureRequest(
        long Generation,
        CancellationToken CancellationToken);

    private sealed record VaultSessionExpectation(
        string RootPath,
        CancellationToken SessionToken);

    private sealed class FeedRuntimeSessionBinding
    {
        private readonly object sync = new();
        private CancellationToken sessionToken;

        public void Bind(CancellationToken value)
        {
            lock (sync)
            {
                sessionToken = value;
            }
        }

        public CancellationToken GetToken()
        {
            lock (sync)
            {
                return sessionToken;
            }
        }
    }

    private sealed record VaultReconfigureResult(
        bool Succeeded,
        NoteDailyFileNameFormatState? State,
        string? ErrorMessage,
        bool IsCancelled)
    {
        public static VaultReconfigureResult Success(NoteDailyFileNameFormatState state) =>
            new(true, state, null, false);

        public static VaultReconfigureResult Failure(
            string? errorMessage,
            NoteDailyFileNameFormatState? state = null) =>
            new(false, state, errorMessage, false);

        public static VaultReconfigureResult Cancelled(NoteDailyFileNameFormatState state) =>
            new(false, state, null, true);
    }

    private sealed record FeedSnapshot(
        IReadOnlyList<FeedDayViewModel> Days,
        IReadOnlyList<FeedAreaOptionViewModel> Areas,
        FeedSearchIndex SearchIndex,
        int TotalDayCount,
        IReadOnlyList<FeedReviewDocument> ReviewDocuments);

    private sealed record FeedSearchAreaResolution(
        string? SelectedIdentity,
        IReadOnlyDictionary<string, string> UniqueAreaIdsByName)
    {
        public FeedSearchEntry Normalize(FeedSearchEntry entry)
        {
            if (entry.AreaIdentitiesAreExplicit || entry.AreaIdentities.Count == 0)
            {
                return entry;
            }

            var resolved = entry.AreaIdentities
                .Select(name => UniqueAreaIdsByName.TryGetValue(name, out var areaId) ? areaId : null)
                .Where(static areaId => !string.IsNullOrWhiteSpace(areaId))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();
            return entry with { AreaIdentities = resolved };
        }

        public bool Matches(FeedSearchEntry entry) => SelectedIdentity switch
        {
            null => true,
            "" => entry.AreaIdentities.Count == 0,
            _ => entry.AreaIdentities.Contains(SelectedIdentity, StringComparer.Ordinal)
        };
    }

    private sealed record ReviewQueueBuildRequest(
        long Version,
        IReadOnlyList<FeedReviewDocument> Documents,
        ReviewStateStore State,
        CausalEnvelope Observer,
        DailyNoteNaming Naming,
        Func<CancellationToken, Task>? BuildGateAsync)
    {
        public static ReviewQueueBuildRequest Empty(
            long version,
            Func<CancellationToken, Task>? buildGateAsync) => new(
            version,
            Array.AsReadOnly(Array.Empty<FeedReviewDocument>()),
            new ReviewStateStore(),
            new CausalEnvelope(
                "review-queue-empty",
                1,
                new Dictionary<string, long>(StringComparer.Ordinal)),
            DailyNoteNaming.Default,
            buildGateAsync);
    }

    private sealed record ReviewQueueSnapshot(
        long Version,
        IReadOnlyList<FeedReviewCandidate> Candidates,
        int PendingDays)
    {
        public static ReviewQueueSnapshot Empty { get; } = new(
            -1,
            Array.AsReadOnly(Array.Empty<FeedReviewCandidate>()),
            0);
    }

    private sealed class ReviewQueueBuildState(
        long version,
        Task<ReviewQueueSnapshot> task,
        CancellationTokenSource cancellation) : IDisposable
    {
        private int isDisposed;

        public long Version { get; } = version;

        public Task<ReviewQueueSnapshot> Task { get; } = task;

        public void CancelAndDisposeWhenCompleted()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _ = Task.ContinueWith(
                static (_, state) => ((ReviewQueueBuildState)state!).Dispose(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) == 0)
            {
                cancellation.Dispose();
            }
        }
    }

    private sealed record FeedReviewDocument(string RelativePath, string Text);
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedDayViewModel(
    DateOnly date,
    string relativePath,
    string text,
    string revision,
    int contentBlockCount,
    IReadOnlyList<FeedTaskReferenceViewModel>? taskReferences = null,
    MarkdownLivePreviewEditorViewModel? markdownEditor = null) : IDisposable
{
    public DateOnly Date { get; } = date;

    public string RelativePath { get; } = relativePath;

    public string Text { get; } = text;

    public string Revision { get; } = revision;

    public int ContentBlockCount { get; } = contentBlockCount;

    public string DisplayDate => Date.ToString("D", CultureInfo.CurrentCulture);

    public ObservableCollection<FeedTaskReferenceViewModel> TaskReferences { get; } =
        new(taskReferences ?? []);

    public MarkdownLivePreviewEditorViewModel MarkdownEditor { get; } =
        markdownEditor ?? new MarkdownLivePreviewEditorViewModel();

    public bool HasTaskReferences => TaskReferences.Count > 0;

    public bool IsCollapsed { get; set; }

    public bool IsReviewTarget { get; private set; }

    public string CollapseAutomationId => $"FeedDay-{Date:yyyyMMdd}-CollapseToggle";

    public string AutomationId => $"FeedDay-{Date:yyyyMMdd}";

    public string AutomationName => DisplayDate;

    public override string ToString() => AutomationName;

    public void SetReviewTarget(
        IEnumerable<int> selectedBlockIndices,
        int anchorBlockIndex,
        string selectedMarkdown)
    {
        IsReviewTarget = true;
        IsCollapsed = false;
        MarkdownEditor.SetReviewSelection(
            selectedBlockIndices,
            anchorBlockIndex,
            selectedMarkdown);
    }

    public void ClearReviewTarget()
    {
        IsReviewTarget = false;
        MarkdownEditor.ClearReviewSelection();
    }

    public void ReplaceTaskReferences(IEnumerable<FeedTaskReferenceViewModel> references)
    {
        foreach (var reference in TaskReferences)
        {
            reference.Dispose();
        }

        TaskReferences.Clear();
        foreach (var reference in references)
        {
            TaskReferences.Add(reference);
        }

        MarkdownEditor.SetTaskReferences(TaskReferences);
    }

    public void Dispose()
    {
        foreach (var reference in TaskReferences)
        {
            reference.Dispose();
        }

        TaskReferences.Clear();
        MarkdownEditor.Dispose();
    }
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedThematicDocumentViewModel(
    string relativePath,
    MarkdownLivePreviewEditorViewModel markdownEditor) : IDisposable
{
    public string RelativePath { get; } = relativePath;

    public string DisplayName => Path.GetFileNameWithoutExtension(RelativePath);

    public MarkdownLivePreviewEditorViewModel MarkdownEditor { get; } = markdownEditor;

    public void Dispose() => MarkdownEditor.Dispose();
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedAreaOptionViewModel
{
    public static FeedAreaOptionViewModel NoArea { get; } = new(null);

    public FeedAreaOptionViewModel(
        AreaReference? area,
        string? identity = null,
        bool isExistingHeadingDestination = false,
        bool? isClassificationSelectable = null)
    {
        Area = area;
        Identity = identity ?? area?.Id ?? string.Empty;
        IsExistingHeadingDestination = isExistingHeadingDestination;
        IsClassificationSelectable = isClassificationSelectable
            ?? !string.IsNullOrWhiteSpace(area?.Id);
    }

    public AreaReference? Area { get; }

    public string Identity { get; }

    public string? StableAreaId => Area?.Id;

    public bool HasStableAreaId => !string.IsNullOrWhiteSpace(StableAreaId);

    public bool IsExistingHeadingDestination { get; }

    public bool IsClassificationSelectable { get; }

    public bool IsUnclassifiedHeadingDestination =>
        IsExistingHeadingDestination && !IsClassificationSelectable;

    public string DisplayName => Area?.Name ?? L10n.Get("FeedNoArea");

    public string DestinationDisplayName => IsUnclassifiedHeadingDestination
        ? $"{DisplayName} · {L10n.Get("FeedNoArea")}"
        : DisplayName;
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedSearchResultViewModel
{
    public FeedSearchResultViewModel(
        FeedSearchEntry entry,
        Func<string, string>? resolveAreaName = null)
    {
        Entry = entry;
        var automationHash = SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key));
        AutomationId = "FeedSearchResult-" + Convert.ToHexString(automationHash.AsSpan(0, 8)).ToLowerInvariant();
        DisplayAreas = entry.AreaIdentities.Count == 0
            ? L10n.Get("FeedNoArea")
            : string.Join(", ", entry.AreaIdentities.Select(areaIdentity =>
                resolveAreaName?.Invoke(areaIdentity) ?? areaIdentity));
    }

    public FeedSearchEntry Entry { get; }

    public string AutomationId { get; }

    public string RelativePath => Entry.RelativePath;

    public DateOnly? Date => Entry.Date;

    public string Text => Entry.Text;

    public string Context => Entry.Context;

    public string DisplayAreas { get; }

    public FeedSearchDocumentType Type => Entry.Type;

    public string TaskId => Type == FeedSearchDocumentType.Task
        ? RelativePath["task:".Length..]
        : string.Empty;

    public string TypeDisplayName => L10n.Get(Type switch
    {
        FeedSearchDocumentType.Daily => "FeedSearchTypeDaily",
        FeedSearchDocumentType.Note => "FeedSearchTypeNote",
        FeedSearchDocumentType.Task => "FeedSearchTypeTask",
        _ => throw new ArgumentOutOfRangeException()
    });

    public string DisplaySource => Date?.ToString("D", CultureInfo.CurrentCulture) ?? RelativePath;

    public string AutomationName => $"{TypeDisplayName}\n{DisplaySource}\n{DisplayAreas}\n{Text}\n{Context}";

    public override string ToString() => AutomationName;
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedSearchAreaOptionViewModel
{
    public static FeedSearchAreaOptionViewModel All { get; } = new(null, null, "FeedSearchAreaAll", true);

    public static FeedSearchAreaOptionViewModel NoArea { get; } = new(string.Empty, null, "FeedNoArea", false);

    public FeedSearchAreaOptionViewModel(string areaIdentity, string displayName)
        : this(areaIdentity, displayName, null, false)
    {
    }

    private FeedSearchAreaOptionViewModel(
        string? areaIdentity,
        string? displayName,
        string? resourceKey,
        bool isAll)
    {
        AreaIdentity = areaIdentity;
        this.displayName = displayName;
        this.resourceKey = resourceKey;
        IsAll = isAll;
    }

    private readonly string? displayName;
    private readonly string? resourceKey;

    public string? AreaIdentity { get; }

    public bool IsAll { get; }

    public string DisplayName => resourceKey is null ? displayName ?? string.Empty : L10n.Get(resourceKey);
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedSearchTypeOptionViewModel(
    FeedSearchDocumentType? type,
    string resourceKey)
{
    public static FeedSearchTypeOptionViewModel All { get; } = new(null, "FeedSearchTypeAll");

    public FeedSearchDocumentType? Type { get; } = type;

    public string DisplayName => L10n.Get(resourceKey);
}

public sealed class FeedSearchNavigationRequestedEventArgs(
    string relativePath,
    MarkdownLivePreviewEditorViewModel editor,
    int blockIndex,
    FeedDayViewModel? day) : EventArgs
{
    public string RelativePath { get; } = relativePath;

    public MarkdownLivePreviewEditorViewModel Editor { get; } = editor;

    public int BlockIndex { get; } = blockIndex;

    public FeedDayViewModel? Day { get; } = day;
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedReviewSelectionViewModel(
    DateOnly date,
    string relativePath,
    string selectedMarkdown,
    int selectedBlockCount,
    bool canExpandUp,
    bool canExpandDown,
    bool canShrinkUp,
    bool canShrinkDown)
{
    public DateOnly Date { get; } = date;

    public string RelativePath { get; } = relativePath;

    public string DisplayDate => Date.ToString("D", CultureInfo.CurrentCulture);

    public string SelectedMarkdown { get; } = selectedMarkdown;

    public int SelectedBlockCount { get; } = selectedBlockCount;

    public bool CanExpandUp { get; } = canExpandUp;

    public bool CanExpandDown { get; } = canExpandDown;

    public bool CanShrinkUp { get; } = canShrinkUp;

    public bool CanShrinkDown { get; } = canShrinkDown;
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedTaskAreaOptionViewModel
{
    private readonly Action<string, bool>? selectionChanged;
    private bool isSelected;

    public FeedTaskAreaOptionViewModel(
        FeedAreaOptionViewModel area,
        bool isSelected,
        Action<string, bool>? selectionChanged = null)
    {
        Area = area;
        this.isSelected = isSelected;
        this.selectionChanged = selectionChanged;
    }

    public FeedAreaOptionViewModel Area { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            selectionChanged?.Invoke(Area.Identity, value);
        }
    }
}

public enum FeedBrokenTaskReferenceAction
{
    Find,
    Unlink,
    RestoreRevision
}

public sealed class FeedTaskReferenceViewModel : ReactiveObject, IDisposable
{
    private readonly PropertyChangedEventHandler? taskPropertyChanged;

    public FeedTaskReferenceViewModel(
        string taskId,
        string fallbackTitle,
        TaskItemViewModel? task)
    {
        TaskId = taskId;
        FallbackTitle = fallbackTitle;
        Task = task;
        if (task is not null)
        {
            taskPropertyChanged = (_, args) =>
            {
                if (string.IsNullOrEmpty(args.PropertyName)
                    || string.Equals(args.PropertyName, nameof(TaskItemViewModel.Title), StringComparison.Ordinal))
                {
                    this.RaisePropertyChanged(nameof(DisplayTitle));
                }
            };
            ((INotifyPropertyChanged)task).PropertyChanged += taskPropertyChanged;
        }
    }

    public string TaskId { get; }

    public string FallbackTitle { get; }

    public TaskItemViewModel? Task { get; }

    public bool IsResolved => Task is not null;

    public bool IsBroken => Task is null;

    public string DisplayTitle => Task?.Title ?? FallbackTitle;

    public string StatusAutomationId => $"FeedTask-{TaskId}-StatusPicker";

    public string TitleAutomationId => $"FeedTask-{TaskId}-TitleButton";

    public void Dispose()
    {
        if (Task is not null && taskPropertyChanged is not null)
        {
            ((INotifyPropertyChanged)Task).PropertyChanged -= taskPropertyChanged;
        }
    }
}

internal sealed class FeedActionCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);
}
