using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData.Binding;
using ReactiveUI;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Search;
using Unlimotion.Views.Graph;

namespace Unlimotion.Views
{
    public partial class GraphControl : UserControl
    {
        public const string CustomFormat = "application/xxx-unlimotion-task-item";
        public const string CustomBatchFormat = "application/xxx-unlimotion-roadmap-task-items";
        internal static readonly DataFormat<string> CustomDataFormat =
            DataFormat.CreateStringPlatformFormat(CustomFormat);
        internal static readonly DataFormat<string> CustomBatchDataFormat =
            DataFormat.CreateStringPlatformFormat(CustomBatchFormat);

        private GraphViewModel? dc;
        private readonly DisposableList disposableList = new DisposableListRealization();
        private readonly SerialDisposable roadmapScopeSubscriptions = new();
        private readonly SerialDisposable roadmapFilterSubscriptions = new();
        private readonly SerialDisposable roadmapCurrentTaskSubscription = new();
        private readonly DispatcherTimer graphUpdateTimer;
        private readonly HashSet<string> selectedRoadmapTaskIds = new(StringComparer.Ordinal);
        private DateTime lastRoadmapPointerDoubleTapAt = DateTime.MinValue;
        private string? lastRoadmapPointerDoubleTapTaskId;
        private string? lastRoadmapClickSelectionTaskId;
        private KeyModifiers lastRoadmapClickSelectionModifiers;
        private bool lastRoadmapClickSelectionSelectedCurrentTask;
        private ReadOnlyObservableCollection<TaskWrapperViewModel>? roadmapScopeSubscriptionRoots;
        private string? roadmapScopeSubscriptionSignature;
        private bool roadmapScopeSubscriptionsDirty = true;
        private bool graphUpdateQueued;
        private bool highlightUpdateQueued;
        private IRoadmapViewportAdapter? roadmapViewport;
        private PendingRoadmapDragContext? pendingRoadmapDrag;
        private PendingRoadmapPanContext? pendingRoadmapPan;
        private PendingRoadmapSelectionContext? pendingRoadmapSelection;
        private TextBox? activeRoadmapInlineTitleEditor;
        private string? lastRoadmapInlineTitleClickTaskId;
        private DateTimeOffset? lastRoadmapInlineTitleClickAt;
        private bool roadmapDragInProgress;
        private int roadmapBuildRequestVersion;
        private int roadmapActiveBuildCount;
        private CancellationTokenSource? currentRoadmapBuildCancellation;
        private RoadmapBuildRequest? pendingRoadmapBuildRequest;
        private const double RoadmapPanStep = 240;
        private const double RoadmapDragThreshold = 4;
        private static readonly TimeSpan RoadmapGraphUpdateDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan RoadmapInlineTitleRepeatedClickDelay = TimeSpan.FromMilliseconds(500);

        public static readonly StyledProperty<bool> RoadmapGraphBuildInProgressProperty =
            AvaloniaProperty.Register<GraphControl, bool>(nameof(RoadmapGraphBuildInProgress));

        public static readonly StyledProperty<double> RoadmapGraphBuildProgressProperty =
            AvaloniaProperty.Register<GraphControl, double>(nameof(RoadmapGraphBuildProgress));

        public static readonly StyledProperty<string> RoadmapGraphBuildProgressTextProperty =
            AvaloniaProperty.Register<GraphControl, string>(
                nameof(RoadmapGraphBuildProgressText),
                "0%");

        public static readonly StyledProperty<bool> IsRoadmapViewportToolbarExpandedProperty =
            AvaloniaProperty.Register<GraphControl, bool>(
                nameof(IsRoadmapViewportToolbarExpanded),
                true);

        public static readonly StyledProperty<bool> IsRoadmapMinimapExpandedProperty =
            AvaloniaProperty.Register<GraphControl, bool>(
                nameof(IsRoadmapMinimapExpanded),
                true);

        public static readonly StyledProperty<bool> IsRoadmapSelectionRectangleVisibleProperty =
            AvaloniaProperty.Register<GraphControl, bool>(
                nameof(IsRoadmapSelectionRectangleVisible));

        public static readonly StyledProperty<Thickness> RoadmapSelectionRectangleMarginProperty =
            AvaloniaProperty.Register<GraphControl, Thickness>(
                nameof(RoadmapSelectionRectangleMargin));

        public static readonly StyledProperty<double> RoadmapSelectionRectangleWidthProperty =
            AvaloniaProperty.Register<GraphControl, double>(
                nameof(RoadmapSelectionRectangleWidth));

        public static readonly StyledProperty<double> RoadmapSelectionRectangleHeightProperty =
            AvaloniaProperty.Register<GraphControl, double>(
                nameof(RoadmapSelectionRectangleHeight));

        public GraphControl()
        {
            graphUpdateTimer = new DispatcherTimer
            {
                Interval = RoadmapGraphUpdateDelay
            };
            graphUpdateTimer.Tick += GraphUpdateTimer_OnTick;

            DataContextChanged += GraphControl_DataContextChanged;
            DetachedFromVisualTree += (_, _) =>
            {
                CancelScheduledGraphUpdate();
                CancelPendingRoadmapBuilds();
                ClearPendingRoadmapDrag();
                ClearPendingRoadmapPan();
                ClearPendingRoadmapSelection();
                ClearRoadmapInlineTitleEditState();
                roadmapCurrentTaskSubscription.Disposable = Disposable.Empty;
            };
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, MainControl.Drop);
            AddHandler(DragDrop.DragOverEvent, MainControl.DragOver);
            AddHandler(
                PointerPressedEvent,
                RoadmapInlineTitleText_OnPointerPressed,
                RoutingStrategies.Tunnel,
                true);
            AddHandler(
                PointerMovedEvent,
                InputElement_OnPointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                true);
            AddHandler(
                PointerReleasedEvent,
                InputElement_OnPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                true);
            KeyDown += RoadmapEditor_KeyDown;
            RoadmapViewport.Control.KeyDown += RoadmapEditor_KeyDown;
        }

        public ObservableCollection<RoadmapNode> RoadmapNodes { get; } = new();

        public ObservableCollection<RoadmapConnection> RoadmapConnections { get; } = new();

        public int RoadmapGraphUpdateCount { get; private set; }

        public TimeSpan RoadmapLastBuildTime { get; private set; }

        public TimeSpan RoadmapLastApplyProjectionTime { get; private set; }

        public int RoadmapScopeSubscriptionRefreshCount { get; private set; }

        public bool RoadmapGraphBuildInProgress
        {
            get => GetValue(RoadmapGraphBuildInProgressProperty);
            private set => SetValue(RoadmapGraphBuildInProgressProperty, value);
        }

        public double RoadmapGraphBuildProgress
        {
            get => GetValue(RoadmapGraphBuildProgressProperty);
            private set => SetValue(RoadmapGraphBuildProgressProperty, value);
        }

        public string RoadmapGraphBuildProgressText
        {
            get => GetValue(RoadmapGraphBuildProgressTextProperty);
            private set => SetValue(RoadmapGraphBuildProgressTextProperty, value);
        }

        public bool IsRoadmapViewportToolbarExpanded
        {
            get => GetValue(IsRoadmapViewportToolbarExpandedProperty);
            set => SetValue(IsRoadmapViewportToolbarExpandedProperty, value);
        }

        public bool IsRoadmapMinimapExpanded
        {
            get => GetValue(IsRoadmapMinimapExpandedProperty);
            set => SetValue(IsRoadmapMinimapExpandedProperty, value);
        }

        public bool IsRoadmapSelectionRectangleVisible
        {
            get => GetValue(IsRoadmapSelectionRectangleVisibleProperty);
            private set => SetValue(IsRoadmapSelectionRectangleVisibleProperty, value);
        }

        public Thickness RoadmapSelectionRectangleMargin
        {
            get => GetValue(RoadmapSelectionRectangleMarginProperty);
            private set => SetValue(RoadmapSelectionRectangleMarginProperty, value);
        }

        public double RoadmapSelectionRectangleWidth
        {
            get => GetValue(RoadmapSelectionRectangleWidthProperty);
            private set => SetValue(RoadmapSelectionRectangleWidthProperty, value);
        }

        public double RoadmapSelectionRectangleHeight
        {
            get => GetValue(RoadmapSelectionRectangleHeightProperty);
            private set => SetValue(RoadmapSelectionRectangleHeightProperty, value);
        }

        private IRoadmapViewportAdapter RoadmapViewport =>
            roadmapViewport ??= new NodifyRoadmapViewportAdapter(RoadmapEditor);

        public bool RoadmapLastBuildRanOnUiThread { get; private set; }

        public int RoadmapGraphBackgroundBuildStartCount { get; private set; }

        public int RoadmapGraphBackgroundBuildCancelRequestCount { get; private set; }

        public int RoadmapGraphBackgroundBuildCanceledCount { get; private set; }

        internal int RoadmapDragStartCount { get; private set; }

        internal Func<RoadmapGraphBuildInput, IProgress<double>?, CancellationToken, RoadmapGraphProjection>? RoadmapGraphBuildOverride { get; set; }

        private void GraphControl_DataContextChanged(object? sender, EventArgs e)
        {
            var newdc = DataContext as GraphViewModel;
            if (dc == newdc)
            {
                return;
            }

            dc = newdc;
            disposableList.Dispose();
            disposableList.Disposables.Clear();
            roadmapScopeSubscriptions.Disposable = Disposable.Empty;
            roadmapFilterSubscriptions.Disposable = Disposable.Empty;
            roadmapCurrentTaskSubscription.Disposable = Disposable.Empty;
            selectedRoadmapTaskIds.Clear();
            ClearPendingRoadmapSelection();
            ResetRoadmapScopeSubscriptionCache();
            CancelScheduledGraphUpdate();
            CancelPendingRoadmapBuilds();
            ClearRoadmapProjection();
            ClearRoadmapInlineTitleEditState();

            if (dc == null)
            {
                return;
            }

            RegisterRoadmapCurrentTaskSubscription(dc.MainWindowViewModel);

            dc.WhenAnyValue(
                    m => m.OnlyUnlocked,
                    m => m.ShowArchived,
                    m => m.ShowCompleted,
                    m => m.ShowWanted)
                .Subscribe(_ => ScheduleUpdateGraph())
                .AddToDispose(disposableList);

            dc.WhenAnyValue(
                    m => m.Tasks,
                    m => m.UnlockedTasks)
                .Subscribe(_ => InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate())
                .AddToDispose(disposableList);

            dc.WhenAnyValue(
                    m => m.EmojiFilters,
                    m => m.EmojiExcludeFilters)
                .Subscribe(_ =>
                {
                    RegisterRoadmapFilterSubscriptions();
                    ScheduleUpdateGraph();
                })
                .AddToDispose(disposableList);

            dc.UnlockedTasks.ObserveCollectionChanges()
                .Throttle(TimeSpan.FromMilliseconds(100))
                .Subscribe(_ => InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate())
                .AddToDispose(disposableList);

            dc.Tasks.ObserveCollectionChanges()
                .Throttle(TimeSpan.FromMilliseconds(100))
                .Subscribe(_ => InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate())
                .AddToDispose(disposableList);

            dc.WhenAnyValue(m => m.UpdateGraph)
                .Subscribe(_ => ScheduleUpdateGraph())
                .AddToDispose(disposableList);

            dc.WhenAnyValue(m => m.Search.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs))
                .Select(t => (t ?? "").Trim())
                .DistinctUntilChanged()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateHighlights())
                .AddToDispose(disposableList);

            UpdateGraph();
        }

        private void RegisterRoadmapFilterSubscriptions()
        {
            var localDc = dc;
            if (localDc == null)
            {
                roadmapFilterSubscriptions.Disposable = Disposable.Empty;
                return;
            }

            var subscriptions = new CompositeDisposable();
            var rebuildTrigger = TimeSpan.FromMilliseconds(100);

            AddFilterCollectionSubscription(localDc.EmojiFilters, subscriptions, rebuildTrigger);
            AddFilterCollectionSubscription(localDc.EmojiExcludeFilters, subscriptions, rebuildTrigger);

            roadmapFilterSubscriptions.Disposable = subscriptions;
        }

        private void ScheduleUpdateGraph()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Post(ScheduleUpdateGraph);
                return;
            }

            roadmapBuildRequestVersion++;
            CancelCurrentRoadmapBuild();
            BeginRoadmapBuildProgress(0);
            graphUpdateQueued = true;
            graphUpdateTimer.Stop();
            graphUpdateTimer.Start();
        }

        private void GraphUpdateTimer_OnTick(object? sender, EventArgs e)
        {
            graphUpdateTimer.Stop();
            if (!graphUpdateQueued)
            {
                return;
            }

            graphUpdateQueued = false;
            UpdateGraph();
        }

        private void CancelScheduledGraphUpdate()
        {
            graphUpdateTimer.Stop();
            graphUpdateQueued = false;
        }

        private void CancelPendingRoadmapBuilds()
        {
            roadmapBuildRequestVersion++;
            pendingRoadmapBuildRequest = null;
            CancelCurrentRoadmapBuild();
            HideRoadmapBuildProgressIfIdle();
        }

        private void InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Post(InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate);
                return;
            }

            roadmapScopeSubscriptionsDirty = true;
            ScheduleUpdateGraph();
        }

        private void ResetRoadmapScopeSubscriptionCache()
        {
            roadmapScopeSubscriptionsDirty = true;
            roadmapScopeSubscriptionRoots = null;
            roadmapScopeSubscriptionSignature = null;
        }

        private void ScheduleUpdateHighlights()
        {
            if (highlightUpdateQueued)
            {
                return;
            }

            highlightUpdateQueued = true;
            Dispatcher.Post(() =>
            {
                highlightUpdateQueued = false;
                UpdateHighlights();
            });
        }

        private void UpdateGraph()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Post(UpdateGraph);
                return;
            }

            CancelScheduledGraphUpdate();

            var localDc = dc;
            if (localDc == null)
            {
                return;
            }

            BeginRoadmapBuildProgress(5);
            var roots = localDc.OnlyUnlocked ? localDc.UnlockedTasks : localDc.Tasks;
            var input = RoadmapGraphBuilder.Capture(roots, GetMeasuredRoadmapNodeWidths());
            SetRoadmapBuildProgress(10);
            QueueRoadmapBuild(new RoadmapBuildRequest(
                ++roadmapBuildRequestVersion,
                roots,
                input));
        }

        private void QueueRoadmapBuild(RoadmapBuildRequest request)
        {
            pendingRoadmapBuildRequest = request;
            CancelCurrentRoadmapBuild();
            StartNextRoadmapBuild();
        }

        private void StartNextRoadmapBuild()
        {
            if (pendingRoadmapBuildRequest == null)
            {
                HideRoadmapBuildProgressIfIdle();
                return;
            }

            var request = pendingRoadmapBuildRequest;
            pendingRoadmapBuildRequest = null;
            var cancellationSource = new CancellationTokenSource();
            currentRoadmapBuildCancellation = cancellationSource;
            roadmapActiveBuildCount++;
            RoadmapGraphBuildInProgress = true;
            SetRoadmapBuildProgress(12);
            RoadmapGraphBackgroundBuildStartCount++;
            _ = RunRoadmapBuildAsync(request, cancellationSource);
        }

        private async Task RunRoadmapBuildAsync(
            RoadmapBuildRequest request,
            CancellationTokenSource cancellationSource)
        {
            RoadmapGraphProjection? projection = null;
            var cancellationToken = cancellationSource.Token;
            var buildStopwatch = Stopwatch.StartNew();
            var buildRanOnUiThread = false;
            var canceled = false;
            var progress = new Progress<double>(value =>
                ReportRoadmapBuildProgress(request.Version, value));

            try
            {
                projection = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    buildRanOnUiThread = Dispatcher.CheckAccess();
                    var build = RoadmapGraphBuildOverride ?? RoadmapGraphBuilder.Build;
                    return build(request.Input, progress, cancellationToken);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
            }
            catch (Exception exception)
            {
                Trace.TraceError("Roadmap graph background build failed: {0}", exception);
            }
            finally
            {
                buildStopwatch.Stop();
            }

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (canceled || cancellationToken.IsCancellationRequested)
                    {
                        canceled = true;
                        if (projection != null)
                        {
                            DisposeProjection(projection);
                        }
                    }
                    else
                    {
                        CompleteRoadmapBuild(
                            request,
                            projection,
                            buildStopwatch.Elapsed,
                            buildRanOnUiThread);
                    }
                }
                finally
                {
                    FinishRoadmapBuild(cancellationSource, canceled);
                    StartNextRoadmapBuild();
                }
            });
        }

        private void CompleteRoadmapBuild(
            RoadmapBuildRequest request,
            RoadmapGraphProjection? projection,
            TimeSpan buildTime,
            bool buildRanOnUiThread)
        {
            if (projection == null)
            {
                return;
            }

            if (request.Version != roadmapBuildRequestVersion || dc == null)
            {
                DisposeProjection(projection);
                return;
            }

            SetRoadmapBuildProgress(95);
            var applyStopwatch = Stopwatch.StartNew();
            RegisterRoadmapScopeSubscriptions(request.Roots, projection);
            SetRoadmapBuildProgress(97);
            ApplyProjection(projection);
            applyStopwatch.Stop();

            RoadmapLastBuildTime = buildTime;
            RoadmapLastApplyProjectionTime = applyStopwatch.Elapsed;
            RoadmapLastBuildRanOnUiThread = buildRanOnUiThread;
            RoadmapGraphUpdateCount++;

            SetRoadmapBuildProgress(100);
            UpdateHighlights();
        }

        private void BeginRoadmapBuildProgress(double progress)
        {
            RoadmapGraphBuildInProgress = true;
            SetRoadmapBuildProgress(progress);
        }

        private void ReportRoadmapBuildProgress(int requestVersion, double progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Post(() => ReportRoadmapBuildProgress(requestVersion, progress));
                return;
            }

            if (requestVersion != roadmapBuildRequestVersion)
            {
                return;
            }

            SetRoadmapBuildProgress(progress);
        }

        private void SetRoadmapBuildProgress(double progress)
        {
            var clamped = Math.Clamp(progress, 0d, 100d);
            RoadmapGraphBuildProgress = clamped;
            RoadmapGraphBuildProgressText = $"{Math.Round(clamped):0}%";
        }

        private void CancelCurrentRoadmapBuild()
        {
            if (currentRoadmapBuildCancellation == null ||
                currentRoadmapBuildCancellation.IsCancellationRequested)
            {
                return;
            }

            RoadmapGraphBackgroundBuildCancelRequestCount++;
            currentRoadmapBuildCancellation.Cancel();
        }

        private void FinishRoadmapBuild(
            CancellationTokenSource cancellationSource,
            bool canceled)
        {
            roadmapActiveBuildCount = Math.Max(0, roadmapActiveBuildCount - 1);
            if (canceled)
            {
                RoadmapGraphBackgroundBuildCanceledCount++;
            }

            if (ReferenceEquals(currentRoadmapBuildCancellation, cancellationSource))
            {
                currentRoadmapBuildCancellation = null;
            }

            cancellationSource.Dispose();
            HideRoadmapBuildProgressIfIdle();
        }

        private void HideRoadmapBuildProgressIfIdle()
        {
            if (currentRoadmapBuildCancellation != null ||
                pendingRoadmapBuildRequest != null ||
                graphUpdateQueued)
            {
                return;
            }

            RoadmapGraphBuildInProgress = false;
            if (roadmapActiveBuildCount == 0)
            {
                SetRoadmapBuildProgress(0);
            }
        }

        private static void DisposeProjection(RoadmapGraphProjection projection)
        {
            foreach (var connection in projection.Connections)
            {
                connection.Dispose();
            }
        }

        private void ApplyProjection(RoadmapGraphProjection projection)
        {
            var existingNodesById = RoadmapNodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var desiredNodes = new List<RoadmapNode>(projection.Nodes.Count);
            var desiredNodesById = new Dictionary<string, RoadmapNode>();

            foreach (var projectedNode in projection.Nodes)
            {
                var node = existingNodesById.TryGetValue(projectedNode.Id, out var existingNode)
                    ? existingNode
                    : projectedNode;

                node.Location = projectedNode.Location;
                node.SetConnectionWidth(projectedNode.ConnectionWidth);
                desiredNodes.Add(node);
                desiredNodesById[node.Id] = node;
            }

            var existingConnectionsByKey = RoadmapConnections
                .GroupBy(connection => connection.Key)
                .ToDictionary(group => group.Key, group => group.First());
            var desiredConnections = new List<RoadmapConnection>(projection.Connections.Count);
            var desiredConnectionKeys = new HashSet<string>();

            foreach (var projectedConnection in projection.Connections)
            {
                if (!desiredNodesById.TryGetValue(projectedConnection.Tail.Id, out var tail) ||
                    !desiredNodesById.TryGetValue(projectedConnection.Head.Id, out var head))
                {
                    continue;
                }

                var key = RoadmapConnection.CreateKey(tail.Id, head.Id, projectedConnection.Kind);
                if (!desiredConnectionKeys.Add(key))
                {
                    continue;
                }

                var connection = existingConnectionsByKey.TryGetValue(key, out var existingConnection)
                    ? existingConnection
                    : new RoadmapConnection(tail, head, projectedConnection.Kind);
                desiredConnections.Add(connection);
            }

            foreach (var projectedConnection in projection.Connections)
            {
                projectedConnection.Dispose();
            }

            RemoveStaleRoadmapConnections(desiredConnectionKeys);
            SynchronizeRoadmapNodes(desiredNodes);
            SynchronizeRoadmapConnections(desiredConnections);
            PruneRoadmapSelectionToVisibleNodes();
            ApplyRoadmapSelectionState();
            ApplyRoadmapCurrentState();
        }

        private void ClearRoadmapProjection()
        {
            foreach (var connection in RoadmapConnections)
            {
                connection.Dispose();
            }

            RoadmapConnections.Clear();
            RoadmapNodes.Clear();
        }

        private void RegisterRoadmapCurrentTaskSubscription(MainWindowViewModel? owner)
        {
            if (owner == null)
            {
                roadmapCurrentTaskSubscription.Disposable = Disposable.Empty;
                return;
            }

            roadmapCurrentTaskSubscription.Disposable = owner
                .WhenAnyValue(m => m.CurrentTaskItem)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ApplyRoadmapCurrentState());
        }

        private void PruneRoadmapSelectionToVisibleNodes()
        {
            if (selectedRoadmapTaskIds.Count == 0)
            {
                return;
            }

            var visibleIds = RoadmapNodes
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);

            selectedRoadmapTaskIds.RemoveWhere(id => !visibleIds.Contains(id));
        }

        private void ApplyRoadmapSelectionState()
        {
            foreach (var node in RoadmapNodes)
            {
                node.IsSelected = selectedRoadmapTaskIds.Contains(node.Id);
            }
        }

        private void ApplyRoadmapCurrentState()
        {
            var currentTaskId = dc?.MainWindowViewModel?.CurrentTaskItem?.Id;
            foreach (var node in RoadmapNodes)
            {
                node.IsCurrent = string.Equals(node.Id, currentTaskId, StringComparison.Ordinal);
            }
        }

        private void RemoveStaleRoadmapConnections(IReadOnlySet<string> desiredConnectionKeys)
        {
            for (var i = RoadmapConnections.Count - 1; i >= 0; i--)
            {
                if (desiredConnectionKeys.Contains(RoadmapConnections[i].Key))
                {
                    continue;
                }

                RoadmapConnections[i].Dispose();
                RoadmapConnections.RemoveAt(i);
            }
        }

        private void SynchronizeRoadmapNodes(IReadOnlyList<RoadmapNode> desiredNodes)
        {
            for (var index = 0; index < desiredNodes.Count; index++)
            {
                var desiredNode = desiredNodes[index];
                if (index < RoadmapNodes.Count && ReferenceEquals(RoadmapNodes[index], desiredNode))
                {
                    continue;
                }

                var existingIndex = IndexOfReference(RoadmapNodes, desiredNode, index + 1);
                if (existingIndex >= 0)
                {
                    RoadmapNodes.Move(existingIndex, index);
                }
                else
                {
                    RoadmapNodes.Insert(index, desiredNode);
                }
            }

            while (RoadmapNodes.Count > desiredNodes.Count)
            {
                RoadmapNodes.RemoveAt(RoadmapNodes.Count - 1);
            }
        }

        private void SynchronizeRoadmapConnections(IReadOnlyList<RoadmapConnection> desiredConnections)
        {
            for (var index = 0; index < desiredConnections.Count; index++)
            {
                var desiredConnection = desiredConnections[index];
                if (index < RoadmapConnections.Count &&
                    ReferenceEquals(RoadmapConnections[index], desiredConnection))
                {
                    continue;
                }

                var existingIndex = IndexOfReference(RoadmapConnections, desiredConnection, index + 1);
                if (existingIndex >= 0)
                {
                    RoadmapConnections.Move(existingIndex, index);
                }
                else
                {
                    RoadmapConnections.Insert(index, desiredConnection);
                }
            }

            while (RoadmapConnections.Count > desiredConnections.Count)
            {
                RoadmapConnections[^1].Dispose();
                RoadmapConnections.RemoveAt(RoadmapConnections.Count - 1);
            }
        }

        private static int IndexOfReference<T>(IReadOnlyList<T> items, T item, int startIndex)
            where T : class
        {
            for (var index = startIndex; index < items.Count; index++)
            {
                if (ReferenceEquals(items[index], item))
                {
                    return index;
                }
            }

            return -1;
        }

        private void RegisterRoadmapScopeSubscriptions(
            ReadOnlyObservableCollection<TaskWrapperViewModel> roots,
            RoadmapGraphProjection projection)
        {
            var scopeSignature = BuildRoadmapScopeSubscriptionSignature(projection);
            if (!roadmapScopeSubscriptionsDirty &&
                ReferenceEquals(roadmapScopeSubscriptionRoots, roots) &&
                string.Equals(roadmapScopeSubscriptionSignature, scopeSignature, StringComparison.Ordinal))
            {
                return;
            }

            var subscriptions = new CompositeDisposable();
            var rebuildTrigger = TimeSpan.FromMilliseconds(100);

            foreach (var task in projection.Nodes
                         .Select(node => node.TaskItem)
                         .GroupBy(task => task.Id)
                         .Select(group => group.First()))
            {
                if (task is INotifyPropertyChanged inpc)
                {
                    var taskChanges = Observable
                        .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                            handler => inpc.PropertyChanged += handler,
                            handler => inpc.PropertyChanged -= handler)
                        .Publish()
                        .RefCount();

                    subscriptions.Add(taskChanges
                        .Where(change => ShouldRebuildRoadmapOnTaskProperty(change.EventArgs.PropertyName))
                        .Throttle(rebuildTrigger)
                        .Subscribe(_ => ScheduleUpdateGraph()));

                    subscriptions.Add(taskChanges
                        .Where(change => IsRoadmapContentProperty(change.EventArgs.PropertyName))
                        .Throttle(rebuildTrigger)
                        .Subscribe(_ => ScheduleUpdateHighlights()));
                }

                AddCollectionSubscription(task.ContainsTasks, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.ParentsTasks, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.BlocksTasks, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.BlockedByTasks, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.Contains, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.Parents, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.Blocks, subscriptions, rebuildTrigger);
                AddCollectionSubscription(task.BlockedBy, subscriptions, rebuildTrigger);
            }

            foreach (var wrapper in EnumerateWrappers(roots))
            {
                AddCollectionSubscription(wrapper.SubTasks, subscriptions, rebuildTrigger);
            }

            roadmapScopeSubscriptions.Disposable = subscriptions;
            roadmapScopeSubscriptionRoots = roots;
            roadmapScopeSubscriptionSignature = scopeSignature;
            roadmapScopeSubscriptionsDirty = false;
            RoadmapScopeSubscriptionRefreshCount++;
        }

        private static string BuildRoadmapScopeSubscriptionSignature(RoadmapGraphProjection projection)
        {
            var builder = new StringBuilder();
            foreach (var task in projection.Nodes
                         .Select(node => node.TaskItem)
                         .GroupBy(task => task.Id)
                         .Select(group => group.First())
                         .OrderBy(task => task.Id, StringComparer.Ordinal))
            {
                builder
                    .Append(task.Id)
                    .Append('#')
                    .Append(RuntimeHelpers.GetHashCode(task))
                    .Append(';');
            }

            return builder.ToString();
        }

        private bool ShouldRebuildRoadmapOnTaskProperty(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return true;
            }

            if (propertyName is nameof(TaskItemViewModel.Title)
                or nameof(TaskItemViewModel.TitleWithoutEmoji)
                or nameof(TaskItemViewModel.OnlyTextTitle))
            {
                return false;
            }

            if (propertyName is nameof(TaskItemViewModel.Emoji)
                or nameof(TaskItemViewModel.GetAllEmoji))
            {
                return HasActiveEmojiFilter();
            }

            if (propertyName is nameof(TaskItemViewModel.Wanted))
            {
                return dc?.ShowWanted.HasValue == true;
            }

            return propertyName is nameof(TaskItemViewModel.IsCompleted)
                       or nameof(TaskItemViewModel.IsCanBeCompleted)
                       or nameof(TaskItemViewModel.UnlockedDateTime);
        }

        private bool HasActiveEmojiFilter()
        {
            var localDc = dc;
            return localDc?.EmojiFilters.Any(filter => filter.ShowTasks) == true ||
                   localDc?.EmojiExcludeFilters.Any(filter => filter.ShowTasks) == true;
        }

        private static bool IsRoadmapContentProperty(string? propertyName)
        {
            return propertyName is nameof(TaskItemViewModel.Title)
                or nameof(TaskItemViewModel.TitleWithoutEmoji)
                or nameof(TaskItemViewModel.OnlyTextTitle)
                or nameof(TaskItemViewModel.Emoji)
                or nameof(TaskItemViewModel.GetAllEmoji)
                or nameof(TaskItemViewModel.Description)
                or nameof(TaskItemViewModel.RepeaterListMarker)
                or nameof(TaskItemViewModel.RepeaterListMarkerToolTip)
                or nameof(TaskItemViewModel.IsHaveRepeater)
                or nameof(TaskItemViewModel.Wanted)
                or nameof(TaskItemViewModel.IsCanBeCompleted);
        }

        private void AddCollectionSubscription<T>(
            ReadOnlyObservableCollection<T> collection,
            CompositeDisposable subscriptions,
            TimeSpan throttle)
        {
            subscriptions.Add(collection
                .ObserveCollectionChanges()
                .Throttle(throttle)
                .Subscribe(_ => InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate()));
        }

        private void AddCollectionSubscription<T>(
            ObservableCollection<T> collection,
            CompositeDisposable subscriptions,
            TimeSpan throttle)
        {
            subscriptions.Add(collection
                .ObserveCollectionChanges()
                .Throttle(throttle)
                .Subscribe(_ => InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate()));
        }

        private void AddFilterCollectionSubscription(
            ReadOnlyObservableCollection<EmojiFilter> collection,
            CompositeDisposable subscriptions,
            TimeSpan throttle)
        {
            subscriptions.Add(collection
                .ObserveCollectionChanges()
                .Throttle(throttle)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    RegisterRoadmapFilterSubscriptions();
                    ScheduleUpdateGraph();
                }));

            foreach (var filter in collection.OfType<INotifyPropertyChanged>())
            {
                subscriptions.Add(Observable
                    .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                        handler => filter.PropertyChanged += handler,
                        handler => filter.PropertyChanged -= handler)
                    .Where(change => string.IsNullOrEmpty(change.EventArgs.PropertyName) ||
                                     change.EventArgs.PropertyName == nameof(EmojiFilter.ShowTasks))
                    .Throttle(throttle)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(_ => ScheduleUpdateGraph()));
            }
        }

        private void RoadmapEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || IsTextInputEventSource(e.Source))
            {
                return;
            }

            if (e.KeyModifiers == KeyModifiers.None &&
                e.Key == Key.F2 &&
                FocusCurrentRoadmapInlineTitleEditor(sender as Control ?? e.Source as Control))
            {
                ClearRoadmapInlineTitleClickState();
                e.Handled = true;
                return;
            }

            if (TryExecuteRoadmapCreateHotkey(sender, e))
            {
                return;
            }

            switch (e.Key)
            {
                case Key.F:
                case Key.U:
                case Key.T:
                    FitRoadmapToScreen();
                    e.Handled = true;
                    break;
                case Key.R:
                    ResetRoadmapViewport();
                    e.Handled = true;
                    break;
            }
        }

        private bool TryExecuteRoadmapCreateHotkey(object? sender, KeyEventArgs e)
        {
            var owner = ResolveMainWindowViewModel(sender as Control ?? e.Source as Control);
            if (owner == null || !TryGetRoadmapCreateCommand(owner, e, out var command))
            {
                return false;
            }

            if (!command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);
            e.Handled = true;
            return true;
        }

        private static bool TryGetRoadmapCreateCommand(
            MainWindowViewModel owner,
            KeyEventArgs e,
            out ICommand command)
        {
            command = null!;

            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter)
            {
                command = owner.CreateSibling;
                return true;
            }

            if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Enter)
            {
                command = owner.CreateBlockedSibling;
                return true;
            }

            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Tab)
            {
                command = owner.CreateInner;
                return true;
            }

            return false;
        }

        private static bool IsTextInputEventSource(object? source)
        {
            return source is Control control &&
                   (IsControlOrAncestor<TextBox>(control) ||
                    IsControlOrAncestor<AutoCompleteBox>(control) ||
                    IsControlOrAncestor<NumericUpDown>(control));
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

        private void FitRoadmapToScreen()
        {
            Dispatcher.Post(() => RoadmapViewport.FitToScreen());
        }

        private void ResetRoadmapViewport()
        {
            Dispatcher.Post(() => RoadmapViewport.Reset());
        }

        private void RoadmapZoomIn_OnClick(object? sender, RoutedEventArgs e)
        {
            RoadmapViewport.ZoomIn();
            e.Handled = true;
        }

        private void RoadmapZoomOut_OnClick(object? sender, RoutedEventArgs e)
        {
            RoadmapViewport.ZoomOut();
            e.Handled = true;
        }

        private void RoadmapFit_OnClick(object? sender, RoutedEventArgs e)
        {
            FitRoadmapToScreen();
            e.Handled = true;
        }

        private void RoadmapResetViewport_OnClick(object? sender, RoutedEventArgs e)
        {
            ResetRoadmapViewport();
            e.Handled = true;
        }

        private void RoadmapPanLeft_OnClick(object? sender, RoutedEventArgs e)
        {
            PanRoadmapViewport(-RoadmapPanStep, 0);
            e.Handled = true;
        }

        private void RoadmapPanRight_OnClick(object? sender, RoutedEventArgs e)
        {
            PanRoadmapViewport(RoadmapPanStep, 0);
            e.Handled = true;
        }

        private void RoadmapPanUp_OnClick(object? sender, RoutedEventArgs e)
        {
            PanRoadmapViewport(0, -RoadmapPanStep);
            e.Handled = true;
        }

        private void RoadmapPanDown_OnClick(object? sender, RoutedEventArgs e)
        {
            PanRoadmapViewport(0, RoadmapPanStep);
            e.Handled = true;
        }

        private void RoadmapViewportToolbarCollapse_OnClick(object? sender, RoutedEventArgs e)
        {
            IsRoadmapViewportToolbarExpanded = false;
            e.Handled = true;
        }

        private void RoadmapViewportToolbarExpand_OnClick(object? sender, RoutedEventArgs e)
        {
            IsRoadmapViewportToolbarExpanded = true;
            e.Handled = true;
        }

        private void RoadmapMinimapCollapse_OnClick(object? sender, RoutedEventArgs e)
        {
            IsRoadmapMinimapExpanded = false;
            e.Handled = true;
        }

        private void RoadmapMinimapExpand_OnClick(object? sender, RoutedEventArgs e)
        {
            IsRoadmapMinimapExpanded = true;
            e.Handled = true;
        }

        private void RoadmapMinimap_OnZoom(object? sender, RoutedEventArgs e)
        {
            var zoom = e.GetType().GetProperty("Zoom")?.GetValue(e);
            var location = e.GetType().GetProperty("Location")?.GetValue(e);

            if (zoom is double zoomValue && location is Point locationValue)
            {
                RoadmapViewport.ZoomAtPosition(zoomValue, locationValue);
            }
        }

        private void PanRoadmapViewport(double deltaX, double deltaY)
        {
            RoadmapViewport.PanBy(deltaX, deltaY);
        }

        private void RoadmapNode_OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Control { DataContext: RoadmapNode node })
            {
                node.SetMeasuredWidth(e.NewSize.Width);
            }
        }

        private Dictionary<string, double> GetMeasuredRoadmapNodeWidths()
        {
            return RoadmapNodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First().Width);
        }

        private void RoadmapInlineTitleText_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Handled ||
                e.KeyModifiers != KeyModifiers.None ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (activeRoadmapInlineTitleEditor != null &&
                (e.Source is Control sourceControl &&
                 IsControlOrDescendantOf(sourceControl, activeRoadmapInlineTitleEditor) ||
                 IsPointWithinControl(activeRoadmapInlineTitleEditor, e.GetPosition(this))))
            {
                return;
            }

            var control = FindRoadmapInlineTitleControl(e.Source as Control) ??
                          FindRoadmapInlineTitleControlAt(e.GetPosition(this));
            if (control == null)
            {
                return;
            }

            if (!TryGetRoadmapTaskItem(control, out var taskItem) ||
                string.IsNullOrWhiteSpace(taskItem.Id))
            {
                return;
            }

            HandleRoadmapInlineTitleClick(control, taskItem, e);
        }

        private static Control? FindRoadmapInlineTitleControl(Control? source)
        {
            var current = source;
            while (current != null)
            {
                var automationId = AutomationProperties.GetAutomationId(current);
                if (automationId is "RoadmapInlineTaskTitleSurface" or "RoadmapInlineTaskTitleTextBlock")
                {
                    return current;
                }

                current = current.FindParent<Control>();
            }

            return null;
        }

        private static bool IsControlOrDescendantOf(Control control, Control ancestor)
        {
            var current = control;
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

        private Control? FindRoadmapInlineTitleControlAt(Point position)
        {
            foreach (var control in this.GetVisualDescendants().OfType<Control>())
            {
                var automationId = AutomationProperties.GetAutomationId(control);
                if (automationId != "RoadmapInlineTaskTitleSurface" ||
                    !control.IsAttachedToVisualTree() ||
                    !control.IsVisible ||
                    !control.IsEnabled ||
                    control.Bounds.Width <= 0 ||
                    control.Bounds.Height <= 0)
                {
                    continue;
                }

                var origin = control.TranslatePoint(new Point(0, 0), this);
                if (!origin.HasValue)
                {
                    continue;
                }

                var bounds = new Rect(origin.Value, control.Bounds.Size);
                if (bounds.Contains(position))
                {
                    return control;
                }
            }

            return null;
        }

        private bool IsPointWithinControl(Control control, Point position)
        {
            if (!control.IsAttachedToVisualTree() ||
                !control.IsVisible ||
                control.Bounds.Width <= 0 ||
                control.Bounds.Height <= 0)
            {
                return false;
            }

            var origin = control.TranslatePoint(new Point(0, 0), this);
            return origin.HasValue &&
                   new Rect(origin.Value, control.Bounds.Size).Contains(position);
        }

        private void HandleRoadmapInlineTitleClick(
            Control context,
            TaskItemViewModel taskItem,
            PointerPressedEventArgs e)
        {
            var now = DateTimeOffset.UtcNow;
            var lastClickElapsed = lastRoadmapInlineTitleClickAt == null
                ? TimeSpan.MaxValue
                : now - lastRoadmapInlineTitleClickAt.Value;
            var isLastClickSameTitle =
                string.Equals(lastRoadmapInlineTitleClickTaskId, taskItem.Id, StringComparison.Ordinal);
            var isRapidRepeatedTitleClick =
                isLastClickSameTitle &&
                lastClickElapsed < RoadmapInlineTitleRepeatedClickDelay;

            if (isRapidRepeatedTitleClick ||
                e.ClickCount > 1 && lastClickElapsed < RoadmapInlineTitleRepeatedClickDelay)
            {
                ClearRoadmapInlineTitleClickState();
                return;
            }

            var isRepeatedTitleClick =
                isLastClickSameTitle &&
                lastClickElapsed >= RoadmapInlineTitleRepeatedClickDelay;

            SelectRoadmapTask(context, taskItem);

            if (isRepeatedTitleClick && FocusRoadmapInlineTitleEditor(taskItem.Id))
            {
                ClearRoadmapInlineTitleClickState();
                e.Handled = true;
                return;
            }

            lastRoadmapInlineTitleClickTaskId = taskItem.Id;
            lastRoadmapInlineTitleClickAt = now;
        }

        private bool FocusCurrentRoadmapInlineTitleEditor(Control? context)
        {
            var owner = ResolveMainWindowViewModel(context);
            return FocusRoadmapInlineTitleEditor(owner?.CurrentTaskItem?.Id);
        }

        private bool FocusRoadmapInlineTitleEditor(string? taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return false;
            }

            var titleEditor = FindRoadmapInlineTitleEditor(taskId) ??
                              CreateRoadmapInlineTitleEditor(taskId);

            if (titleEditor == null)
            {
                return false;
            }

            if (!FocusRoadmapInlineTitleEditor(titleEditor))
            {
                QueueRoadmapInlineTitleEditorFocus(titleEditor);
            }

            return true;
        }

        private void QueueRoadmapInlineTitleEditorFocus(TextBox titleEditor)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(activeRoadmapInlineTitleEditor, titleEditor))
                {
                    FocusRoadmapInlineTitleEditor(titleEditor);
                }
            }, DispatcherPriority.Loaded);
        }

        private TextBox? CreateRoadmapInlineTitleEditor(string taskId)
        {
            var titleText = FindRoadmapInlineTitleTextBlock(taskId);
            if (titleText?.Parent is not Panel parent ||
                !TryGetRoadmapTaskItem(titleText, out var taskItem))
            {
                return null;
            }

            ClearActiveRoadmapInlineTitleEditor();

            var titleEditor = new TextBox
            {
                DataContext = taskItem,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = titleText.VerticalAlignment,
                MinWidth = Math.Max(titleText.Bounds.Width, 48),
                MinHeight = Math.Max(titleText.Bounds.Height, 22),
                MaxWidth = titleText.MaxWidth,
                TextWrapping = TextWrapping.Wrap
            };
            titleEditor.Classes.Add("RoadmapInlineTaskTitleEditor");
            if (taskItem.Wanted)
            {
                titleEditor.Classes.Add("IsWanted");
            }

            AutomationProperties.SetAutomationId(titleEditor, "RoadmapInlineTaskTitleTextBox");
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

            titleEditor.LostFocus += RoadmapInlineTitleEditor_OnLostFocus;
            parent.Children.Add(titleEditor);
            activeRoadmapInlineTitleEditor = titleEditor;

            return titleEditor;
        }

        private TextBlock? FindRoadmapInlineTitleTextBlock(string taskId)
        {
            return this.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "RoadmapInlineTaskTitleTextBlock",
                        StringComparison.Ordinal) &&
                    TryGetRoadmapTaskItem(control, out var taskItem) &&
                    string.Equals(taskItem.Id, taskId, StringComparison.Ordinal) &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private TextBox? FindRoadmapInlineTitleEditor(string taskId)
        {
            return this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "RoadmapInlineTaskTitleTextBox",
                        StringComparison.Ordinal) &&
                    TryGetRoadmapTaskItem(control, out var taskItem) &&
                    string.Equals(taskItem.Id, taskId, StringComparison.Ordinal) &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private static bool FocusRoadmapInlineTitleEditor(TextBox titleEditor)
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

        private void RoadmapInlineTitleEditor_OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, activeRoadmapInlineTitleEditor))
            {
                ClearActiveRoadmapInlineTitleEditor();
            }
        }

        private void ClearRoadmapInlineTitleEditState()
        {
            ClearRoadmapInlineTitleClickState();
            ClearActiveRoadmapInlineTitleEditor();
        }

        private void ClearRoadmapInlineTitleClickState()
        {
            lastRoadmapInlineTitleClickTaskId = null;
            lastRoadmapInlineTitleClickAt = null;
        }

        private void ClearActiveRoadmapInlineTitleEditor()
        {
            var titleEditor = activeRoadmapInlineTitleEditor;
            if (titleEditor == null)
            {
                return;
            }

            titleEditor.LostFocus -= RoadmapInlineTitleEditor_OnLostFocus;
            if (titleEditor.Parent is Panel parent)
            {
                parent.Children.Remove(titleEditor);
            }

            activeRoadmapInlineTitleEditor = null;
        }

        private void RoadmapEditor_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            var pointer = e.GetCurrentPoint(RoadmapSurface);
            if (pointer.Properties.IsRightButtonPressed)
            {
                StartPendingRoadmapPan(RoadmapSurface, e);
                return;
            }

            if (!pointer.Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (TryGetRoadmapTaskItem(e.Source as Control, out _))
            {
                return;
            }

            ClearPendingRoadmapDrag();
            ClearPendingRoadmapPan();
            ClearPendingRoadmapSelection();

            e.Pointer.Capture(RoadmapSurface);
            pendingRoadmapSelection = new PendingRoadmapSelectionContext(
                e.Pointer,
                e.GetPosition(RoadmapSurface),
                e.KeyModifiers);
            e.Handled = true;
        }

        private void StartPendingRoadmapPan(Control captureControl, PointerPressedEventArgs e)
        {
            ClearPendingRoadmapDrag();
            ClearPendingRoadmapPan();
            ClearPendingRoadmapSelection();

            e.Pointer.Capture(captureControl);
            pendingRoadmapPan = new PendingRoadmapPanContext(
                captureControl,
                e.Pointer,
                e.GetPosition(RoadmapViewport.Control),
                RoadmapViewport.Location);
            e.Handled = true;
        }

        private void ApplyRoadmapClickSelection(
            Control? context,
            TaskItemViewModel taskItem,
            KeyModifiers modifiers)
        {
            var operation = GetRoadmapSelectionOperation(modifiers);
            var wasSelected = selectedRoadmapTaskIds.Contains(taskItem.Id);
            var shouldSelectCurrentTask = ShouldSelectRoadmapCurrentTask(operation, wasSelected);

            switch (operation)
            {
                case RoadmapSelectionOperation.Replace:
                    selectedRoadmapTaskIds.Clear();
                    selectedRoadmapTaskIds.Add(taskItem.Id);
                    break;
                case RoadmapSelectionOperation.Toggle:
                    if (!selectedRoadmapTaskIds.Remove(taskItem.Id))
                    {
                        selectedRoadmapTaskIds.Add(taskItem.Id);
                    }

                    break;
                case RoadmapSelectionOperation.Add:
                    selectedRoadmapTaskIds.Add(taskItem.Id);
                    break;
                case RoadmapSelectionOperation.Remove:
                    selectedRoadmapTaskIds.Remove(taskItem.Id);
                    break;
            }

            ApplyRoadmapSelectionState();
            lastRoadmapClickSelectionTaskId = taskItem.Id;
            lastRoadmapClickSelectionModifiers = modifiers;
            lastRoadmapClickSelectionSelectedCurrentTask = shouldSelectCurrentTask;

            if (shouldSelectCurrentTask)
            {
                SelectRoadmapTask(context, taskItem);
            }
        }

        private bool ShouldSuppressDuplicateRoadmapClickSelection(
            TaskItemViewModel taskItem,
            KeyModifiers modifiers,
            int clickCount)
        {
            return clickCount > 1 &&
                   lastRoadmapClickSelectionTaskId == taskItem.Id &&
                   lastRoadmapClickSelectionModifiers == modifiers;
        }

        private static RoadmapSelectionOperation GetRoadmapSelectionOperation(KeyModifiers modifiers)
        {
            if ((modifiers & KeyModifiers.Alt) != 0)
            {
                return RoadmapSelectionOperation.Remove;
            }

            if ((modifiers & KeyModifiers.Control) != 0)
            {
                return RoadmapSelectionOperation.Toggle;
            }

            if ((modifiers & KeyModifiers.Shift) != 0)
            {
                return RoadmapSelectionOperation.Add;
            }

            return RoadmapSelectionOperation.Replace;
        }

        private static bool ShouldSelectRoadmapCurrentTask(
            RoadmapSelectionOperation operation,
            bool wasSelected)
        {
            return operation switch
            {
                RoadmapSelectionOperation.Replace => true,
                RoadmapSelectionOperation.Add => true,
                RoadmapSelectionOperation.Toggle => !wasSelected,
                RoadmapSelectionOperation.Remove => false,
                _ => false
            };
        }

        private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointer = e.GetCurrentPoint(this);
            var control = sender as Control;
            var gestureControl = control ?? (e.Source as Control);

            if (pointer.Properties.IsRightButtonPressed &&
                TryGetRoadmapTaskItem(gestureControl, out _))
            {
                StartPendingRoadmapPan(gestureControl ?? this, e);
                return;
            }

            if (!pointer.Properties.IsLeftButtonPressed)
            {
                ClearPendingRoadmapDrag();
                ClearPendingRoadmapPan();
                return;
            }

            ClearPendingRoadmapPan();
            if (!TryGetRoadmapTaskItem(gestureControl, out var taskItem))
            {
                ClearPendingRoadmapDrag();
                return;
            }

            if (e.ClickCount > 1)
            {
                var shouldSuppressClickSelection =
                    ShouldSuppressDuplicateRoadmapClickSelection(taskItem, e.KeyModifiers, e.ClickCount);
                var shouldSelectCurrentTask = shouldSuppressClickSelection
                    ? lastRoadmapClickSelectionSelectedCurrentTask
                    : ShouldSelectRoadmapCurrentTask(
                        GetRoadmapSelectionOperation(e.KeyModifiers),
                        selectedRoadmapTaskIds.Contains(taskItem.Id));

                if (!shouldSuppressClickSelection)
                {
                    ApplyRoadmapClickSelection(gestureControl, taskItem, e.KeyModifiers);
                }

                ToggleRoadmapTaskDetails(gestureControl, taskItem, shouldSelectCurrentTask);
                lastRoadmapPointerDoubleTapAt = DateTime.UtcNow;
                lastRoadmapPointerDoubleTapTaskId = taskItem.Id;
                ClearPendingRoadmapDrag();
                e.Handled = true;
                return;
            }

            if (HasRoadmapSelectionModifier(e.KeyModifiers))
            {
                if (!ShouldSuppressDuplicateRoadmapClickSelection(taskItem, e.KeyModifiers, e.ClickCount))
                {
                    ApplyRoadmapClickSelection(gestureControl, taskItem, e.KeyModifiers);
                }

                ClearPendingRoadmapDrag();
                e.Handled = true;
                return;
            }

            var selectedDragTasks = GetSelectedRoadmapDragTasks(taskItem);
            var shouldDeferClickSelection = selectedDragTasks.Count > 1 &&
                                            selectedRoadmapTaskIds.Contains(taskItem.Id);
            if (shouldDeferClickSelection)
            {
                SelectRoadmapTask(gestureControl, taskItem);
            }
            else if (!ShouldSuppressDuplicateRoadmapClickSelection(taskItem, e.KeyModifiers, e.ClickCount))
            {
                ApplyRoadmapClickSelection(gestureControl, taskItem, e.KeyModifiers);
                selectedDragTasks = [taskItem];
            }

            e.Pointer.Capture(gestureControl ?? this);
            pendingRoadmapDrag = new PendingRoadmapDragContext(
                gestureControl ?? this,
                e.Pointer,
                taskItem,
                e,
                e.GetPosition(gestureControl ?? this),
                selectedDragTasks,
                shouldDeferClickSelection);
        }

        private async void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (TryHandlePendingRoadmapPan(e))
            {
                return;
            }

            if (TryHandlePendingRoadmapSelection(e))
            {
                return;
            }

            var pending = pendingRoadmapDrag;
            if (pending == null ||
                roadmapDragInProgress ||
                !ReferenceEquals(e.Pointer, pending.Pointer))
            {
                return;
            }

            if (!e.GetCurrentPoint(pending.Control).Properties.IsLeftButtonPressed)
            {
                ClearPendingRoadmapDrag();
                return;
            }

            if (!HasExceededRoadmapDragThreshold(
                    pending.StartPoint,
                    e.GetPosition(pending.Control)))
            {
                return;
            }

            pendingRoadmapDrag = null;
            roadmapDragInProgress = true;
            e.Handled = true;

            try
            {
                await StartRoadmapDragAsync(pending, e);
            }
            finally
            {
                roadmapDragInProgress = false;
                ClearPendingRoadmapDrag();
            }
        }

        private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (pendingRoadmapSelection != null &&
                ReferenceEquals(e.Pointer, pendingRoadmapSelection.Pointer))
            {
                CommitPendingRoadmapSelection();
                ClearPendingRoadmapSelection();
                e.Handled = true;
                return;
            }

            if (pendingRoadmapPan != null &&
                ReferenceEquals(e.Pointer, pendingRoadmapPan.Pointer))
            {
                ClearPendingRoadmapPan();
                e.Handled = true;
                return;
            }

            if (pendingRoadmapDrag != null &&
                !ReferenceEquals(e.Pointer, pendingRoadmapDrag.Pointer))
            {
                return;
            }

            if (pendingRoadmapDrag?.ApplyClickSelectionOnRelease == true)
            {
                ApplyRoadmapClickSelection(
                    pendingRoadmapDrag.Control,
                    pendingRoadmapDrag.TaskItem,
                    KeyModifiers.None);
                e.Handled = true;
            }

            ClearPendingRoadmapDrag();
        }

        private bool TryHandlePendingRoadmapPan(PointerEventArgs e)
        {
            var pending = pendingRoadmapPan;
            if (pending == null || !ReferenceEquals(e.Pointer, pending.Pointer))
            {
                return false;
            }

            if (!e.GetCurrentPoint(pending.Control).Properties.IsRightButtonPressed)
            {
                ClearPendingRoadmapPan();
                return true;
            }

            var zoom = RoadmapViewport.Zoom;
            var scale = Math.Abs(zoom) < 0.001 ? 1 : zoom;
            var currentPoint = e.GetPosition(RoadmapViewport.Control);
            var delta = currentPoint - pending.StartPoint;
            RoadmapViewport.Location = new Point(
                pending.StartViewportLocation.X - delta.X / scale,
                pending.StartViewportLocation.Y - delta.Y / scale);
            e.Handled = true;
            return true;
        }

        private bool TryHandlePendingRoadmapSelection(PointerEventArgs e)
        {
            var pending = pendingRoadmapSelection;
            if (pending == null || !ReferenceEquals(e.Pointer, pending.Pointer))
            {
                return false;
            }

            if (!e.GetCurrentPoint(RoadmapSurface).Properties.IsLeftButtonPressed)
            {
                ClearPendingRoadmapSelection();
                return true;
            }

            var currentPoint = e.GetPosition(RoadmapSurface);
            if (!pending.HasExceededThreshold &&
                !HasExceededRoadmapDragThreshold(pending.StartPoint, currentPoint))
            {
                e.Handled = true;
                return true;
            }

            pending.HasExceededThreshold = true;
            pending.CurrentPoint = currentPoint;
            UpdateRoadmapSelectionRectangle(pending.StartPoint, currentPoint);
            e.Handled = true;
            return true;
        }

        private void CommitPendingRoadmapSelection()
        {
            var pending = pendingRoadmapSelection;
            if (pending == null || !pending.HasExceededThreshold)
            {
                return;
            }

            var rectangle = CreateNormalizedRect(pending.StartPoint, pending.CurrentPoint);
            var hitNodes = GetRoadmapNodesIntersecting(rectangle);
            ApplyRoadmapRectangleSelection(hitNodes, pending.Modifiers);
        }

        private void ApplyRoadmapRectangleSelection(
            IReadOnlyList<RoadmapNode> nodes,
            KeyModifiers modifiers)
        {
            var operation = GetRoadmapSelectionOperation(modifiers);

            if (operation == RoadmapSelectionOperation.Replace)
            {
                selectedRoadmapTaskIds.Clear();
            }

            foreach (var node in nodes)
            {
                switch (operation)
                {
                    case RoadmapSelectionOperation.Replace:
                    case RoadmapSelectionOperation.Add:
                        selectedRoadmapTaskIds.Add(node.Id);
                        break;
                    case RoadmapSelectionOperation.Toggle:
                        if (!selectedRoadmapTaskIds.Remove(node.Id))
                        {
                            selectedRoadmapTaskIds.Add(node.Id);
                        }

                        break;
                    case RoadmapSelectionOperation.Remove:
                        selectedRoadmapTaskIds.Remove(node.Id);
                        break;
                }
            }

            ApplyRoadmapSelectionState();
        }

        private IReadOnlyList<RoadmapNode> GetRoadmapNodesIntersecting(Rect rectangle)
        {
            var result = new List<RoadmapNode>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var border in RoadmapEditor
                         .GetVisualDescendants()
                         .OfType<Border>())
            {
                if (border.DataContext is not RoadmapNode node ||
                    !seenIds.Add(node.Id) ||
                    border.Bounds.Width <= 0 ||
                    border.Bounds.Height <= 0)
                {
                    continue;
                }

                var nodeBounds = GetRoadmapNodeBoundsInSelectionCoordinates(border);
                if (!nodeBounds.HasValue)
                {
                    continue;
                }

                if (RectanglesIntersect(rectangle, nodeBounds.Value))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        private Rect? GetRoadmapNodeBoundsInSelectionCoordinates(Control nodeControl)
        {
            var topLeft = nodeControl.TranslatePoint(new Point(0, 0), RoadmapSurface);
            var bottomRight = nodeControl.TranslatePoint(
                new Point(nodeControl.Bounds.Width, nodeControl.Bounds.Height),
                RoadmapSurface);

            if (!topLeft.HasValue || !bottomRight.HasValue)
            {
                return null;
            }

            return CreateNormalizedRect(topLeft.Value, bottomRight.Value);
        }

        private void UpdateRoadmapSelectionRectangle(Point startPoint, Point currentPoint)
        {
            var rectangle = CreateNormalizedRect(startPoint, currentPoint);
            RoadmapSelectionRectangleMargin = new Thickness(rectangle.X, rectangle.Y, 0, 0);
            RoadmapSelectionRectangleWidth = rectangle.Width;
            RoadmapSelectionRectangleHeight = rectangle.Height;
            IsRoadmapSelectionRectangleVisible = true;
        }

        private void ClearPendingRoadmapSelection()
        {
            pendingRoadmapSelection?.Pointer.Capture(null);
            pendingRoadmapSelection = null;
            IsRoadmapSelectionRectangleVisible = false;
            RoadmapSelectionRectangleWidth = 0;
            RoadmapSelectionRectangleHeight = 0;
        }

        private static Rect CreateNormalizedRect(Point first, Point second)
        {
            var x = Math.Min(first.X, second.X);
            var y = Math.Min(first.Y, second.Y);
            return new Rect(
                x,
                y,
                Math.Abs(first.X - second.X),
                Math.Abs(first.Y - second.Y));
        }

        private static bool RectanglesIntersect(Rect first, Rect second)
        {
            return first.X < second.Right &&
                   second.X < first.Right &&
                   first.Y < second.Bottom &&
                   second.Y < first.Bottom;
        }

        private static bool HasExceededRoadmapDragThreshold(Point startPoint, Point currentPoint)
        {
            return Math.Abs(currentPoint.X - startPoint.X) >= RoadmapDragThreshold ||
                   Math.Abs(currentPoint.Y - startPoint.Y) >= RoadmapDragThreshold;
        }

        private static async Task StartRoadmapDragAsync(PendingRoadmapDragContext pending, PointerEventArgs e)
        {
            var dragData = CreateRoadmapDragTransfer(pending.TaskItems);

            var graphControl = pending.Control.FindParent<GraphControl>();
            if (graphControl != null)
            {
                graphControl.RoadmapDragStartCount++;
            }

            try
            {
                await DragDrop.DoDragDropAsync(
                    pending.PressEvent,
                    dragData,
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                pending.Pointer.Capture(null);
            }
        }

        internal static InMemoryDragDataTransfer CreateRoadmapDragTransfer(
            IReadOnlyList<TaskItemViewModel> taskItems)
        {
            var normalizedTasks = NormalizeRoadmapDragTasks(taskItems);
            if (normalizedTasks.Count == 0)
            {
                return new InMemoryDragDataTransfer();
            }

            if (normalizedTasks.Count == 1)
            {
                return DragDataFormats.CreateTransfer(CustomDataFormat, normalizedTasks[0]);
            }

            var dragData = new InMemoryDragDataTransfer();
            var item = new DataTransferItem();
            item.Set(CustomBatchDataFormat, dragData.Track(new RoadmapTaskDragData(normalizedTasks)));
            item.Set(CustomDataFormat, dragData.Track(normalizedTasks[0]));
            dragData.Add(item);
            return dragData;
        }

        private IReadOnlyList<TaskItemViewModel> GetSelectedRoadmapDragTasks(TaskItemViewModel fallbackTask)
        {
            if (selectedRoadmapTaskIds.Count <= 1 ||
                !selectedRoadmapTaskIds.Contains(fallbackTask.Id))
            {
                return [fallbackTask];
            }

            var tasks = RoadmapNodes
                .Where(node => selectedRoadmapTaskIds.Contains(node.Id))
                .Select(node => node.TaskItem)
                .ToArray();
            return tasks.Length > 0 ? tasks : [fallbackTask];
        }

        private static IReadOnlyList<TaskItemViewModel> NormalizeRoadmapDragTasks(
            IEnumerable<TaskItemViewModel>? taskItems)
        {
            return taskItems?
                       .Where(static task => task != null && !string.IsNullOrWhiteSpace(task.Id))
                       .GroupBy(static task => task.Id, StringComparer.Ordinal)
                       .Select(static group => group.First())
                       .ToList()
                   ?? [];
        }

        private static bool HasRoadmapSelectionModifier(KeyModifiers modifiers)
        {
            var relevantModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
            return relevantModifiers != KeyModifiers.None;
        }

        private void ClearPendingRoadmapDrag()
        {
            pendingRoadmapDrag?.Pointer.Capture(null);
            pendingRoadmapDrag = null;
        }

        private void ClearPendingRoadmapPan()
        {
            pendingRoadmapPan?.Pointer.Capture(null);
            pendingRoadmapPan = null;
        }

        private void TaskTree_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            var control = sender as Control;
            if (!TryGetRoadmapTaskItem(control ?? (e.Source as Control), out var taskItem))
            {
                return;
            }

            if (ShouldIgnoreDuplicateRoadmapDoubleTap(taskItem))
            {
                e.Handled = true;
                return;
            }

            ApplyRoadmapClickSelection(control, taskItem, KeyModifiers.None);
            ToggleRoadmapTaskDetails(control, taskItem);
            e.Handled = true;
        }

        private bool ShouldIgnoreDuplicateRoadmapDoubleTap(TaskItemViewModel taskItem)
        {
            return lastRoadmapPointerDoubleTapTaskId == taskItem.Id &&
                   DateTime.UtcNow - lastRoadmapPointerDoubleTapAt < TimeSpan.FromMilliseconds(500);
        }

        private MainWindowViewModel? ToggleRoadmapTaskDetails(
            Control? context,
            TaskItemViewModel taskItem,
            bool selectTask = true)
        {
            var owner = selectTask
                ? SelectRoadmapTask(context, taskItem)
                : ResolveMainWindowViewModel(context);
            if (owner != null)
            {
                owner.DetailsAreOpen = !owner.DetailsAreOpen;
            }

            return owner;
        }

        private MainWindowViewModel? SelectRoadmapTask(Control? context, TaskItemViewModel taskItem)
        {
            var owner = ResolveMainWindowViewModel(context);
            if (owner == null)
            {
                return null;
            }

            owner.CurrentTaskItem = taskItem;
            if (owner.GraphMode)
            {
                owner.SelectCurrentTask();
            }

            ApplyRoadmapCurrentState();
            return owner;
        }

        private MainWindowViewModel? ResolveMainWindowViewModel(Control? context)
        {
            return context?.FindParentDataContext<MainWindowViewModel>() ??
                   this.FindParentDataContext<MainWindowViewModel>() ??
                   (DataContext as GraphViewModel)?.MainWindowViewModel ??
                   dc?.MainWindowViewModel ??
                   TaskItemViewModel.MainWindowInstance;
        }

        private static bool TryGetRoadmapTaskItem(Control? control, out TaskItemViewModel taskItem)
        {
            taskItem = null!;
            if (control == null)
            {
                return false;
            }

            taskItem = (control.DataContext switch
            {
                TaskItemViewModel item => item,
                RoadmapNode node => node.TaskItem,
                _ => control.FindParentDataContext<RoadmapNode>()?.TaskItem ??
                     control.FindParentDataContext<TaskItemViewModel>(),
            })!;

            return taskItem != null;
        }

        private sealed class PendingRoadmapDragContext(
            Control control,
            IPointer pointer,
            TaskItemViewModel taskItem,
            PointerPressedEventArgs pressEvent,
            Point startPoint,
            IReadOnlyList<TaskItemViewModel> taskItems,
            bool applyClickSelectionOnRelease)
        {
            public Control Control { get; } = control;

            public IPointer Pointer { get; } = pointer;

            public TaskItemViewModel TaskItem { get; } = taskItem;

            public PointerPressedEventArgs PressEvent { get; } = pressEvent;

            public Point StartPoint { get; } = startPoint;

            public IReadOnlyList<TaskItemViewModel> TaskItems { get; } = taskItems;

            public bool ApplyClickSelectionOnRelease { get; } = applyClickSelectionOnRelease;
        }

        internal sealed class RoadmapTaskDragData(IReadOnlyList<TaskItemViewModel> taskItems)
        {
            public IReadOnlyList<TaskItemViewModel> TaskItems { get; } =
                NormalizeRoadmapDragTasks(taskItems);
        }

        private sealed class PendingRoadmapPanContext(
            Control control,
            IPointer pointer,
            Point startPoint,
            Point startViewportLocation)
        {
            public Control Control { get; } = control;

            public IPointer Pointer { get; } = pointer;

            public Point StartPoint { get; } = startPoint;

            public Point StartViewportLocation { get; } = startViewportLocation;
        }

        private sealed class PendingRoadmapSelectionContext(
            IPointer pointer,
            Point startPoint,
            KeyModifiers modifiers)
        {
            public IPointer Pointer { get; } = pointer;

            public Point StartPoint { get; } = startPoint;

            public KeyModifiers Modifiers { get; } = modifiers;

            public Point CurrentPoint { get; set; } = startPoint;

            public bool HasExceededThreshold { get; set; }
        }

        private enum RoadmapSelectionOperation
        {
            Replace,
            Toggle,
            Add,
            Remove
        }

        private bool Matches(TaskItemViewModel task, string normalizedQuery, bool isFuzzy)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery)) return false;
            var hay = SearchDefinition.NormalizeText($"{task.OnlyTextTitle} {task.Description} {task.GetAllEmoji} {task.Id}");
            var words = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (words.Length == 0)
                return false;

            if (!isFuzzy)
            {
                return words.All(w => hay.Contains(w));
            }

            foreach (var w in words)
            {
                var maxDist = FuzzyMatcher.GetMaxDistanceForWord(w);
                if (!FuzzyMatcher.IsFuzzyMatch(hay, w, maxDist))
                    return false;
            }

            return true;
        }

        private IEnumerable<TaskItemViewModel> EnumerateTasks(ReadOnlyObservableCollection<TaskWrapperViewModel> roots)
        {
            var q = new Queue<TaskWrapperViewModel>(roots);
            var visited = new HashSet<string>();
            while (q.Count > 0)
            {
                var w = q.Dequeue();
                if (!visited.Add(w.TaskItem.Id))
                {
                    continue;
                }

                yield return w.TaskItem;
                foreach (var c in w.SubTasks) q.Enqueue(c);
            }
        }

        private IEnumerable<TaskWrapperViewModel> EnumerateWrappers(ReadOnlyObservableCollection<TaskWrapperViewModel> roots)
        {
            var q = new Queue<TaskWrapperViewModel>(roots);
            var visited = new HashSet<TaskWrapperViewModel>();
            while (q.Count > 0)
            {
                var wrapper = q.Dequeue();
                if (!visited.Add(wrapper))
                {
                    continue;
                }

                yield return wrapper;
                foreach (var child in wrapper.SubTasks)
                {
                    q.Enqueue(child);
                }
            }
        }

        private void UpdateHighlights()
        {
            try
            {
                var localDc = dc;
                if (localDc == null) return;

                var normalized = SearchDefinition.NormalizeText(localDc.Search?.SearchText ?? "");
                var roots = localDc.OnlyUnlocked ? localDc.UnlockedTasks : localDc.Tasks;
                var isFuzzy = localDc.Search?.IsFuzzySearch == true;

                var items = EnumerateTasks(roots).ToList();

                if (string.IsNullOrEmpty(normalized))
                {
                    foreach (var t in items)
                        t.IsHighlighted = false;
                }
                else
                {
                    foreach (var t in items)
                        t.IsHighlighted = Matches(t, normalized, isFuzzy);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateHighlights failed: {ex.Message}");
            }
        }

        private sealed record RoadmapBuildRequest(
            int Version,
            ReadOnlyObservableCollection<TaskWrapperViewModel> Roots,
            RoadmapGraphBuildInput Input);
    }
}
