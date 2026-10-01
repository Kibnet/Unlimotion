using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class FeedControl : UserControl
{
    public Func<Task>? CloseWorkspaceTabRequested { get; set; }
    public static readonly StyledProperty<bool> ShowChronologyProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(ShowChronology));

    public static readonly StyledProperty<bool> ShowReviewBannerProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(ShowReviewBanner));

    public static readonly StyledProperty<FeedThematicDocumentViewModel?> DisplayedDocumentProperty =
        AvaloniaProperty.Register<FeedControl, FeedThematicDocumentViewModel?>(nameof(DisplayedDocument));

    public bool ShowChronology
    {
        get => GetValue(ShowChronologyProperty);
        private set => SetValue(ShowChronologyProperty, value);
    }

    public bool ShowReviewBanner
    {
        get => GetValue(ShowReviewBannerProperty);
        private set => SetValue(ShowReviewBannerProperty, value);
    }

    public FeedThematicDocumentViewModel? DisplayedDocument
    {
        get => GetValue(DisplayedDocumentProperty);
        private set => SetValue(DisplayedDocumentProperty, value);
    }

    private bool useWorkspaceTabs;
    public static readonly StyledProperty<bool> ShowStandaloneDocumentChromeProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(ShowStandaloneDocumentChrome), true);
    public bool ShowStandaloneDocumentChrome
    {
        get => GetValue(ShowStandaloneDocumentChromeProperty);
        private set => SetValue(ShowStandaloneDocumentChromeProperty, value);
    }
    private WorkspaceLocation? workspaceLocation;
    private Guid workspaceTabId;
    private bool isWorkspacePaneActive;
    private readonly Func<IEnumerable<MarkdownLiveBlockViewModel>> activeVisibleBlocksProvider;

    public bool UseWorkspaceTabs
    {
        get => useWorkspaceTabs;
        set
        {
            useWorkspaceTabs = value;
            ShowStandaloneDocumentChrome = !value;
            if (value && DataContext is FeedViewModel feed) feed.EnableWorkspaceAreaPresentation();
            if (DocumentHost is not null) DocumentHost.ShowDocumentTabs = !value;
            RefreshDisplayAreaBindings();
            RefreshDisplayedRoute();
        }
    }

    public void SetWorkspaceRoute(Guid tabId, WorkspaceLocation? location, bool isActive)
    {
        if (workspaceTabId == tabId && workspaceLocation == location
            && isWorkspacePaneActive == isActive) return;
        var routeChanged = workspaceTabId != tabId || workspaceLocation != location;
        workspaceTabId = tabId;
        workspaceLocation = location;
        isWorkspacePaneActive = isActive;
        if (observedViewModel is { } feed)
        {
            if (isActive && location?.Mode == WorkspaceMode.Feed)
                feed.ActivePresentationVisibleBlocks = activeVisibleBlocksProvider;
            else if (ReferenceEquals(feed.ActivePresentationVisibleBlocks, activeVisibleBlocksProvider))
                feed.ActivePresentationVisibleBlocks = null;
        }
        if (location?.Kind == WorkspaceLocationKind.Feed)
            RestoreWorkspaceAreaFilter(location.StateKey);
        RefreshDisplayedRoute();
        UpdateReviewBannerVisibility();
        if (!routeChanged) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (workspaceTabId != tabId || workspaceLocation != location) return;
            if (location?.Kind == WorkspaceLocationKind.Feed)
                ChronologyScroller.Offset = new Vector(0, location.ScrollOffset ?? 0);
            else if (location?.Kind == WorkspaceLocationKind.Note)
                DocumentScroller.Offset = new Vector(0, location.ScrollOffset ?? 0);
        }, DispatcherPriority.Loaded);
    }

    public WorkspaceLocation? CaptureWorkspaceLocation()
    {
        if (!useWorkspaceTabs || workspaceLocation is null) return null;
        if (workspaceLocation.Kind == WorkspaceLocationKind.Note)
            return workspaceLocation with { ScrollOffset = DocumentScroller.Offset.Y };
        if (workspaceLocation.Kind != WorkspaceLocationKind.Feed)
            return workspaceLocation;

        var offset = ChronologyScroller.Offset.Y;
        var viewportHeight = ChronologyScroller.Viewport.Height;
        var visibleDay = ChronologyList.GetRealizedContainers()
            .Select(control => (Control: control, Day: control.DataContext as FeedDayViewModel,
                Y: control.TranslatePoint(default, ChronologyScroller)?.Y))
            .Where(item => item.Day is not null && item.Y is { } y
                && y < viewportHeight && y + item.Control.Bounds.Height > 0)
            .OrderBy(item => item.Y)
            .FirstOrDefault();
        if (visibleDay.Day is null)
            return workspaceLocation with { ScrollOffset = offset, StateKey = workspaceFilterKey };

        var visibleBlock = visibleDay.Control.GetVisualDescendants().OfType<Control>()
            .Where(control => control.DataContext is MarkdownLiveBlockViewModel
                && control.IsEffectivelyVisible
                && string.Equals(AutomationProperties.GetAutomationId(control),
                    ((MarkdownLiveBlockViewModel)control.DataContext).BlockAutomationId,
                    StringComparison.Ordinal))
            .Select(control => (Block: (MarkdownLiveBlockViewModel)control.DataContext!,
                Y: control.TranslatePoint(default, ChronologyScroller)?.Y,
                Height: control.Bounds.Height))
            .Where(item => item.Y is { } y && y < viewportHeight && y + item.Height > 0)
            .OrderBy(item => item.Y)
            .FirstOrDefault();
        return WorkspaceLocation.ForFeedDay(visibleDay.Day.RelativePath,
            visibleDay.Day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            visibleBlock.Block?.Index.ToString(CultureInfo.InvariantCulture),
            workspaceFilterKey, offset);
    }

    private FeedViewModel? observedViewModel;
    private INotifyPropertyChanged? observedPropertyChanges;
    private Vector savedChronologyOffset;
    private bool hasSavedChronologyOffset;
    private bool wasSearchActive;
    private bool suppressNextChronologyRestore;
    private bool loadOlderDaysWhenIdle;
    private bool userReachedChronologyEnd;
    private bool isRestoringChronologyAnchor;
    private FeedThematicDocumentViewModel? displayedDocument;
    private WorkspaceOpenDisposition pendingSearchResultDisposition = WorkspaceOpenDisposition.CurrentTab;

    public FeedControl()
    {
        InitializeComponent();
        activeVisibleBlocksProvider = EnumerateWorkspaceVisibleBlocks;
        InitializeReadingNavigation();
        ChronologyScroller.ScrollChanged += OnChronologyScrollChanged;
        DocumentScroller.ScrollChanged += (_, _) =>
        {
            if (!UseWorkspaceTabs && displayedDocument is not null)
                displayedDocument.ScrollOffset = DocumentScroller.Offset.Y;
        };
        AddHandler(KeyDownEvent, OnDocumentKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnFeedBackgroundPressed, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => ObserveDataContext();
        AttachedToVisualTree += (_, _) =>
        {
            ObserveDataContext();
            UpdateLocalToolbarLayout();
            ObserveContextSources();
            UpdateNavigationState();
        };
        DetachedFromVisualTree += (_, _) => StopObservingDataContext();
        SizeChanged += (_, _) => UpdateLocalToolbarLayout();
    }

    private void OnFeedBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not FeedViewModel feed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || e.Source is not Control source || source is not (Panel or Border or Avalonia.Controls.Presenters.ScrollContentPresenter)) return;
        if (source.GetVisualAncestors().Any(control => control is MarkdownBlockLivePreviewEditor or TextBox or Button
                or Avalonia.Controls.Primitives.ScrollBar or MenuBase)) return;
        feed.BlockSelection.Clear();
    }

    private void UpdateLocalToolbarLayout()
    {
        if (FeedLocalToolbar is null || FeedAreaFilterButton is null)
        {
            return;
        }

        var compact = Bounds.Width > 0 && Bounds.Width < 620;
        Grid.SetRow(FeedAreaFilterButton, 0);
        Grid.SetColumn(FeedAreaFilterButton, 0);
        Grid.SetColumnSpan(FeedAreaFilterButton, compact ? 4 : 1);
        FeedAreaFilterButton.Width = compact ? double.NaN : 220;
        FeedAreaFilterButton.HorizontalAlignment = compact
            ? Avalonia.Layout.HorizontalAlignment.Stretch
            : Avalonia.Layout.HorizontalAlignment.Left;

        SetToolbarButtonPosition(FeedAreasButton, compact, 1);
        SetToolbarButtonPosition(FeedFilesButton, compact, 2);
        SetToolbarButtonPosition(FeedRefreshButton, compact, 3);
    }

    private static void SetToolbarButtonPosition(Control button, bool compact, int column)
    {
        Grid.SetRow(button, compact ? 1 : 0);
        Grid.SetColumn(button, column);
    }

    private async void OnChronologyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateNavigationState();
        if (!UseWorkspaceTabs && DataContext is FeedViewModel feed)
            feed.ChronologyScrollOffset = ChronologyScroller.Offset.Y;
        // A collapse/expand or a virtualized page changes extent without an
        // intentional scroll.  It must never be interpreted as a request for
        // another page at the bottom of the chronology.
        if (isRestoringChronologyAnchor || e.ExtentDelta.Y != 0 || e.OffsetDelta.Y <= 0) return;
        userReachedChronologyEnd = true;
        await TryLoadOlderDaysFromCurrentPositionAsync();
    }

    private async Task TryLoadOlderDaysFromCurrentPositionAsync()
    {
        if (DataContext is not FeedViewModel viewModel
            || !viewModel.HasMoreDays
            || viewModel.IsLoadingOlderDays
            || !userReachedChronologyEnd)
        {
            return;
        }

        var scrollViewer = FindChronologyScrollViewer();
        if (scrollViewer is null
            || scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height
                > scrollViewer.Viewport.Height)
        {
            return;
        }

        if (viewModel.IsBusy)
        {
            loadOlderDaysWhenIdle = true;
            return;
        }

        loadOlderDaysWhenIdle = false;
        var anchor = scrollViewer.Offset;
        await viewModel.LoadOlderDaysAsync();
        Dispatcher.UIThread.Post(
            () => RestoreChronologyAnchor(scrollViewer, anchor),
            DispatcherPriority.Render);
    }

    private void RestoreChronologyAnchor(ScrollViewer scrollViewer, Vector anchor)
    {
        if (!ReferenceEquals(scrollViewer, FindChronologyScrollViewer())) return;
        isRestoringChronologyAnchor = true;
        try { scrollViewer.Offset = anchor; }
        finally { isRestoringChronologyAnchor = false; }
    }

    private async void OnMarkdownLinkInvoked(object? sender, MarkdownLinkInvokedEventArgs e)
    {
        const string taskPrefix = "unlimotion://task/";
        if (DataContext is not FeedViewModel viewModel)
        {
            return;
        }

        if (e.Target.StartsWith(taskPrefix, StringComparison.Ordinal))
            viewModel.OpenTaskReference(e.Target[taskPrefix.Length..], e.Disposition);
        else if (sender is MarkdownBlockLivePreviewEditor { DataContext: MarkdownLivePreviewEditorViewModel editor })
            await viewModel.OpenVaultLinkAsync(e.Target, editor.Snapshot?.RelativePath,
                e.Kind == MarkdownInlineTokenKind.WikiLink, e.Disposition);
    }

    private async void OnBrokenTaskReferenceActionInvoked(
        object? sender,
        BrokenTaskReferenceActionEventArgs e)
    {
        if (sender is not MarkdownBlockLivePreviewEditor
            {
                DataContext: MarkdownLivePreviewEditorViewModel editor
            }
            || DataContext is not FeedViewModel viewModel)
        {
            return;
        }

        await viewModel.HandleBrokenTaskReferenceAsync(
            editor,
            e.BlockIndex,
            e.TaskId,
            e.Action);
    }

    private void OnTaskReferenceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FeedTaskReferenceViewModel reference }
            && DataContext is FeedViewModel viewModel)
        {
            viewModel.OpenTaskReference(reference.TaskId);
            e.Handled = true;
        }
    }

    private void OnSearchResultPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        pendingSearchResultDisposition = (e.KeyModifiers & KeyModifiers.Control) != 0
            ? WorkspaceOpenDisposition.NewTab
            : WorkspaceOpenDisposition.CurrentTab;
    }

    private void OnSearchResultActionsLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not DropDownButton actions || actions.Flyout is not null) return;
        var menu = new MenuFlyout();
        Add("WorkspaceOpenInNewTab", WorkspaceOpenDisposition.NewTab);
        Add("WorkspaceOpenBeside", WorkspaceOpenDisposition.AdjacentPane);
        actions.Flyout = menu;

        void Add(string labelKey, WorkspaceOpenDisposition disposition)
        {
            var item = new MenuItem { Header = L10n.Get(labelKey), MinHeight = 44 };
            item.Click += async (_, _) =>
            {
                if (actions.DataContext is FeedSearchResultViewModel result
                    && DataContext is FeedViewModel viewModel)
                    await viewModel.OpenSearchResultAsync(result, disposition);
            };
            menu.Items.Add(item);
        }
    }

    private async void OnSearchResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FeedSearchResultViewModel result }
            && DataContext is FeedViewModel viewModel
            && viewModel.OpenSearchResultCommand.CanExecute(result))
        {
            var disposition = pendingSearchResultDisposition;
            pendingSearchResultDisposition = WorkspaceOpenDisposition.CurrentTab;
            await viewModel.OpenSearchResultAsync(result, disposition);
            e.Handled = true;
        }
    }

    private void ObserveDataContext()
    {
        if (ReferenceEquals(observedViewModel, DataContext))
        {
            return;
        }

        StopObservingDataContext();
        observedViewModel = DataContext as FeedViewModel;
        if (observedViewModel is null)
        {
            return;
        }

        observedPropertyChanges = (object)observedViewModel as INotifyPropertyChanged;
        if (observedPropertyChanges is not null)
        {
            observedPropertyChanges.PropertyChanged += OnViewModelPropertyChanged;
        }
        observedViewModel.SearchNavigationStarting += OnSearchNavigationStarting;
        observedViewModel.SearchNavigationRequested += OnSearchNavigationRequested;
        observedViewModel.ReviewNavigationRequested += OnReviewNavigationRequested;
        observedViewModel.VisibleDays.CollectionChanged += OnVisibleDaysChanged;
        observedViewModel.Days.CollectionChanged += OnSourceDaysChanged;
        observedViewModel.FeedAreaFilterOptions.CollectionChanged += OnSourceAreaOptionsChanged;
        observedViewModel.AttachPresentation();
        wasSearchActive = observedViewModel.IsSearchActive;
        if (UseWorkspaceTabs) observedViewModel.EnableWorkspaceAreaPresentation();
        RefreshDisplayAreaBindings();
        RefreshDisplayedRoute();
        UpdateReviewBannerVisibility();
        ObserveContextSources();
        UpdateNavigationState();
    }

    private void StopObservingDataContext()
    {
        if (observedViewModel is null)
        {
            return;
        }

        if (observedPropertyChanges is not null)
        {
            observedPropertyChanges.PropertyChanged -= OnViewModelPropertyChanged;
            observedPropertyChanges = null;
        }
        observedViewModel.SearchNavigationStarting -= OnSearchNavigationStarting;
        observedViewModel.SearchNavigationRequested -= OnSearchNavigationRequested;
        observedViewModel.ReviewNavigationRequested -= OnReviewNavigationRequested;
        observedViewModel.VisibleDays.CollectionChanged -= OnVisibleDaysChanged;
        observedViewModel.Days.CollectionChanged -= OnSourceDaysChanged;
        observedViewModel.FeedAreaFilterOptions.CollectionChanged -= OnSourceAreaOptionsChanged;
        StopWorkspaceDayBlockObservation();
        if (ReferenceEquals(observedViewModel.ActivePresentationVisibleBlocks, activeVisibleBlocksProvider))
            observedViewModel.ActivePresentationVisibleBlocks = null;
        StopObservingContextSources();
        observedViewModel = null;
        loadOlderDaysWhenIdle = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        if (!ReferenceEquals(sender, observedViewModel)) return;
        if (!UseWorkspaceTabs && e.PropertyName == nameof(FeedViewModel.ChronologyScrollOffset)
            && observedViewModel is { } owner)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(owner, observedViewModel) && ChronologyScroller.IsVisible)
                    ChronologyScroller.Offset = new Vector(ChronologyScroller.Offset.X,
                        Math.Clamp(owner.ChronologyScrollOffset, 0, Math.Max(0, ChronologyScroller.Extent.Height - ChronologyScroller.Viewport.Height)));
            }, DispatcherPriority.Render);
        }
        observedViewModel?.AttachPresentation();
        if (!UseWorkspaceTabs) RefreshDisplayAreaBindings();
        if (e.PropertyName is nameof(FeedViewModel.OpenedThematicFile)
            or nameof(FeedViewModel.IsChronologyVisible)) RefreshDisplayedRoute();
        if (e.PropertyName is nameof(FeedViewModel.IsReviewBannerVisible)
            or nameof(FeedViewModel.IsChronologyVisible)) UpdateReviewBannerVisibility();
        ObserveContextSources();
        UpdateNavigationState();
        if (e.PropertyName is nameof(FeedViewModel.SearchQuery) or nameof(FeedViewModel.IsSearchActive))
        {
            UpdateSearchModeState();
        }

        if (e.PropertyName == nameof(FeedViewModel.IsBusy)
            && observedViewModel is { IsBusy: false }
            && loadOlderDaysWhenIdle)
        {
            _ = TryLoadOlderDaysFromCurrentPositionAsync();
        }
    }

    private void RefreshDisplayedRoute()
    {
        var document = useWorkspaceTabs
            ? workspaceLocation?.Kind == WorkspaceLocationKind.Note
                ? observedViewModel?.DocumentWorkspace.Find(workspaceLocation.Id)
                : null
            : observedViewModel?.OpenedThematicFile;
        ShowChronology = useWorkspaceTabs
            ? workspaceLocation?.Kind == WorkspaceLocationKind.Feed
            : observedViewModel?.IsChronologyVisible == true;
        UpdateReviewBannerVisibility();
        if (ReferenceEquals(displayedDocument, document)) return;

        observedViewModel?.BlockSelection.Clear();
        displayedDocument = document;
        DisplayedDocument = document;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(document, displayedDocument)) return;
            DocumentScroller.Offset = new Vector(0, useWorkspaceTabs
                ? workspaceLocation?.ScrollOffset ?? 0
                : document?.ScrollOffset ?? 0);
            if (useWorkspaceTabs && !isWorkspacePaneActive) return;
            if (document?.MarkdownEditor.ActiveBlock is not { } active) return;
            var input = DocumentScroller.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control =>
                AutomationProperties.GetAutomationId(control) == active.EditorAutomationId);
            if (input is null) return;
            var caret = document.MarkdownEditor.LastCaretPosition;
            input.Focus();
            if (caret is null) return;
            input.SelectionStart = Math.Clamp(caret.SelectionStart, 0, input.Text?.Length ?? 0);
            input.SelectionEnd = Math.Clamp(caret.SelectionEnd, 0, input.Text?.Length ?? 0);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateReviewBannerVisibility() => ShowReviewBanner =
        observedViewModel?.IsReviewBannerVisible == true
        && ShowChronology
        && (!useWorkspaceTabs || isWorkspacePaneActive);

    private void UpdateSearchModeState()
    {
        var isSearchActive = observedViewModel?.IsSearchActive == true;
        if (isSearchActive == wasSearchActive)
        {
            return;
        }

        if (isSearchActive)
        {
            var scrollViewer = FindChronologyScrollViewer();
            if (scrollViewer is not null)
            {
                savedChronologyOffset = scrollViewer.Offset;
                hasSavedChronologyOffset = true;
            }
        }
        else if (suppressNextChronologyRestore)
        {
            suppressNextChronologyRestore = false;
        }
        else if (hasSavedChronologyOffset)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    var scrollViewer = FindChronologyScrollViewer();
                    if (scrollViewer is not null)
                    {
                        scrollViewer.Offset = savedChronologyOffset;
                    }
                },
                DispatcherPriority.Loaded);
        }

        wasSearchActive = isSearchActive;
    }

    private async void OnDocumentKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FeedViewModel feed) return;
        if (e.Key == Key.Escape) feed.BlockSelection.Clear();
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.W && UseWorkspaceTabs && displayedDocument is not null
            && CloseWorkspaceTabRequested is { } closeTab)
        {
            e.Handled = true;
            await closeTab();
        }
        else if (e.Key == Key.W && !UseWorkspaceTabs && feed.HasOpenedThematicFile)
        {
            e.Handled = true;
            await feed.CloseDocumentAsync(feed.OpenedThematicFile);
        }
        else if (e.Key == Key.Tab && !UseWorkspaceTabs && feed.DocumentWorkspace.Documents.Count > 0)
        {
            e.Handled = true;
            var documents = feed.DocumentWorkspace.Documents;
            var index = feed.OpenedThematicFile is null ? 0 : documents.IndexOf(feed.OpenedThematicFile) + 1;
            index = (index + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1) + documents.Count + 1) % (documents.Count + 1);
            await feed.ActivateDocumentAsync(index == 0 ? null : documents[index - 1]);
        }
    }

    private async void OnDisplayedDocumentCloseClick(object? sender, RoutedEventArgs e)
    {
        if (UseWorkspaceTabs)
        {
            if (CloseWorkspaceTabRequested is { } closeTab) await closeTab();
        }
        else if (observedViewModel?.OpenedThematicFile is { } document)
        {
            await observedViewModel.CloseDocumentAsync(document);
        }
    }

    private void OnSearchNavigationStarting(object? sender, EventArgs e)
    {
        if (!UseWorkspaceTabs || isWorkspacePaneActive)
            suppressNextChronologyRestore = true;
    }

    private void OnSearchNavigationRequested(object? sender, FeedSearchNavigationRequestedEventArgs e)
        => NavigateToFeedBlock(e);

    private void OnReviewNavigationRequested(object? sender, FeedSearchNavigationRequestedEventArgs e)
        => NavigateToFeedBlock(e);

    private void NavigateToFeedBlock(FeedSearchNavigationRequestedEventArgs e)
    {
        if (UseWorkspaceTabs && ((!isWorkspacePaneActive && !(DataContext is FeedViewModel { IsReviewActive: true }))
            || !string.Equals(workspaceLocation?.Id, e.RelativePath, StringComparison.OrdinalIgnoreCase)))
            return;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (e.Day is not null)
                {
                    if (DataContext is FeedViewModel viewModel)
                    {
                        viewModel.SelectedDay = e.Day;
                    }

                    ChronologyList.ScrollIntoView(e.Day);
                    var dayControl = this.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(control => string.Equals(
                            AutomationProperties.GetAutomationId(control),
                            e.Day.AutomationId,
                            StringComparison.Ordinal));
                    dayControl?.BringIntoView();
                }

                Dispatcher.UIThread.Post(
                    () => FocusNavigatedBlock(e, remainingAttempts: 8),
                    DispatcherPriority.Loaded);
            },
            DispatcherPriority.Loaded);
    }

    private void FocusNavigatedBlock(FeedSearchNavigationRequestedEventArgs e, int remainingAttempts)
    {
        if (UseWorkspaceTabs && ((!isWorkspacePaneActive && !(DataContext is FeedViewModel { IsReviewActive: true }))
            || !string.Equals(workspaceLocation?.Id, e.RelativePath, StringComparison.OrdinalIgnoreCase)))
            return;
        var scroller = e.Day is null ? DocumentScroller : ChronologyScroller;
        scroller.UpdateLayout();
        var block = e.Editor.Blocks.FirstOrDefault(candidate => candidate.Index == e.BlockIndex);
        if (block is null)
        {
            return;
        }

        var preview = this.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .FirstOrDefault(control => string.Equals(
                AutomationProperties.GetAutomationId(control),
                block.PreviewAutomationId,
                StringComparison.Ordinal));
        if (preview is not null)
        {
            preview.BringIntoView();
            scroller.UpdateLayout();
            if (!string.IsNullOrWhiteSpace(e.SearchText))
            {
                var matchIndex = block.PreviewText.IndexOf(e.SearchText, StringComparison.OrdinalIgnoreCase);
                var matchingText = preview.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                if (matchIndex >= 0 && matchingText is not null)
                {
                    var line = matchingText.TextLayout.HitTestTextPosition(matchIndex);
                    if (matchingText.TranslatePoint(default, scroller) is { } origin)
                    {
                        var targetY = scroller.Offset.Y + origin.Y + line.Y - scroller.Viewport.Height / 3;
                        scroller.Offset = new Vector(scroller.Offset.X,
                            Math.Clamp(targetY, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
                    }
                }
            }
            if (!isWorkspacePaneActive && UseWorkspaceTabs) return;
            if (preview.Focus())
            {
                return;
            }
        }

        if (remainingAttempts <= 0)
        {
            return;
        }

        if (e.Day is not null)
        {
            ChronologyList.ScrollIntoView(e.Day);
        }

        Dispatcher.UIThread.Post(
            () => FocusNavigatedBlock(e, remainingAttempts - 1),
            DispatcherPriority.Loaded);
    }

    private ScrollViewer? FindChronologyScrollViewer() => ChronologyScroller;
}
