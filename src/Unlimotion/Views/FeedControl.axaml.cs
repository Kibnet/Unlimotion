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
        QuickCaptureTextBox.AddHandler(
            InputElement.KeyDownEvent,
            OnQuickCaptureKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DataContextChanged += (_, _) => ObserveDataContext();
        AttachedToVisualTree += (_, _) => ObserveDataContext();
        DetachedFromVisualTree += (_, _) => StopObservingDataContext();
    }

    private void OnQuickCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || DataContext is not FeedViewModel viewModel
            || !viewModel.CaptureCommand.CanExecute(null))
        {
            return;
        }

        viewModel.CaptureCommand.Execute(null);
        e.Handled = true;
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
                    ChronologyList.SelectedItem = e.Day;
                    ChronologyList.ScrollIntoView(e.Day);
                }

                Dispatcher.UIThread.Post(
                    () =>
                    {
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
                        preview?.BringIntoView();
                        preview?.Focus();
                    },
                    DispatcherPriority.Loaded);
            },
            DispatcherPriority.Loaded);
    }

    private ScrollViewer? FindChronologyScrollViewer() => ChronologyList
        .GetVisualDescendants()
        .OfType<ScrollViewer>()
        .FirstOrDefault();
}
