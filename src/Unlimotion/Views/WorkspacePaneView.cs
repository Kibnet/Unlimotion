using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.ViewModel.Feed;
using ReactiveUI;

namespace Unlimotion.Views;

/// <summary>Hosts one independently navigable workspace pane.</summary>
public sealed class WorkspacePaneView : Border, IDisposable
{
    private readonly MainWindowViewModel owner;
    private readonly WorkspacePaneViewModel pane;
    private readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Button backButton = new()
    {
        Content = "←", MinWidth = 44, MinHeight = 44, FontSize = 17,
        Background = Brushes.Transparent, BorderBrush = Brushes.Transparent
    };
    private readonly Button forwardButton = new()
    {
        Content = "→", MinWidth = 44, MinHeight = 44, FontSize = 17,
        Background = Brushes.Transparent, BorderBrush = Brushes.Transparent
    };
    private readonly Button historyButton = new()
    {
        Content = "⌄", MinWidth = 44, MinHeight = 44, FontSize = 17
    };
    private readonly Border headerBorder = new();
    private TaskPresentationControl? tasksView;
    private string? taskObjectKey;
    private IDisposable? taskTitleSubscription;
    private ITaskStorage? observedTaskRepository;
    private IDisposable? taskRepositorySubscription;
    private readonly FeedControl feedView;
    private readonly Grid routeContent = new();
    private readonly TextBlock unavailableTask = new()
    {
        Text = Localization.Get("TaskDeepLinkTaskNotFound"), Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };
    private readonly ScrollViewer reviewView;
    private WorkspaceNavigationTabViewModel? observedTab;
    private readonly IDisposable[] ownerSubscriptions;
    private bool disposed;

    public WorkspacePaneView(MainWindowViewModel owner, WorkspacePaneViewModel pane)
    {
        this.owner = owner;
        this.pane = pane;
        AutomationProperties.SetAutomationId(this, ReferenceEquals(pane, owner.WorkspaceNavigation.PrimaryPane)
            ? "WorkspacePrimaryPane"
            : "WorkspaceSecondaryPane");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Group);
        AutomationProperties.SetIsControlElementOverride(this, true);

        feedView = new FeedControl { DataContext = owner.Feed, UseWorkspaceTabs = true };
        feedView.CloseWorkspaceTabRequested = async () =>
        {
            if (pane.ActiveTab is { } tab) await owner.CloseWorkspaceTabAsync(pane, tab);
        };
        routeContent.Children.Add(feedView);
        routeContent.Children.Add(unavailableTask);
        AutomationProperties.SetAutomationId(unavailableTask, "WorkspaceTaskUnavailable");
        var review = new FeedReviewDialog { DataContext = owner.Feed };
        review.UseWorkspacePresentation();
        var sourceButton = new Button { Content = Localization.Get("WorkspaceGoToSource"), MinHeight = 44 };
        AutomationProperties.SetAutomationId(sourceButton, "WorkspaceReviewSourceButton");
        sourceButton.Click += async (_, _) => await owner.ShowReviewSourceAsync();
        reviewView = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 8, Children = { sourceButton, review } },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        routeContent.Children.Add(reviewView);

        var tabsViewport = new ScrollViewer
        {
            Content = tabStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(tabsViewport, 0);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 0,
            Margin = new Thickness(6, 0),
            MinHeight = 44
        };
        header.Children.Add(tabsViewport);
        Grid.SetColumn(backButton, 1);
        header.Children.Add(backButton);
        Grid.SetColumn(forwardButton, 2);
        header.Children.Add(forwardButton);
        Grid.SetColumn(historyButton, 3);
        header.Children.Add(historyButton);
        foreach (var button in new[] { backButton, forwardButton, historyButton })
            button.Classes.Add("WorkspaceChromeButton");
        AutomationProperties.SetAutomationId(backButton, "WorkspacePaneBackButton");
        AutomationProperties.SetAutomationId(forwardButton, "WorkspacePaneForwardButton");
        AutomationProperties.SetAutomationId(historyButton, "WorkspacePaneHistoryButton");
        backButton.Classes.Add("WorkspaceNavButton");
        forwardButton.Classes.Add("WorkspaceNavButton");
        headerBorder.Classes.Add("WorkspacePaneHeader");
        headerBorder.Child = header;

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(headerBorder);
        Grid.SetRow(routeContent, 1);
        layout.Children.Add(routeContent);
        Child = layout;
        BorderThickness = new Thickness(0);

        backButton.Click += async (_, _) => await owner.NavigateWorkspaceBackAsync(pane);
        forwardButton.Click += async (_, _) => await owner.NavigateWorkspaceForwardAsync(pane);
        historyButton.Click += (_, _) => ShowHistoryMenu();
        // Activate this pane before child controls process selection/click events. Otherwise
        // a click in the inactive task pane can update the shared selection on the old pane.
        AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        pane.PropertyChanged += OnPanePropertyChanged;
        pane.Tabs.CollectionChanged += OnTabsChanged;
        owner.WorkspaceNavigation.PropertyChanged += OnNavigationPropertyChanged;
        ownerSubscriptions =
        [
            owner.WhenAnyValue(static viewModel => viewModel.SelectedWorkspaceMode)
                .Subscribe(_ => UpdatePaneState()),
            owner.WhenAnyValue(static viewModel => viewModel.IsTasksLoading)
                .Subscribe(_ => UpdatePaneState())
        ];
        ObserveActiveTab();
        UpdateView();
    }

    public TaskPresentationControl? TasksView => tasksView;
    public FeedControl FeedView => feedView;
    public WorkspaceLocation? CaptureFeedLocation() => feedView.CaptureWorkspaceLocation();

    public object? CaptureViewState(WorkspaceNavigationTabViewModel tab)
    {
        if (!ReferenceEquals(pane.ActiveTab, tab)) return tab.CurrentEntry?.ViewState;
        return tasksView switch
        {
            TaskListDocumentView list when list.IsVisible => list.CaptureViewState(),
            TaskCardView card when card.IsVisible => card.CaptureViewState(),
            _ => feedView.CaptureWorkspaceLocation() is { } location
                ? new FeedWorkspaceViewState(location, location.Kind == WorkspaceLocationKind.Feed
                    ? owner.Feed.Days.ToDictionary(day => day.RelativePath, day => day.IsCollapsed)
                    : new Dictionary<string, bool>())
                : null
        };
    }

    public void RestoreViewState(WorkspaceNavigationTabViewModel tab, object? state)
    {
        if (!ReferenceEquals(pane.ActiveTab, tab)) return;
        UpdateRouteContent();
        if (tasksView is TaskListDocumentView { IsVisible: true } list) list.RestoreViewState(state);
        else if (tasksView is TaskCardView { IsVisible: true } card) card.RestoreViewState(state);
        else if (state is FeedWorkspaceViewState feedState && tab.CurrentLocation is { } target)
        {
            var saved = feedState.Location;
            if (target.Kind == WorkspaceLocationKind.Feed)
                foreach (var day in owner.Feed.Days)
                    if (feedState.CollapsedDays.TryGetValue(day.RelativePath, out var collapsed)) day.IsCollapsed = collapsed;
            var restored = target with
            {
                StateKey = saved.StateKey,
                ScrollOffset = target.HasExplicitLocator && target.LocatorKey != saved.LocatorKey
                    ? target.ScrollOffset : saved.ScrollOffset
            };
            owner.WorkspaceNavigation.UpdateLocation(tab, restored);
            feedView.SetWorkspaceRoute(tab.Id, restored, IsActivePane);
        }
    }

    private bool IsActivePane => ReferenceEquals(owner.WorkspaceNavigation.ActivePane, pane);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Closing a background tab or opening its actions is not pane navigation.
        for (var control = e.Source as Control; control is not null && !ReferenceEquals(control, this);
             control = control.Parent as Control)
        {
            var id = AutomationProperties.GetAutomationId(control);
            if (id?.StartsWith("WorkspaceClose", StringComparison.Ordinal) == true
                || id?.StartsWith("WorkspaceTabActions-", StringComparison.Ordinal) == true) return;
        }
        if (!IsActivePane) owner.ActivateWorkspacePane(pane);
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspacePaneViewModel.ActiveTab)) ObserveActiveTab();
        UpdateView();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveActiveTab();
        UpdateView();
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceNavigationViewModel.ActivePane)
            or nameof(WorkspaceNavigationViewModel.SecondaryPane)) UpdatePaneState();
    }

    private void ObserveActiveTab()
    {
        if (observedTab is not null)
        {
            observedTab.PropertyChanged -= OnTabPropertyChanged;
            observedTab.History.CollectionChanged -= OnHistoryChanged;
        }
        observedTab = pane.ActiveTab;
        if (observedTab is not null)
        {
            observedTab.PropertyChanged += OnTabPropertyChanged;
            observedTab.History.CollectionChanged += OnHistoryChanged;
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateView();
    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateView();

    private void UpdateView()
    {
        backButton.IsEnabled = pane.ActiveTab?.CanGoBack == true;
        forwardButton.IsEnabled = pane.ActiveTab?.CanGoForward == true;
        AutomationProperties.SetName(backButton, Localization.Get("WorkspaceBack"));
        AutomationProperties.SetName(forwardButton, Localization.Get("WorkspaceForward"));
        AutomationProperties.SetName(historyButton, Localization.Get("WorkspaceHistory"));
        RenderTabs();
        UpdatePaneState();
    }

    private void UpdatePaneState()
    {
        UpdateRouteContent();
        headerBorder.Classes.Set("WorkspacePaneActive", IsActivePane);
    }

    private void RenderTabs()
    {
        tabStrip.Children.Clear();
        foreach (var tab in pane.Tabs)
        {
            var location = tab.CurrentLocation;
            var label = location?.Title ?? Localization.Get("TasksMode");
            var tabButton = new Button
            {
                Content = label,
                Padding = new Thickness(12, 4),
                MaxWidth = 190,
                MinHeight = 44,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontWeight = ReferenceEquals(tab, pane.ActiveTab) ? FontWeight.SemiBold : FontWeight.Normal,
                Opacity = ReferenceEquals(tab, pane.ActiveTab) ? 1 : 0.74
            };
            tabButton.Classes.Add("WorkspaceTabButton");
            tabButton.Classes.Set("WorkspaceTabActive", ReferenceEquals(tab, pane.ActiveTab));
            AutomationProperties.SetAutomationId(tabButton, "WorkspaceTab-" + tab.Id.ToString("N"));
            AutomationProperties.SetName(tabButton, label);
            ToolTip.SetTip(tabButton, location is { Kind: WorkspaceLocationKind.Note or WorkspaceLocationKind.Feed }
                && location.Id is { Length: > 0 } path && owner.Feed.VaultRootPath is { } root
                ? System.IO.Path.Combine(root, path) : location?.Id ?? label);
            tabButton.Click += async (_, _) => await owner.SelectWorkspaceTabAsync(pane, tab);
            tabButton.ContextMenu = CreateTabMenu(tab);
            var actionsButton = new Button
            {
                Content = "⋯",
                Padding = new Thickness(0),
                MinWidth = 44,
                MinHeight = 44
            };
            actionsButton.Classes.Add("WorkspaceChromeButton");
            AutomationProperties.SetAutomationId(actionsButton, "WorkspaceTabActions-" + tab.Id.ToString("N"));
            AutomationProperties.SetName(actionsButton, Localization.Get("WorkspaceOpenActions"));
            actionsButton.Click += (_, _) =>
            {
                var menu = CreateTabMenu(tab);
                actionsButton.ContextMenu = menu;
                menu.Open(actionsButton);
            };
            var closeButton = new Button { Content = "×", MinWidth = 32, MinHeight = 44, Padding = default };
            closeButton.Classes.Add("WorkspaceChromeButton");
            AutomationProperties.SetName(closeButton, Localization.Get("WorkspaceCloseTab"));
            AutomationProperties.SetAutomationId(closeButton,
                ReferenceEquals(tab, pane.ActiveTab) ? "WorkspaceCloseActiveTabButton" : "WorkspaceCloseTab-" + tab.Id.ToString("N"));
            closeButton.Click += async (_, _) => await owner.CloseWorkspaceTabAsync(pane, tab);
            var tabShell = new Border
            {
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { tabButton, actionsButton, closeButton }
                }
            };
            tabShell.Classes.Add("WorkspaceTabShell");
            tabShell.Classes.Set("WorkspaceTabActive", ReferenceEquals(tab, pane.ActiveTab));
            tabStrip.Children.Add(tabShell);
        }
    }

    private ContextMenu CreateTabMenu(WorkspaceNavigationTabViewModel tab)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem(Localization.Get("WorkspaceMoveTab"), async () =>
            await owner.MoveWorkspaceTabAsync(pane, tab)));
        if (tab.CurrentLocation is { Kind: WorkspaceLocationKind.Note } note)
            menu.Items.Add(CreateMenuItem(Localization.Get(owner.IsNotePinned(note.Id) ? "WorkspaceUnpinNote" : "WorkspacePinNote"), () =>
            {
                owner.ToggleNotePin(note.Id);
                return System.Threading.Tasks.Task.CompletedTask;
            }));
        if (owner.WorkspaceNavigation.HasSecondaryPane)
            menu.Items.Add(CreateMenuItem(Localization.Get("WorkspaceMergePanes"), async () =>
                await owner.MergeWorkspacePanesAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(Localization.Get("WorkspaceCloseTab"), async () =>
            await owner.CloseWorkspaceTabAsync(pane, tab)));
        if (!ReferenceEquals(pane, owner.WorkspaceNavigation.PrimaryPane))
            menu.Items.Add(CreateMenuItem(Localization.Get("WorkspaceClosePaneTabs") + $" ({pane.Tabs.Count})", async () =>
                await owner.CloseWorkspaceSecondaryPaneAsync()));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Func<System.Threading.Tasks.Task> action)
    {
        var item = new MenuItem { Header = header, MinHeight = 44 };
        item.Click += async (_, _) => await action();
        return item;
    }

    private void ShowHistoryMenu()
    {
        var tab = pane.ActiveTab;
        if (tab is null) return;
        var menu = new ContextMenu();
        for (var index = tab.History.Count - 1; index >= 0; index--)
        {
            var historyIndex = index;
            var location = tab.History[index].Location;
            var item = new MenuItem
            {
                Header = location.Title,
                MinHeight = 44,
                IsEnabled = historyIndex != tab.CurrentIndex
            };
            ToolTip.SetTip(item, location.Id);
            item.Click += async (_, _) => await owner.NavigateWorkspaceHistoryAsync(pane, historyIndex);
            menu.Items.Add(item);
        }
        historyButton.ContextMenu = menu;
        menu.Open(historyButton);
    }

    private void UpdateRouteContent()
    {
        ObserveTaskRepository();
        var location = pane.ActiveTab?.CurrentLocation;
        var isReview = location?.Kind == WorkspaceLocationKind.Review;
        var isTasks = location?.Mode != WorkspaceMode.Feed;
        var routedTask = location?.Kind == WorkspaceLocationKind.Task
            ? owner.ResolveTaskById(location.Id) : null;
        if (!isTasks) ClearTaskView();
        if (isTasks && location is not null && (taskObjectKey != location.ObjectKey ||
            location.Kind == WorkspaceLocationKind.Task &&
            !ReferenceEquals((tasksView as TaskCardView)?.RouteTaskItem, routedTask)))
        {
            ClearTaskView();
            taskObjectKey = location.ObjectKey;
            tasksView = location.Kind == WorkspaceLocationKind.Task
                ? routedTask is not null ? new TaskCardView(owner, routedTask) : null
                : new TaskListDocumentView(owner, location.TaskListKind);
            if (tasksView is not null) routeContent.Children.Add(tasksView);
            if (tasksView is TaskCardView && routedTask is not null)
                taskTitleSubscription = routedTask.WhenAnyValue(task => task.Title).Subscribe(title =>
                {
                    if (pane.ActiveTab is { CurrentLocation: { Kind: WorkspaceLocationKind.Task } current } tab
                        && current.Id == routedTask.Id && current.Title != title)
                        owner.WorkspaceNavigation.UpdateLocation(tab, current with { Title = title });
                });
        }
        unavailableTask.IsVisible = isTasks && location?.Kind == WorkspaceLocationKind.Task && tasksView is null;
        if (unavailableTask.IsVisible) unavailableTask.Text = Localization.Format("TaskDeepLinkTaskNotFound", location!.Id);
        if (tasksView is not null)
        {
            tasksView.IsVisible = isTasks;
            if (tasksView is TaskListDocumentView list) list.Activate(IsActivePane && isTasks);
            else if (tasksView is TaskCardView card) card.Activate(IsActivePane && isTasks);
        }
        feedView.IsVisible = !isTasks && !isReview;
        feedView.DataContext = isTasks || isReview ? null : owner.Feed;
        reviewView.IsVisible = isReview;
        feedView.SetWorkspaceRoute(pane.ActiveTab?.Id ?? Guid.Empty, location, IsActivePane);
    }

    private void ObserveTaskRepository()
    {
        if (ReferenceEquals(observedTaskRepository, owner.taskRepository)) return;
        taskRepositorySubscription?.Dispose();
        observedTaskRepository = owner.taskRepository;
        var repository = observedTaskRepository;
        taskRepositorySubscription = repository?.Tasks.Connect().Subscribe(changes =>
        {
            if (pane.ActiveTab?.CurrentLocation is not { Kind: WorkspaceLocationKind.Task } target ||
                !changes.Any(change => change.Key == target.Id)) return;
            var revision = owner.WorkspaceNavigation.ScopeRevision;
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && revision == owner.WorkspaceNavigation.ScopeRevision &&
                    ReferenceEquals(repository, owner.taskRepository)) UpdateRouteContent();
            });
        });
    }

    private void ClearTaskView()
    {
        taskTitleSubscription?.Dispose();
        taskTitleSubscription = null;
        if (tasksView is not null)
        {
            routeContent.Children.Remove(tasksView);
            tasksView.Dispose();
            tasksView = null;
        }
        taskObjectKey = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pane.PropertyChanged -= OnPanePropertyChanged;
        pane.Tabs.CollectionChanged -= OnTabsChanged;
        owner.WorkspaceNavigation.PropertyChanged -= OnNavigationPropertyChanged;
        foreach (var subscription in ownerSubscriptions) subscription.Dispose();
        taskRepositorySubscription?.Dispose();
        observedTaskRepository = null;
        ClearTaskView();
        if (observedTab is not null)
        {
            observedTab.PropertyChanged -= OnTabPropertyChanged;
            observedTab.History.CollectionChanged -= OnHistoryChanged;
        }
    }
}
