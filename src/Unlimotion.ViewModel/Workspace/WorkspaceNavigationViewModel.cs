using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;

namespace Unlimotion.ViewModel.Workspace;

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
            this.RaisePropertyChanged(nameof(CurrentEntry));
            this.RaisePropertyChanged(nameof(CurrentLocation));
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
        if (CurrentLocation?.HistoryKey == location.HistoryKey)
        {
            CurrentEntry!.Location = location;
            this.RaisePropertyChanged(nameof(CurrentEntry));
            this.RaisePropertyChanged(nameof(CurrentLocation));
            return false;
        }

        while (History.Count > currentIndex + 1) History.RemoveAt(History.Count - 1);
        History.Add(new WorkspaceNavigationEntryViewModel(location));
        CurrentIndex = History.Count - 1;
        return true;
    }

    public void UpdateCurrentLocation(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (CurrentEntry is null)
        {
            NavigateTo(location);
            return;
        }

        if (CurrentEntry.Location.HistoryKey != location.HistoryKey)
            throw new InvalidOperationException("A location update cannot change the current history entry.");
        CurrentEntry.Location = location;
        this.RaisePropertyChanged(nameof(CurrentEntry));
        this.RaisePropertyChanged(nameof(CurrentLocation));
    }

    public void ReplaceCurrentLocation(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (CurrentEntry is null)
        {
            NavigateTo(location);
            return;
        }
        CurrentEntry.Location = location;
        this.RaisePropertyChanged(nameof(CurrentEntry));
        this.RaisePropertyChanged(nameof(CurrentLocation));
    }

    public bool GoBack() => MoveTo(currentIndex - 1);
    public bool GoForward() => MoveTo(currentIndex + 1);

    public bool GoToHistoryEntry(int index) => MoveTo(index);

    public void Reset(WorkspaceLocation root)
    {
        History.Clear();
        currentIndex = -1;
        this.RaisePropertyChanged(nameof(CurrentIndex));
        this.RaisePropertyChanged(nameof(CurrentEntry));
        this.RaisePropertyChanged(nameof(CurrentLocation));
        this.RaisePropertyChanged(nameof(CanGoBack));
        this.RaisePropertyChanged(nameof(CanGoForward));
        NavigateTo(root);
    }

    private bool MoveTo(int index)
    {
        if (index < 0 || index >= History.Count || index == currentIndex) return false;
        CurrentIndex = index;
        return true;
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

    private void HandleActiveTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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

    public bool CloseTab(WorkspaceNavigationTabViewModel tab, WorkspaceLocation fallback)
    {
        if (!Tabs.Contains(tab)) return false;
        if (Tabs.Count == 1)
        {
            tab.Reset(fallback);
            ActiveTab = tab;
            return true;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (ReferenceEquals(ActiveTab, tab)) ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        return true;
    }

    public void Reset(WorkspaceLocation root)
    {
        Tabs.Clear();
        ActiveTab = null;
        AddTab(root);
    }
}

public sealed class WorkspaceNavigationViewModel : ReactiveObject
{
    private readonly Func<WorkspaceNavigationTabViewModel, Task<bool>> commitBeforeLeaving;
    private WorkspacePaneViewModel? secondaryPane;
    private WorkspacePaneViewModel activePane;
    private int currentIndex;

    public WorkspaceNavigationViewModel(
        WorkspaceLocation root,
        Func<WorkspaceNavigationTabViewModel, Task<bool>>? commitBeforeLeaving = null)
    {
        this.commitBeforeLeaving = commitBeforeLeaving ?? (_ => Task.FromResult(true));
        PrimaryPane = new WorkspacePaneViewModel();
        PrimaryPane.AddTab(root);
        activePane = PrimaryPane;
        History.Add(new WorkspaceNavigationEntryViewModel(root, PrimaryPane.Id, ActiveTab.Id));
    }

    public WorkspacePaneViewModel PrimaryPane { get; }

    public ObservableCollection<WorkspaceNavigationEntryViewModel> History { get; } = [];

    public int CurrentIndex
    {
        get => currentIndex;
        private set
        {
            if (currentIndex == value) return;
            this.RaiseAndSetIfChanged(ref currentIndex, value);
            this.RaisePropertyChanged(nameof(CanGoBack));
            this.RaisePropertyChanged(nameof(CanGoForward));
        }
    }

    public bool CanGoBack => CurrentIndex > 0;
    public bool CanGoForward => CurrentIndex < History.Count - 1;

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
    public WorkspacePaneViewModel[] Panes => SecondaryPane is null
        ? [PrimaryPane]
        : [PrimaryPane, SecondaryPane];

    public WorkspacePaneViewModel ActivePane
    {
        get => activePane;
        private set
        {
            if (ReferenceEquals(activePane, value)) return;
            this.RaiseAndSetIfChanged(ref activePane, value);
            this.RaisePropertyChanged(nameof(ActiveTab));
        }
    }

    public WorkspaceNavigationTabViewModel ActiveTab => ActivePane.ActiveTab!;

    public WorkspaceNavigationTabViewModel ActiveTabIn(WorkspacePaneViewModel pane) => pane.ActiveTab!;

    public void ActivatePane(WorkspacePaneViewModel pane)
    {
        if (!ReferenceEquals(pane, PrimaryPane) && !ReferenceEquals(pane, SecondaryPane))
            throw new ArgumentException("The pane does not belong to this workspace.", nameof(pane));
        if (ReferenceEquals(pane, ActivePane)) return;
        ActivePane = pane;
        RecordCurrentLocation();
    }

    public void Reset(WorkspaceLocation root)
    {
        if (SecondaryPane is { } secondary)
        {
            secondary.Tabs.Clear();
            SecondaryPane = null;
        }
        PrimaryPane.Reset(root);
        ActivePane = PrimaryPane;
        History.Clear();
        History.Add(new WorkspaceNavigationEntryViewModel(root, PrimaryPane.Id, ActiveTab.Id));
        CurrentIndex = 0;
        this.RaisePropertyChanged(nameof(CanGoBack));
        this.RaisePropertyChanged(nameof(CanGoForward));
    }

    public async Task<bool> SelectTabAsync(WorkspacePaneViewModel pane, WorkspaceNavigationTabViewModel tab)
    {
        if ((!ReferenceEquals(pane, PrimaryPane) && !ReferenceEquals(pane, SecondaryPane))
            || !pane.Tabs.Contains(tab)) return false;

        var previousTab = ActiveTab;
        if (!await commitBeforeLeaving(previousTab).ConfigureAwait(true)) return false;
        if (!ReferenceEquals(tab, previousTab)
            && !await commitBeforeLeaving(tab).ConfigureAwait(true)) return false;

        pane.SelectTab(tab);
        ActivePane = pane;
        RecordCurrentLocation();
        return true;
    }

    public async Task<bool> CloseTabAsync(
        WorkspacePaneViewModel pane,
        WorkspaceNavigationTabViewModel tab,
        WorkspaceLocation fallback)
    {
        if ((!ReferenceEquals(pane, PrimaryPane) && !ReferenceEquals(pane, SecondaryPane))
            || !pane.Tabs.Contains(tab)) return false;
        if (!await commitBeforeLeaving(tab).ConfigureAwait(true)) return false;
        if (ReferenceEquals(pane, ActivePane) && ReferenceEquals(tab, pane.ActiveTab))
        {
            var next = pane.Tabs.Count > 1
                ? pane.Tabs[Math.Min(pane.Tabs.IndexOf(tab), pane.Tabs.Count - 2)]
                : null;
            if (next is not null && !await commitBeforeLeaving(next).ConfigureAwait(true)) return false;
        }
        if (pane.Tabs.Count == 1
            && FindOpenLocation(fallback.ObjectKey) is { } existing
            && !ReferenceEquals(existing.Tab, tab))
        {
            if (ReferenceEquals(pane, SecondaryPane))
            {
                pane.Tabs.Clear();
                SecondaryPane = null;
                PrimaryPane.SelectTab(existing.Tab);
                ActivePane = PrimaryPane;
            }
            else
            {
                pane.Tabs.Clear();
                existing.Pane.Tabs.Remove(existing.Tab);
                pane.Tabs.Add(existing.Tab);
                pane.SelectTab(existing.Tab);
                if (ReferenceEquals(existing.Pane, SecondaryPane) && existing.Pane.Tabs.Count == 0)
                    SecondaryPane = null;
                ActivePane = pane;
            }
            UpdateCurrentHistoryLocation();
            return true;
        }
        var closed = pane.CloseTab(tab, fallback);
        if (closed && ReferenceEquals(pane, ActivePane)) UpdateCurrentHistoryLocation();
        return closed;
    }

    public async Task<bool> OpenAsync(WorkspaceLocation location, WorkspaceOpenDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(location);
        var sourceTab = ActiveTab;
        var sourcePane = ActivePane;
        var existing = FindOpenLocation(location.ObjectKey);
        if (disposition == WorkspaceOpenDisposition.AdjacentPane
            && existing is { } same && ReferenceEquals(same.Tab, sourceTab)) return false;
        if (!await commitBeforeLeaving(sourceTab).ConfigureAwait(true)) return false;

        if (existing is { } opened)
        {
            if (disposition == WorkspaceOpenDisposition.AdjacentPane
                && ReferenceEquals(opened.Pane, sourcePane))
            {
                if (!await commitBeforeLeaving(opened.Tab).ConfigureAwait(true)) return false;
                var adjacent = GetAdjacentPane(sourcePane);
                if (SecondaryPane is null && !ReferenceEquals(adjacent, PrimaryPane)) SecondaryPane = adjacent;
                opened.Pane.Tabs.Remove(opened.Tab);
                if (ReferenceEquals(opened.Pane.ActiveTab, opened.Tab))
                    opened.Pane.SelectTab(sourceTab);
                adjacent.Tabs.Add(opened.Tab);
                adjacent.SelectTab(opened.Tab);
                ActivePane = adjacent;
            }
            else
            {
                opened.Pane.SelectTab(opened.Tab);
                ActivePane = opened.Pane;
            }

            if (location.Kind != WorkspaceLocationKind.Feed || location.Id != "feed")
            {
                if (opened.Tab.CurrentLocation?.HistoryKey != location.HistoryKey)
                    opened.Tab.ReplaceCurrentLocation(location);
            }
        }
        else
        {
            WorkspacePaneViewModel targetPane;
            WorkspaceNavigationTabViewModel? targetTab;
            switch (disposition)
            {
                case WorkspaceOpenDisposition.CurrentTab:
                    targetPane = sourcePane;
                    targetTab = sourceTab;
                    break;
                case WorkspaceOpenDisposition.NewTab:
                    targetPane = sourcePane;
                    targetTab = null;
                    break;
                case WorkspaceOpenDisposition.AdjacentPane:
                    targetPane = GetAdjacentPane(sourcePane);
                    targetTab = targetPane.ActiveTab;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(disposition));
            }

            if (targetTab is not null && !ReferenceEquals(sourceTab, targetTab)
                && !await commitBeforeLeaving(targetTab).ConfigureAwait(true)) return false;
            if (disposition == WorkspaceOpenDisposition.AdjacentPane
                && SecondaryPane is null && !ReferenceEquals(targetPane, PrimaryPane))
                SecondaryPane = targetPane;
            if (targetTab is null)
                targetPane.AddTab(location);
            else
                targetTab.ReplaceCurrentLocation(location);
            ActivePane = targetPane;
        }

        RecordCurrentLocation();
        return true;
    }

    public async Task<bool> GoBackAsync() => await MoveHistoryAsync(forward: false).ConfigureAwait(true);
    public async Task<bool> GoForwardAsync() => await MoveHistoryAsync(forward: true).ConfigureAwait(true);

    public async Task<bool> GoToHistoryEntryAsync(int index)
    {
        if (index < 0 || index >= History.Count || index == CurrentIndex) return false;
        if (!await commitBeforeLeaving(ActiveTab).ConfigureAwait(true)) return false;
        return await RestoreHistoryEntryAsync(index).ConfigureAwait(true);
    }

    public async Task<bool> CloseActiveTabAsync(WorkspaceLocation fallback)
    {
        return await CloseTabAsync(ActivePane, ActiveTab, fallback).ConfigureAwait(true);
    }

    public async Task<bool> CloseSecondaryPaneAsync(WorkspaceLocation fallback)
    {
        if (SecondaryPane is not { } pane) return false;
        foreach (var tab in pane.Tabs.ToArray())
        {
            if (!await commitBeforeLeaving(tab).ConfigureAwait(true)) return false;
        }

        if (ActivePane == pane) ActivePane = PrimaryPane;
        pane.Tabs.Clear();
        SecondaryPane = null;
        if (PrimaryPane.ActiveTab is null) PrimaryPane.AddTab(fallback);
        UpdateCurrentHistoryLocation();
        return true;
    }

    public async Task<bool> MoveTabAsync(WorkspacePaneViewModel source, WorkspaceNavigationTabViewModel tab)
    {
        if (!Panes.Contains(source) || !source.Tabs.Contains(tab)) return false;
        if (!await CommitAllAsync().ConfigureAwait(true)) return false;
        var target = GetAdjacentPane(source);
        if (!ReferenceEquals(target, PrimaryPane)) SecondaryPane = target;
        TransferTab(source, target, tab);
        EnsurePrimaryTab();
        if (SecondaryPane?.Tabs.Count == 0) SecondaryPane = null;
        target.SelectTab(tab);
        ActivePane = target;
        RecordCurrentLocation();
        return true;
    }

    public async Task<bool> MergePanesAsync()
    {
        if (SecondaryPane is not { } secondary || !await CommitAllAsync().ConfigureAwait(true)) return false;
        var active = ActiveTab;
        foreach (var tab in secondary.Tabs.ToArray()) TransferTab(secondary, PrimaryPane, tab);
        PrimaryPane.SelectTab(active);
        ActivePane = PrimaryPane;
        SecondaryPane = null;
        RecordCurrentLocation();
        return true;
    }

    /// <summary>Shows two related documents without replacing any user tab.</summary>
    public async Task<bool> ShowPairAsync(WorkspaceLocation source, WorkspaceLocation adjacent)
    {
        if (source.ObjectKey == adjacent.ObjectKey || !await CommitAllAsync().ConfigureAwait(true)) return false;
        var target = SecondaryPane ?? new WorkspacePaneViewModel();
        SecondaryPane = target;
        ShowInPane(source, PrimaryPane);
        ShowInPane(adjacent, target);
        ActivePane = target;
        RecordCurrentLocation();
        return true;
    }

    private void ShowInPane(WorkspaceLocation location, WorkspacePaneViewModel target)
    {
        if (FindOpenLocation(location.ObjectKey) is { } existing)
        {
            if (!ReferenceEquals(existing.Pane, target)) TransferTab(existing.Pane, target, existing.Tab);
            existing.Tab.ReplaceCurrentLocation(location);
            target.SelectTab(existing.Tab);
        }
        else target.AddTab(location);
    }

    private async Task<bool> CommitAllAsync()
    {
        foreach (var tab in Panes.SelectMany(pane => pane.Tabs).ToArray())
            if (!await commitBeforeLeaving(tab).ConfigureAwait(true)) return false;
        return true;
    }

    private static void TransferTab(WorkspacePaneViewModel source, WorkspacePaneViewModel target,
        WorkspaceNavigationTabViewModel tab)
    {
        var wasActive = ReferenceEquals(source.ActiveTab, tab);
        source.Tabs.Remove(tab);
        if (wasActive && source.Tabs.Count > 0) source.SelectTab(source.Tabs[0]);
        target.Tabs.Add(tab);
        target.SelectTab(tab);
    }

    private void EnsurePrimaryTab()
    {
        if (PrimaryPane.Tabs.Count > 0) return;
        var fallback = ActiveTab.CurrentLocation?.ObjectKey == WorkspaceLocation.TasksRoot.ObjectKey
            ? WorkspaceLocation.FeedRoot : WorkspaceLocation.TasksRoot;
        if (FindOpenLocation(fallback.ObjectKey) is { } existing)
            TransferTab(existing.Pane, PrimaryPane, existing.Tab);
        else PrimaryPane.AddTab(fallback);
    }

    private async Task<bool> MoveHistoryAsync(bool forward)
    {
        var targetIndex = CurrentIndex + (forward ? 1 : -1);
        if (targetIndex < 0 || targetIndex >= History.Count) return false;
        if (!await commitBeforeLeaving(ActiveTab).ConfigureAwait(true)) return false;
        return await RestoreHistoryEntryAsync(targetIndex).ConfigureAwait(true);
    }

    public void UpdateActiveLocation(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (ActiveTab.CurrentLocation?.ObjectKey != location.ObjectKey)
            throw new InvalidOperationException("A location update cannot change the current object.");
        ActiveTab.ReplaceCurrentLocation(location);
        UpdateCurrentHistoryLocation();
    }

    private (WorkspacePaneViewModel Pane, WorkspaceNavigationTabViewModel Tab)? FindOpenLocation(string objectKey)
    {
        foreach (var pane in Panes)
            foreach (var tab in pane.Tabs)
                if (tab.CurrentLocation?.ObjectKey == objectKey) return (pane, tab);
        return null;
    }

    private void RecordCurrentLocation()
    {
        if (ActiveTab.CurrentLocation is not { } location) return;
        var current = History.Count > 0 ? History[CurrentIndex] : null;
        if (current is not null
            && current.TabId == ActiveTab.Id
            && current.PaneId == ActivePane.Id
            && current.Location.HistoryKey == location.HistoryKey)
        {
            current.Location = location;
            return;
        }

        while (History.Count > CurrentIndex + 1) History.RemoveAt(History.Count - 1);
        History.Add(new WorkspaceNavigationEntryViewModel(location, ActivePane.Id, ActiveTab.Id));
        CurrentIndex = History.Count - 1;
        this.RaisePropertyChanged(nameof(CanGoForward));
    }

    private void UpdateCurrentHistoryLocation()
    {
        if (History.Count > CurrentIndex && ActiveTab.CurrentLocation is { } location)
            History[CurrentIndex].Location = location;
    }

    private async Task<bool> RestoreHistoryEntryAsync(int index)
    {
        var entry = History[index];
        var opened = FindOpenLocation(entry.Location.ObjectKey);
        WorkspacePaneViewModel pane;
        WorkspaceNavigationTabViewModel tab;
        if (opened is { } existing)
        {
            pane = existing.Pane;
            tab = existing.Tab;
        }
        else
        {
            pane = Panes.FirstOrDefault(candidate => candidate.Id == entry.PaneId) ?? ActivePane;
            tab = pane.Tabs.FirstOrDefault(candidate => candidate.Id == entry.TabId)
                ?? pane.ActiveTab ?? pane.AddTab(entry.Location);
            if (!ReferenceEquals(tab, ActiveTab)
                && !await commitBeforeLeaving(tab).ConfigureAwait(true)) return false;
        }

        tab.ReplaceCurrentLocation(entry.Location);
        pane.SelectTab(tab);
        ActivePane = pane;
        CurrentIndex = index;
        return true;
    }

    private WorkspacePaneViewModel GetAdjacentPane(WorkspacePaneViewModel source)
    {
        if (ReferenceEquals(source, PrimaryPane))
        {
            if (SecondaryPane is not null) return SecondaryPane;
            return new WorkspacePaneViewModel();
        }

        return PrimaryPane;
    }
}
