using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
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
        private MainWindowViewModel? _workspaceOwner;
        private WorkspacePaneView? _primaryPaneView;
        private WorkspacePaneView? _secondaryPaneView;
        private WorkspacePaneViewModel? _primaryPaneModel;
        private WorkspacePaneViewModel? _secondaryPaneModel;
        private WorkspaceOpenDisposition _pendingGlobalSearchDisposition = WorkspaceOpenDisposition.CurrentTab;

        public MainScreen()
        {
            InitializeComponent();
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
            SizeChanged += (_, _) => ScheduleShellLayoutUpdate();
            SizeChanged += (_, _) => UpdateWorkspacePaneLayout();
            DataContextChanged += OnDataContextChanged;
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
                overflowFlyout.Opening += (_, _) => PopulateTaskSpaceOverflow();
            }
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = true;
            if (DataContext is MainWindowViewModel owner) owner.IsWorkspaceShellAttached = true;
            AttachShellLayoutSources();
            ScheduleShellLayoutUpdate();
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
            viewModel.WorkspaceNavigation.History.CollectionChanged += OnWorkspaceHistoryChanged;
            UpdateWorkspaceHistoryControls();

            _taskSpacesNotifier.CollectionChanged += OnTaskSpacesChanged;
        }

        private void DetachShellLayoutSources()
        {
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
            if (_workspaceNavigationNotifier is WorkspaceNavigationViewModel navigation)
                navigation.History.CollectionChanged -= OnWorkspaceHistoryChanged;

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
                UpdateWorkspaceHistoryControls();
            if (e.PropertyName == nameof(WorkspaceNavigationViewModel.ActivePane))
                UpdateWorkspaceRailLayout();
            if (e.PropertyName is nameof(WorkspaceNavigationViewModel.SecondaryPane)
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
                if (_workspaceOwner is not null) _workspaceOwner.CaptureActiveFeedLocation = null;
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
            WorkspacePanesHost.Children.Clear();
            var secondary = _secondaryPaneView;
            var narrow = secondary is not null && Bounds.Width > 0 && Bounds.Width < 900;
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
                    Content = L10n.Get("WorkspacePrimaryPane"),
                    MinHeight = 40,
                    MinWidth = 112,
                    FontWeight = ReferenceEquals(_workspaceOwner!.WorkspaceNavigation.ActivePane,
                        _workspaceOwner.WorkspaceNavigation.PrimaryPane) ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal
                };
                AutomationProperties.SetAutomationId(primaryButton, "WorkspacePrimaryPaneSelector");
                primaryButton.Click += (_, _) => _workspaceOwner.ActivateWorkspacePane(_workspaceOwner.WorkspaceNavigation.PrimaryPane);
                selector.Children.Add(primaryButton);
                var secondaryButton = new Button
                {
                    Content = L10n.Get("WorkspaceSecondaryPane"),
                    MinHeight = 40,
                    MinWidth = 112,
                    FontWeight = ReferenceEquals(_workspaceOwner.WorkspaceNavigation.ActivePane,
                        _workspaceOwner.WorkspaceNavigation.SecondaryPane) ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal
                };
                AutomationProperties.SetAutomationId(secondaryButton, "WorkspaceSecondaryPaneSelector");
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
            WorkspaceRailTaskCategories.IsVisible = expanded;
            if (DataContext is not MainWindowViewModel owner) return;
            WorkspaceRailFeedButton.IsVisible = owner.Settings.IsFeedEnabled;
            WorkspaceRailFeedButton.Classes.Set("WorkspaceRailActive", owner.IsFeedMode);
            WorkspaceRailTasksButton.Classes.Set("WorkspaceRailActive", owner.IsTasksMode);
        }

        private void OnTaskSpacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleShellLayoutUpdate();
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
                + ShellAppBarGrid.ColumnSpacing * fixedControls.Count(static control => control.IsVisible);
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
            GlobalFeedModeMenuItem.IsVisible = modeInOverflow
                && DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true };
            GlobalTasksModeMenuItem.IsVisible = modeInOverflow;
            GlobalReviewMenuItem.IsVisible = !GlobalReviewButton.IsVisible;
            GlobalSettingsMenuItem.IsVisible = !GlobalSettingsButton.IsVisible;
            GlobalOverflowContextSeparator.IsVisible = (spaceInOverflow || modeInOverflow)
                && (GlobalReviewMenuItem.IsVisible || GlobalSettingsMenuItem.IsVisible);
            PopulateTaskSpaceOverflow();
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
                + Math.Max(0, visible.Length - 1) * ShellAppBarGrid.ColumnSpacing;
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
            if (DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true } viewModel)
            {
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Feed);
            }
        }

        private void OnTasksModeMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Tasks);
            }
        }

        private void OnFeedRootClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel { Settings.IsFeedEnabled: true } viewModel)
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Feed);
        }

        private void OnTasksRootClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                _ = viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Tasks);
        }

        private async void OnWorkspaceTaskCategoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string index }
                && DataContext is MainWindowViewModel viewModel)
                await viewModel.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.TasksRoot with { StateKey = $"tasktab:{index}" });
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            if (DataContext is MainWindowViewModel owner) owner.IsWorkspaceShellAttached = false;
            DetachShellLayoutSources();
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
            Add("WorkspaceOpenInNewTab", WorkspaceOpenDisposition.NewTab);
            Add("WorkspaceOpenBeside", WorkspaceOpenDisposition.AdjacentPane);
            actions.Flyout = menu;

            void Add(string labelKey, WorkspaceOpenDisposition disposition)
            {
                var item = new MenuItem { Header = L10n.Get(labelKey), MinHeight = 44 };
                item.Click += async (_, _) =>
                {
                    if (actions.DataContext is FeedSearchResultViewModel result
                        && DataContext is MainWindowViewModel viewModel)
                        await viewModel.Feed.OpenSearchResultAsync(result, disposition);
                };
                menu.Items.Add(item);
            }
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
            return DataContext is MainWindowViewModel { IsTasksMode: true }
                && GetActiveMainControl()?.TryHandleHotkeyHelpKey(e) == true;
        }

        internal void ShowHotkeyHelp()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.CloseSettings();
                _ = ShowHotkeyHelpAsync(viewModel);
                return;
            }

            GetActiveMainControl()?.ShowHotkeyHelp();
        }

        private async System.Threading.Tasks.Task ShowHotkeyHelpAsync(MainWindowViewModel viewModel)
        {
            if (!viewModel.IsTasksMode)
                await viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Tasks);
            GetActiveMainControl()?.ShowHotkeyHelp();
        }

        internal bool TryHandleShellHotkey(KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel)
            {
                return false;
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
            if (modifiers == KeyModifiers.Control && !IsTextEditorFocused())
            {
                var key = e.Key.ToString();
                if (key is "OemOpenBrackets" or "LeftBracket")
                {
                    _ = viewModel.NavigateWorkspaceBackAsync();
                    e.Handled = true;
                    return true;
                }
                if (key is "OemCloseBrackets" or "RightBracket")
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

        private MainControl? GetActiveMainControl() => DataContext is MainWindowViewModel viewModel
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
