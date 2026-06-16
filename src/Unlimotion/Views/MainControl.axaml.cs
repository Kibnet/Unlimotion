using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views.Graph;
using SearchBarView = Unlimotion.Views.SearchControl.SearchBar;
using SearchControlView = Unlimotion.Views.SearchControl.SearchControl;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views
{
    public partial class MainControl : UserControl
    {
        private enum TreeCommandRoute
        {
            Hotkey,
            ContextMenu
        }

        private static readonly string[] KnownMainTaskTreeNames =
        [
            "AllTasksTree",
            "LastCreatedTree",
            "LastUpdatedTree",
            "UnlockedTree",
            "InProgressTree",
            "CompletedTree",
            "ArchivedTree",
            "LastOpenedTree"
        ];

        // Static dependencies - set once during app initialization
        public static IDialogs? DialogsInstance { get; set; }
        private const int MaxTitleFocusRetries = 5;
        private const int MaxRelationEditorFocusRetries = 5;
        private const int MaxCompletionCriterionFocusRetries = 5;
        private const double NarrowFilterToolbarMaxWidth = 520d;
        private const double CompactTaskDetailsMaxWidth = 430d;
        private const double RegularTaskPlanningGroupWidth = 176d;
        private const double CompactTaskDetailsContentInset = 18d;
        private const double RegularTaskDetailsContentInset = 24d;
        private const double TaskPlanningGroupGap = 4d;
        private const double RepeaterControlGap = 6d;
        private const double WeekdayToggleGap = 4d;
        private const double RegularTaskIdMaxWidth = 180d;
        private const double MainTabsOverflowButtonSpacing = 6d;
        private const string NarrowFilterToolbarClass = "NarrowFilterToolbar";
        private const string CompactTaskDetailsClass = "TaskDetailsCompact";
        private IDisposable? _titleFocusSubscription;
        private IDisposable? _relationEditorFocusSubscription;
        private IDisposable? _currentTaskCompletionCriterionSubscription;
        private IDisposable? _completionCriterionFocusSubscription;
        private IDisposable? _taskDetailsBoundsSubscription;
        private readonly List<IDisposable> _mainTabsLayoutSubscriptions = [];
        private MainWindowViewModel? _treeCommandViewModel;
        private TreeView? _activeTaskTree;
        private TreeView? _contextMenuTree;
        private TaskWrapperViewModel? _contextMenuWrapper;
        private PendingTreeDragContext? _pendingTreeDrag;
        private TextBox? _activeInlineTitleEditor;
        private TreeView? _lastInlineTitleClickTree;
        private string? _lastInlineTitleClickTaskId;
        private DateTimeOffset? _lastInlineTitleClickAt;
        private bool _treeDragInProgress;
        private bool _filterToolbarLayoutUpdateQueued;
        private bool _taskDetailsLayoutUpdateQueued;
        private bool _mainTabsOverflowUpdateQueued;
        private int _selectionRestoreVersion;
        private ILocalizationService? _mainTabsLocalizationSubscriptionSource;
        private readonly Dictionary<string, double> _mainTabWidthCache = [];
        private readonly Dictionary<string, MainTabVisualState> _mainTabVisualStateCache = [];
        private readonly HashSet<Grid> _observedFilterToolbars = [];
        private readonly List<IDisposable> _filterToolbarBoundsSubscriptions = [];

        private sealed class PendingTreeDragContext(
            Control control,
            TreeView tree,
            TaskWrapperViewModel wrapper,
            PointerPressedEventArgs pressEvent,
            Point startPoint,
            IReadOnlyList<TaskWrapperViewModel> selectionSnapshot,
            bool wasSelected)
        {
            public Control Control { get; } = control;
            public TreeView Tree { get; } = tree;
            public TaskWrapperViewModel Wrapper { get; } = wrapper;
            public PointerPressedEventArgs PressEvent { get; } = pressEvent;
            public Point StartPoint { get; } = startPoint;
            public IReadOnlyList<TaskWrapperViewModel> SelectionSnapshot { get; } = selectionSnapshot;
            public bool WasSelected { get; } = wasSelected;
        }

        private sealed class TaskTreeDragData(IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            public IReadOnlyList<TaskWrapperViewModel> Wrappers { get; } =
                wrappers.NormalizeForDeleteBatch();
        }

        private enum BatchDropOperationKind
        {
            CopyInto,
            MoveInto,
            CloneInto,
            SourcesBlockTarget,
            TargetBlocksSources
        }

        private sealed class DragSourceOperationItem(
            TaskItemViewModel taskItem,
            TaskItemViewModel? sourceParent = null,
            TaskWrapperViewModel? wrapper = null)
        {
            public TaskItemViewModel TaskItem { get; } = taskItem;
            public TaskItemViewModel? SourceParent { get; } = sourceParent;
            public TaskWrapperViewModel? Wrapper { get; } = wrapper;
        }

        private sealed record MainTabVisualState(
            Thickness Padding,
            double Width,
            double MinWidth,
            double MaxWidth);

        public MainControl()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, MainControl_OnKeyDown, RoutingStrategies.Tunnel);
            AddHandler(GotFocusEvent, MainControl_OnGotFocus, RoutingStrategies.Tunnel);
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            DataContextChanged += MainWindow_DataContextChanged;
            AttachedToVisualTree += MainControl_OnAttachedToVisualTree;
            DetachedFromVisualTree += MainControl_OnDetachedFromVisualTree;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty)
            {
                QueueMainTabsOverflowUpdate();
                QueueFilterToolbarLayoutUpdate();
                QueueTaskDetailsLayoutUpdate();
            }
        }

        private void MainControl_OnGotFocus(object? sender, RoutedEventArgs e)
        {
            if (_activeInlineTitleEditor == null ||
                e.Source is not Control focused ||
                IsControlOrDescendantOf(focused, _activeInlineTitleEditor))
            {
                return;
            }

            ClearActiveInlineTitleEditor();
        }

        private void MainControl_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            ObserveMainTabsLayout();
            ObserveMainTabsLocalization();
            QueueMainTabsOverflowUpdate();
            QueueLateMainTabsOverflowUpdate();
            QueueFilterToolbarLayoutUpdate();
            ObserveTaskDetailsBounds();
            QueueTaskDetailsLayoutUpdate();
        }

        private void MainControl_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            foreach (var subscription in _filterToolbarBoundsSubscriptions)
            {
                subscription.Dispose();
            }

            _filterToolbarBoundsSubscriptions.Clear();
            _observedFilterToolbars.Clear();
            _filterToolbarLayoutUpdateQueued = false;
            _taskDetailsBoundsSubscription?.Dispose();
            _taskDetailsBoundsSubscription = null;
            _taskDetailsLayoutUpdateQueued = false;
            foreach (var subscription in _mainTabsLayoutSubscriptions)
            {
                subscription.Dispose();
            }

            _mainTabsLayoutSubscriptions.Clear();
            StopObservingMainTabsLocalization();
            _mainTabsOverflowUpdateQueued = false;
        }

        private void ObserveMainTabsLayout()
        {
            if (_mainTabsLayoutSubscriptions.Count > 0)
            {
                return;
            }

            _mainTabsLayoutSubscriptions.Add(MainTabsHost.GetObservable(BoundsProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            _mainTabsLayoutSubscriptions.Add(MainTabs.GetObservable(BoundsProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            _mainTabsLayoutSubscriptions.Add(MainNavigationSplitView.GetObservable(BoundsProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            _mainTabsLayoutSubscriptions.Add(MainNavigationSplitView.GetObservable(SplitView.IsPaneOpenProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            _mainTabsLayoutSubscriptions.Add(MainNavigationSplitView.GetObservable(SplitView.OpenPaneLengthProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            _mainTabsLayoutSubscriptions.Add(MainNavigationSplitView.GetObservable(SplitView.CompactPaneLengthProperty)
                .Skip(1)
                .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            foreach (var tab in MainTabs.Items.OfType<TabItem>())
            {
                _mainTabsLayoutSubscriptions.Add(tab.GetObservable(BoundsProperty)
                    .Skip(1)
                    .Subscribe(_ => QueueMainTabsOverflowUpdate()));
            }
        }

        private void ObserveMainTabsLocalization()
        {
            var localization = LocalizationService.Current;
            if (ReferenceEquals(_mainTabsLocalizationSubscriptionSource, localization))
            {
                return;
            }

            StopObservingMainTabsLocalization();
            _mainTabsLocalizationSubscriptionSource = localization;
            localization.CultureChanged += MainControl_OnCultureChanged;
        }

        private void StopObservingMainTabsLocalization()
        {
            if (_mainTabsLocalizationSubscriptionSource == null)
            {
                return;
            }

            _mainTabsLocalizationSubscriptionSource.CultureChanged -= MainControl_OnCultureChanged;
            _mainTabsLocalizationSubscriptionSource = null;
        }

        private void MainControl_OnCultureChanged(object? sender, EventArgs e)
        {
            _mainTabWidthCache.Clear();
            QueueMainTabsOverflowUpdate();
            QueueFilterToolbarLayoutUpdate();
        }

        private void ObserveTaskDetailsBounds()
        {
            _taskDetailsBoundsSubscription ??= CurrentTaskDetailsScrollViewer.GetObservable(BoundsProperty)
                .Skip(1)
                .Subscribe(_ => QueueTaskDetailsLayoutUpdate());
        }

        private void QueueTaskDetailsLayoutUpdate()
        {
            if (_taskDetailsLayoutUpdateQueued)
            {
                return;
            }

            _taskDetailsLayoutUpdateQueued = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _taskDetailsLayoutUpdateQueued = false;
                    UpdateTaskDetailsLayout();
                },
                DispatcherPriority.Loaded);
        }

        private void UpdateTaskDetailsLayout()
        {
            var detailsWidth = CurrentTaskDetailsScrollViewer.Bounds.Width > 0
                ? CurrentTaskDetailsScrollViewer.Bounds.Width
                : Bounds.Width;

            if (detailsWidth <= 0)
            {
                return;
            }

            var isCompact = detailsWidth <= CompactTaskDetailsMaxWidth;
            if (TaskDetailsPanelRoot.Classes.Contains(CompactTaskDetailsClass) != isCompact)
            {
                if (isCompact)
                {
                    TaskDetailsPanelRoot.Classes.Add(CompactTaskDetailsClass);
                }
                else
                {
                    TaskDetailsPanelRoot.Classes.Remove(CompactTaskDetailsClass);
                }
            }

            ApplyTaskDetailsMeasuredWidths(detailsWidth, isCompact);
        }

        private void ApplyTaskDetailsMeasuredWidths(double detailsWidth, bool isCompact)
        {
            var compactCardContentWidth = Math.Max(180d, detailsWidth - CompactTaskDetailsContentInset);
            var regularCardContentWidth = Math.Max(420d, detailsWidth - RegularTaskDetailsContentInset);
            var planningGroups = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<StackPanel>()
                .Where(static panel => panel.Classes.Contains("TaskPlanningGroup"))
                .ToArray();
            var regularPlanningGroupWidth = planningGroups.Length > 0
                ? Math.Max(
                    RegularTaskPlanningGroupWidth,
                    (regularCardContentWidth - (planningGroups.Length - 1) * TaskPlanningGroupGap) / planningGroups.Length)
                : RegularTaskPlanningGroupWidth;

            for (var i = 0; i < planningGroups.Length; i++)
            {
                var group = planningGroups[i];
                var isLastGroup = i == planningGroups.Length - 1;
                group.Width = isCompact ? compactCardContentWidth : regularPlanningGroupWidth;
                group.Margin = isCompact
                    ? new Thickness(0, 0, 0, 8)
                    : new Thickness(0, 0, isLastGroup ? 0 : TaskPlanningGroupGap, 5);
            }

            var repeaterSelectors = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<ComboBox>()
                .Where(static comboBox => comboBox.Classes.Contains("RepeaterSelector"))
                .ToArray();
            var repeaterControlGrids = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<Grid>()
                .Where(static grid => grid.Classes.Contains("RepeaterControls"))
                .ToArray();
            var measuredRepeaterSectionWidth = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(static control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "CurrentTaskRepeaterSection",
                        StringComparison.Ordinal))
                ?.Bounds.Width ?? 0d;
            var repeaterContentWidth = isCompact ? compactCardContentWidth : regularCardContentWidth;
            if (measuredRepeaterSectionWidth > 0)
            {
                repeaterContentWidth = Math.Min(repeaterContentWidth, measuredRepeaterSectionWidth);
            }

            var repeaterPatternTypeSelectors = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<ComboBox>()
                .Where(static comboBox => comboBox.Classes.Contains("RepeaterPatternTypeSelector"))
                .ToArray();
            var repeaterPeriodInputs = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<NumericUpDown>()
                .Where(static numericUpDown => numericUpDown.Classes.Contains("RepeaterPeriodInput"))
                .ToArray();
            var repeaterAfterCompleteCheckBoxes = TaskDetailsPanelRoot.GetVisualDescendants()
                .OfType<CheckBox>()
                .Where(static checkBox => checkBox.Classes.Contains("RepeaterAfterCompleteCheckBox"))
                .ToArray();
            var useCompactRepeaterLayout = isCompact;
            var regularRepeaterPeriodWidth = MeasureMaxDesiredWidth(repeaterPeriodInputs);
            var regularRepeaterFlexibleColumnsWidth = Math.Max(
                0d,
                repeaterContentWidth - regularRepeaterPeriodWidth - 3 * RepeaterControlGap);
            var regularRepeaterPatternControlsWidth = Math.Max(
                0d,
                regularRepeaterFlexibleColumnsWidth / 3 * 2 + regularRepeaterPeriodWidth + 2 * RepeaterControlGap);

            foreach (var grid in repeaterControlGrids)
            {
                grid.Width = double.NaN;
                grid.MaxWidth = repeaterContentWidth;
                grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.ColumnDefinitions = useCompactRepeaterLayout
                    ? new ColumnDefinitions("*,Auto")
                    : new ColumnDefinitions("*,*,Auto,*");
                grid.RowDefinitions = useCompactRepeaterLayout
                    ? new RowDefinitions("Auto,Auto,Auto")
                    : new RowDefinitions("Auto,Auto");
            }

            foreach (var selector in repeaterSelectors)
            {
                var hasVisiblePattern = selector.DataContext is TaskItemViewModel { IsHaveRepeater: true };
                Grid.SetRow(selector, 0);
                Grid.SetColumn(selector, 0);
                Grid.SetColumnSpan(selector, useCompactRepeaterLayout && !hasVisiblePattern ? 2 : 1);
                selector.Width = double.NaN;
                selector.HorizontalAlignment = HorizontalAlignment.Stretch;
                selector.Margin = useCompactRepeaterLayout
                    ? new Thickness(0, 0, hasVisiblePattern ? RepeaterControlGap : 0, 8)
                    : new Thickness(0, 0, RepeaterControlGap, 5);
            }

            foreach (var selector in repeaterPatternTypeSelectors)
            {
                Grid.SetRow(selector, 0);
                Grid.SetColumn(selector, 1);
                Grid.SetColumnSpan(selector, 1);
                selector.Width = double.NaN;
                selector.HorizontalAlignment = HorizontalAlignment.Stretch;
                selector.Margin = useCompactRepeaterLayout ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, RepeaterControlGap, 5);
            }

            foreach (var input in repeaterPeriodInputs)
            {
                Grid.SetRow(input, useCompactRepeaterLayout ? 1 : 0);
                Grid.SetColumn(input, useCompactRepeaterLayout ? 0 : 2);
                Grid.SetColumnSpan(input, 1);
                input.Width = double.NaN;
                input.HorizontalAlignment = useCompactRepeaterLayout ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                input.Margin = useCompactRepeaterLayout ? new Thickness(0, 0, 10, 8) : new Thickness(0, 0, RepeaterControlGap, 5);
            }

            foreach (var checkbox in repeaterAfterCompleteCheckBoxes)
            {
                Grid.SetRow(checkbox, useCompactRepeaterLayout ? 1 : 0);
                Grid.SetColumn(checkbox, useCompactRepeaterLayout ? 1 : 3);
                Grid.SetColumnSpan(checkbox, 1);
                checkbox.Width = double.NaN;
                checkbox.HorizontalAlignment = useCompactRepeaterLayout ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
                checkbox.Margin = useCompactRepeaterLayout ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 5);
            }

            foreach (var weekdayPanel in TaskDetailsPanelRoot.GetVisualDescendants()
                         .OfType<WrapPanel>()
                         .Where(static panel => panel.Classes.Contains("WeekdayToggles")))
            {
                Grid.SetRow(weekdayPanel, useCompactRepeaterLayout ? 2 : 1);
                Grid.SetColumn(weekdayPanel, useCompactRepeaterLayout ? 0 : 1);
                Grid.SetColumnSpan(weekdayPanel, useCompactRepeaterLayout ? 2 : 3);
                var weekdayPanelWidth = useCompactRepeaterLayout ? repeaterContentWidth : regularRepeaterPatternControlsWidth;
                weekdayPanel.Width = weekdayPanelWidth;
                weekdayPanel.Margin = useCompactRepeaterLayout ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 0, 5);

                var weekdayToggles = weekdayPanel.GetVisualDescendants()
                    .OfType<ToggleButton>()
                    .Where(static toggle => toggle.Classes.Contains("WeekdayToggle"))
                    .ToArray();
                var weekdayWidth = weekdayToggles.Length > 0
                    ? Math.Max(
                        38d,
                        Math.Floor((weekdayPanelWidth - (weekdayToggles.Length - 1) * WeekdayToggleGap) / weekdayToggles.Length))
                    : 46d;

                for (var i = 0; i < weekdayToggles.Length; i++)
                {
                    var toggle = weekdayToggles[i];
                    var isLastToggle = i == weekdayToggles.Length - 1;
                    toggle.Width = weekdayWidth;
                    toggle.Margin = new Thickness(0, 0, isLastToggle ? 0 : WeekdayToggleGap, 4);
                }
            }

            foreach (var idText in TaskDetailsPanelRoot.GetVisualDescendants()
                         .OfType<TextBlock>()
                         .Where(static textBlock => textBlock.Classes.Contains("TaskHeaderIdMeta")))
            {
                idText.MaxWidth = isCompact ? compactCardContentWidth : RegularTaskIdMaxWidth;
            }

            foreach (var suggestions in TaskDetailsPanelRoot.GetVisualDescendants()
                         .OfType<ListBox>()
                         .Where(static listBox => listBox.Classes.Contains("RelationEditorSuggestions")))
            {
                suggestions.MaxHeight = isCompact ? 180d : 240d;
            }

            foreach (var tree in TaskDetailsPanelRoot.GetVisualDescendants()
                         .OfType<TreeView>()
                         .Where(static treeView => treeView.Classes.Contains("RelationTaskTree")))
            {
                tree.MaxWidth = isCompact ? compactCardContentWidth : double.PositiveInfinity;
            }
        }

        private static double MeasureMaxDesiredWidth<TControl>(IEnumerable<TControl> controls)
            where TControl : Control
        {
            return controls
                .Where(static control => control.IsVisible)
                .Select(static control =>
                {
                    control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return control.DesiredSize.Width;
                })
                .DefaultIfEmpty(0d)
                .Max();
        }

        private void MainTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(sender, e.Source))
            {
                QueueMainTabsOverflowUpdate();
                QueueFilterToolbarLayoutUpdate();
            }
        }

        private void QueueMainTabsOverflowUpdate()
        {
            if (_mainTabsOverflowUpdateQueued)
            {
                return;
            }

            _mainTabsOverflowUpdateQueued = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _mainTabsOverflowUpdateQueued = false;
                    UpdateMainTabsOverflow();
                },
                DispatcherPriority.Loaded);
        }

        private void QueueLateMainTabsOverflowUpdate()
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    _mainTabWidthCache.Clear();
                    QueueMainTabsOverflowUpdate();
                },
                DispatcherPriority.Background);
        }

        private void UpdateMainTabsOverflow()
        {
            var tabs = MainTabs.Items.OfType<TabItem>().ToList();
            if (tabs.Count == 0)
            {
                MainTabsOverflowButton.IsVisible = false;
                GetMainTabsOverflowFlyout().Items.Clear();
                return;
            }

            var availableWidth = GetMainTabsAvailableWidth();
            if (availableWidth <= 0)
            {
                return;
            }

            ApplyMainTabsHostWidth(availableWidth);
            var tabWidths = MeasureMainTabWidths(tabs);
            var allTabsWidth = tabWidths.Values.Sum();
            if (allTabsWidth <= availableWidth + 1)
            {
                ApplyMainTabsWidth(availableWidth);
                foreach (var tab in tabs)
                {
                    ShowMainTab(tab);
                }

                MainTabsOverflowButton.IsVisible = false;
                GetMainTabsOverflowFlyout().Items.Clear();
                return;
            }

            var selectedTab = tabs.FirstOrDefault(static tab => tab.IsSelected) ??
                              MainTabs.SelectedItem as TabItem ??
                              tabs[0];
            var selectedAutomationId = GetMainTabAutomationId(selectedTab);
            var overflowButtonWidth = GetMainTabsOverflowButtonWidth();
            var tabCapacity = Math.Max(
                0,
                availableWidth - overflowButtonWidth - MainTabsOverflowButtonSpacing);
            var visibleAutomationIds = new HashSet<string>(StringComparer.Ordinal)
            {
                selectedAutomationId
            };
            var usedWidth = tabWidths[selectedAutomationId];

            foreach (var tab in tabs)
            {
                var automationId = GetMainTabAutomationId(tab);
                if (string.Equals(automationId, selectedAutomationId, StringComparison.Ordinal))
                {
                    continue;
                }

                var tabWidth = tabWidths[automationId];
                if (usedWidth + tabWidth <= tabCapacity + 1)
                {
                    visibleAutomationIds.Add(automationId);
                    usedWidth += tabWidth;
                }
            }

            var hiddenTabs = new List<TabItem>();
            foreach (var tab in tabs)
            {
                var isVisible = visibleAutomationIds.Contains(GetMainTabAutomationId(tab));
                if (isVisible)
                {
                    ShowMainTab(tab);
                }
                else
                {
                    HideMainTab(tab);
                    hiddenTabs.Add(tab);
                }
            }

            ApplyMainTabsWidth(availableWidth);
            ApplyMainTabsOverflowButtonOffset(usedWidth, availableWidth, overflowButtonWidth);
            MainTabsOverflowButton.IsVisible = hiddenTabs.Count > 0;
            RebuildMainTabsOverflowFlyout(hiddenTabs);
        }

        private void ApplyMainTabsHostWidth(double availableWidth)
        {
            if (availableWidth <= 0 || double.IsNaN(availableWidth) || double.IsInfinity(availableWidth))
            {
                return;
            }

            if (Math.Abs(MainTabsHost.Width - availableWidth) > 0.5 ||
                double.IsNaN(MainTabsHost.Width))
            {
                MainTabsHost.Width = availableWidth;
            }
        }

        private void ApplyMainTabsWidth(double width)
        {
            if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width))
            {
                return;
            }

            if (Math.Abs(MainTabs.Width - width) > 0.5 ||
                double.IsNaN(MainTabs.Width))
            {
                MainTabs.Width = width;
            }
        }

        private double GetMainTabsAvailableWidth()
        {
            var availableWidth = Bounds.Width > 0
                ? Bounds.Width
                : MainTabsHost.Bounds.Width;
            var splitViewContentWidth = GetMainNavigationContentWidth();
            if (splitViewContentWidth > 0)
            {
                availableWidth = availableWidth > 0
                    ? Math.Min(availableWidth, splitViewContentWidth)
                    : splitViewContentWidth;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.ClientSize.Width > 0)
            {
                var hostTopLeft = MainTabsHost.TranslatePoint(new Point(0, 0), topLevel);
                var viewportWidth = hostTopLeft.HasValue
                    ? Math.Max(0, topLevel.ClientSize.Width - hostTopLeft.Value.X - MainTabsHost.Margin.Right)
                    : topLevel.ClientSize.Width;
                availableWidth = availableWidth > 0
                    ? Math.Min(availableWidth, viewportWidth)
                    : viewportWidth;
            }

            return availableWidth;
        }

        private double GetMainNavigationContentWidth()
        {
            var splitViewWidth = MainNavigationSplitView.Bounds.Width;
            if (splitViewWidth <= 0)
            {
                return 0;
            }

            var paneWidth = MainNavigationSplitView.DisplayMode switch
            {
                SplitViewDisplayMode.Inline => MainNavigationSplitView.IsPaneOpen
                    ? MainNavigationSplitView.OpenPaneLength
                    : 0,
                SplitViewDisplayMode.CompactInline => MainNavigationSplitView.IsPaneOpen
                    ? MainNavigationSplitView.OpenPaneLength
                    : MainNavigationSplitView.CompactPaneLength,
                _ => 0
            };

            return Math.Max(
                0,
                splitViewWidth - paneWidth - MainTabsHost.Margin.Left - MainTabsHost.Margin.Right);
        }

        private Dictionary<string, double> MeasureMainTabWidths(IReadOnlyCollection<TabItem> tabs)
        {
            var widths = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var tab in tabs)
            {
                ShowMainTab(tab);

                var automationId = GetMainTabAutomationId(tab);
                tab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var width = GetPositiveWidth(tab.Bounds.Width);
                if (width <= 0)
                {
                    width = Math.Max(
                        GetPositiveWidth(tab.DesiredSize.Width),
                        MeasureMainTabHeaderWidth(tab));
                }
                if (_mainTabWidthCache.TryGetValue(automationId, out var cachedWidth))
                {
                    width = Math.Max(width, GetPositiveWidth(cachedWidth));
                }

                if (width <= 0)
                {
                    width = 72d;
                }

                _mainTabWidthCache[automationId] = width;
                widths[automationId] = width;
            }

            return widths;
        }

        private static double MeasureMainTabHeaderWidth(TabItem tab)
        {
            var headerText = tab.Header?.ToString();
            if (string.IsNullOrWhiteSpace(headerText))
            {
                return 0;
            }

            var headerTextBlock = new TextBlock
            {
                Text = headerText,
                FontSize = tab.FontSize,
                FontWeight = tab.FontWeight
            };
            headerTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var textWidth = headerTextBlock.DesiredSize.Width;
            var tabChromeWidth = tab.Padding.Left + tab.Padding.Right +
                                 tab.Margin.Left + tab.Margin.Right + 6d;
            return GetPositiveWidth(textWidth + tabChromeWidth);
        }

        private static double GetPositiveWidth(double width)
        {
            return width > 0 && !double.IsNaN(width) && !double.IsInfinity(width)
                ? width
                : 0;
        }

        private void ApplyMainTabsOverflowButtonOffset(
            double usedTabWidth,
            double availableWidth,
            double overflowButtonWidth)
        {
            var maxLeft = Math.Max(0, availableWidth - overflowButtonWidth);
            var desiredLeft = usedTabWidth + MainTabsOverflowButtonSpacing;
            var left = Math.Min(desiredLeft, maxLeft);
            var margin = new Thickness(left, 0, 0, 0);
            if (MainTabsOverflowButton.Margin != margin)
            {
                MainTabsOverflowButton.Margin = margin;
            }
        }

        private double GetMainTabsOverflowButtonWidth()
        {
            MainTabsOverflowButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = GetPositiveWidth(MainTabsOverflowButton.Bounds.Width);
            if (width <= 0)
            {
                width = GetPositiveWidth(MainTabsOverflowButton.Width);
            }
            if (width <= 0)
            {
                width = GetPositiveWidth(
                    MainTabsOverflowButton.DesiredSize.Width -
                    MainTabsOverflowButton.Margin.Left -
                    MainTabsOverflowButton.Margin.Right);
            }
            if (width <= 0 || double.IsNaN(width))
            {
                width = 40d;
            }

            return width;
        }

        private void ShowMainTab(TabItem tab)
        {
            RestoreMainTabVisualState(tab);
            tab.IsVisible = true;
        }

        private void HideMainTab(TabItem tab)
        {
            CacheMainTabVisualState(tab);
            tab.IsVisible = false;
            tab.Padding = new Thickness(0);
            tab.Width = 0;
            tab.MinWidth = 0;
            tab.MaxWidth = 0;
        }

        private void CacheMainTabVisualState(TabItem tab)
        {
            var automationId = GetMainTabAutomationId(tab);
            _mainTabVisualStateCache.TryAdd(
                automationId,
                new MainTabVisualState(
                    tab.Padding,
                    tab.Width,
                    tab.MinWidth,
                    tab.MaxWidth));
        }

        private void RestoreMainTabVisualState(TabItem tab)
        {
            var automationId = GetMainTabAutomationId(tab);
            if (!_mainTabVisualStateCache.TryGetValue(automationId, out var state))
            {
                return;
            }

            tab.Padding = state.Padding;
            tab.Width = state.Width;
            tab.MinWidth = state.MinWidth;
            tab.MaxWidth = state.MaxWidth;
        }

        private void RebuildMainTabsOverflowFlyout(IReadOnlyCollection<TabItem> hiddenTabs)
        {
            var flyout = GetMainTabsOverflowFlyout();
            flyout.Items.Clear();
            foreach (var tab in hiddenTabs)
            {
                var menuItem = new MenuItem
                {
                    Header = tab.Header
                };
                AutomationProperties.SetAutomationId(
                    menuItem,
                    $"MainTabsOverflow{GetMainTabAutomationId(tab)}");
                menuItem.Click += (_, _) => SelectMainTabFromOverflow(tab);
                flyout.Items.Add(menuItem);
            }
        }

        private MenuFlyout GetMainTabsOverflowFlyout()
        {
            return MainTabsOverflowButton.Flyout as MenuFlyout ??
                   throw new InvalidOperationException("Main tabs overflow button must use a MenuFlyout.");
        }

        private void SelectMainTabFromOverflow(TabItem tab)
        {
            ShowMainTab(tab);
            MainTabs.SelectedItem = tab;
            tab.IsSelected = true;
            QueueMainTabsOverflowUpdate();
            QueueFilterToolbarLayoutUpdate();
        }

        private static string GetMainTabAutomationId(TabItem tab)
        {
            return AutomationProperties.GetAutomationId(tab) ??
                   throw new InvalidOperationException("Main tab item must have an automation id.");
        }

        private void QueueFilterToolbarLayoutUpdate()
        {
            if (_filterToolbarLayoutUpdateQueued)
            {
                return;
            }

            _filterToolbarLayoutUpdateQueued = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _filterToolbarLayoutUpdateQueued = false;
                    UpdateFilterToolbarLayouts();
                },
                DispatcherPriority.Loaded);
        }

        private void UpdateFilterToolbarLayouts()
        {
            foreach (var toolbar in this.GetVisualDescendants().OfType<Grid>()
                         .Where(static grid =>
                             grid.Classes.Contains("FilterToolbar") ||
                             grid.Classes.Contains("RoadmapFilterToolbar")))
            {
                UpdateFilterToolbarLayout(toolbar);
            }
        }

        private void UpdateFilterToolbarLayout(Grid toolbar)
        {
            ObserveFilterToolbarBounds(toolbar);

            var toolbarWidth = toolbar.Bounds.Width > 0 ? toolbar.Bounds.Width : Bounds.Width;
            if (toolbarWidth <= 0)
            {
                return;
            }

            ApplyFilterFlyoutPanelBounds(toolbar);

            var filterItems = toolbar.Children
                .OfType<WrapPanel>()
                .FirstOrDefault(static panel => panel.Classes.Contains("FilterToolbarItems")) ??
                toolbar.Children.OfType<WrapPanel>().FirstOrDefault();
            var searchBar = toolbar.Children.OfType<SearchBarView>().FirstOrDefault();

            if (filterItems == null || searchBar == null)
            {
                return;
            }

            var isNarrow = toolbarWidth <= NarrowFilterToolbarMaxWidth;
            var wasNarrow = toolbar.Classes.Contains(NarrowFilterToolbarClass);

            if (wasNarrow != isNarrow)
            {
                ApplyFilterToolbarMode(toolbar, filterItems, searchBar, isNarrow);
            }

            ApplyFilterToolbarSearchWidth(searchBar, toolbarWidth, isNarrow);
            FilterToolbarLayout.ApplyAdaptiveEmojiFilterWidths(toolbar, filterItems, searchBar);
        }

        private void ObserveFilterToolbarBounds(Grid toolbar)
        {
            if (!_observedFilterToolbars.Add(toolbar))
            {
                return;
            }

            _filterToolbarBoundsSubscriptions.Add(
                toolbar.GetObservable(BoundsProperty)
                    .Skip(1)
                    .Subscribe(_ => QueueFilterToolbarLayoutUpdate()));
        }

        private static void ApplyFilterToolbarMode(
            Grid toolbar,
            WrapPanel filterItems,
            SearchBarView searchBar,
            bool isNarrow)
        {
            if (isNarrow)
            {
                toolbar.Classes.Add(NarrowFilterToolbarClass);
                toolbar.ColumnDefinitions = new ColumnDefinitions("Auto,*");
                toolbar.RowDefinitions = new RowDefinitions("Auto");
                toolbar.ColumnSpacing = 8;

                Grid.SetRow(searchBar, 0);
                Grid.SetColumn(searchBar, 1);
                Grid.SetRow(filterItems, 0);
                Grid.SetColumn(filterItems, 0);

                searchBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                searchBar.VerticalAlignment = VerticalAlignment.Top;
                searchBar.Margin = new Thickness(0);
                filterItems.HorizontalAlignment = HorizontalAlignment.Left;
                return;
            }

            toolbar.Classes.Remove(NarrowFilterToolbarClass);
            toolbar.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            toolbar.RowDefinitions = new RowDefinitions("Auto");
            toolbar.ColumnSpacing = 8;

            Grid.SetRow(searchBar, 0);
            Grid.SetColumn(searchBar, 1);
            Grid.SetRow(filterItems, 0);
            Grid.SetColumn(filterItems, 0);

            searchBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            searchBar.VerticalAlignment = VerticalAlignment.Top;
            searchBar.Margin = new Thickness(0);
            filterItems.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private static void ApplyFilterToolbarSearchWidth(
            SearchBarView searchBar,
            double toolbarWidth,
            bool isNarrow)
        {
            var searchControl = searchBar.GetVisualDescendants().OfType<SearchControlView>().FirstOrDefault();
            if (isNarrow)
            {
                searchBar.MinWidth = 0;
                searchBar.MaxWidth = Math.Max(0, toolbarWidth);
                if (searchControl != null)
                {
                    searchControl.MinWidth = 0;
                    searchControl.MaxWidth = Math.Max(0, toolbarWidth);
                }

                return;
            }

            searchBar.ClearValue(MinWidthProperty);
            searchBar.ClearValue(MaxWidthProperty);
            if (searchControl != null)
            {
                searchControl.ClearValue(MinWidthProperty);
                searchControl.ClearValue(MaxWidthProperty);
            }
        }

        private void ApplyFilterFlyoutPanelBounds(Control toolbar)
        {
            foreach (var filtersButton in toolbar.GetVisualDescendants()
                         .OfType<DropDownButton>()
                         .Where(static button => button.Classes.Contains("FilterToolbarFiltersButton")))
            {
                FilterFlyoutLayout.ApplyResponsiveBounds(this, filtersButton);
            }
        }

        private void MainControl_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || IsTextInputFocused())
            {
                return;
            }

            if (TryGetTreeCommandHotkey(e, out var kind))
            {
                ExecuteTreeCommand(kind);
                e.Handled = true;
            }
        }

        private void CompletionCriterionRemoveButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskCompletionCriterion criterion } ||
                DataContext is not MainWindowViewModel { CurrentTaskItem: { } currentTask })
            {
                return;
            }

            if (currentTask.RemoveCompletionCriterionCommand.CanExecute(criterion))
            {
                currentTask.RemoveCompletionCriterionCommand.Execute(criterion);
            }

            e.Handled = true;
        }

        private static bool TryGetTreeCommandHotkey(KeyEventArgs e, out TreeCommandKind kind)
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A)
            {
                kind = TreeCommandKind.SelectAll;
                return true;
            }

            if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Delete)
            {
                kind = TreeCommandKind.DeleteSelection;
                return true;
            }

            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                if (e.Key == Key.Right)
                {
                    kind = TreeCommandKind.ExpandCurrentNested;
                    return true;
                }

                if (e.Key == Key.Left)
                {
                    kind = TreeCommandKind.CollapseCurrentNested;
                    return true;
                }

                if (e.Key == Key.C)
                {
                    kind = TreeCommandKind.CopyOutline;
                    return true;
                }

                if (e.Key == Key.V)
                {
                    kind = TreeCommandKind.PasteOutline;
                    return true;
                }
            }

            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
            {
                if (e.Key == Key.Right)
                {
                    kind = TreeCommandKind.ExpandAll;
                    return true;
                }

                if (e.Key == Key.Left)
                {
                    kind = TreeCommandKind.CollapseAll;
                    return true;
                }
            }

            kind = default;
            return false;
        }

        private void MainWindow_DataContextChanged(object? sender, EventArgs e)
        {
            _titleFocusSubscription?.Dispose();
            _titleFocusSubscription = null;
            _relationEditorFocusSubscription?.Dispose();
            _relationEditorFocusSubscription = null;
            _currentTaskCompletionCriterionSubscription?.Dispose();
            _currentTaskCompletionCriterionSubscription = null;
            _completionCriterionFocusSubscription?.Dispose();
            _completionCriterionFocusSubscription = null;
            if (_treeCommandViewModel != null)
            {
                _treeCommandViewModel.ExecuteTreeCommandAction = null;
                _treeCommandViewModel.SetClipboardTextAsync = null;
                _treeCommandViewModel.GetClipboardTextAsync = null;
                _treeCommandViewModel = null;
            }

            _activeTaskTree = null;
            _contextMenuTree = null;
            _contextMenuWrapper = null;
            ClearInlineTitleEditState();

            if (DataContext is MainWindowViewModel vm)
            {
                _treeCommandViewModel = vm;
                vm.ExecuteTreeCommandAction = ExecuteTreeCommand;
                vm.SetClipboardTextAsync = SetClipboardTextAsync;
                vm.GetClipboardTextAsync = GetClipboardTextAsync;
                _titleFocusSubscription = vm.WhenAnyValue(m => m.TitleFocusRequestVersion)
                    .Subscribe(requestVersion => QueueTitleFocus(requestVersion, vm.CurrentTaskItem?.Id, MaxTitleFocusRetries));
                _relationEditorFocusSubscription = vm.CurrentRelationEditor
                    .WhenAnyValue(m => m.FocusRequestVersion)
                    .Subscribe(requestVersion =>
                        QueueRelationEditorFocus(
                            requestVersion,
                            vm.CurrentRelationEditor.InputAutomationId,
                            MaxRelationEditorFocusRetries));
                _currentTaskCompletionCriterionSubscription = vm.WhenAnyValue(m => m.CurrentTaskItem)
                    .Subscribe(task =>
                    {
                        _completionCriterionFocusSubscription?.Dispose();
                        _completionCriterionFocusSubscription = null;

                        if (task == null)
                        {
                            return;
                        }

                        _completionCriterionFocusSubscription = task.WhenAnyValue(m => m.CompletionCriterionFocusRequestVersion)
                            .Subscribe(requestVersion =>
                                QueueCompletionCriterionFocus(
                                    requestVersion,
                                    task.Id,
                                    task.CompletionCriterionFocusTargetId,
                                    MaxCompletionCriterionFocusRetries));
                    });
                vm.MoveToPath = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (vm.CurrentTaskItem == null)
                        return;
                    var dialogs = DialogsInstance;
                    if (dialogs == null) return;
                    
                    var currentTaskStoragePath = (vm.taskRepository?.TaskTreeManager.Storage as FileStorage)?.Path;
                    var path = await dialogs.ShowOpenFolderDialogAsync(
                        L10n.Get("FolderPickerTaskStoragePath"),
                        currentTaskStoragePath);

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var fileStorage = new FileStorage(path);
                        var set = new HashSet<string>();
                        var queue = new Queue<TaskItemViewModel>();
                        queue.Enqueue(vm.CurrentTaskItem);
                        while (queue.Count > 0)
                        {
                            var task = queue.Dequeue();
                            if (!set.Contains(task.Id))
                            {
                                set.Add(task.Id);
                                await fileStorage.Save(task.Model);
                                foreach (var item in task.ContainsTasks)
                                {
                                    queue.Enqueue(item);
                                }
                            }
                        }

                        var currentTaskStorage = vm.taskRepository?.TaskTreeManager.Storage;
                        if (currentTaskStorage != null)
                        {
                            foreach (var id in set)
                            {
                                await currentTaskStorage.Remove(id);
                            }
                        }
                    }
                });
            }
        }

        private async Task SetClipboardTextAsync(string text)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ManagerWrapper?.ErrorToast(L10n.Get("ClipboardUnavailable"));
                }

                return;
            }

            await clipboard.SetTextAsync(text);
        }

        private async Task<string?> GetClipboardTextAsync()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ManagerWrapper?.ErrorToast(L10n.Get("ClipboardUnavailable"));
                }

                return null;
            }

            return await clipboard.TryGetTextAsync();
        }

        private void QueueTitleFocus(long requestVersion, string? targetTaskId, int retriesRemaining)
        {
            if (requestVersion <= 0 || string.IsNullOrWhiteSpace(targetTaskId))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => TryFocusTitle(requestVersion, targetTaskId, retriesRemaining),
                DispatcherPriority.Background);
        }

        private void TryFocusTitle(long requestVersion, string targetTaskId, int retriesRemaining)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (vm.TitleFocusRequestVersion != requestVersion || vm.CurrentTaskItem?.Id != targetTaskId)
            {
                return;
            }

            var titleTextBox = this.FindControl<TextBox>("CurrentTaskTitleTextBox");
            if (titleTextBox == null || !titleTextBox.IsAttachedToVisualTree() || !titleTextBox.IsVisible || !titleTextBox.IsEnabled)
            {
                RetryTitleFocus(requestVersion, targetTaskId, retriesRemaining);
                return;
            }

            if (!titleTextBox.Focus())
            {
                RetryTitleFocus(requestVersion, targetTaskId, retriesRemaining);
                return;
            }

            if (string.IsNullOrEmpty(titleTextBox.Text))
            {
                titleTextBox.CaretIndex = titleTextBox.Text?.Length ?? 0;
            }
            else
            {
                titleTextBox.SelectAll();
            }
        }

        private void RetryTitleFocus(long requestVersion, string targetTaskId, int retriesRemaining)
        {
            if (retriesRemaining <= 0)
            {
                return;
            }

            QueueTitleFocus(requestVersion, targetTaskId, retriesRemaining - 1);
        }

        private void QueueRelationEditorFocus(long requestVersion, string? automationId, int retriesRemaining)
        {
            if (requestVersion <= 0 || string.IsNullOrWhiteSpace(automationId))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => TryFocusRelationEditor(requestVersion, automationId, retriesRemaining),
                DispatcherPriority.Loaded);
        }

        private void TryFocusRelationEditor(long requestVersion, string automationId, int retriesRemaining)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var editor = vm.CurrentRelationEditor;
            if (!editor.IsOpen ||
                editor.FocusRequestVersion != requestVersion ||
                !string.Equals(editor.InputAutomationId, automationId, StringComparison.Ordinal))
            {
                return;
            }

            var input = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsVisible &&
                    candidate.IsEnabled);

            if (input == null)
            {
                RetryRelationEditorFocus(requestVersion, automationId, retriesRemaining);
                return;
            }

            if (!input.Focus())
            {
                RetryRelationEditorFocus(requestVersion, automationId, retriesRemaining);
                return;
            }

            input.CaretIndex = input.Text?.Length ?? 0;
        }

        private void RetryRelationEditorFocus(long requestVersion, string automationId, int retriesRemaining)
        {
            if (retriesRemaining <= 0)
            {
                return;
            }

            QueueRelationEditorFocus(requestVersion, automationId, retriesRemaining - 1);
        }

        private void QueueCompletionCriterionFocus(
            long requestVersion,
            string? targetTaskId,
            string? criterionId,
            int retriesRemaining)
        {
            if (requestVersion <= 0 ||
                string.IsNullOrWhiteSpace(targetTaskId) ||
                string.IsNullOrWhiteSpace(criterionId))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => TryFocusCompletionCriterion(requestVersion, targetTaskId, criterionId, retriesRemaining),
                DispatcherPriority.Loaded);
        }

        private void TryFocusCompletionCriterion(
            long requestVersion,
            string targetTaskId,
            string criterionId,
            int retriesRemaining)
        {
            if (DataContext is not MainWindowViewModel { CurrentTaskItem: { } currentTask } ||
                currentTask.Id != targetTaskId ||
                currentTask.CompletionCriterionFocusRequestVersion != requestVersion ||
                !string.Equals(currentTask.CompletionCriterionFocusTargetId, criterionId, StringComparison.Ordinal))
            {
                return;
            }

            var input = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        "CompletionCriterionTextBox",
                        StringComparison.Ordinal) &&
                    candidate.DataContext is TaskCompletionCriterion criterion &&
                    string.Equals(criterion.Id, criterionId, StringComparison.Ordinal) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsVisible &&
                    candidate.IsEnabled);

            if (input == null)
            {
                RetryCompletionCriterionFocus(requestVersion, targetTaskId, criterionId, retriesRemaining);
                return;
            }

            if (!input.Focus())
            {
                RetryCompletionCriterionFocus(requestVersion, targetTaskId, criterionId, retriesRemaining);
                return;
            }

            input.CaretIndex = input.Text?.Length ?? 0;
        }

        private void RetryCompletionCriterionFocus(
            long requestVersion,
            string targetTaskId,
            string criterionId,
            int retriesRemaining)
        {
            if (retriesRemaining <= 0)
            {
                return;
            }

            QueueCompletionCriterionFocus(requestVersion, targetTaskId, criterionId, retriesRemaining - 1);
        }

        private const string CustomFormat = "application/xxx-unlimotion-task";
        private const string CustomBatchFormat = "application/xxx-unlimotion-task-batch";
        private static readonly DataFormat<string> CustomDataFormat =
            DataFormat.CreateStringPlatformFormat(CustomFormat);
        private static readonly DataFormat<string> CustomBatchDataFormat =
            DataFormat.CreateStringPlatformFormat(CustomBatchFormat);
        private const double TreeDragThreshold = 4;
        private static readonly TimeSpan InlineTitleRepeatedClickDelay = TimeSpan.FromMilliseconds(500);

        private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control)
            {
                return;
            }

            unchecked
            {
                _selectionRestoreVersion++;
            }

            UpdateActiveTreeContext(control);

            var tree = TryGetTaskTree(control);
            var wrapper = TryGetWrapper(e.Source) ?? TryGetWrapper(control);
            var point = e.GetCurrentPoint(control);

            if (tree != null && wrapper != null && point.Properties.IsRightButtonPressed)
            {
                var wasContextWrapperSelected = ContainsWrapper(GetSelectedWrappersForTree(tree), wrapper);
                NormalizeSelectionForContextMenu(tree, wrapper);
                if (!wasContextWrapperSelected)
                {
                    QueueSingleWrapperSelectionRestore(tree, wrapper);
                }

                if (tree.ContextMenu != null)
                {
                    tree.ContextMenu.PlacementTarget = e.Source as Control ?? control;
                }

                _contextMenuTree = tree;
                _contextMenuWrapper = wrapper;
                ClearPendingTreeDrag();
                return;
            }

            if (tree == null || wrapper == null || !point.Properties.IsLeftButtonPressed)
            {
                ClearPendingTreeDrag();
                return;
            }

            if (!IsInlineTitleTextSource(e.Source) &&
                IsPointerOverInlineTitleText(tree, wrapper.TaskItem.Id, e))
            {
                HandleInlineTitleClick(tree, wrapper.TaskItem, wrapper, e);
                if (e.Handled)
                {
                    ClearPendingTreeDrag();
                    return;
                }
            }

            if (HasSelectionModifier(e.KeyModifiers))
            {
                ClearPendingTreeDrag();
                return;
            }

            var selectionSnapshot = GetSelectedWrappersForTree(tree);
            var wasSelected = ContainsWrapper(selectionSnapshot, wrapper);
            if (!wasSelected)
            {
                SelectSingleWrapper(tree, wrapper);
                QueueSingleWrapperSelectionRestore(tree, wrapper);
                selectionSnapshot = [wrapper];
                e.Handled = true;
            }
            else
            {
                SyncCurrentWrapperForTree(tree, wrapper);
                e.Handled = true;

                if (selectionSnapshot.Count > 1)
                {
                    // Prevent TreeView from collapsing an existing multi-selection to the drag source
                    // before the drag gesture crosses the threshold.
                }
            }

            _pendingTreeDrag = new PendingTreeDragContext(
                control,
                tree,
                wrapper,
                e,
                e.GetPosition(control),
                selectionSnapshot,
                wasSelected);
        }

        private async void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (sender is not Control control ||
                _pendingTreeDrag == null ||
                _treeDragInProgress ||
                !ReferenceEquals(_pendingTreeDrag.Control, control))
            {
                return;
            }

            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                ClearPendingTreeDrag();
                return;
            }

            if (!HasExceededDragThreshold(_pendingTreeDrag.StartPoint, e.GetPosition(control)))
            {
                return;
            }

            var pending = _pendingTreeDrag;
            _pendingTreeDrag = null;
            _treeDragInProgress = true;

            try
            {
                await StartTreeDragAsync(pending);
            }
            finally
            {
                ClearPendingTreeDrag();
            }
        }

        private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            ClearPendingTreeDrag();
        }

        public static void DragOver(object? sender, DragEventArgs e)
        {
            var mainControl = TryResolveMainControl(sender, e.Source);
            if (mainControl == null || !ContainsSupportedDragData(e.DataTransfer))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            mainControl.HandleDragOver(e);
        }

        public static async Task Drop(object sender, DragEventArgs e)
        {
            var mainControl = TryResolveMainControl(sender, e.Source);
            if (mainControl == null || !ContainsSupportedDragData(e.DataTransfer))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            await mainControl.HandleDropAsync(e);
        }

        private async Task StartTreeDragAsync(PendingTreeDragContext pending)
        {
            var wrappers = GetWrappersForTreeDrag(pending);

            if (wrappers.Count == 0)
            {
                return;
            }

            EnsureTreeSelectionForDragStart(pending.Tree, wrappers, pending.WasSelected);

            var dragData = BuildTreeDragData(wrappers);
                await DragDrop.DoDragDropAsync(
                    pending.PressEvent,
                    dragData,
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private static IReadOnlyList<TaskWrapperViewModel> GetWrappersForTreeDrag(PendingTreeDragContext pending)
        {
            return pending.WasSelected
                ? pending.SelectionSnapshot.NormalizeForDeleteBatch()
                : pending.SelectionSnapshot;
        }

        private void EnsureTreeSelectionForDragStart(
            TreeView tree,
            IReadOnlyList<TaskWrapperViewModel> wrappers,
            bool preserveExistingSelection)
        {
            if (preserveExistingSelection || wrappers.Count == 0)
            {
                return;
            }

            SelectSingleWrapper(tree, wrappers[0]);
        }

        private static InMemoryDragDataTransfer BuildTreeDragData(IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            var dragData = new InMemoryDragDataTransfer();
            var item = new DataTransferItem();
            item.Set(CustomBatchDataFormat, dragData.Track(new TaskTreeDragData(wrappers)));
            item.Set(CustomDataFormat, dragData.Track(wrappers[0]));
            dragData.Add(item);
            return dragData;
        }

        private void HandleDragOver(DragEventArgs e)
        {
            if (!TryGetDropTargetTask(e, out var targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var operationKind = GetBatchDropOperationKind(e.KeyModifiers);
            if (!TryBuildOperationItems(e, operationKind, out var items, out _))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            e.DragEffects = CanApplyDrop(targetTask, items, operationKind)
                ? GetDragEffects(operationKind)
                : DragDropEffects.None;
        }

        private async Task HandleDropAsync(DragEventArgs e)
        {
            if (!TryGetDropTargetTask(e, out var targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var operationKind = GetBatchDropOperationKind(e.KeyModifiers);
            if (!TryBuildOperationItems(e, operationKind, out var items, out var buildError))
            {
                e.DragEffects = DragDropEffects.None;
                ShowBatchDropError(buildError);
                e.Handled = true;
                return;
            }

            if (!CanApplyDrop(targetTask, items, operationKind))
            {
                e.DragEffects = DragDropEffects.None;
                ShowBatchDropError(null);
                e.Handled = true;
                return;
            }

            if (!await ConfirmBatchDropAsync(operationKind, items.Count, targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.DragEffects = GetDragEffects(operationKind);
            await ApplyDropAsync(targetTask, items, operationKind);
            UpdateGraph(e.Source);
            e.Handled = true;
        }

        private static MainControl? TryResolveMainControl(object? sender, object? source)
        {
            return sender as MainControl ??
                   (sender as Control)?.FindParent<MainControl>() ??
                   (source as Control)?.FindParent<MainControl>();
        }

        private static bool ContainsSupportedDragData(IDataTransfer data)
        {
            return data.Contains(CustomBatchDataFormat) ||
                   data.Contains(CustomDataFormat) ||
                   data.Contains(GraphControl.CustomBatchDataFormat) ||
                   data.Contains(GraphControl.CustomDataFormat);
        }

        private static bool TryGetDropTargetTask(DragEventArgs e, out TaskItemViewModel targetTask)
        {
            var control = e.Source as Control;
            targetTask = TryGetDropTargetTask(control)!;
            return targetTask != null;
        }

        private static TaskItemViewModel? TryGetDropTargetTask(Control? control)
        {
            if (control == null)
            {
                return null;
            }

            return control.DataContext switch
            {
                TaskWrapperViewModel wrapper => wrapper.TaskItem,
                TaskItemViewModel task => task,
                RoadmapNode node => node.TaskItem,
                _ => control.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem ??
                     control.FindParentDataContext<TaskItemViewModel>() ??
                     control.FindParentDataContext<RoadmapNode>()?.TaskItem
            };
        }

        private bool TryBuildOperationItems(
            DragEventArgs e,
            BatchDropOperationKind operationKind,
            out IReadOnlyList<DragSourceOperationItem> items,
            out string? errorMessage)
        {
            errorMessage = null;

            if (DragDataFormats.TryGetValue<TaskTreeDragData>(
                    e.DataTransfer,
                    CustomBatchDataFormat,
                    out var batchData))
            {
                var normalizedWrappers = operationKind == BatchDropOperationKind.MoveInto
                    ? batchData.Wrappers.NormalizeForMoveBatch()
                    : batchData.Wrappers.NormalizeForNonMoveBatch();

                var result = new List<DragSourceOperationItem>(normalizedWrappers.Count);
                foreach (var wrapper in normalizedWrappers)
                {
                    if (wrapper?.TaskItem == null)
                    {
                        continue;
                    }

                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(wrapper, out sourceParent))
                    {
                        items = Array.Empty<DragSourceOperationItem>();
                        errorMessage = L10n.Get("BatchMoveMissingParents");
                        return false;
                    }

                    result.Add(new DragSourceOperationItem(
                        wrapper.TaskItem,
                        operationKind == BatchDropOperationKind.MoveInto ? sourceParent : null,
                        wrapper));
                }

                items = result;
                return items.Count > 0;
            }

            if (DragDataFormats.TryGetValue<GraphControl.RoadmapTaskDragData>(
                    e.DataTransfer,
                    GraphControl.CustomBatchDataFormat,
                    out var roadmapBatchData))
            {
                var normalizedTasks = NormalizeRoadmapTasksForBatch(roadmapBatchData.TaskItems);
                var result = new List<DragSourceOperationItem>(normalizedTasks.Count);
                foreach (var taskItem in normalizedTasks)
                {
                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(taskItem, out sourceParent))
                    {
                        items = Array.Empty<DragSourceOperationItem>();
                        errorMessage = L10n.Get("BatchMoveMissingParents");
                        return false;
                    }

                    result.Add(new DragSourceOperationItem(
                        taskItem,
                        operationKind == BatchDropOperationKind.MoveInto ? sourceParent : null));
                }

                items = result;
                return items.Count > 0;
            }

            object? singleSource = null;
            if (!DragDataFormats.TryGetValue<object>(e.DataTransfer, CustomDataFormat, out singleSource))
            {
                DragDataFormats.TryGetValue<TaskItemViewModel>(
                    e.DataTransfer,
                    GraphControl.CustomDataFormat,
                    out var graphSource);
                singleSource = graphSource;
            }

            if (!TryBuildSingleOperationItem(singleSource, operationKind, out var item, out errorMessage))
            {
                items = Array.Empty<DragSourceOperationItem>();
                return false;
            }

            items = [item];
            return true;
        }

        private static bool TryBuildSingleOperationItem(
            object? dragSource,
            BatchDropOperationKind operationKind,
            out DragSourceOperationItem item,
            out string? errorMessage)
        {
            errorMessage = null;
            item = null!;

            switch (dragSource)
            {
                case TaskWrapperViewModel wrapper when wrapper.TaskItem != null:
                {
                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(wrapper, out sourceParent))
                    {
                        errorMessage = L10n.Get("MoveMissingParent");
                        return false;
                    }

                    item = new DragSourceOperationItem(
                        wrapper.TaskItem,
                        operationKind == BatchDropOperationKind.MoveInto ? sourceParent : null,
                        wrapper);
                    return true;
                }
                case TaskItemViewModel taskItem:
                {
                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(taskItem, out sourceParent))
                    {
                        errorMessage = L10n.Get("MoveMissingParent");
                        return false;
                    }

                    item = new DragSourceOperationItem(taskItem, sourceParent);
                    return true;
                }
                default:
                    errorMessage = L10n.Get("UnsupportedDragSource");
                    return false;
            }
        }

        private static bool TryResolveMoveSourceParent(TaskWrapperViewModel wrapper, out TaskItemViewModel? sourceParent)
        {
            sourceParent = null;
            if (wrapper?.TaskItem == null)
            {
                return false;
            }

            if (wrapper.TaskItem.Parents.Count <= 1)
            {
                sourceParent = wrapper.TaskItem.ParentsTasks.FirstOrDefault();
                return true;
            }

            if (wrapper.Parent?.TaskItem != null)
            {
                sourceParent = wrapper.Parent.TaskItem;
                return true;
            }

            return false;
        }

        private static bool TryResolveMoveSourceParent(
            TaskItemViewModel taskItem,
            out TaskItemViewModel? sourceParent)
        {
            sourceParent = null;
            if (taskItem.Parents.Count > 1)
            {
                return false;
            }

            sourceParent = taskItem.ParentsTasks.FirstOrDefault();
            return true;
        }

        private static IReadOnlyList<TaskItemViewModel> NormalizeRoadmapTasksForBatch(
            IEnumerable<TaskItemViewModel>? taskItems)
        {
            var distinctTasks = taskItems?
                                    .Where(static task => task != null &&
                                                          !string.IsNullOrWhiteSpace(task.Id))
                                    .GroupBy(static task => task.Id, StringComparer.Ordinal)
                                    .Select(static group => group.First())
                                    .ToList()
                                ?? [];
            if (distinctTasks.Count <= 1)
            {
                return distinctTasks;
            }

            var selectedIds = distinctTasks
                .Select(static task => task.Id)
                .ToHashSet(StringComparer.Ordinal);

            return distinctTasks
                .Where(task => task.GetAllParents().All(parent => !selectedIds.Contains(parent.Id)))
                .ToList();
        }

        private static bool CanApplyDrop(
            TaskItemViewModel targetTask,
            IReadOnlyList<DragSourceOperationItem> items,
            BatchDropOperationKind operationKind)
        {
            return items.Count > 0 &&
                   items.All(item => item.TaskItem != null) &&
                   operationKind switch
                   {
                       BatchDropOperationKind.CopyInto => items.All(item => item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.MoveInto => items.All(item =>
                           item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.CloneInto => items.All(item => item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.SourcesBlockTarget => items.All(item => targetTask.CanCreateBlockingRelation(item.TaskItem)),
                       BatchDropOperationKind.TargetBlocksSources => items.All(item => item.TaskItem.CanCreateBlockingRelation(targetTask)),
                       _ => false
                   };
        }

        private async Task<bool> ConfirmBatchDropAsync(
            BatchDropOperationKind operationKind,
            int itemCount,
            TaskItemViewModel targetTask)
        {
            if (itemCount <= 1 || DataContext is not MainWindowViewModel vm)
            {
                return true;
            }

            var manager = vm.ManagerWrapper;
            if (manager == null)
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>();
            manager.Ask(
                GetBatchConfirmationHeader(operationKind),
                GetBatchConfirmationMessage(operationKind, itemCount, targetTask),
                () => tcs.TrySetResult(true),
                () => tcs.TrySetResult(false));

            return await tcs.Task;
        }

        private static string GetBatchConfirmationHeader(BatchDropOperationKind operationKind)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto => L10n.Get("CopyTasksHeader"),
                BatchDropOperationKind.MoveInto => L10n.Get("MoveTasksHeader"),
                BatchDropOperationKind.CloneInto => L10n.Get("CloneTasksHeader"),
                BatchDropOperationKind.SourcesBlockTarget => L10n.Get("AddBlockingTasksHeader"),
                BatchDropOperationKind.TargetBlocksSources => L10n.Get("AddBlockedTasksHeader"),
                _ => L10n.Get("ConfirmBatchOperationHeader")
            };
        }

        private static string GetBatchConfirmationMessage(
            BatchDropOperationKind operationKind,
            int itemCount,
            TaskItemViewModel targetTask)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto =>
                    L10n.Format("CopyTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.MoveInto =>
                    L10n.Format("MoveTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.CloneInto =>
                    L10n.Format("CloneTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.SourcesBlockTarget =>
                    L10n.Format("AddBlockingTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.TargetBlocksSources =>
                    L10n.Format("AddBlockedTasksMessage", targetTask.Title, itemCount),
                _ => L10n.Format("ConfirmBatchOperationMessage", itemCount)
            };
        }

        private static async Task ApplyDropAsync(
            TaskItemViewModel targetTask,
            IReadOnlyList<DragSourceOperationItem> items,
            BatchDropOperationKind operationKind)
        {
            foreach (var item in items)
            {
                switch (operationKind)
                {
                    case BatchDropOperationKind.CopyInto:
                        await item.TaskItem.CopyInto(targetTask);
                        break;
                    case BatchDropOperationKind.MoveInto:
                        await item.TaskItem.MoveInto(targetTask, item.SourceParent);
                        break;
                    case BatchDropOperationKind.CloneInto:
                        await item.TaskItem.CloneInto(targetTask);
                        break;
                    case BatchDropOperationKind.SourcesBlockTarget:
                        await targetTask.BlockBy(item.TaskItem);
                        break;
                    case BatchDropOperationKind.TargetBlocksSources:
                        await item.TaskItem.BlockBy(targetTask);
                        break;
                }
            }
        }

        private void ShowBatchDropError(string? errorMessage)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ManagerWrapper?.ErrorToast(errorMessage ?? L10n.Get("BatchOperationInvalid"));
            }
        }

        private static BatchDropOperationKind GetBatchDropOperationKind(KeyModifiers modifiers)
        {
            var relevantModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
            return relevantModifiers switch
            {
                KeyModifiers.Control | KeyModifiers.Shift => BatchDropOperationKind.CloneInto,
                KeyModifiers.Shift => BatchDropOperationKind.MoveInto,
                KeyModifiers.Control => BatchDropOperationKind.SourcesBlockTarget,
                KeyModifiers.Alt => BatchDropOperationKind.TargetBlocksSources,
                _ => BatchDropOperationKind.CopyInto
            };
        }

        private static DragDropEffects GetDragEffects(BatchDropOperationKind operationKind)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto => DragDropEffects.Copy,
                BatchDropOperationKind.MoveInto => DragDropEffects.Move,
                BatchDropOperationKind.CloneInto => DragDropEffects.Copy,
                BatchDropOperationKind.SourcesBlockTarget => DragDropEffects.Link,
                BatchDropOperationKind.TargetBlocksSources => DragDropEffects.Link,
                _ => DragDropEffects.None
            };
        }

        private static bool HasSelectionModifier(KeyModifiers modifiers)
        {
            var relevantModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift);
            return relevantModifiers != KeyModifiers.None;
        }

        private static bool HasExceededDragThreshold(Point startPoint, Point currentPoint)
        {
            return Math.Abs(currentPoint.X - startPoint.X) >= TreeDragThreshold ||
                   Math.Abs(currentPoint.Y - startPoint.Y) >= TreeDragThreshold;
        }

        private void ClearPendingTreeDrag()
        {
            _pendingTreeDrag = null;
            _treeDragInProgress = false;
        }

        private async void BreadScrumbs_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dc = (DataContext as MainWindowViewModel)?.CurrentTaskItem;
            if (dc == null)
            {
                return;
            }

            var dragData = DragDataFormats.CreateTransfer(CustomDataFormat, dc);

            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void Task_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var control = sender as Control;
                if (control?.DataContext is TaskWrapperViewModel wrapper)
                {
                    vm.CurrentTaskItem = wrapper.TaskItem;
                }
            }
        }

        private void RelationAddButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm ||
                sender is not Control control ||
                !Enum.TryParse<TaskRelationKind>(control.Tag?.ToString(), out var kind))
            {
                return;
            }

            vm.OpenRelationEditor(kind);
        }

        private void RelationEditorControl_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not Control { DataContext: TaskRelationEditorViewModel editor })
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (editor.ConfirmCommand.CanExecute(null))
                {
                    editor.ConfirmCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (editor.CancelCommand.CanExecute(null))
                {
                    editor.CancelCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void RelationEditorSuggestions_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskRelationEditorViewModel editor })
            {
                return;
            }

            if (editor.ConfirmCommand.CanExecute(null))
            {
                editor.ConfirmCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void TaskTree_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TreeView tree)
            {
                return;
            }

            _activeTaskTree = tree;
            UpdateContextMenuContext(tree, e);
        }

        private void InlineTaskTitleText_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Handled ||
                sender is not Control control ||
                e.KeyModifiers != KeyModifiers.None ||
                !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var tree = TryGetTaskTree(control);
            if (tree == null || !IsKnownMainTaskTree(tree))
            {
                return;
            }

            var task = TryGetTaskItem(control.DataContext);
            if (task == null || string.IsNullOrWhiteSpace(task.Id))
            {
                return;
            }

            HandleInlineTitleClick(tree, task, TryGetWrapper(control), e);
        }

        private void HandleInlineTitleClick(
            TreeView tree,
            TaskItemViewModel task,
            TaskWrapperViewModel? wrapper,
            PointerPressedEventArgs e)
        {
            var now = DateTimeOffset.UtcNow;
            var lastClickElapsed = _lastInlineTitleClickAt == null
                ? TimeSpan.MaxValue
                : now - _lastInlineTitleClickAt.Value;
            var isLastClickSameTitle =
                ReferenceEquals(_lastInlineTitleClickTree, tree) &&
                string.Equals(_lastInlineTitleClickTaskId, task.Id, StringComparison.Ordinal);
            var isRapidRepeatedTitleClick =
                isLastClickSameTitle && lastClickElapsed < InlineTitleRepeatedClickDelay;

            if (isRapidRepeatedTitleClick ||
                e.ClickCount > 1 && lastClickElapsed < InlineTitleRepeatedClickDelay)
            {
                ClearInlineTitleClickState();
                return;
            }

            var isRepeatedTitleClick =
                isLastClickSameTitle && lastClickElapsed >= InlineTitleRepeatedClickDelay;

            if (wrapper != null)
            {
                UpdateActiveTreeContext(tree);
                SelectSingleWrapper(tree, wrapper);
                QueueSingleWrapperSelectionRestore(tree, wrapper);
                e.Handled = true;
            }
            else if (DataContext is MainWindowViewModel vm && vm.CurrentTaskItem != task)
            {
                vm.CurrentTaskItem = task;
            }

            if (isRepeatedTitleClick && FocusCurrentTaskInlineTitleEditor(tree, task.Id))
            {
                ClearInlineTitleClickState();
                e.Handled = true;
                return;
            }

            _lastInlineTitleClickTree = tree;
            _lastInlineTitleClickTaskId = task.Id;
            _lastInlineTitleClickAt = now;
        }

        private void TaskTree_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled ||
                sender is not TreeView tree ||
                DataContext is not MainWindowViewModel vm ||
                IsTextInputFocused())
            {
                return;
            }

            if (!KnownMainTaskTreeNames.Contains(tree.Name, StringComparer.Ordinal))
            {
                return;
            }

            if (e.KeyModifiers == KeyModifiers.None && IsInlineTitleEditKey(e))
            {
                ClearInlineTitleClickState();
                e.Handled = FocusCurrentTaskInlineTitleEditor(tree, vm.CurrentTaskItem?.Id);
            }
        }

        private static bool IsSelectAllHotkey(KeyEventArgs e)
        {
            return e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A;
        }

        private static bool IsInlineTitleEditKey(KeyEventArgs e)
        {
            return e.Key == Key.F2;
        }

        private bool FocusCurrentTaskInlineTitleEditor(TreeView tree, string? currentTaskId)
        {
            if (string.IsNullOrWhiteSpace(currentTaskId))
            {
                return false;
            }

            var titleEditor = FindInlineTitleEditor(tree, currentTaskId) ??
                              CreateInlineTitleEditor(tree, currentTaskId);

            if (titleEditor == null)
            {
                return false;
            }

            if (!FocusInlineTitleEditor(titleEditor))
            {
                QueueInlineTitleEditorFocus(titleEditor);
            }

            return true;
        }

        private void QueueInlineTitleEditorFocus(TextBox titleEditor)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_activeInlineTitleEditor, titleEditor))
                {
                    FocusInlineTitleEditor(titleEditor);
                }
            }, DispatcherPriority.Loaded);
        }

        private TextBox? CreateInlineTitleEditor(TreeView tree, string currentTaskId)
        {
            var titleText = FindInlineTitleTextBlock(tree, currentTaskId);
            var task = TryGetTaskItem(titleText?.DataContext);
            if (titleText?.Parent is not Panel parent || task == null)
            {
                return null;
            }

            ClearActiveInlineTitleEditor();

            var titleEditor = new TextBox
            {
                DataContext = task,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = titleText.VerticalAlignment,
                MinWidth = Math.Max(titleText.Bounds.Width, 48)
            };
            titleEditor.Classes.Add("InlineTaskTitleEditor");
            if (task.Wanted)
            {
                titleEditor.Classes.Add("IsWanted");
            }

            if (!task.IsCanBeCompleted)
            {
                titleEditor.Classes.Add("IsCanBeCompleted");
            }

            AutomationProperties.SetAutomationId(titleEditor, "InlineTaskTitleTextBox");
            titleEditor.Bind(
                TextBox.TextProperty,
                new Binding(nameof(TaskItemViewModel.Title))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            Grid.SetColumn(titleEditor, Grid.GetColumn(titleText));
            Grid.SetColumnSpan(titleEditor, Grid.GetColumnSpan(titleText));
            Grid.SetRow(titleEditor, Grid.GetRow(titleText));
            Grid.SetRowSpan(titleEditor, Grid.GetRowSpan(titleText));

            titleEditor.LostFocus += InlineTitleEditor_OnLostFocus;
            parent.Children.Add(titleEditor);
            _activeInlineTitleEditor = titleEditor;

            return titleEditor;
        }

        private static TextBlock? FindInlineTitleTextBlock(TreeView tree, string currentTaskId)
        {
            return tree.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "InlineTaskTitleTextBlock",
                        StringComparison.Ordinal) &&
                    TryGetTaskItem(control.DataContext)?.Id == currentTaskId &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private static bool IsPointerOverInlineTitleText(TreeView tree, string currentTaskId, PointerEventArgs e)
        {
            var titleText = FindInlineTitleTextBlock(tree, currentTaskId);
            if (titleText == null)
            {
                return false;
            }

            var point = e.GetPosition(titleText);
            return point.X >= 0 &&
                   point.Y >= 0 &&
                   point.X <= titleText.Bounds.Width &&
                   point.Y <= titleText.Bounds.Height;
        }

        private static bool IsInlineTitleTextSource(object? source)
        {
            return source is Control control &&
                   string.Equals(
                       AutomationProperties.GetAutomationId(control),
                       "InlineTaskTitleTextBlock",
                       StringComparison.Ordinal);
        }

        private static TextBox? FindInlineTitleEditor(TreeView tree, string currentTaskId)
        {
            return tree.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "InlineTaskTitleTextBox",
                        StringComparison.Ordinal) &&
                    TryGetTaskItem(control.DataContext)?.Id == currentTaskId &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private static bool FocusInlineTitleEditor(TextBox titleEditor)
        {
            if (!titleEditor.Focus())
            {
                return false;
            }

            if (!string.IsNullOrEmpty(titleEditor.Text))
            {
                titleEditor.SelectAll();
            }
            else
            {
                titleEditor.CaretIndex = 0;
            }

            return true;
        }

        private void InlineTitleEditor_OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, _activeInlineTitleEditor))
            {
                ClearActiveInlineTitleEditor();
            }
        }

        private void ClearInlineTitleEditState()
        {
            ClearInlineTitleClickState();
            ClearActiveInlineTitleEditor();
        }

        private void ClearInlineTitleClickState()
        {
            _lastInlineTitleClickTree = null;
            _lastInlineTitleClickTaskId = null;
            _lastInlineTitleClickAt = null;
        }

        private void ClearActiveInlineTitleEditor()
        {
            var titleEditor = _activeInlineTitleEditor;
            if (titleEditor == null)
            {
                return;
            }

            titleEditor.LostFocus -= InlineTitleEditor_OnLostFocus;
            if (titleEditor.Parent is Panel parent)
            {
                parent.Children.Remove(titleEditor);
            }

            _activeInlineTitleEditor = null;
        }

        private void TaskTreeContextMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                !Enum.TryParse<TreeCommandKind>(menuItem.Tag?.ToString(), out var kind))
            {
                return;
            }

            RestoreContextMenuContextFromPlacementTarget(menuItem);
            ExecuteTreeCommand(kind, TreeCommandRoute.ContextMenu);
        }
        
        private void TaskTree_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DetailsAreOpen = !vm.DetailsAreOpen;
            }
        }

        private static void UpdateGraph(object? eSource)
        {
            var control = eSource as Control;
            var vm = control?.FindParentDataContext<MainWindowViewModel>();
            if (vm?.Graph?.UpdateGraph != null)
            {
                vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
            }
        }

        private void ExecuteTreeCommand(TreeCommandKind kind)
        {
            ExecuteTreeCommand(kind, TreeCommandRoute.Hotkey, allowDeferredRetry: true);
        }

        private void ExecuteTreeCommand(TreeCommandKind kind, TreeCommandRoute route)
        {
            ExecuteTreeCommand(kind, route, allowDeferredRetry: false);
        }

        private void ExecuteTreeCommand(TreeCommandKind kind, TreeCommandRoute route, bool allowDeferredRetry)
        {
            try
            {
                if (route == TreeCommandRoute.Hotkey)
                {
                    if (IsTextInputFocused())
                    {
                        return;
                    }

                    if (allowDeferredRetry && IsTabHeaderFocused())
                    {
                        Dispatcher.UIThread.Post(
                            () => ExecuteTreeCommand(kind, route, allowDeferredRetry: false),
                            DispatcherPriority.Background);
                        return;
                    }
                }

                if (DataContext is not MainWindowViewModel vm || !TryGetCommandTree(route, out var tree))
                {
                    return;
                }

                switch (kind)
                {
                    case TreeCommandKind.ExpandCurrentNested:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        if (current != null)
                        {
                            vm.ExpandNodeAndDescendants(current);
                        }

                        break;
                    }
                    case TreeCommandKind.CollapseCurrentNested:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        if (current != null)
                        {
                            vm.CollapseNodeDescendants(current);
                        }

                        break;
                    }
                    case TreeCommandKind.ExpandAll:
                    {
                        var roots = GetTreeRoots(tree);
                        if (roots == null)
                        {
                            return;
                        }

                        vm.ExpandAllNodes(roots);
                        break;
                    }
                    case TreeCommandKind.CollapseAll:
                    {
                        var roots = GetTreeRoots(tree);
                        if (roots == null)
                        {
                            return;
                        }

                        vm.CollapseAllNodes(roots);
                        break;
                    }
                    case TreeCommandKind.DeleteSelection:
                    {
                        if (!IsKnownMainTaskTree(tree))
                        {
                            return;
                        }

                        var wrappers = GetSelectedWrappersForTree(vm, tree);
                        if (wrappers.Count == 0)
                        {
                            return;
                        }

                        vm.RemoveSelectedWrappers(wrappers);
                        break;
                    }
                    case TreeCommandKind.SelectAll:
                        tree.SelectAll();
                        break;
                    case TreeCommandKind.CopyOutline:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        ExecuteTaskOutlineCommandAsync(vm, () => current != null
                            ? vm.CopyTaskOutline(current)
                            : vm.CopyTaskOutline(vm.CurrentTaskItem));
                        break;
                    }
                    case TreeCommandKind.PasteOutline:
                    {
                        var destination = GetCurrentWrapperForRoute(vm, tree, route)?.TaskItem ?? vm.CurrentTaskItem;
                        ExecuteTaskOutlineCommandAsync(vm, () => vm.PasteTaskOutline(destination));
                        break;
                    }
                }
            }
            finally
            {
                if (route == TreeCommandRoute.ContextMenu)
                {
                    ClearContextMenuContext();
                }
            }
        }

        private static async void ExecuteTaskOutlineCommandAsync(MainWindowViewModel vm, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                vm.ManagerWrapper?.ErrorToast(L10n.Format("ReactiveUnhandledError", ex.Message));
            }
        }

        private void UpdateActiveTreeContext(Control control)
        {
            var tree = TryGetTaskTree(control);
            if (tree == null)
            {
                return;
            }

            _activeTaskTree = tree;
            tree.Focus();
        }

        private void UpdateContextMenuContext(TreeView tree, PointerPressedEventArgs e)
        {
            ClearContextMenuContext();

            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed)
            {
                return;
            }

            _contextMenuTree = tree;
            _contextMenuWrapper = TryGetWrapper(e.Source);
        }

        private bool TryGetValidatedActiveTree(out TreeView tree)
        {
            if (TryGetValidatedTree(_activeTaskTree, out tree))
            {
                return true;
            }

            _activeTaskTree = null;
            return false;
        }

        private bool TryGetValidatedContextMenuTree(out TreeView tree)
        {
            if (TryGetValidatedTree(_contextMenuTree, out tree))
            {
                return true;
            }

            ClearContextMenuContext();
            return false;
        }

        private bool TryGetFocusedTaskTree(out TreeView tree)
        {
            tree = null!;

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            var candidate = TryGetTaskTree(focused);
            return TryGetValidatedTree(candidate, out tree);
        }

        private bool TryGetCommandTree(TreeCommandRoute route, out TreeView tree)
        {
            if (route == TreeCommandRoute.ContextMenu && TryGetValidatedContextMenuTree(out tree))
            {
                return true;
            }

            if (route == TreeCommandRoute.Hotkey)
            {
                return TryGetHotkeyTree(out tree);
            }

            return TryGetValidatedActiveTree(out tree);
        }

        private bool TryGetHotkeyTree(out TreeView tree)
        {
            tree = null!;

            if (TryGetFocusedTaskTree(out var focusedTree) && ShouldUseActiveTreeForHotkey(focusedTree))
            {
                _activeTaskTree = focusedTree;
                tree = focusedTree;
                return true;
            }

            if (TryGetValidatedActiveTree(out var activeTree) && ShouldUseActiveTreeForHotkey(activeTree))
            {
                tree = activeTree;
                return true;
            }

            if (DataContext is not MainWindowViewModel vm)
            {
                return TryGetVisibleMainTaskTree(out tree);
            }

            return TryGetCurrentModeTree(vm, out tree) ||
                   TryGetVisibleMainTaskTree(out tree);
        }

        private bool TryGetCurrentModeTree(MainWindowViewModel vm, out TreeView tree)
        {
            tree = null!;

            if (!TryGetCurrentModeTreeName(vm, out var treeName))
            {
                return false;
            }

            var candidate = this.FindControl<TreeView>(treeName);
            return TryGetValidatedTree(candidate, out tree);
        }

        private bool ShouldUseActiveTreeForHotkey(TreeView activeTree)
        {
            if (!IsKnownMainTaskTree(activeTree))
            {
                return true;
            }

            if (DataContext is MainWindowViewModel vm &&
                TryGetCurrentModeTreeName(vm, out var treeName))
            {
                return StringComparer.Ordinal.Equals(activeTree.Name, treeName);
            }

            return !TryGetVisibleMainTaskTree(out var visibleTree) ||
                   StringComparer.Ordinal.Equals(activeTree.Name, visibleTree.Name);
        }

        private static bool TryGetCurrentModeTreeName(MainWindowViewModel vm, out string treeName)
        {
            treeName = vm switch
            {
                _ when vm.AllTasksMode => "AllTasksTree",
                _ when vm.LastCreatedMode => "LastCreatedTree",
                _ when vm.LastUpdatedMode => "LastUpdatedTree",
                _ when vm.UnlockedMode => "UnlockedTree",
                _ when vm.InProgressMode => "InProgressTree",
                _ when vm.CompletedMode => "CompletedTree",
                _ when vm.ArchivedMode => "ArchivedTree",
                _ when vm.LastOpenedMode => "LastOpenedTree",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(treeName);
        }

        private static bool IsKnownMainTaskTree(TreeView tree)
        {
            return KnownMainTaskTreeNames.Contains(tree.Name, StringComparer.Ordinal);
        }

        private static TaskItemViewModel? TryGetTaskItem(object? source)
        {
            return source switch
            {
                TaskItemViewModel taskItem => taskItem,
                TaskWrapperViewModel wrapper => wrapper.TaskItem,
                _ => null
            };
        }

        private bool TryGetVisibleMainTaskTree(out TreeView tree)
        {
            foreach (var treeName in KnownMainTaskTreeNames)
            {
                var candidate = this.FindControl<TreeView>(treeName);
                if (TryGetValidatedTree(candidate, out tree))
                {
                    return true;
                }
            }

            tree = null!;
            return false;
        }

        private bool TryGetValidatedTree(TreeView? candidate, out TreeView tree)
        {
            if (candidate == null)
            {
                tree = null!;
                return false;
            }

            tree = candidate;
            if (!tree.IsAttachedToVisualTree() || !tree.IsEnabled || !IsVisibleInVisualTree(tree))
            {
                return false;
            }

            var parentMainControl = tree.FindParent<MainControl>();
            return ReferenceEquals(parentMainControl, this);
        }

        private static bool IsVisibleInVisualTree(Control control)
        {
            for (var current = control; current != null; current = current.GetVisualParent() as Control)
            {
                if (!current.IsVisible)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<TaskWrapperViewModel>? GetTreeRoots(TreeView tree)
        {
            return tree.ItemsSource switch
            {
                IEnumerable<TaskWrapperViewModel> roots => roots,
                IEnumerable roots => roots.OfType<TaskWrapperViewModel>(),
                _ => null
            };
        }

        private TaskWrapperViewModel? GetCurrentWrapperForRoute(MainWindowViewModel vm, TreeView tree, TreeCommandRoute route)
        {
            if (route == TreeCommandRoute.ContextMenu &&
                ReferenceEquals(tree, _contextMenuTree) &&
                _contextMenuWrapper != null)
            {
                return _contextMenuWrapper;
            }

            return GetSelectedWrappersForTree(tree).LastOrDefault() ??
                   GetBoundCurrentWrapperForTree(vm, tree);
        }

        private IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappersForTree(MainWindowViewModel vm, TreeView tree)
        {
            var selectedWrappers = GetSelectedWrappersForTree(tree);
            if (selectedWrappers.Count > 0)
            {
                return selectedWrappers;
            }

            var currentWrapper = GetBoundCurrentWrapperForTree(vm, tree);

            return currentWrapper != null ? [currentWrapper] : Array.Empty<TaskWrapperViewModel>();
        }

        private static TaskWrapperViewModel? GetBoundCurrentWrapperForTree(MainWindowViewModel vm, TreeView tree)
        {
            return tree.Name switch
            {
                "AllTasksTree" => vm.CurrentAllTasksItem,
                "LastCreatedTree" => vm.CurrentLastCreated,
                "LastUpdatedTree" => vm.CurrentLastUpdated,
                "UnlockedTree" => vm.CurrentUnlockedItem,
                "InProgressTree" => vm.CurrentInProgressItem,
                "CompletedTree" => vm.CurrentCompletedItem,
                "ArchivedTree" => vm.CurrentArchivedItem,
                "LastOpenedTree" => vm.CurrentLastOpenedItem,
                _ => null
            };
        }

        private static IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappersForTree(TreeView tree)
        {
            var selectedWrappers = tree.SelectedItems?
                .OfType<TaskWrapperViewModel>()
                .ToList();

            if (selectedWrappers?.Count > 0)
            {
                return selectedWrappers;
            }

            return tree.SelectedItem is TaskWrapperViewModel selectedWrapper
                ? [selectedWrapper]
                : Array.Empty<TaskWrapperViewModel>();
        }

        private void NormalizeSelectionForContextMenu(TreeView tree, TaskWrapperViewModel wrapper)
        {
            var selectedWrappers = GetSelectedWrappersForTree(tree);
            if (ContainsWrapper(selectedWrappers, wrapper))
            {
                return;
            }

            SelectSingleWrapper(tree, wrapper);
        }

        private void SelectSingleWrapper(TreeView tree, TaskWrapperViewModel wrapper)
        {
            SetTreeSelection(tree, [wrapper]);
        }

        private void QueueSingleWrapperSelectionRestore(TreeView tree, TaskWrapperViewModel wrapper)
        {
            var restoreVersion = unchecked(++_selectionRestoreVersion);
            Dispatcher.UIThread.Post(() =>
            {
                if (restoreVersion == _selectionRestoreVersion &&
                    TryGetValidatedTree(tree, out _))
                {
                    SelectSingleWrapper(tree, wrapper);
                }
            }, DispatcherPriority.Normal);
        }

        private void SetTreeSelection(TreeView tree, IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            var selectedItems = tree.SelectedItems;
            if (selectedItems != null)
            {
                selectedItems.Clear();
                foreach (var wrapper in wrappers)
                {
                    selectedItems.Add(wrapper);
                }
            }

            var currentWrapper = wrappers.LastOrDefault();
            tree.SelectedItem = currentWrapper;
            SyncCurrentWrapperForTree(tree, currentWrapper);
        }

        private void SyncCurrentWrapperForTree(TreeView tree, TaskWrapperViewModel? wrapper)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            switch (tree.Name)
            {
                case "AllTasksTree":
                    vm.CurrentAllTasksItem = wrapper;
                    break;
                case "LastCreatedTree":
                    vm.CurrentLastCreated = wrapper;
                    break;
                case "LastUpdatedTree":
                    vm.CurrentLastUpdated = wrapper;
                    break;
                case "UnlockedTree":
                    vm.CurrentUnlockedItem = wrapper;
                    break;
                case "InProgressTree":
                    vm.CurrentInProgressItem = wrapper;
                    break;
                case "CompletedTree":
                    vm.CurrentCompletedItem = wrapper;
                    break;
                case "ArchivedTree":
                    vm.CurrentArchivedItem = wrapper;
                    break;
                case "LastOpenedTree":
                    vm.CurrentLastOpenedItem = wrapper;
                    break;
            }
        }

        private static bool ContainsWrapper(
            IReadOnlyList<TaskWrapperViewModel> wrappers,
            TaskWrapperViewModel candidate)
        {
            var candidatePath = candidate.GetWrapperPathKey();
            return wrappers.Any(wrapper =>
                ReferenceEquals(wrapper, candidate) ||
                StringComparer.Ordinal.Equals(wrapper.GetWrapperPathKey(), candidatePath));
        }

        private void ClearContextMenuContext()
        {
            _contextMenuTree = null;
            _contextMenuWrapper = null;
        }

        private void RestoreContextMenuContextFromPlacementTarget(MenuItem menuItem)
        {
            var contextMenu = FindOwningContextMenu(menuItem);
            if (contextMenu?.PlacementTarget is not Control placementTarget)
            {
                return;
            }

            _contextMenuTree = TryGetTaskTree(placementTarget) ?? _contextMenuTree;

            if (TryGetWrapper(placementTarget) is { } targetWrapper)
            {
                _contextMenuWrapper = targetWrapper;
            }
        }

        private ContextMenu? FindOwningContextMenu(MenuItem menuItem)
        {
            var visualParent = menuItem.FindParent<ContextMenu>();
            if (visualParent != null)
            {
                return visualParent;
            }

            var logicalParent = menuItem.FindLogicalAncestorOfType<ContextMenu>();
            if (logicalParent != null)
            {
                return logicalParent;
            }

            var tag = menuItem.Tag?.ToString();
            return GetTaskTrees()
                .Select(tree => tree.ContextMenu)
                .FirstOrDefault(contextMenu =>
                    contextMenu?.Items
                        .OfType<MenuItem>()
                        .Any(item => ReferenceEquals(item, menuItem)) == true)
                ?? GetTaskTrees()
                    .Select(tree => tree.ContextMenu)
                    .FirstOrDefault(contextMenu =>
                        contextMenu?.PlacementTarget is Control &&
                        !string.IsNullOrWhiteSpace(tag) &&
                        contextMenu.Items
                            .OfType<MenuItem>()
                            .Any(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal)));
        }

        private IEnumerable<TreeView> GetTaskTrees()
        {
            return KnownMainTaskTreeNames
                .Select(this.FindControl<TreeView>)
                .Where(tree => tree != null)
                .Cast<TreeView>()
                .Concat(this.GetVisualDescendants().OfType<TreeView>())
                .Distinct();
        }

        private static TaskWrapperViewModel? TryGetWrapper(object? source)
        {
            return source switch
            {
                TaskWrapperViewModel wrapper => wrapper,
                Control control when control.DataContext is TaskWrapperViewModel wrapper => wrapper,
                Control control => control.FindParentDataContext<TaskWrapperViewModel>(),
                _ => null
            };
        }

        private bool IsTextInputFocused()
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (focused == null)
            {
                return false;
            }

            return IsTextInputControlOrAncestor(focused);
        }

        private bool IsTabHeaderFocused()
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            return focused != null && IsControlOrAncestor<TabItem>(focused);
        }

        private static bool IsTextInputControlOrAncestor(Control control)
        {
            return IsControlOrAncestor<TextBox>(control) ||
                   IsControlOrAncestor<AutoCompleteBox>(control) ||
                   IsControlOrAncestor<NumericUpDown>(control) ||
                   IsRelationEditorSuggestionsOrAncestor(control);
        }

        private static bool IsRelationEditorSuggestionsOrAncestor(Control control)
        {
            Control? current = control;
            while (current != null)
            {
                if (current is ListBox listBox && listBox.DataContext is TaskRelationEditorViewModel)
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }

        private static bool IsControlOrAncestor<TControl>(Control control)
            where TControl : Control
        {
            Control? current = control;
            while (current != null)
            {
                if (current is TControl)
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }

        private static bool IsControlOrDescendantOf(Control control, Control ancestor)
        {
            Control? current = control;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }

        private static TreeView? TryGetTaskTree(Control? control)
        {
            return control as TreeView ?? control?.FindParent<TreeView>();
        }
    }
}
