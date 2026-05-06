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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

        private GraphViewModel? dc;
        private readonly DisposableList disposableList = new DisposableListRealization();
        private readonly SerialDisposable roadmapScopeSubscriptions = new();
        private readonly SerialDisposable roadmapFilterSubscriptions = new();
        private readonly DispatcherTimer graphUpdateTimer;
        private ReadOnlyObservableCollection<TaskWrapperViewModel>? roadmapScopeSubscriptionRoots;
        private string? roadmapScopeSubscriptionSignature;
        private bool roadmapScopeSubscriptionsDirty = true;
        private bool graphUpdateQueued;
        private bool highlightUpdateQueued;
        private bool roadmapBuildRunning;
        private int roadmapBuildRequestVersion;
        private RoadmapBuildRequest? pendingRoadmapBuildRequest;
        private const double RoadmapPanStep = 240;
        private static readonly TimeSpan RoadmapGraphUpdateDelay = TimeSpan.FromMilliseconds(100);

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
            };
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, MainControl.Drop);
            AddHandler(DragDrop.DragOverEvent, MainControl.DragOver);
            KeyDown += RoadmapEditor_KeyDown;
            RoadmapEditor.KeyDown += RoadmapEditor_KeyDown;
        }

        public ObservableCollection<RoadmapNode> RoadmapNodes { get; } = new();

        public ObservableCollection<RoadmapConnection> RoadmapConnections { get; } = new();

        public int RoadmapGraphUpdateCount { get; private set; }

        public TimeSpan RoadmapLastBuildTime { get; private set; }

        public TimeSpan RoadmapLastApplyProjectionTime { get; private set; }

        public int RoadmapScopeSubscriptionRefreshCount { get; private set; }

        public bool RoadmapGraphBuildInProgress { get; private set; }

        public bool RoadmapLastBuildRanOnUiThread { get; private set; }

        public int RoadmapGraphBackgroundBuildStartCount { get; private set; }

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
            ResetRoadmapScopeSubscriptionCache();
            CancelScheduledGraphUpdate();
            CancelPendingRoadmapBuilds();
            ClearRoadmapProjection();

            if (dc == null)
            {
                return;
            }

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
                .ObserveOn(RxApp.MainThreadScheduler)
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
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ScheduleUpdateGraph);
                return;
            }

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
            if (!roadmapBuildRunning)
            {
                RoadmapGraphBuildInProgress = false;
            }
        }

        private void InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(InvalidateRoadmapScopeSubscriptionsAndScheduleUpdate);
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
            Dispatcher.UIThread.Post(() =>
            {
                highlightUpdateQueued = false;
                UpdateHighlights();
            });
        }

        private void UpdateGraph()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(UpdateGraph);
                return;
            }

            CancelScheduledGraphUpdate();

            var localDc = dc;
            if (localDc == null)
            {
                return;
            }

            var roots = localDc.OnlyUnlocked ? localDc.UnlockedTasks : localDc.Tasks;
            var input = RoadmapGraphBuilder.Capture(roots, GetMeasuredRoadmapNodeWidths());
            QueueRoadmapBuild(new RoadmapBuildRequest(
                ++roadmapBuildRequestVersion,
                roots,
                input));
        }

        private void QueueRoadmapBuild(RoadmapBuildRequest request)
        {
            pendingRoadmapBuildRequest = request;
            if (!roadmapBuildRunning)
            {
                StartNextRoadmapBuild();
            }
        }

        private void StartNextRoadmapBuild()
        {
            if (pendingRoadmapBuildRequest == null)
            {
                RoadmapGraphBuildInProgress = false;
                return;
            }

            var request = pendingRoadmapBuildRequest;
            pendingRoadmapBuildRequest = null;
            roadmapBuildRunning = true;
            RoadmapGraphBuildInProgress = true;
            RoadmapGraphBackgroundBuildStartCount++;
            _ = RunRoadmapBuildAsync(request);
        }

        private async Task RunRoadmapBuildAsync(RoadmapBuildRequest request)
        {
            RoadmapGraphProjection? projection = null;
            var buildStopwatch = Stopwatch.StartNew();
            var buildRanOnUiThread = false;

            try
            {
                projection = await Task.Run(() =>
                {
                    buildRanOnUiThread = Dispatcher.UIThread.CheckAccess();
                    return RoadmapGraphBuilder.Build(request.Input);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Trace.TraceError("Roadmap graph background build failed: {0}", exception);
            }
            finally
            {
                buildStopwatch.Stop();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    CompleteRoadmapBuild(
                        request,
                        projection,
                        buildStopwatch.Elapsed,
                        buildRanOnUiThread);
                }
                finally
                {
                    roadmapBuildRunning = false;
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

            var applyStopwatch = Stopwatch.StartNew();
            RegisterRoadmapScopeSubscriptions(request.Roots, projection);
            ApplyProjection(projection);
            applyStopwatch.Stop();

            RoadmapLastBuildTime = buildTime;
            RoadmapLastApplyProjectionTime = applyStopwatch.Elapsed;
            RoadmapLastBuildRanOnUiThread = buildRanOnUiThread;
            RoadmapGraphUpdateCount++;

            UpdateHighlights();
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
                or nameof(TaskItemViewModel.OnlyTextTitle)
                or nameof(TaskItemViewModel.Emoji)
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
                .ObserveOn(RxApp.MainThreadScheduler)
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
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ => ScheduleUpdateGraph()));
            }
        }

        private void RoadmapEditor_KeyDown(object? sender, KeyEventArgs e)
        {
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

        private void FitRoadmapToScreen()
        {
            Dispatcher.UIThread.Post(() => RoadmapEditor.FitToScreen());
        }

        private void ResetRoadmapViewport()
        {
            Dispatcher.UIThread.Post(() =>
            {
                RoadmapEditor.ViewportZoom = 1;
                RoadmapEditor.ViewportLocation = new Point(0, 0);
            });
        }

        private void RoadmapZoomIn_OnClick(object? sender, RoutedEventArgs e)
        {
            RoadmapEditor.ZoomIn();
            e.Handled = true;
        }

        private void RoadmapZoomOut_OnClick(object? sender, RoutedEventArgs e)
        {
            RoadmapEditor.ZoomOut();
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

        private void RoadmapMinimap_OnZoom(object? sender, RoutedEventArgs e)
        {
            var zoom = e.GetType().GetProperty("Zoom")?.GetValue(e);
            var location = e.GetType().GetProperty("Location")?.GetValue(e);

            if (zoom is double zoomValue && location is Point locationValue)
            {
                RoadmapEditor.ZoomAtPosition(zoomValue, locationValue);
            }
        }

        private void PanRoadmapViewport(double deltaX, double deltaY)
        {
            var current = RoadmapEditor.ViewportLocation;
            RoadmapEditor.ViewportLocation = new Point(
                current.X + deltaX,
                current.Y + deltaY);
        }

        private void RoadmapNode_OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Control { DataContext: RoadmapNode node })
            {
                if (node.SetMeasuredWidth(e.NewSize.Width))
                {
                    ScheduleUpdateGraph();
                }
            }
        }

        private Dictionary<string, double> GetMeasuredRoadmapNodeWidths()
        {
            return RoadmapNodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First().Width);
        }

        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointer = e.GetCurrentPoint(this);
            if (!pointer.Properties.IsLeftButtonPressed)
            {
                return;
            }

            var control = sender as Control;
            var taskItem = control?.DataContext switch
            {
                TaskItemViewModel item => item,
                RoadmapNode node => node.TaskItem,
                _ => null
            };

            if (taskItem == null)
            {
                return;
            }

            var mwm = TaskItemViewModel.MainWindowInstance;
            if (mwm != null)
            {
                mwm.CurrentTaskItem = taskItem;
            }

            var dragData = new DataObject();
            dragData.Set(CustomFormat, taskItem);

            await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void TaskTree_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            var mwm = TaskItemViewModel.MainWindowInstance;
            if (mwm != null)
            {
                mwm.DetailsAreOpen = !mwm.DetailsAreOpen;
            }
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
