using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class FeedControl : UserControl
{
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

    public FeedControl()
    {
        InitializeComponent();
        InitializeReadingNavigation();
        ChronologyScroller.ScrollChanged += OnChronologyScrollChanged;
        DocumentScroller.ScrollChanged += (_, _) =>
        {
            if (displayedDocument is not null) displayedDocument.ScrollOffset = DocumentScroller.Offset.Y;
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
            viewModel.OpenTaskReference(e.Target[taskPrefix.Length..]);
        else if (sender is MarkdownBlockLivePreviewEditor { DataContext: MarkdownLivePreviewEditorViewModel editor })
            await viewModel.OpenVaultLinkAsync(e.Target, editor.Snapshot?.RelativePath, e.Kind == MarkdownInlineTokenKind.WikiLink);
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

    private void OnSearchResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FeedSearchResultViewModel result }
            && DataContext is FeedViewModel viewModel
            && viewModel.OpenSearchResultCommand.CanExecute(result))
        {
            viewModel.OpenSearchResultCommand.Execute(result);
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
        observedViewModel.AttachPresentation();
        wasSearchActive = observedViewModel.IsSearchActive;
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
        observedViewModel?.AttachPresentation();
        if (e.PropertyName == nameof(FeedViewModel.OpenedThematicFile))
        {
            observedViewModel?.BlockSelection.Clear();
            displayedDocument = observedViewModel?.OpenedThematicFile;
            var document = displayedDocument;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(document, displayedDocument))
                {
                    DocumentScroller.Offset = new Vector(0, document?.ScrollOffset ?? 0);
                    if (document?.MarkdownEditor.ActiveBlock is { } active)
                    {
                        var input = DocumentScroller.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control =>
                            AutomationProperties.GetAutomationId(control) == active.EditorAutomationId);
                        if (input is not null)
                        {
                            var caret = document.MarkdownEditor.LastCaretPosition;
                            input.Focus();
                            if (caret is not null)
                            {
                                input.SelectionStart = Math.Clamp(caret.SelectionStart, 0, input.Text?.Length ?? 0);
                                input.SelectionEnd = Math.Clamp(caret.SelectionEnd, 0, input.Text?.Length ?? 0);
                            }
                        }
                    }
                }
            }, DispatcherPriority.Loaded);
        }
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
        if (e.Key == Key.W && feed.HasOpenedThematicFile)
        {
            e.Handled = true;
            await feed.CloseDocumentAsync(feed.OpenedThematicFile);
        }
        else if (e.Key == Key.Tab && feed.DocumentWorkspace.Documents.Count > 0)
        {
            e.Handled = true;
            var documents = feed.DocumentWorkspace.Documents;
            var index = feed.OpenedThematicFile is null ? 0 : documents.IndexOf(feed.OpenedThematicFile) + 1;
            index = (index + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1) + documents.Count + 1) % (documents.Count + 1);
            await feed.ActivateDocumentAsync(index == 0 ? null : documents[index - 1]);
        }
    }

    private void OnSearchNavigationStarting(object? sender, EventArgs e) => suppressNextChronologyRestore = true;

    private void OnSearchNavigationRequested(object? sender, FeedSearchNavigationRequestedEventArgs e)
        => NavigateToFeedBlock(e);

    private void OnReviewNavigationRequested(object? sender, FeedSearchNavigationRequestedEventArgs e)
        => NavigateToFeedBlock(e);

    private void NavigateToFeedBlock(FeedSearchNavigationRequestedEventArgs e)
    {
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
        ChronologyList.UpdateLayout();
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
