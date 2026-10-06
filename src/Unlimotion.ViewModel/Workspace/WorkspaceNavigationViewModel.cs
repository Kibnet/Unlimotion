using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;

namespace Unlimotion.ViewModel.Workspace;

public enum WorkspaceNavigationNotice
{
    None,
    ExistingHistoryDocumentFocused
}

public sealed class WorkspaceNavigationEntryViewModel : ReactiveObject
{
    private WorkspaceLocation location;
    public WorkspaceNavigationEntryViewModel(WorkspaceLocation location, Guid paneId = default, Guid tabId = default)
    {
        this.location = location;
        PaneId = paneId;
        TabId = tabId;
    }
    public Guid PaneId { get; }
    public Guid TabId { get; }
    // Detached immutable presentation DTOs only, never views or task-store copies.
    public object? ViewState { get; set; }
    public WorkspaceLocation Location
    {
        get => location;
        set => this.RaiseAndSetIfChanged(ref location, value);
    }
}

public sealed class WorkspaceNavigationTabViewModel : ReactiveObject
{
    private int currentIndex = -1;
    public Guid Id { get; } = Guid.NewGuid();
    public ObservableCollection<WorkspaceNavigationEntryViewModel> History { get; } = [];
    public int CurrentIndex
    {
        get => currentIndex;
        private set
        {
            if (currentIndex == value) return;
            this.RaiseAndSetIfChanged(ref currentIndex, value);
            NotifyLocation();
            this.RaisePropertyChanged(nameof(CanGoBack));
            this.RaisePropertyChanged(nameof(CanGoForward));
        }
    }
    public WorkspaceNavigationEntryViewModel? CurrentEntry =>
        currentIndex >= 0 && currentIndex < History.Count ? History[currentIndex] : null;
    public WorkspaceLocation? CurrentLocation => CurrentEntry?.Location;
    public bool CanGoBack => currentIndex > 0;
    public bool CanGoForward => currentIndex >= 0 && currentIndex < History.Count - 1;

    public bool NavigateTo(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (CurrentLocation?.LocatorKey == location.LocatorKey)
        {
            ReplaceCurrentLocation(location);
            return false;
        }
        var retainedState = CurrentLocation?.ObjectKey == location.ObjectKey ? CurrentEntry?.ViewState : null;
        while (History.Count > currentIndex + 1) History.RemoveAt(History.Count - 1);
        History.Add(new WorkspaceNavigationEntryViewModel(location) { ViewState = retainedState });
        CurrentIndex = History.Count - 1;
        this.RaisePropertyChanged(nameof(CanGoForward));
        return true;
    }
    public void UpdateCurrentLocation(WorkspaceLocation location)
    {
        if (CurrentLocation is { } current && current.ObjectKey != location.ObjectKey)
            throw new InvalidOperationException("A location update cannot change the current object.");
        ReplaceCurrentLocation(location);
    }
    public void ReplaceCurrentLocation(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (CurrentEntry is null) { NavigateTo(location); return; }
        CurrentEntry.Location = location;
        NotifyLocation();
    }
    public bool GoBack() => GoToHistoryEntry(currentIndex - 1);
    public bool GoForward() => GoToHistoryEntry(currentIndex + 1);
    public bool GoToHistoryEntry(int index)
    {
        if (index < 0 || index >= History.Count || index == currentIndex) return false;
        CurrentIndex = index;
        return true;
    }
    public void Reset(WorkspaceLocation root)
    {
        History.Clear();
        currentIndex = -1;
        NavigateTo(root);
    }
    private void NotifyLocation()
    {
        this.RaisePropertyChanged(nameof(CurrentEntry));
        this.RaisePropertyChanged(nameof(CurrentLocation));
    }
}

public sealed class WorkspacePaneViewModel : ReactiveObject
{
    private WorkspaceNavigationTabViewModel? activeTab;
    public Guid Id { get; } = Guid.NewGuid();
    public ObservableCollection<WorkspaceNavigationTabViewModel> Tabs { get; } = [];
    public WorkspaceNavigationTabViewModel? ActiveTab
    {
        get => activeTab;
        private set
        {
            if (ReferenceEquals(activeTab, value)) return;
            if (activeTab is not null) activeTab.PropertyChanged -= HandleActiveTabChanged;
            this.RaiseAndSetIfChanged(ref activeTab, value);
            if (activeTab is not null) activeTab.PropertyChanged += HandleActiveTabChanged;
            this.RaisePropertyChanged(nameof(CurrentLocation));
        }
    }
    public WorkspaceLocation? CurrentLocation => ActiveTab?.CurrentLocation;
    private void HandleActiveTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceNavigationTabViewModel.CurrentLocation)
            or nameof(WorkspaceNavigationTabViewModel.CurrentEntry))
            this.RaisePropertyChanged(nameof(CurrentLocation));
    }
    public WorkspaceNavigationTabViewModel AddTab(WorkspaceLocation location)
    {
        var tab = new WorkspaceNavigationTabViewModel();
        tab.NavigateTo(location);
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }
    public bool SelectTab(WorkspaceNavigationTabViewModel tab)
    {
        if (!Tabs.Contains(tab)) return false;
        ActiveTab = tab;
        return true;
    }
    internal void RemoveTab(WorkspaceNavigationTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        var selected = ReferenceEquals(ActiveTab, tab);
        Tabs.Remove(tab);
        if (selected) ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
    }
    public bool CloseTab(WorkspaceNavigationTabViewModel tab, WorkspaceLocation fallback)
    {
        if (!Tabs.Contains(tab)) return false;
        RemoveTab(tab);
        if (Tabs.Count == 0) AddTab(fallback);
        return true;
    }
    internal void ClearTabs()
    {
        ActiveTab = null;
        Tabs.Clear();
    }
    public void Reset(WorkspaceLocation root)
    {
        ClearTabs();
        AddTab(root);
    }
}

public sealed class WorkspaceNavigationViewModel : ReactiveObject
{
    private readonly Func<WorkspaceNavigationTabViewModel, Task<bool>> commitBeforeLeaving;
    private readonly SemaphoreSlim transitions = new(1, 1);
    private WorkspacePaneViewModel primaryPane;
    private WorkspacePaneViewModel? secondaryPane;
    private WorkspacePaneViewModel activePane;
    private WorkspaceNavigationTabViewModel? observedTab;
    private readonly ObservableCollection<WorkspaceNavigationEntryViewModel> emptyHistory = [];
    private long scopeRevision;
    private WorkspaceNavigationNotice lastNavigationNotice;

    public WorkspaceNavigationViewModel(WorkspaceLocation root,
        Func<WorkspaceNavigationTabViewModel, Task<bool>>? commitBeforeLeaving = null)
    {
        this.commitBeforeLeaving = commitBeforeLeaving ?? (_ => Task.FromResult(true));
        primaryPane = new WorkspacePaneViewModel();
        primaryPane.AddTab(root);
        activePane = primaryPane;
        activePane.PropertyChanged += HandlePaneChanged;
        ObserveActiveTab();
    }
    public Func<WorkspaceNavigationTabViewModel, object?>? CaptureViewState { get; set; }
    public Action<WorkspaceNavigationTabViewModel, WorkspaceNavigationEntryViewModel>? RestoreViewState { get; set; }
    /// <summary>Synchronous readiness check for a guarded document after other editors awaited storage.</summary>
    public Func<WorkspaceNavigationTabViewModel, bool>? HasPendingEditorChanges { get; set; }
    public WorkspacePaneViewModel PrimaryPane
    {
        get => primaryPane;
        private set
        {
            this.RaiseAndSetIfChanged(ref primaryPane, value);
            this.RaisePropertyChanged(nameof(Panes));
        }
    }
    public WorkspacePaneViewModel? SecondaryPane
    {
        get => secondaryPane;
        private set
        {
            if (ReferenceEquals(secondaryPane, value)) return;
            this.RaiseAndSetIfChanged(ref secondaryPane, value);
            this.RaisePropertyChanged(nameof(HasSecondaryPane));
            this.RaisePropertyChanged(nameof(Panes));
        }
    }
    public bool HasSecondaryPane => SecondaryPane is not null;
    public WorkspacePaneViewModel[] Panes => SecondaryPane is null ? [PrimaryPane] : [PrimaryPane, SecondaryPane];
    public WorkspacePaneViewModel ActivePane
    {
        get => activePane;
        private set
        {
            if (ReferenceEquals(activePane, value)) return;
            activePane.PropertyChanged -= HandlePaneChanged;
            this.RaiseAndSetIfChanged(ref activePane, value);
            activePane.PropertyChanged += HandlePaneChanged;
            ObserveActiveTab();
        }
    }
    public WorkspaceNavigationTabViewModel ActiveTab => ActivePane.ActiveTab!;
    public WorkspaceNavigationTabViewModel ActiveTabIn(WorkspacePaneViewModel pane) => pane.ActiveTab!;
    // Compatibility aliases exclusively expose the active tab's history.
    public ObservableCollection<WorkspaceNavigationEntryViewModel> History => ActivePane.ActiveTab?.History ?? emptyHistory;
    public int CurrentIndex => ActivePane.ActiveTab?.CurrentIndex ?? -1;
    public bool CanGoBack => ActivePane.ActiveTab?.CanGoBack == true;
    public bool CanGoForward => ActivePane.ActiveTab?.CanGoForward == true;
    public long ScopeRevision => Volatile.Read(ref scopeRevision);
    public WorkspaceNavigationNotice LastNavigationNotice
    {
        get => lastNavigationNotice;
        private set => this.RaiseAndSetIfChanged(ref lastNavigationNotice, value);
    }
    public (WorkspacePaneViewModel Pane, WorkspaceNavigationTabViewModel Tab)? FindOpenObject(string objectKey)
    {
        foreach (var pane in Panes)
            foreach (var tab in pane.Tabs)
                if (tab.CurrentLocation?.ObjectKey == objectKey) return (pane, tab);
        return null;
    }
    public (WorkspacePaneViewModel Pane, WorkspaceNavigationTabViewModel Tab)? GetOpenObject(WorkspaceLocation location) =>
        FindOpenObject(location.ObjectKey);
    public void ActivatePane(WorkspacePaneViewModel pane)
    {
        if (!Contains(pane)) throw new ArgumentException("The pane does not belong to this workspace.", nameof(pane));
        ActivePane = pane;
    }
    public void InvalidatePendingNavigation() => Interlocked.Increment(ref scopeRevision);
    public void Reset(WorkspaceLocation root)
    {
        using var notifications = this.DelayChangeNotifications();
        InvalidatePendingNavigation();
        foreach (var pane in Panes) pane.ClearTabs();
        SecondaryPane = null;
        PrimaryPane.Reset(root);
        ActivePane = PrimaryPane;
        LastNavigationNotice = WorkspaceNavigationNotice.None;
        ObserveActiveTab();
    }
    public Task<bool> ResetAsync(WorkspaceLocation root) => RunAsync(async revision =>
    {
        var tabs = AllTabs();
        if (!await GuardAsync(revision, tabs)) return false;
        Capture(tabs);
        Reset(root);
        Restore(ActiveTab);
        return true;
    });
    public Task<bool> CommitAllEditorsAsync() => RunAsync(revision => GuardAsync(revision, AllTabs()));
    public Task<bool> SelectTabAsync(WorkspacePaneViewModel pane, WorkspaceNavigationTabViewModel tab) =>
        RunAsync(async revision =>
        {
            if (!Contains(pane) || !pane.Tabs.Contains(tab)) return false;
            var guarded = Distinct(ActiveTab, pane.ActiveTab, tab);
            if (!await GuardAsync(revision, guarded)) return false;
            Capture(guarded);
            pane.SelectTab(tab);
            ActivePane = pane;
            Restore(tab);
            return true;
        });
    public Task<bool> OpenAsync(WorkspaceLocation location, WorkspaceOpenDisposition disposition,
        WorkspaceNavigationIntent? intent = null)
    {
        // The request belongs to the document that received the click, not to
        // whichever pane a preceding queued transition happens to activate.
        var sourcePane = ActivePane;
        var source = ActiveTab;
        return RunAsync(async revision =>
        {
        ArgumentNullException.ThrowIfNull(location);
        if (!Contains(sourcePane) || !sourcePane.Tabs.Contains(source)) return false;
        if (intent == WorkspaceNavigationIntent.TraverseHistory)
            throw new ArgumentException("Use the tab history APIs to traverse history.", nameof(intent));
        var requestedIntent = intent ?? (location.HasExplicitLocator
            ? WorkspaceNavigationIntent.TargetLocator : WorkspaceNavigationIntent.FocusObject);
        if (FindOpenObject(location.ObjectKey) is { } existing)
        {
            var wasInactive = !ReferenceEquals(existing.Pane.ActiveTab, existing.Tab);
            var guarded = Distinct(source, ActiveTab, existing.Pane.ActiveTab, existing.Tab);
            if (!await GuardAsync(revision, guarded)) return false;
            Capture(guarded);
            if (requestedIntent == WorkspaceNavigationIntent.TargetLocator
                && existing.Tab.CurrentLocation?.LocatorKey != location.LocatorKey)
            {
                existing.Tab.NavigateTo(location);
                existing.Pane.SelectTab(existing.Tab);
                ActivePane = existing.Pane;
                Restore(existing.Tab);
            }
            else
            {
                existing.Pane.SelectTab(existing.Tab);
                ActivePane = existing.Pane;
                if (wasInactive) Restore(existing.Tab);
            }
            return true;
        }
        var targetPane = disposition switch
        {
            WorkspaceOpenDisposition.CurrentTab or WorkspaceOpenDisposition.NewTab => sourcePane,
            WorkspaceOpenDisposition.AdjacentPane => GetAdjacentPane(sourcePane),
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };
        var targetTab = disposition switch
        {
            WorkspaceOpenDisposition.NewTab => null,
            WorkspaceOpenDisposition.CurrentTab => source,
            _ => targetPane.ActiveTab
        };
        var leaving = Distinct(source, ActiveTab, targetPane.ActiveTab, targetTab);
        if (!await GuardAsync(revision, leaving)) return false;
        Capture(leaving);
        if (!ReferenceEquals(targetPane, PrimaryPane) && SecondaryPane is null) SecondaryPane = targetPane;
        if (targetTab is null) targetTab = targetPane.AddTab(location);
        else targetTab.NavigateTo(location);
        targetPane.SelectTab(targetTab);
        ActivePane = targetPane;
        Restore(targetTab);
        return true;
        });
    }
    public Task<bool> GoBackAsync() => GoBackAsync(ActivePane);
    public Task<bool> GoForwardAsync() => GoForwardAsync(ActivePane);
    public Task<bool> GoToHistoryEntryAsync(int index) => GoToHistoryEntryAsync(ActivePane, index);
    public Task<bool> GoBackAsync(WorkspacePaneViewModel pane) =>
        RunAsync(revision => TraverseAsync(revision, pane, (pane.ActiveTab?.CurrentIndex ?? 0) - 1));
    public Task<bool> GoForwardAsync(WorkspacePaneViewModel pane) =>
        RunAsync(revision => TraverseAsync(revision, pane, (pane.ActiveTab?.CurrentIndex ?? -1) + 1));
    public Task<bool> GoToHistoryEntryAsync(WorkspacePaneViewModel pane, int index) =>
        RunAsync(revision => TraverseAsync(revision, pane, index));
    private async Task<bool> TraverseAsync(long revision, WorkspacePaneViewModel pane, int index)
    {
        if (!Contains(pane) || pane.ActiveTab is not { } source
            || index < 0 || index >= source.History.Count || index == source.CurrentIndex) return false;
        var entry = source.History[index];
        var existing = FindOpenObject(entry.Location.ObjectKey);
        var guarded = Distinct(ActiveTab, source, existing?.Pane.ActiveTab, existing?.Tab);
        if (!await GuardAsync(revision, guarded)) return false;
        Capture(guarded);
        if (existing is { } other && !ReferenceEquals(other.Tab, source))
        {
            var wasInactive = !ReferenceEquals(other.Pane.ActiveTab, other.Tab);
            other.Pane.SelectTab(other.Tab);
            ActivePane = other.Pane;
            if (wasInactive) Restore(other.Tab);
            LastNavigationNotice = WorkspaceNavigationNotice.ExistingHistoryDocumentFocused;
            return true;
        }
        source.GoToHistoryEntry(index);
        pane.SelectTab(source);
        ActivePane = pane;
        Restore(source);
        return true;
    }
    public void UpdateActiveLocation(WorkspaceLocation location) => UpdateLocation(ActiveTab, location);
    public void UpdateLocation(WorkspaceNavigationTabViewModel tab, WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!AllTabs().Contains(tab)) throw new ArgumentException("The tab does not belong to this workspace.", nameof(tab));
        tab.UpdateCurrentLocation(location);
    }
    public Task<bool> CloseActiveTabAsync(WorkspaceLocation fallback) => CloseTabAsync(ActivePane, ActiveTab, fallback);
    public Task<bool> CloseTabAsync(WorkspacePaneViewModel pane, WorkspaceNavigationTabViewModel tab,
        WorkspaceLocation fallback) => RunAsync(async revision =>
    {
        if (!Contains(pane) || !pane.Tabs.Contains(tab)) return false;
        var wasSelected = ReferenceEquals(pane.ActiveTab, tab);
        var wasGlobalActivePane = ReferenceEquals(pane, ActivePane);
        var emptiedPrimary = ReferenceEquals(pane, PrimaryPane) && pane.Tabs.Count == 1;
        var oldIndex = pane.Tabs.IndexOf(tab);
        var next = wasSelected && pane.Tabs.Count > 1
            ? pane.Tabs[oldIndex < pane.Tabs.Count - 1 ? oldIndex + 1 : oldIndex - 1] : null;
        var promoted = wasSelected && pane.Tabs.Count == 1
            ? ReferenceEquals(pane, PrimaryPane) ? SecondaryPane?.ActiveTab
                : ReferenceEquals(ActivePane, pane) ? PrimaryPane.ActiveTab : null
            : null;
        var guarded = Distinct(tab, next, promoted);
        if (!await GuardAsync(revision, guarded)) return false;
        Capture(guarded);
        pane.RemoveTab(tab);
        NormalizeGroups(fallback.ScopeKey);
        if (wasSelected)
        {
            if (Contains(pane) && pane.ActiveTab is { } successor) Restore(successor);
            else if (wasGlobalActivePane || emptiedPrimary)
                Restore(ActiveTab);
        }
        return true;
    });
    public Task<bool> CloseSecondaryPaneAsync(WorkspaceLocation fallback) => RunAsync(async revision =>
    {
        if (SecondaryPane is not { } pane) return false;
        var guarded = Distinct(pane.Tabs.Cast<WorkspaceNavigationTabViewModel?>().Append(PrimaryPane.ActiveTab).ToArray());
        if (!await GuardAsync(revision, guarded)) return false;
        Capture(guarded);
        pane.ClearTabs();
        NormalizeGroups(fallback.ScopeKey);
        Restore(ActiveTab);
        return true;
    });
    public Task<bool> MoveTabAsync(WorkspacePaneViewModel source, WorkspaceNavigationTabViewModel tab) =>
        RunAsync(async revision =>
        {
            if (!Contains(source) || !source.Tabs.Contains(tab)) return false;
            var tabs = AllTabs();
            if (!await GuardAsync(revision, tabs)) return false;
            Capture(tabs);
            var target = GetAdjacentPane(source);
            if (!ReferenceEquals(target, PrimaryPane)) SecondaryPane = target;
            TransferTab(source, target, tab);
            ActivePane = target;
            NormalizeGroups(tab.CurrentLocation?.ScopeKey);
            Restore(tab);
            return true;
        });
    public Task<bool> MergePanesAsync() => RunAsync(async revision =>
    {
        if (SecondaryPane is not { } secondary) return false;
        var tabs = AllTabs();
        if (!await GuardAsync(revision, tabs)) return false;
        Capture(tabs);
        var active = ActiveTab;
        foreach (var tab in secondary.Tabs.ToArray()) TransferTab(secondary, PrimaryPane, tab);
        PrimaryPane.SelectTab(active);
        ActivePane = PrimaryPane;
        SecondaryPane = null;
        Restore(active);
        return true;
    });
    /// <summary>An explicit paired layout, unlike focus-only object opening.</summary>
    public Task<bool> ShowPairAsync(WorkspaceLocation source, WorkspaceLocation adjacent) => RunAsync(async revision =>
    {
        if (source.ObjectKey == adjacent.ObjectKey) return false;
        var tabs = AllTabs();
        if (!await GuardAsync(revision, tabs)) return false;
        Capture(tabs);
        var sourcePane = FindOpenObject(source.ObjectKey)?.Pane ?? PrimaryPane;
        var target = GetAdjacentPane(sourcePane);
        if (!ReferenceEquals(target, PrimaryPane)) SecondaryPane = target;
        var sourceTab = ShowInPane(source, sourcePane);
        var adjacentTab = ShowInPane(adjacent, target);
        ActivePane = target;
        Restore(sourceTab);
        Restore(adjacentTab);
        return true;
    });
    private WorkspaceNavigationTabViewModel ShowInPane(WorkspaceLocation location, WorkspacePaneViewModel target)
    {
        if (FindOpenObject(location.ObjectKey) is not { } existing) return target.AddTab(location);
        if (!ReferenceEquals(existing.Pane, target)) TransferTab(existing.Pane, target, existing.Tab);
        if (location.HasExplicitLocator && existing.Tab.CurrentLocation?.LocatorKey != location.LocatorKey)
            existing.Tab.NavigateTo(location);
        target.SelectTab(existing.Tab);
        return existing.Tab;
    }
    private void NormalizeGroups(string? scope)
    {
        using var notifications = this.DelayChangeNotifications();
        if (PrimaryPane.Tabs.Count == 0)
        {
            if (SecondaryPane is { Tabs.Count: > 0 } remaining)
            {
                PrimaryPane = remaining;
                SecondaryPane = null;
                ActivePane = remaining;
            }
            else
            {
                SecondaryPane = null;
                PrimaryPane.AddTab(WorkspaceLocation.TasksRoot with { ScopeKey = scope });
                ActivePane = PrimaryPane;
            }
        }
        else if (SecondaryPane is { Tabs.Count: 0 } empty)
        {
            if (ReferenceEquals(ActivePane, empty)) ActivePane = PrimaryPane;
            SecondaryPane = null;
        }
        ObserveActiveTab();
    }
    private static void TransferTab(WorkspacePaneViewModel source, WorkspacePaneViewModel target,
        WorkspaceNavigationTabViewModel tab)
    {
        source.RemoveTab(tab);
        target.Tabs.Add(tab);
        target.SelectTab(tab);
    }
    private WorkspacePaneViewModel GetAdjacentPane(WorkspacePaneViewModel source) =>
        ReferenceEquals(source, PrimaryPane) ? SecondaryPane ?? new WorkspacePaneViewModel() : PrimaryPane;
    private bool Contains(WorkspacePaneViewModel pane) => ReferenceEquals(pane, PrimaryPane) || ReferenceEquals(pane, SecondaryPane);
    private WorkspaceNavigationTabViewModel[] AllTabs() => Panes.SelectMany(pane => pane.Tabs).ToArray();
    private static WorkspaceNavigationTabViewModel[] Distinct(params WorkspaceNavigationTabViewModel?[] tabs) =>
        tabs.OfType<WorkspaceNavigationTabViewModel>().Distinct().ToArray();
    private async Task<bool> GuardAsync(long revision, IEnumerable<WorkspaceNavigationTabViewModel> tabs)
    {
        var guarded = tabs.Distinct().ToArray();
        while (true)
        {
            foreach (var tab in guarded)
                if (revision != scopeRevision || !await commitBeforeLeaving(tab).ConfigureAwait(true)
                    || revision != scopeRevision) return false;
            if (revision != scopeRevision) return false;
            // The first editor can acquire a new draft while a later editor's
            // save awaits I/O. Do not capture or mutate navigation until every
            // guarded surface is clean in this same UI turn.
            if (!guarded.Any(tab => HasPendingEditorChanges?.Invoke(tab) == true)) return true;
        }
    }
    private void Capture(IEnumerable<WorkspaceNavigationTabViewModel> tabs)
    {
        if (CaptureViewState is null) return;
        foreach (var tab in tabs.Distinct())
            if (tab.CurrentEntry is { } entry) entry.ViewState = CaptureViewState(tab);
    }
    private void Restore(WorkspaceNavigationTabViewModel tab)
    {
        if (tab.CurrentEntry is { } entry) RestoreViewState?.Invoke(tab, entry);
    }
    private async Task<bool> RunAsync(Func<long, Task<bool>> transition)
    {
        var revision = scopeRevision;
        await transitions.WaitAsync().ConfigureAwait(true);
        try
        {
            if (revision != scopeRevision) return false;
            LastNavigationNotice = WorkspaceNavigationNotice.None;
            return await transition(revision).ConfigureAwait(true);
        }
        finally { transitions.Release(); }
    }
    private void HandlePaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspacePaneViewModel.ActiveTab) && ActivePane.ActiveTab is not null) ObserveActiveTab();
    }
    private void ObserveActiveTab()
    {
        if (observedTab is not null) observedTab.PropertyChanged -= HandleTabChanged;
        observedTab = ActivePane.ActiveTab;
        if (observedTab is not null) observedTab.PropertyChanged += HandleTabChanged;
        NotifyActiveHistory();
    }
    private void HandleTabChanged(object? sender, PropertyChangedEventArgs e) => NotifyActiveHistory();
    private void NotifyActiveHistory()
    {
        this.RaisePropertyChanged(nameof(ActiveTab));
        this.RaisePropertyChanged(nameof(History));
        this.RaisePropertyChanged(nameof(CurrentIndex));
        this.RaisePropertyChanged(nameof(CanGoBack));
        this.RaisePropertyChanged(nameof(CanGoForward));
    }
}
