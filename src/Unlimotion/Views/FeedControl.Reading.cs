using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class FeedControl
{
    private readonly List<INotifyPropertyChanged> contextSources = new();
    private bool returningToCurrentDay;

    private void InitializeReadingNavigation()
    {
        ChronologyList.LayoutUpdated += (_, _) => UpdateNavigationState();
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Bubble);
    }

    private MainWindowViewModel? Shell => this.GetVisualAncestors().OfType<MainScreen>()
        .FirstOrDefault()?.DataContext as MainWindowViewModel;

    private bool IsFeedSurfaceAvailable => observedViewModel is { IsVaultInitialized: true } feed
        && IsEffectivelyVisible && !feed.IsIdentityFrozen && !feed.IsReviewActive
        && !feed.HasHeadingAreaConversion && !feed.HasPendingRecoveries
        && feed.FilesDrawer?.IsOpen != true && feed.AreaManagement?.IsOpen != true
        && feed.DocumentConflict?.IsOpen != true && feed.IdentityConflict?.IsOpen != true
        && feed.ReviewRecovery?.IsOpen != true
        && (Shell is not { } shell || (shell.IsFeedMode && !shell.IsSettingsOpen && !shell.IsQuickCaptureOpen));

    private bool IsChronologyAvailable => IsFeedSurfaceAvailable && observedViewModel?.IsSearchActive == false;

    private FeedDayViewModel? ReturnTarget => observedViewModel is { } feed
        ? feed.VisibleDays.FirstOrDefault(day => day.Date == feed.EffectiveToday)
          ?? feed.VisibleDays.FirstOrDefault()
        : null;

    private void ObserveContextSources()
    {
        var feed = observedViewModel;
        var sources = new object?[] { Shell, feed?.FilesDrawer, feed?.AreaManagement, feed?.DocumentConflict,
            feed?.IdentityConflict, feed?.ReviewRecovery, feed?.OpenedThematicFile?.MarkdownEditor }
            .OfType<INotifyPropertyChanged>().ToArray();
        if (contextSources.SequenceEqual(sources)) return;
        StopObservingContextSources();
        contextSources.AddRange(sources);
        foreach (var source in contextSources) source.PropertyChanged += OnContextChanged;
    }

    private void StopObservingContextSources()
    {
        foreach (var source in contextSources) source.PropertyChanged -= OnContextChanged;
        contextSources.Clear();
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.Post(UpdateNavigationState);
        else UpdateNavigationState();
    }

    private void OnVisibleDaysChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateNavigationState();

    private void UpdateNavigationState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateNavigationState);
            return;
        }

        if (ReturnToCurrentDayButton is null) return;
        var feed = observedViewModel;
        var chronology = IsChronologyAvailable && feed?.HasOpenedThematicFile == false;
        FeedAreaFilterButton.IsVisible = chronology;
        SearchFilters.IsVisible = IsFeedSurfaceAvailable && feed is { IsSearchActive: true, HasOpenedThematicFile: false };
        ThematicTitle.IsVisible = !MarkdownReadingPresentation.HasMatchingThematicHeading(
            feed?.OpenedThematicFile?.MarkdownEditor.GetSnapshotWithActiveDraft()?.Raw,
            feed?.OpenedThematicFile?.DisplayName);
        var target = ReturnTarget;
        var header = target is null ? null : ChronologyList.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == target.HeaderAutomationId);
        var y = header?.TranslatePoint(default, ChronologyScroller)?.Y;
        // A partially visible long card is not the start of its entry. An unrealized
        // target before the first realized day is also above the viewport.
        var firstRealized = ChronologyList.GetRealizedContainers().Select(control => control.DataContext)
            .OfType<FeedDayViewModel>().FirstOrDefault();
        var above = y is { } top ? top < -1 : target is not null && firstRealized is not null
            && feed!.VisibleDays.IndexOf(target) < feed.VisibleDays.IndexOf(firstRealized);
        ReturnToCurrentDayButton.IsVisible = chronology && target is not null && above;
        ReturnToCurrentDayButton.IsEnabled = chronology && feed?.IsBusy == false && !returningToCurrentDay;
        if (target is not null)
        {
            var label = L10n.Get(target.Date == feed!.EffectiveToday ? "FeedReturnToday" : "FeedReturnLatest");
            ReturnToCurrentDayButton.Content = label;
            AutomationProperties.SetName(ReturnToCurrentDayButton, label);
            ToolTip.SetTip(ReturnToCurrentDayButton, string.Format(CultureInfo.CurrentCulture,
                L10n.Get("FeedReturnDateFormat"), target.DisplayDate));
        }
        if (!chronology) ReturnNavigationError.IsVisible = false;
    }

    private async void OnReturnToCurrentDayClick(object? sender, RoutedEventArgs e) => await ReturnToCurrentDayAsync();

    private async void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Home || e.KeyModifiers != KeyModifiers.Alt || !CanReturnToCurrentDay) return;
        e.Handled = true;
        await ReturnToCurrentDayAsync();
    }

    public bool CanReturnToCurrentDay => IsChronologyAvailable
        && observedViewModel is { IsBusy: false, HasOpenedThematicFile: false }
        && ReturnTarget is not null && !returningToCurrentDay;

    public async Task ReturnToCurrentDayAsync()
    {
        if (!CanReturnToCurrentDay || observedViewModel is not { } feed) return;
        returningToCurrentDay = true;
        var rootPath = feed.VaultRootPath;
        bool IsOriginalContextAvailable() => ReferenceEquals(observedViewModel, feed)
            && rootPath == feed.VaultRootPath && IsChronologyAvailable && !feed.HasOpenedThematicFile;
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
        var selectionStart = focus?.SelectionStart ?? 0;
        var selectionEnd = focus?.SelectionEnd ?? 0;
        var offset = ChronologyScroller.Offset;
        ReturnNavigationError.IsVisible = false;
        UpdateNavigationState();
        try
        {
            await feed.CommitActiveEditorsAsync();
            if (!IsOriginalContextAvailable() || ReturnTarget is not { } target) return;
            ChronologyList.ScrollIntoView(target);
            ChronologyList.UpdateLayout();
            var heading = ChronologyList.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == target.HeaderAutomationId);
            if (heading?.TranslatePoint(default, ChronologyScroller) is { } point)
                ChronologyScroller.Offset = new Vector(0, Math.Max(0, ChronologyScroller.Offset.Y + point.Y - 10));
            else if (feed.VisibleDays.IndexOf(target) == 0) ChronologyScroller.Offset = default;
        }
        catch (Exception exception)
        {
            if (!IsOriginalContextAvailable()) return;
            // The editor renders its concrete save error next to the preserved draft.
            ReturnNavigationError.Text = exception.Message;
            ReturnNavigationError.IsVisible = !feed.Days.Any(day => day.MarkdownEditor.ActiveBlock?.HasError == true);
            focus?.Focus();
            if (focus is not null)
            {
                focus.SelectionStart = selectionStart;
                focus.SelectionEnd = selectionEnd;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsOriginalContextAvailable()) ChronologyScroller.Offset = offset;
            }, DispatcherPriority.Background);
        }
        finally
        {
            returningToCurrentDay = false;
            UpdateNavigationState();
        }
    }
}
