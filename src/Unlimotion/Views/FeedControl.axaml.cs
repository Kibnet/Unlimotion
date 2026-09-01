using System;
using System.ComponentModel;
using System.Linq;
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

    public FeedControl()
    {
        InitializeComponent();
        ChronologyScroller.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            OnChronologyScrollChanged,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        DataContextChanged += (_, _) => ObserveDataContext();
        AttachedToVisualTree += (_, _) =>
        {
            ObserveDataContext();
            UpdateLocalToolbarLayout();
        };
        DetachedFromVisualTree += (_, _) => StopObservingDataContext();
        SizeChanged += (_, _) => UpdateLocalToolbarLayout();
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

    private void OnChronologyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not FeedViewModel viewModel
            || !viewModel.HasMoreDays
            || viewModel.IsLoadingOlderDays
            || e.ExtentDelta == default && e.OffsetDelta == default)
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

        if (viewModel.LoadOlderDaysCommand.CanExecute(null))
        {
            viewModel.LoadOlderDaysCommand.Execute(null);
        }
    }

    private void OnMarkdownLinkInvoked(object? sender, MarkdownLinkInvokedEventArgs e)
    {
        const string taskPrefix = "unlimotion://task/";
        if (DataContext is not FeedViewModel viewModel
            || !e.Target.StartsWith(taskPrefix, StringComparison.Ordinal))
        {
            return;
        }

        viewModel.OpenTaskReference(e.Target[taskPrefix.Length..]);
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
        wasSearchActive = observedViewModel.IsSearchActive;
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
        observedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FeedViewModel.SearchQuery) or nameof(FeedViewModel.IsSearchActive))
        {
            UpdateSearchModeState();
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
