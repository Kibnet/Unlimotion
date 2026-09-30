using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class FeedControl
{
    public static readonly StyledProperty<ObservableCollection<FeedAreaFilterOptionViewModel>?> DisplayAreaFilterOptionsProperty =
        AvaloniaProperty.Register<FeedControl, ObservableCollection<FeedAreaFilterOptionViewModel>?>(nameof(DisplayAreaFilterOptions));
    public static readonly StyledProperty<ObservableCollection<FeedDayViewModel>?> DisplayDaysProperty =
        AvaloniaProperty.Register<FeedControl, ObservableCollection<FeedDayViewModel>?>(nameof(DisplayDays));
    public static readonly StyledProperty<string?> DisplayAreaFilterSummaryProperty =
        AvaloniaProperty.Register<FeedControl, string?>(nameof(DisplayAreaFilterSummary));
    public static readonly StyledProperty<bool> DisplayHasVisibleDaysProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(DisplayHasVisibleDays));
    public static readonly StyledProperty<bool> DisplayFilteredChronologyEmptyProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(DisplayFilteredChronologyEmpty));
    public static readonly StyledProperty<bool> DisplayUnfilteredChronologyEmptyProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(DisplayUnfilteredChronologyEmpty));
    public static readonly StyledProperty<bool> WorkspaceFilterAllProperty =
        AvaloniaProperty.Register<FeedControl, bool>(nameof(WorkspaceFilterAll), true);
    public static readonly StyledProperty<FeedAreaFilterSelection[]> WorkspaceSelectedAreasProperty =
        AvaloniaProperty.Register<FeedControl, FeedAreaFilterSelection[]>(nameof(WorkspaceSelectedAreas), []);

    public ObservableCollection<FeedAreaFilterOptionViewModel>? DisplayAreaFilterOptions
    {
        get => GetValue(DisplayAreaFilterOptionsProperty);
        private set => SetValue(DisplayAreaFilterOptionsProperty, value);
    }

    public ObservableCollection<FeedDayViewModel>? DisplayDays
    {
        get => GetValue(DisplayDaysProperty);
        private set => SetValue(DisplayDaysProperty, value);
    }

    public string? DisplayAreaFilterSummary
    {
        get => GetValue(DisplayAreaFilterSummaryProperty);
        private set => SetValue(DisplayAreaFilterSummaryProperty, value);
    }

    public bool DisplayHasVisibleDays
    {
        get => GetValue(DisplayHasVisibleDaysProperty);
        private set => SetValue(DisplayHasVisibleDaysProperty, value);
    }

    public bool DisplayFilteredChronologyEmpty
    {
        get => GetValue(DisplayFilteredChronologyEmptyProperty);
        private set => SetValue(DisplayFilteredChronologyEmptyProperty, value);
    }

    public bool DisplayUnfilteredChronologyEmpty
    {
        get => GetValue(DisplayUnfilteredChronologyEmptyProperty);
        private set => SetValue(DisplayUnfilteredChronologyEmptyProperty, value);
    }

    public bool WorkspaceFilterAll
    {
        get => GetValue(WorkspaceFilterAllProperty);
        private set => SetValue(WorkspaceFilterAllProperty, value);
    }

    public FeedAreaFilterSelection[] WorkspaceSelectedAreas
    {
        get => GetValue(WorkspaceSelectedAreasProperty);
        private set => SetValue(WorkspaceSelectedAreasProperty, value);
    }

    private readonly ObservableCollection<FeedAreaFilterOptionViewModel> workspaceAreaOptions = [];
    private readonly ObservableCollection<FeedDayViewModel> workspaceVisibleDays = [];
    private string? workspaceFilterKey;
    private bool isUpdatingWorkspaceAreaOptions;
    private bool workspaceDaysRefreshPending;
    private bool workspaceAreaOptionsRefreshPending;
    private readonly HashSet<MarkdownLivePreviewEditorViewModel> observedDayEditors = [];

    private void RefreshDisplayAreaBindings()
    {
        if (observedViewModel is not { } feed) return;
        if (!UseWorkspaceTabs)
        {
            DisplayAreaFilterOptions = feed.FeedAreaFilterOptions;
            DisplayDays = feed.VisibleDays;
            DisplayAreaFilterSummary = feed.FeedAreaFilterSummary;
            DisplayHasVisibleDays = feed.HasVisibleDays;
            DisplayFilteredChronologyEmpty = feed.IsFilteredChronologyEmpty;
            DisplayUnfilteredChronologyEmpty = feed.IsUnfilteredChronologyEmpty;
            return;
        }

        DisplayAreaFilterOptions = workspaceAreaOptions;
        DisplayDays = workspaceVisibleDays;
        SyncWorkspaceDayBlockObservation();
        RebuildWorkspaceAreaOptions();
    }

    private void RebuildWorkspaceAreaOptions()
    {
        if (!UseWorkspaceTabs || observedViewModel is not { } feed) return;
        var selected = ParseWorkspaceFilterKey(workspaceFilterKey);
        isUpdatingWorkspaceAreaOptions = true;
        workspaceAreaOptions.Clear();
        foreach (var option in feed.FeedAreaFilterOptions)
            workspaceAreaOptions.Add(new FeedAreaFilterOptionViewModel(
                option.Identity, option.DisplayName, option.AreaName, option.ParentId,
                option.Depth, option.HasChildren, option.IsAll,
                selected is null || option.IsAll == false && selected.Contains(option.Identity ?? string.Empty),
                OnWorkspaceAreaOptionChanged));
        isUpdatingWorkspaceAreaOptions = false;
        RecomputeWorkspaceAreaVisualStates();
        RefreshWorkspaceVisibleDays();
    }

    private void RestoreWorkspaceAreaFilter(string? key)
    {
        if (!UseWorkspaceTabs || string.Equals(workspaceFilterKey, key, StringComparison.Ordinal)) return;
        workspaceFilterKey = key;
        RebuildWorkspaceAreaOptions();
    }

    private void OnWorkspaceAreaOptionChanged(FeedAreaFilterOptionViewModel option, bool selected)
    {
        if (isUpdatingWorkspaceAreaOptions) return;
        isUpdatingWorkspaceAreaOptions = true;
        var leaves = workspaceAreaOptions.Where(static candidate => !candidate.IsAll).ToArray();
        if (option.IsAll)
        {
            foreach (var leaf in leaves) leaf.SetSelected(selected, notifyOwner: false);
        }
        else
        {
            foreach (var descendant in leaves.Where(candidate => IsWorkspaceAreaDescendant(candidate, option)))
                descendant.SetSelected(selected, notifyOwner: false);
        }
        RecomputeWorkspaceAreaVisualStates();
        workspaceFilterKey = option.IsAll && selected ? null :
            JsonSerializer.Serialize(leaves.Where(static leaf => leaf.IsSelected)
                .Select(static leaf => leaf.Identity ?? string.Empty).ToArray());
        isUpdatingWorkspaceAreaOptions = false;
        RefreshWorkspaceVisibleDays();
    }

    private void RecomputeWorkspaceAreaVisualStates()
    {
        var all = workspaceAreaOptions.FirstOrDefault(static option => option.IsAll);
        if (all is null) return;
        var leaves = workspaceAreaOptions.Where(static option => !option.IsAll).ToArray();
        foreach (var parent in leaves.Where(static option => option.HasChildren))
        {
            var subtree = leaves.Where(candidate => ReferenceEquals(candidate, parent)
                || IsWorkspaceAreaDescendant(candidate, parent)).ToArray();
            var count = subtree.Count(static option => option.IsSelected);
            parent.SetChecked(count == 0 ? false : count == subtree.Length ? true : null);
        }
        var selectedCount = leaves.Count(static option => option.IsSelected);
        all.SetSelected(selectedCount == leaves.Length, notifyOwner: false);
        all.SetChecked(selectedCount == 0 ? false : selectedCount == leaves.Length ? true : null);
    }

    private bool IsWorkspaceAreaDescendant(FeedAreaFilterOptionViewModel candidate,
        FeedAreaFilterOptionViewModel ancestor)
    {
        if (string.IsNullOrEmpty(candidate.ParentId) || string.IsNullOrEmpty(ancestor.Identity)) return false;
        var parentId = candidate.ParentId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrEmpty(parentId) && visited.Add(parentId))
        {
            if (string.Equals(parentId, ancestor.Identity, StringComparison.Ordinal)) return true;
            parentId = workspaceAreaOptions.FirstOrDefault(option =>
                string.Equals(option.Identity, parentId, StringComparison.Ordinal))?.ParentId;
        }
        return false;
    }

    private void RefreshWorkspaceVisibleDays()
    {
        if (!UseWorkspaceTabs || observedViewModel is not { } feed) return;
        var leaves = workspaceAreaOptions.Where(static option => !option.IsAll).ToArray();
        var showAll = workspaceAreaOptions.FirstOrDefault(static option => option.IsAll)?.IsSelected != false;
        var selected = leaves.Where(static option => option.IsSelected)
            .Select(static option => new FeedAreaFilterSelection(option.Identity ?? string.Empty, option.AreaName))
            .ToArray();
        WorkspaceFilterAll = showAll;
        WorkspaceSelectedAreas = selected;
        var days = feed.Days.Where(day => showAll || day.MarkdownEditor.Blocks.Any(block =>
            block.Block.IsContent && selected.Any(area => FeedAreaPresentationFilter.MatchesArea(block.Block, area))));
        var desired = days.ToArray();
        // Keep the realized day containers (and their scroll anchor) when older days
        // are appended or a single day changes its filter visibility.
        var desiredSet = desired.ToHashSet();
        for (var index = workspaceVisibleDays.Count - 1; index >= 0; index--)
            if (!desiredSet.Contains(workspaceVisibleDays[index])) workspaceVisibleDays.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            if (index < workspaceVisibleDays.Count && ReferenceEquals(workspaceVisibleDays[index], desired[index]))
                continue;
            var existingIndex = workspaceVisibleDays.IndexOf(desired[index]);
            if (existingIndex >= 0) workspaceVisibleDays.Move(existingIndex, index);
            else workspaceVisibleDays.Insert(index, desired[index]);
        }
        DisplayHasVisibleDays = workspaceVisibleDays.Count > 0;
        DisplayFilteredChronologyEmpty = !DisplayHasVisibleDays && !showAll;
        DisplayUnfilteredChronologyEmpty = !DisplayHasVisibleDays && showAll;
        DisplayAreaFilterSummary = showAll ? L10n.Get("FeedAreaFilterAll")
            : selected.Length == 0 ? L10n.Get("FeedAreaFilterNone")
            : L10n.Format("FeedAreaFilterSelected", selected.Length);
    }

    private static HashSet<string>? ParseWorkspaceFilterKey(string? key)
    {
        if (key is null) return null;
        try
        {
            return (JsonSerializer.Deserialize<string[]>(key) ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return key.Split('|', StringSplitOptions.None).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void OnSourceAreaOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!UseWorkspaceTabs)
        {
            RefreshDisplayAreaBindings();
            return;
        }
        if (workspaceAreaOptionsRefreshPending) return;
        workspaceAreaOptionsRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            workspaceAreaOptionsRefreshPending = false;
            if (observedViewModel is not null) RebuildWorkspaceAreaOptions();
        }, DispatcherPriority.Loaded);
    }

    private void OnSourceDaysChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!UseWorkspaceTabs || workspaceDaysRefreshPending) return;
        workspaceDaysRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            workspaceDaysRefreshPending = false;
            SyncWorkspaceDayBlockObservation();
            RefreshWorkspaceVisibleDays();
        }, DispatcherPriority.Loaded);
    }

    private void SyncWorkspaceDayBlockObservation()
    {
        var desired = observedViewModel?.Days.Select(static day => day.MarkdownEditor)
            .ToHashSet() ?? [];
        foreach (var editor in observedDayEditors.Except(desired).ToArray())
        {
            editor.Blocks.CollectionChanged -= OnSourceDayBlocksChanged;
            observedDayEditors.Remove(editor);
        }
        foreach (var editor in desired.Except(observedDayEditors))
        {
            editor.Blocks.CollectionChanged += OnSourceDayBlocksChanged;
            observedDayEditors.Add(editor);
        }
    }

    private void StopWorkspaceDayBlockObservation()
    {
        foreach (var editor in observedDayEditors)
            editor.Blocks.CollectionChanged -= OnSourceDayBlocksChanged;
        observedDayEditors.Clear();
    }

    private void OnSourceDayBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnSourceDaysChanged(sender, e);

    private IEnumerable<MarkdownLiveBlockViewModel> EnumerateWorkspaceVisibleBlocks()
    {
        if (workspaceLocation?.Kind == WorkspaceLocationKind.Note)
            return DisplayedDocument?.MarkdownEditor.Blocks
                .Where(static block => block.IsPresentationVisible) ?? [];
        if (workspaceLocation?.Kind != WorkspaceLocationKind.Feed)
            return [];
        return workspaceVisibleDays.Where(static day => !day.IsCollapsed)
            .SelectMany(day => day.MarkdownEditor.Blocks.Where(block => block.IsPresentationVisible
                && (WorkspaceFilterAll || FeedAreaPresentationFilter.IsVisible(
                    block.Block, WorkspaceSelectedAreas, showAll: false))));
    }

    private void OnFeedAreaFilterResetClick(object? sender, RoutedEventArgs e)
    {
        if (!UseWorkspaceTabs)
        {
            observedViewModel?.ResetFeedAreaFilterCommand.Execute(null);
            return;
        }
        workspaceFilterKey = null;
        RebuildWorkspaceAreaOptions();
    }
}
