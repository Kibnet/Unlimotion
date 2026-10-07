using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Notes.Search;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views
{
    public partial class MainScreen : UserControl
    {
        private INotifyPropertyChanged? _shellViewModelNotifier;
        private INotifyPropertyChanged? _shellSettingsNotifier;
        private INotifyPropertyChanged? _shellFeedNotifier;
        private INotifyPropertyChanged? _workspaceNavigationNotifier;
        private INotifyCollectionChanged? _taskSpacesNotifier;
        private bool _shellLayoutUpdatePending;
        private bool _isAttached;
        private TopLevel? _keyboardHost;
        private MainWindowViewModel? _workspaceOwner;
        private WorkspacePaneView? _primaryPaneView;
        private WorkspacePaneView? _secondaryPaneView;
        private WorkspacePaneViewModel? _primaryPaneModel;
        private WorkspacePaneViewModel? _secondaryPaneModel;
        private WorkspaceOpenDisposition _pendingGlobalSearchDisposition = WorkspaceOpenDisposition.CurrentTab;
        private WorkspaceOpenDisposition _pendingRailDisposition = WorkspaceOpenDisposition.CurrentTab;
        private WorkspacePaneView? _layoutPrimaryPane;
        private WorkspacePaneView? _layoutSecondaryPane;
        private bool _layoutIsNarrow;
        private WorkspacePaneViewModel? _layoutActivePane;

        public MainScreen()
        {
            InitializeComponent();
            // The shell also runs in non-MainWindow hosts (including mobile).
            AddHandler(InputElement.KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
            ShellHotkeyHelpPanel.CloseRequested += (_, _) => SetHotkeyHelpVisibility(false);
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
            SizeChanged += (_, _) => ScheduleShellLayoutUpdate();
            SizeChanged += (_, _) => UpdateWorkspacePaneLayout();
            DataContextChanged += OnDataContextChanged;
            WorkspaceNavigationRail.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                _pendingRailDisposition = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                    ? WorkspaceOpenDisposition.NewTab : WorkspaceOpenDisposition.CurrentTab,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            foreach (var control in new Control[]
                     {
                         GlobalCreateMenuButton,
                         TaskSpaceSelector,
                         ShellModeSelector,
                         WorkspaceGlobalNavigation,
                         GlobalReviewButton,
                         GlobalSettingsButton,
                         GlobalOverflowMenuButton
                     })
            {
                control.SizeChanged += (_, _) => ScheduleShellLayoutUpdate();
            }
            if (GlobalOverflowMenuButton.Flyout is MenuFlyout overflowFlyout)
            {
                overflowFlyout.Opening += (_, _) =>
                {
                    PopulateWorkspaceOpeningMenus();
                    PopulateTaskSpaceOverflow();
                };
            }
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = true;
            if (TopLevel.GetTopLevel(this) is { } host && host is not MainWindow)
            {
                _keyboardHost = host;
                host.AddHandler(InputElement.KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
            }
            if (DataContext is MainWindowViewModel owner) owner.IsWorkspaceShellAttached = true;
            AttachShellLayoutSources();
            EnsureWorkspacePaneViews();
            UpdateWorkspacePaneLayout();
            RestoreAttachedPaneState(_primaryPaneView, _primaryPaneModel);
            RestoreAttachedPaneState(_secondaryPaneView, _secondaryPaneModel);
            ScheduleShellLayoutUpdate();
        }

        private static void RestoreAttachedPaneState(WorkspacePaneView? view, WorkspacePaneViewModel? pane)
        {
            if (view is not null && pane?.ActiveTab is { CurrentEntry: { ViewState: { } state } } tab)
                view.RestoreViewState(tab, state);
        }

        private void OnShellKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.Handled && (TryHandleShellHotkey(e) || TryHandleHotkeyHelpKey(e))) e.Handled = true;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_workspaceOwner is not null) _workspaceOwner.IsWorkspaceShellAttached = false;
            if (_isAttached && DataContext is MainWindowViewModel owner)
                owner.IsWorkspaceShellAttached = true;
            DetachShellLayoutSources();
            if (_isAttached)
            {
                AttachShellLayoutSources();
            }

            ScheduleShellLayoutUpdate();
            EnsureWorkspacePaneViews();
            UpdateWorkspacePaneLayout();
        }

        private void AttachShellLayoutSources()
        {
            if (DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            _shellViewModelNotifier = viewModel as INotifyPropertyChanged;
            _shellSettingsNotifier = viewModel.Settings as INotifyPropertyChanged;
            _shellFeedNotifier = viewModel.Feed;
            _workspaceNavigationNotifier = viewModel.WorkspaceNavigation;
            _taskSpacesNotifier = viewModel.Settings.TaskSpaces;
            if (_shellViewModelNotifier is not null)
            {
                _shellViewModelNotifier.PropertyChanged += OnShellLayoutSourceChanged;
            }

            if (_shellSettingsNotifier is not null)
            {
                _shellSettingsNotifier.PropertyChanged += OnShellLayoutSourceChanged;
            }

            _shellFeedNotifier.PropertyChanged += OnShellLayoutSourceChanged;
            _workspaceNavigationNotifier.PropertyChanged += OnWorkspaceNavigationChanged;
            UpdateWorkspaceHistoryControls();

            _taskSpacesNotifier.CollectionChanged += OnTaskSpacesChanged;
            viewModel.PinnedNotes.CollectionChanged += OnPinsChanged;
        }

        private void DetachShellLayoutSources()
        {
            if (_shellViewModelNotifier is MainWindowViewModel pinOwner)
                pinOwner.PinnedNotes.CollectionChanged -= OnPinsChanged;
            if (_shellViewModelNotifier is not null)
            {
                _shellViewModelNotifier.PropertyChanged -= OnShellLayoutSourceChanged;
            }

            if (_shellSettingsNotifier is not null)
            {
                _shellSettingsNotifier.PropertyChanged -= OnShellLayoutSourceChanged;
            }

            if (_taskSpacesNotifier is not null)
            {
                _taskSpacesNotifier.CollectionChanged -= OnTaskSpacesChanged;
            }

            if (_shellFeedNotifier is not null)
            {
                _shellFeedNotifier.PropertyChanged -= OnShellLayoutSourceChanged;
            }

            if (_workspaceNavigationNotifier is not null)
                _workspaceNavigationNotifier.PropertyChanged -= OnWorkspaceNavigationChanged;

            _shellViewModelNotifier = null;
            _shellSettingsNotifier = null;
            _shellFeedNotifier = null;
            _workspaceNavigationNotifier = null;
            _taskSpacesNotifier = null;
        }

        private void OnShellLayoutSourceChanged(object? sender, PropertyChangedEventArgs e)
        {
            ScheduleShellLayoutUpdate();
            if (e.PropertyName is nameof(MainWindowViewModel.SelectedWorkspaceMode)
                or nameof(Unlimotion.ViewModel.SettingsViewModel.IsFeedEnabled))
                UpdateWorkspaceRailLayout();
        }

        private void OnWorkspaceNavigationChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(WorkspaceNavigationViewModel.CurrentIndex)
                or nameof(WorkspaceNavigationViewModel.CanGoBack)
                or nameof(WorkspaceNavigationViewModel.CanGoForward))
            {
                UpdateWorkspaceHistoryControls();
                UpdateWorkspaceRailLayout();
            }
            if (e.PropertyName == nameof(WorkspaceNavigationViewModel.ActivePane))
                UpdateWorkspaceRailLayout();
            if (e.PropertyName is nameof(WorkspaceNavigationViewModel.PrimaryPane)
                or nameof(WorkspaceNavigationViewModel.SecondaryPane)
                or nameof(WorkspaceNavigationViewModel.HasSecondaryPane))
            {
                EnsureWorkspacePaneViews();
                UpdateWorkspacePaneLayout();
            }
            else if (e.PropertyName == nameof(WorkspaceNavigationViewModel.ActivePane)
                && _secondaryPaneView is not null && Bounds.Width > 0 && Bounds.Width < 900)
            {
                UpdateWorkspacePaneLayout();
            }
        }

        private void OnWorkspaceHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            UpdateWorkspaceHistoryControls();

        private void UpdateWorkspaceHistoryControls()
        {
            if (DataContext is not MainWindowViewModel owner) return;
            WorkspaceGlobalBackButton.IsEnabled = owner.WorkspaceNavigation.CanGoBack;
            WorkspaceGlobalForwardButton.IsEnabled = owner.WorkspaceNavigation.CanGoForward;
            AutomationProperties.SetName(WorkspaceGlobalBackButton, L10n.Get("WorkspaceBack"));
            AutomationProperties.SetName(WorkspaceGlobalForwardButton, L10n.Get("WorkspaceForward"));
            AutomationProperties.SetName(WorkspaceGlobalHistoryButton, L10n.Get("WorkspaceHistory"));
        }

        private async void OnWorkspaceGlobalBackClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel owner) await owner.NavigateWorkspaceBackAsync();
        }

        private async void OnWorkspaceGlobalForwardClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel owner) await owner.NavigateWorkspaceForwardAsync();
        }

        private void OnWorkspaceGlobalHistoryClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel owner) return;
            var history = owner.WorkspaceNavigation;
            var menu = new ContextMenu();
            for (var index = history.History.Count - 1; index >= 0; index--)
            {
                var historyIndex = index;
                var entry = history.History[index];
                var item = new MenuItem
                {
                    Header = entry.Location.Title,
                    MinHeight = 44,
                    IsEnabled = index != history.CurrentIndex
                };
                ToolTip.SetTip(item, entry.Location.Id);
                item.Click += async (_, _) => await owner.NavigateWorkspaceHistoryAsync(history.ActivePane, historyIndex);
                menu.Items.Add(item);
            }
            WorkspaceGlobalHistoryButton.ContextMenu = menu;
            menu.Open(WorkspaceGlobalHistoryButton);
        }

        private void EnsureWorkspacePaneViews()
        {
            if (WorkspacePanesHost is null) return;
            var owner = DataContext as MainWindowViewModel;
            if (!ReferenceEquals(_workspaceOwner, owner))
            {
                if (_workspaceOwner is not null)
                {
                    _workspaceOwner.CaptureActiveFeedLocation = null;
                    _workspaceOwner.CaptureWorkspaceTabState = null;
                    _workspaceOwner.RestoreWorkspaceTabState = null;
                }
                _primaryPaneView?.Dispose();
                _secondaryPaneView?.Dispose();
                _primaryPaneView = null;
                _secondaryPaneView = null;
                _primaryPaneModel = null;
                _secondaryPaneModel = null;
                _workspaceOwner = owner;
            }

            if (owner is null) return;
            owner.CaptureActiveFeedLocation = () => ReferenceEquals(
                    owner.WorkspaceNavigation.ActivePane, owner.WorkspaceNavigation.PrimaryPane)
                ? _primaryPaneView?.CaptureFeedLocation()
                : _secondaryPaneView?.CaptureFeedLocation();
            owner.CaptureWorkspaceTabState = tab =>
                _primaryPaneModel?.ActiveTab == tab ? _primaryPaneView?.CaptureViewState(tab)
                : _secondaryPaneModel?.ActiveTab == tab ? _secondaryPaneView?.CaptureViewState(tab)
                : tab.CurrentEntry?.ViewState;
            owner.RestoreWorkspaceTabState = (tab, state) =>
            {
                EnsureWorkspacePaneViews();
                if (_primaryPaneModel?.ActiveTab == tab) _primaryPaneView?.RestoreViewState(tab, state);
                else if (_secondaryPaneModel?.ActiveTab == tab) _secondaryPaneView?.RestoreViewState(tab, state);
            };
            if (!ReferenceEquals(_primaryPaneModel, owner.WorkspaceNavigation.PrimaryPane))
            {
                _primaryPaneView?.Dispose();
                _primaryPaneModel = owner.WorkspaceNavigation.PrimaryPane;
                _primaryPaneView = new WorkspacePaneView(owner, _primaryPaneModel);
            }

            var secondary = owner.WorkspaceNavigation.SecondaryPane;
            if (!ReferenceEquals(_secondaryPaneModel, secondary))
            {
                _secondaryPaneView?.Dispose();
                _secondaryPaneModel = secondary;
                _secondaryPaneView = secondary is null ? null : new WorkspacePaneView(owner, secondary);
            }
        }

        private void UpdateWorkspacePaneLayout()
        {
            if (WorkspacePanesHost is null || _primaryPaneView is null) return;
            UpdateWorkspaceRailLayout();
            var secondary = _secondaryPaneView;
            var narrow = secondary is not null && Bounds.Width > 0 && Bounds.Width < 900;
            var activePane = _workspaceOwner?.WorkspaceNavigation.ActivePane;
            if (ReferenceEquals(_layoutPrimaryPane, _primaryPaneView)
                && ReferenceEquals(_layoutSecondaryPane, secondary) && _layoutIsNarrow == narrow
                && (!narrow || ReferenceEquals(_layoutActivePane, activePane))) return;
            _layoutPrimaryPane = _primaryPaneView;
            _layoutSecondaryPane = secondary;
            _layoutIsNarrow = narrow;
            _layoutActivePane = activePane;
            WorkspacePanesHost.Children.Clear();
            if (narrow)
            {
                WorkspacePanesHost.ColumnDefinitions = new ColumnDefinitions("*");
                WorkspacePanesHost.RowDefinitions = new RowDefinitions("Auto,*");
                var selector = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Spacing = 6,
                    Margin = new Thickness(8, 4)
                };
                var primaryButton = new Button
                {
                    Content = PaneSelectorLabel(_workspaceOwner!.WorkspaceNavigation.PrimaryPane),
                    MinHeight = 40,
                    MaxWidth = Math.Max(90, (Bounds.Width - 32) / 2),
                    FontWeight = ReferenceEquals(_workspaceOwner!.WorkspaceNavigation.ActivePane,
                        _workspaceOwner.WorkspaceNavigation.PrimaryPane) ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal
                };
                AutomationProperties.SetAutomationId(primaryButton, "WorkspacePrimaryPaneSelector");
                BindPaneSelector(primaryButton, _workspaceOwner.WorkspaceNavigation.PrimaryPane);
                primaryButton.Click += (_, _) => _workspaceOwner.ActivateWorkspacePane(_workspaceOwner.WorkspaceNavigation.PrimaryPane);
                selector.Children.Add(primaryButton);
                var secondaryButton = new Button
                {
                    Content = PaneSelectorLabel(_workspaceOwner.WorkspaceNavigation.SecondaryPane!),
                    MinHeight = 40,
                    MaxWidth = Math.Max(90, (Bounds.Width - 32) / 2),
                    FontWeight = ReferenceEquals(_workspaceOwner.WorkspaceNavigation.ActivePane,
                        _workspaceOwner.WorkspaceNavigation.SecondaryPane) ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal
                };
                AutomationProperties.SetAutomationId(secondaryButton, "WorkspaceSecondaryPaneSelector");
                BindPaneSelector(secondaryButton, _workspaceOwner.WorkspaceNavigation.SecondaryPane!);
                secondaryButton.Click += (_, _) => _workspaceOwner.ActivateWorkspacePane(_workspaceOwner.WorkspaceNavigation.SecondaryPane!);
                selector.Children.Add(secondaryButton);
                WorkspacePanesHost.Children.Add(selector);
                Grid.SetRow(_primaryPaneView, 1);
                Grid.SetRow(secondary!, 1);
                var active = _workspaceOwner.WorkspaceNavigation.ActivePane;
                _primaryPaneView.IsVisible = ReferenceEquals(active, _workspaceOwner.WorkspaceNavigation.PrimaryPane);
                secondary!.IsVisible = ReferenceEquals(active, _workspaceOwner.WorkspaceNavigation.SecondaryPane);
                WorkspacePanesHost.Children.Add(_primaryPaneView);
                WorkspacePanesHost.Children.Add(secondary!);
                return;
            }

            WorkspacePanesHost.RowDefinitions = new RowDefinitions("*");
            if (secondary is null)
            {
                WorkspacePanesHost.ColumnDefinitions = new ColumnDefinitions("*");
                _primaryPaneView.IsVisible = true;
                WorkspacePanesHost.Children.Add(_primaryPaneView);
                return;
            }

            WorkspacePanesHost.ColumnDefinitions = new ColumnDefinitions("*,6,*");
            _primaryPaneView.IsVisible = true;
            secondary.IsVisible = true;
            Grid.SetColumn(secondary, 2);
            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter, 1);
            WorkspacePanesHost.Children.Add(_primaryPaneView);
            WorkspacePanesHost.Children.Add(splitter);
            WorkspacePanesHost.Children.Add(secondary);
        }

        private void UpdateWorkspaceRailLayout()
        {
            if (WorkspaceNavigationRail is null) return;
            var width = Bounds.Width;
            var show = width >= 900;
            var expanded = width >= 1450;
            WorkspaceNavigationRail.IsVisible = show;
            WorkspaceNavigationRail.Width = expanded ? 170 : 50;
            WorkspaceRailFeedLabel.IsVisible = expanded;
            WorkspaceRailTasksLabel.IsVisible = expanded;
            WorkspaceRailReviewLabel.IsVisible = expanded;
            WorkspaceRailNotesLabel.IsVisible = expanded;
            WorkspaceRailTaskCategories.IsVisible = expanded;
            if (DataContext is not MainWindowViewModel owner) return;
            WorkspaceRailFeedButton.ContextMenu = WorkspaceOpenMenu.Create(owner, WorkspaceLocation.FeedRoot);
            WorkspaceRailTasksButton.ContextMenu = CreateTaskViewsMenu(owner);
            foreach (var button in WorkspaceRailTaskCategories.Children.OfType<Button>())
            {
                if (button.Tag is not string index || !int.TryParse(index, out var kind)) continue;
                var target = WorkspaceLocation.ForTaskList((TaskListKind)kind);
                button.ContextMenu = WorkspaceOpenMenu.Create(owner, target);
                button.Classes.Set("WorkspaceRailActive", owner.WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation is
                    { Kind: WorkspaceLocationKind.Tasks } current && current.TaskListKind == (TaskListKind)kind);
            }
            WorkspaceRailNotesButton.IsVisible = owner.Settings.IsFeedEnabled;
            WorkspacePinnedNotesPanel.Children.Clear();
            GlobalPinnedNotesMenuItem.Items.Clear();
            GlobalPinnedNotesMenuItem.IsVisible = owner.PinnedNotes.Count > 0;
            foreach (var pin in owner.PinnedNotes)
            {
                var button = new Button
                {
                    Content = expanded ? pin.Title : "▱",
                    Command = owner.OpenPinnedNoteCommand,
                    CommandParameter = pin,
                    Opacity = pin.IsAvailable ? 1 : 0.5
                };
                button.Classes.Add("WorkspaceRailButton");
                AutomationProperties.SetName(button, pin.Title);
                AutomationProperties.SetAutomationId(button, "WorkspacePin-" + pin.RelativePath);
                ToolTip.SetTip(button, pin.RelativePath);
                var target = WorkspaceLocation.ForNote(pin.RelativePath, pin.Title);
                button.Command = null;
                button.Click += async (_, _) => await owner.OpenWorkspaceLocationAsync(target, TakeRailDisposition());
                var remove = new MenuItem { Header = L10n.Get("WorkspaceUnpinNote") };
                remove.Click += (_, _) => owner.ToggleNotePin(pin.RelativePath);
                button.ContextMenu = WorkspaceOpenMenu.Create(owner, target);
                button.ContextMenu.Items.Add(new Separator());
                button.ContextMenu.Items.Add(remove);
                WorkspacePinnedNotesPanel.Children.Add(button);
                var item = new MenuItem { Header = pin.Title };
                foreach (var command in WorkspaceOpenMenu.TakeItems(WorkspaceOpenMenu.Create(owner, target))) item.Items.Add(command);
                var unpin = new MenuItem { Header = L10n.Get("WorkspaceUnpinNote"), MinHeight = 44 };
                unpin.Click += (_, _) => owner.ToggleNotePin(pin.RelativePath);
                item.Items.Add(unpin);
                ToolTip.SetTip(item, pin.RelativePath);
                GlobalPinnedNotesMenuItem.Items.Add(item);
            }
            WorkspaceRailFeedButton.IsVisible = owner.Settings.IsFeedEnabled;
            WorkspaceRailFeedButton.Classes.Set("WorkspaceRailActive", owner.IsFeedMode);
            WorkspaceRailTasksButton.Classes.Set("WorkspaceRailActive", owner.IsTasksMode);
        }

        private ContextMenu CreateTaskViewsMenu(MainWindowViewModel owner)
        {
            var menu = WorkspaceOpenMenu.Create(owner, WorkspaceLocation.TasksRoot);
            menu.Items.Add(new Separator());
            foreach (var kind in Enum.GetValues<TaskListKind>())
            {
                var target = WorkspaceLocation.ForTaskList(kind);
                var item = new MenuItem { Header = target.Title, MinHeight = 44 };
                item.Click += async (_, e) =>
                {
                    if (ReferenceEquals(e.Source, item)) await owner.OpenWorkspaceLocationAsync(target);
                };
                foreach (var command in WorkspaceOpenMenu.TakeItems(WorkspaceOpenMenu.Create(owner, target))) item.Items.Add(command);
                menu.Items.Add(item);
            }
            return menu;
        }

        private void OnWorkspaceRailActionsClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not MainWindowViewModel owner) return;
            var menu = CreateTaskViewsMenu(owner);
            if (owner.Settings.IsFeedEnabled)
            {
                var feed = new MenuItem { Header = L10n.Get("FeedMode"), MinHeight = 44 };
                foreach (var command in WorkspaceOpenMenu.TakeItems(WorkspaceOpenMenu.Create(owner, WorkspaceLocation.FeedRoot)))
                    feed.Items.Add(command);
                menu.Items.Add(feed);
            }
            button.ContextMenu = menu;
            menu.Open(button);
        }

        private WorkspaceOpenDisposition TakeRailDisposition()
        {
            var disposition = _pendingRailDisposition;
            _pendingRailDisposition = WorkspaceOpenDisposition.CurrentTab;
            return disposition;
        }

        private void OnTaskSpacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleShellLayoutUpdate();
        }

        private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateWorkspaceRailLayout();

        private static void BindPaneSelector(Button button, WorkspacePaneViewModel pane)
        {
            button.Bind(AutomationProperties.NameProperty, new Binding("CurrentLocation.Title") { Source = pane });
            button.Bind(ToolTip.TipProperty, new Binding("CurrentLocation.Title") { Source = pane });
        }

        private static TextBlock PaneSelectorLabel(WorkspacePaneViewModel pane)
        {
            var label = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            label.Bind(TextBlock.TextProperty, new Binding("CurrentLocation.Title") { Source = pane });
            return label;
        }

        private void ScheduleShellLayoutUpdate()
        {
            if (_shellLayoutUpdatePending)
            {
                return;
            }

            _shellLayoutUpdatePending = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _shellLayoutUpdatePending = false;
                    UpdateShellLayout();
                },
                DispatcherPriority.Loaded);
        }

        private void UpdateShellLayout()
        {
            if (ShellAppBarGrid is null
                || GlobalSearchHost is null
                || Bounds.Width <= 0)
            {
                return;
            }

            TaskSpaceSelector.IsVisible = true;
            // The rail is the mode selector on desktop. Keep the radio selector only where
            // the rail is hidden, so both navigation systems do not compete in the app bar.
            ShellModeSelector.IsVisible = Bounds.Width < 900;
            WorkspaceGlobalNavigation.IsVisible = false;
            GlobalReviewButton.IsVisible = true;
            GlobalSettingsButton.IsVisible = true;

            var availableWidth = Math.Max(0, Bounds.Width - 24);
            GlobalSearchPopupBody.Width = Math.Min(620, availableWidth);
            GlobalSearchPopupBody.MaxHeight = Math.Max(160, Math.Min(520, Bounds.Height - 160));
            var fixedControls = new Control[]
            {
                GlobalCreateMenuButton,
                TaskSpaceSelector,
                ShellModeSelector,
                WorkspaceGlobalNavigation,
                GlobalReviewButton,
                GlobalSettingsButton,
                GlobalOverflowMenuButton
            };
            var wideRequired = fixedControls.Where(static control => control.IsVisible).Sum(GetMeasuredWidth)
                + 240
                + ShellAppBarGrid.ColumnSpacing * (ShellAppBarGrid.ColumnDefinitions.Count - 1);
            var compact = wideRequired > availableWidth;
            Grid.SetRow(GlobalSearchHost, compact ? 1 : 0);
            Grid.SetColumn(GlobalSearchHost, compact ? 0 : 4);
            Grid.SetColumnSpan(GlobalSearchHost, compact ? 8 : 1);
            GlobalSearchHost.Margin = compact ? new Thickness(0, 8, 0, 0) : default;

            HideLastActionWhileOverflowing(GlobalSettingsButton, availableWidth);
            HideLastActionWhileOverflowing(GlobalReviewButton, availableWidth);
            HideLastActionWhileOverflowing(TaskSpaceSelector, availableWidth);
            HideLastActionWhileOverflowing(ShellModeSelector, availableWidth);

            var spaceInOverflow = !TaskSpaceSelector.IsVisible;
            var modeInOverflow = Bounds.Width < 900 && !ShellModeSelector.IsVisible;
            GlobalTaskSpaceMenuItem.IsVisible = spaceInOverflow;
            GlobalFeedModeMenuItem.IsVisible = Bounds.Width < 900
                && DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true };
            GlobalTasksModeMenuItem.IsVisible = true;
            GlobalReviewMenuItem.IsVisible = !GlobalReviewButton.IsVisible;
            GlobalSettingsMenuItem.IsVisible = !GlobalSettingsButton.IsVisible;
            GlobalOverflowContextSeparator.IsVisible = (spaceInOverflow || modeInOverflow)
                && (GlobalReviewMenuItem.IsVisible || GlobalSettingsMenuItem.IsVisible);
            PopulateTaskSpaceOverflow();
        }

        private void PopulateWorkspaceOpeningMenus()
        {
            if (DataContext is not MainWindowViewModel owner) return;
            // Capture current singleton/opening choices when the menu opens. Replacing
            // these controls during shell layout interrupts an in-flight submenu gesture.
            GlobalFeedModeMenuItem.Items.Clear();
            foreach (var item in WorkspaceOpenMenu.TakeItems(WorkspaceOpenMenu.Create(owner, WorkspaceLocation.FeedRoot)))
                GlobalFeedModeMenuItem.Items.Add(item);
            GlobalTasksModeMenuItem.Items.Clear();
            foreach (var item in WorkspaceOpenMenu.TakeItems(CreateTaskViewsMenu(owner))) GlobalTasksModeMenuItem.Items.Add(item);
        }

        private void HideLastActionWhileOverflowing(Control control, double availableWidth)
        {
            if (GetVisibleTopRowWidth() <= availableWidth)
            {
                return;
            }

            control.IsVisible = false;
        }

        private double GetVisibleTopRowWidth()
        {
            var controls = new Control[]
            {
                GlobalCreateMenuButton,
                TaskSpaceSelector,
                ShellModeSelector,
                WorkspaceGlobalNavigation,
                GlobalReviewButton,
                GlobalSettingsButton,
                GlobalOverflowMenuButton
            };
            var visible = controls.Where(static control => control.IsVisible).ToArray();
            return visible.Sum(GetMeasuredWidth)
                // Grid retains gaps around empty/hidden columns. Counting only
                // visible buttons underestimates the row and clips the overflow menu.
                + Math.Max(0, ShellAppBarGrid.ColumnDefinitions.Count - 1) * ShellAppBarGrid.ColumnSpacing;
        }

        private static double GetMeasuredWidth(Control control)
        {
            return Math.Max(
                Math.Max(control.DesiredSize.Width, control.Bounds.Width),
                control.MinWidth);
        }

        private void PopulateTaskSpaceOverflow()
        {
            if (!GlobalTaskSpaceMenuItem.IsVisible
                || DataContext is not MainWindowViewModel viewModel)
            {
                GlobalTaskSpaceMenuItem.ItemsSource = null;
                return;
            }

            GlobalTaskSpaceMenuItem.ItemsSource = viewModel.Settings.TaskSpaces
                .Select(option =>
                {
                    var item = new MenuItem
                    {
                        Header = option.DisplayName,
                        ToggleType = MenuItemToggleType.Radio,
                        GroupName = "TaskSpaceOverflow",
                        IsChecked = ReferenceEquals(option, viewModel.Settings.HeaderTaskSpace) || option.IsActive,
                        IsEnabled = !viewModel.Settings.IsTaskSpaceSwitching,
                        Tag = option
                    };
                    item.Click += OnTaskSpaceMenuItemClick;
                    return item;
                })
                .ToArray();
        }

        private void OnTaskSpaceMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: TaskSpaceOptionViewModel option }
                && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Settings.HeaderTaskSpace = option;
            }
        }

        private void OnFeedModeMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, sender)) return;
            if (DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true } viewModel)
            {
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Feed);
            }
        }

        private void OnTasksModeMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, sender)) return;
            if (DataContext is MainWindowViewModel viewModel)
            {
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Tasks);
            }
        }

        private void OnFeedRootClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true } viewModel)
                _ = viewModel.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot, TakeRailDisposition());
        }

        private void OnTasksRootClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                _ = viewModel.OpenWorkspaceLocationAsync(WorkspaceLocation.TasksRoot, TakeRailDisposition());
        }

        private async void OnWorkspaceTaskCategoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string index }
                && DataContext is MainWindowViewModel viewModel)
                await viewModel.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList((TaskListKind)int.Parse(index)), TakeRailDisposition());
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _keyboardHost?.RemoveHandler(InputElement.KeyDownEvent, OnShellKeyDown);
            _keyboardHost = null;
            _isAttached = false;
            if (DataContext is MainWindowViewModel owner) owner.IsWorkspaceShellAttached = false;
            DetachShellLayoutSources();
            if (_workspaceOwner is not null)
            {
                foreach (var pane in _workspaceOwner.WorkspaceNavigation.Panes)
                    if (pane.ActiveTab is { CurrentEntry: { } entry } tab)
                        entry.ViewState = _workspaceOwner.CaptureWorkspaceTabState?.Invoke(tab) ?? entry.ViewState;
                _workspaceOwner.CaptureActiveFeedLocation = null;
                _workspaceOwner.CaptureWorkspaceTabState = null;
                _workspaceOwner.RestoreWorkspaceTabState = null;
            }
            _primaryPaneView?.Dispose();
            _secondaryPaneView?.Dispose();
            WorkspacePanesHost.Children.Clear();
            _primaryPaneView = null;
            _secondaryPaneView = null;
            _primaryPaneModel = null;
            _secondaryPaneModel = null;
            _layoutPrimaryPane = null;
            _layoutSecondaryPane = null;
        }

        private void OnGlobalSearchResultPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pendingGlobalSearchDisposition = (e.KeyModifiers & KeyModifiers.Control) != 0
                ? WorkspaceOpenDisposition.NewTab
                : WorkspaceOpenDisposition.CurrentTab;
        }

        private void OnGlobalSearchResultActionsLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not DropDownButton actions || actions.Flyout is not null) return;
            var menu = new MenuFlyout();
            actions.Flyout = menu;
            menu.Opening += (_, _) =>
            {
                menu.Items.Clear();
                if (actions.DataContext is not FeedSearchResultViewModel result
                    || DataContext is not MainWindowViewModel viewModel) return;
                var anchor = result.Entry.BlockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var location = result.Type switch
                {
                    FeedSearchDocumentType.Task => WorkspaceLocation.ForTask(result.TaskId, result.Text),
                    FeedSearchDocumentType.Daily => WorkspaceLocation.ForFeedDay(result.RelativePath, result.DisplaySource, anchor),
                    _ => WorkspaceLocation.ForNote(result.RelativePath, result.DisplaySource, anchor)
                };
                // Resolve the current search anchor when the command executes, not
                // from this display snapshot. Rebuild reuse labels on every open.
                var commands = WorkspaceOpenMenu.Create(viewModel, location,
                    disposition => viewModel.Feed.OpenSearchResultAsync(result, disposition));
                foreach (var command in WorkspaceOpenMenu.TakeItems(commands)) menu.Items.Add(command);
            };
        }

        private async void OnGlobalSearchResultClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: FeedSearchResultViewModel result }
                || DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            if (viewModel.Feed.OpenSearchResultCommand.CanExecute(result))
            {
                var disposition = _pendingGlobalSearchDisposition;
                _pendingGlobalSearchDisposition = WorkspaceOpenDisposition.CurrentTab;
                await viewModel.Feed.OpenSearchResultAsync(result, disposition);
                e.Handled = true;
            }
        }

        internal bool TryHandleHotkeyHelpKey(KeyEventArgs e)
        {
            if (e.Handled || e.KeyModifiers != KeyModifiers.None) return false;
            if (e.Key == Key.F1 && DataContext is MainWindowViewModel { IsTasksMode: true })
            {
                SetHotkeyHelpVisibility(!IsHotkeyHelpVisible);
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.Escape && IsHotkeyHelpVisible)
            {
                SetHotkeyHelpVisibility(false);
                e.Handled = true;
                return true;
            }
            return false;
        }

        internal bool IsHotkeyHelpVisible => ShellHotkeyHelpOverlayHost.IsVisible;

        private void SetHotkeyHelpVisibility(bool visible)
        {
            ShellHotkeyHelpOverlayHost.IsVisible = visible;
            if (visible) ShellHotkeyHelpOverlayHost.Focus();
            else GetActiveMainControl()?.Focus();
        }

        internal void ShowHotkeyHelp()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.CloseSettings();
                _ = ShowHotkeyHelpAsync(viewModel);
                return;
            }

            SetHotkeyHelpVisibility(true);
        }

        private async System.Threading.Tasks.Task ShowHotkeyHelpAsync(MainWindowViewModel viewModel)
        {
            if (!viewModel.IsTasksMode)
                await viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Tasks);
            SetHotkeyHelpVisibility(true);
        }

        internal bool TryHandleShellHotkey(KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel)
            {
                return false;
            }

            if (e.Key == Key.Escape && IsHotkeyHelpVisible)
            {
                SetHotkeyHelpVisibility(false);
                return true;
            }
            if (e.Key == Key.Escape && viewModel.CloseTopmostOverlay())
            {
                return true;
            }

            var modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
            var activeFeed = GetActiveFeedControl();
            if (e.Key == Key.Home && modifiers == KeyModifiers.Alt && activeFeed?.CanReturnToCurrentDay == true)
            {
                _ = activeFeed.ReturnToCurrentDayAsync();
                return true;
            }
            if (modifiers == KeyModifiers.Control)
            {
                if (e.Key == Key.OemOpenBrackets)
                {
                    _ = viewModel.NavigateWorkspaceBackAsync();
                    e.Handled = true;
                    return true;
                }
                if (e.Key == Key.OemCloseBrackets)
                {
                    _ = viewModel.NavigateWorkspaceForwardAsync();
                    e.Handled = true;
                    return true;
                }
            }
            if (modifiers == KeyModifiers.Alt && !IsTextEditorFocused())
            {
                if (e.Key == Key.Left)
                {
                    _ = viewModel.NavigateWorkspaceBackAsync();
                    e.Handled = true;
                    return true;
                }
                if (e.Key == Key.Right)
                {
                    _ = viewModel.NavigateWorkspaceForwardAsync();
                    e.Handled = true;
                    return true;
                }
            }
            if (e.Key == Key.Space
                && modifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                viewModel.OpenQuickCapture(isTask: false);
                return true;
            }

            if (e.Key == Key.R
                && modifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                viewModel.OpenReviewCommand.Execute(null);
                return true;
            }

            if (e.Key == Key.OemComma && modifiers == KeyModifiers.Control)
            {
                viewModel.OpenSettings();
                return true;
            }

            return false;
        }

        private TaskPresentationControl? GetActiveMainControl() => DataContext is MainWindowViewModel viewModel
            ? _primaryPaneView is not null && viewModel.WorkspaceNavigation.ActivePane == viewModel.WorkspaceNavigation.PrimaryPane
                ? _primaryPaneView.TasksView
                : _secondaryPaneView?.TasksView
            : null;

        private FeedControl? GetActiveFeedControl() => DataContext is MainWindowViewModel viewModel
            ? _primaryPaneView is not null && viewModel.WorkspaceNavigation.ActivePane == viewModel.WorkspaceNavigation.PrimaryPane
                ? _primaryPaneView.FeedView
                : _secondaryPaneView?.FeedView
            : null;

        private bool IsTextEditorFocused()
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused) return false;
            return focused is TextBox
                || focused.GetVisualAncestors().Any(control => control is TextBox
                    or MarkdownBlockLivePreviewEditor);
        }
    }
}
