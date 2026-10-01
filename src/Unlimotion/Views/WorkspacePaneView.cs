using System;
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
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Workspace;
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
    private readonly MainControl tasksView;
    private readonly FeedControl feedView;
    private readonly Grid routeContent = new();
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

        tasksView = new MainControl { DataContext = owner };
        feedView = new FeedControl { DataContext = owner.Feed, UseWorkspaceTabs = true };
        feedView.CloseWorkspaceTabRequested = async () =>
        {
            if (pane.ActiveTab is { } tab) await owner.CloseWorkspaceTabAsync(pane, tab);
        };
        routeContent.Children.Add(tasksView);
        routeContent.Children.Add(feedView);
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
        backButton.IsVisible = false;
        forwardButton.IsVisible = false;
        historyButton.IsVisible = false;
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
            owner.WhenAnyValue(static viewModel => viewModel.CurrentTaskItem)
                .Subscribe(_ => OnOwnerCurrentTaskChanged()),
            owner.WhenAnyValue(static viewModel => viewModel.SelectedWorkspaceMode)
                .Subscribe(_ => UpdatePaneState()),
            owner.WhenAnyValue(static viewModel => viewModel.IsTasksLoading)
                .Subscribe(_ => UpdatePaneState())
        ];
        ObserveActiveTab();
        UpdateView();
    }

    public MainControl TasksView => tasksView;
    public FeedControl FeedView => feedView;
    public WorkspaceLocation? CaptureFeedLocation() => feedView.CaptureWorkspaceLocation();

    private bool IsActivePane => ReferenceEquals(owner.WorkspaceNavigation.ActivePane, pane);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
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

    private void OnOwnerCurrentTaskChanged()
    {
        if (IsActivePane && owner.CurrentTaskItem is { } selected
            && pane.ActiveTab?.CurrentLocation?.Mode == WorkspaceMode.Tasks)
        {
            var target = WorkspaceLocation.ForTask(selected.Id, selected.Title);
            if (pane.ActiveTab.CurrentLocation?.ObjectKey != target.ObjectKey)
                _ = owner.OpenWorkspaceLocationAsync(target);
        }
        UpdateRouteContent();
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
            var tabShell = new Border
            {
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { tabButton, actionsButton }
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
        var location = pane.ActiveTab?.CurrentLocation;
        var isReview = location?.Kind == WorkspaceLocationKind.Review;
        var isTasks = location?.Mode != WorkspaceMode.Feed;
        tasksView.IsVisible = isTasks;
        tasksView.DataContext = isTasks ? owner : null;
        feedView.IsVisible = !isTasks && !isReview;
        feedView.DataContext = isTasks || isReview ? null : owner.Feed;
        reviewView.IsVisible = isReview;
        feedView.SetWorkspaceRoute(pane.ActiveTab?.Id ?? Guid.Empty, location, IsActivePane);
        tasksView.SetWorkspaceTaskRoute(location, IsActivePane);
        tasksView.RouteTaskItem = location?.Kind == WorkspaceLocationKind.Task
            ? owner.ResolveTaskById(location.Id)
            : null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pane.PropertyChanged -= OnPanePropertyChanged;
        pane.Tabs.CollectionChanged -= OnTabsChanged;
        owner.WorkspaceNavigation.PropertyChanged -= OnNavigationPropertyChanged;
        foreach (var subscription in ownerSubscriptions) subscription.Dispose();
        if (observedTab is not null)
        {
            observedTab.PropertyChanged -= OnTabPropertyChanged;
            observedTab.History.CollectionChanged -= OnHistoryChanged;
        }
    }
}
