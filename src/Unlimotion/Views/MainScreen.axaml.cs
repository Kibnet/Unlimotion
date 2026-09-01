using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views
{
    public partial class MainScreen : UserControl
    {
        private readonly RotateTransform _tasksLoadingSpinnerTransform = new();
        private readonly DispatcherTimer _tasksLoadingSpinnerTimer;
        private INotifyPropertyChanged? _shellViewModelNotifier;
        private INotifyPropertyChanged? _shellSettingsNotifier;
        private INotifyPropertyChanged? _shellFeedNotifier;
        private INotifyCollectionChanged? _taskSpacesNotifier;
        private bool _shellLayoutUpdatePending;
        private bool _isAttached;

        public MainScreen()
        {
            InitializeComponent();
            TasksLoadingSpinner.RenderTransform = _tasksLoadingSpinnerTransform;
            _tasksLoadingSpinnerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _tasksLoadingSpinnerTimer.Tick += TasksLoadingSpinnerTimerOnTick;
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
            SizeChanged += (_, _) => ScheduleShellLayoutUpdate();
            DataContextChanged += OnDataContextChanged;
            foreach (var control in new Control[]
                     {
                         GlobalCreateMenuButton,
                         TaskSpaceSelector,
                         ShellModeSelector,
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
            AttachShellLayoutSources();
            _tasksLoadingSpinnerTimer.Start();
            ScheduleShellLayoutUpdate();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            DetachShellLayoutSources();
            if (_isAttached)
            {
                AttachShellLayoutSources();
            }

            ScheduleShellLayoutUpdate();
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

            _shellViewModelNotifier = null;
            _shellSettingsNotifier = null;
            _shellFeedNotifier = null;
            _taskSpacesNotifier = null;
        }

        private void OnShellLayoutSourceChanged(object? sender, PropertyChangedEventArgs e) =>
            ScheduleShellLayoutUpdate();

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
            ShellModeSelector.IsVisible = true;
            GlobalReviewButton.IsVisible = true;
            GlobalSettingsButton.IsVisible = true;

            var availableWidth = Math.Max(0, Bounds.Width - 24);
            var fixedControls = new Control[]
            {
                GlobalCreateMenuButton,
                TaskSpaceSelector,
                ShellModeSelector,
                GlobalReviewButton,
                GlobalSettingsButton,
                GlobalOverflowMenuButton
            };
            var wideRequired = fixedControls.Sum(GetMeasuredWidth)
                + 240
                + ShellAppBarGrid.ColumnSpacing * fixedControls.Length;
            var compact = wideRequired > availableWidth;
            Grid.SetRow(GlobalSearchHost, compact ? 1 : 0);
            Grid.SetColumn(GlobalSearchHost, compact ? 0 : 3);
            Grid.SetColumnSpan(GlobalSearchHost, compact ? 7 : 1);
            GlobalSearchHost.Margin = compact ? new Thickness(0, 8, 0, 0) : default;

            HideLastActionWhileOverflowing(GlobalSettingsButton, availableWidth);
            HideLastActionWhileOverflowing(GlobalReviewButton, availableWidth);
            HideLastActionWhileOverflowing(TaskSpaceSelector, availableWidth);
            HideLastActionWhileOverflowing(ShellModeSelector, availableWidth);

            var spaceInOverflow = !TaskSpaceSelector.IsVisible;
            var modeInOverflow = !ShellModeSelector.IsVisible;
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
                viewModel.SelectedWorkspaceMode = WorkspaceMode.Feed;
            }
        }

        private void OnTasksModeMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SelectedWorkspaceMode = WorkspaceMode.Tasks;
            }
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            DetachShellLayoutSources();
            _tasksLoadingSpinnerTimer.Stop();
        }

        private void TasksLoadingSpinnerTimerOnTick(object? sender, EventArgs e)
        {
            if (!TasksLoadingOverlay.IsVisible)
            {
                return;
            }

            _tasksLoadingSpinnerTransform.Angle = (_tasksLoadingSpinnerTransform.Angle + 18d) % 360d;
        }

        private void OnGlobalSearchResultClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: FeedSearchResultViewModel result }
                || DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            if (result.Type != Unlimotion.Notes.Search.FeedSearchDocumentType.Task)
            {
                viewModel.SelectedWorkspaceMode = WorkspaceMode.Feed;
            }

            if (viewModel.Feed.OpenSearchResultCommand.CanExecute(result))
            {
                viewModel.Feed.OpenSearchResultCommand.Execute(result);
                e.Handled = true;
            }
        }

        internal bool TryHandleHotkeyHelpKey(KeyEventArgs e)
        {
            return DataContext is MainWindowViewModel { IsTasksMode: true }
                && MainControl.TryHandleHotkeyHelpKey(e);
        }

        internal void ShowHotkeyHelp()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.CloseSettings();
                viewModel.SelectedWorkspaceMode = WorkspaceMode.Tasks;
            }

            MainControl.ShowHotkeyHelp();
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
    }
}
