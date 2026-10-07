using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData.Binding;
using TUnit.Assertions.Exceptions;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class EmojiProjectionUiAffinityTests
{
    [Test]
    [Arguments("none")]
    [Arguments("input")]
    [Arguments("scope")]
    [Arguments("detach")]
    [Arguments("restore")]
    public async Task Workspace_SearchClearLateWorker_RestoresListAWithoutChangingActiveCardB(string cancellation)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            var timer = new ControlledRootScheduler();
            var retries = new ControlledRootScheduler();
            vm.EmojiSearchRefreshScheduler = timer;
            vm.RootSelectionRestoreScheduler = retries;
            await vm.Connect();
            var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
            var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
            var taskB = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask3Id);
            parent.Title = "Search parent A";
            child.Title = "Unique search target A";
            taskB.Description = string.Join("\n", Enumerable.Range(0, 80).Select(i => $"Card B immutable context line {i}"));
            await vm.CommitWorkspaceEditorsAsync();
            var shell = new MainScreen { DataContext = vm };
            var window = new Window { Content = shell, Width = 1600, Height = 800 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(await vm.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.AllTasks))).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var list = shell.GetVisualDescendants().OfType<TaskListDocumentView>().Single();
                var tree = list.TaskTree!;
                vm.Search.SearchText = "Unique search target A";
                timer.RunAll();
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                    vm.CurrentAllTasksItems.Count == 1 && vm.CurrentAllTasksItems[0].TaskItem.Id == child.Id,
                    TimeSpan.FromSeconds(5))).IsTrue();
                var promoted = vm.CurrentAllTasksItems.Single();
                tree.SelectedItem = promoted;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(await vm.OpenWorkspaceTaskAsync(taskB, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var card = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                var primary = vm.WorkspaceNavigation.PrimaryPane.ActiveTab!;
                var secondary = vm.WorkspaceNavigation.SecondaryPane!.ActiveTab!;
                var historyA = primary.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var historyB = secondary.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var indexA = primary.CurrentIndex;
                var indexB = secondary.CurrentIndex;
                var scrollB = card.GetVisualDescendants().OfType<ScrollViewer>().First(control => control.IsEffectivelyVisible);
                window.UpdateLayout();
                scrollB.Offset = new Avalonia.Vector(0, 100);
                Dispatcher.UIThread.RunJobs();
                var offsetB = scrollB.Offset;
                await Assert.That(offsetB.Y).IsGreaterThan(90);
                var locationB = secondary.CurrentLocation;
                vm.Search.SearchText = string.Empty;
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                    vm.CurrentAllTasksItems.Any(item => item.TaskItem.Id == parent.Id) &&
                    vm.CurrentAllTasksItems.Any(item => ReferenceEquals(item, promoted)), TimeSpan.FromSeconds(5))).IsTrue();
                if (cancellation == "none") retries.RunAll();
                await Assert.That(tree.SelectedItem).IsSameReferenceAs(promoted);
                if (cancellation != "none")
                {
                    if (cancellation == "input")
                        tree.RaiseEvent(new Avalonia.Input.KeyEventArgs
                        {
                            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
                            Key = Avalonia.Input.Key.Escape, Source = tree
                        });
                    else if (cancellation == "scope") vm.WorkspaceNavigation.InvalidatePendingNavigation();
                    else if (cancellation == "detach") window.Content = null;
                    else list.RestoreViewState((TaskListDocumentState)list.CaptureViewState() with { SelectedIds = [] });
                    tree.SelectedItem = null;
                    vm.CurrentAllTasksItem = null;
                    Dispatcher.UIThread.RunJobs();
                    // These callbacks were queued before the input/scope/detach/restore.
                    // They must not reapply the stale child before the late worker batch.
                    retries.RunAll();
                    await Assert.That(vm.CurrentAllTasksItem).IsNull();
                    await Assert.That(tree.SelectedItem).IsNull();
                }
                await Task.Run(timer.RunAll).WaitAsync(TimeSpan.FromSeconds(10));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var nested = vm.FindTaskWrapperViewModel(child, vm.CurrentAllTasksItems);
                await Assert.That(nested).IsNotNull();
                await Assert.That(ReferenceEquals(nested, promoted)).IsFalse();
                if (cancellation == "none")
                {
                    await Assert.That(vm.CurrentAllTasksItem).IsSameReferenceAs(nested);
                    await Assert.That(tree.SelectedItem).IsSameReferenceAs(nested);
                    await Assert.That(nested!.Parent!.IsExpanded).IsTrue();
                    await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        return tree.GetVisualDescendants().OfType<TreeViewItem>()
                            .Any(item => ReferenceEquals(item.DataContext, nested) && item.IsSelected && item.IsEffectivelyVisible);
                    }, TimeSpan.FromSeconds(5))).IsTrue();
                }
                else
                {
                    await Assert.That(vm.CurrentAllTasksItem).IsNull();
                    await Assert.That(tree.SelectedItem).IsNull();
                }
                await Assert.That(card.RouteTaskItem).IsSameReferenceAs(taskB);
                await Assert.That(card.CardContext!.Task).IsSameReferenceAs(taskB);
                await Assert.That(vm.CurrentTaskItem).IsSameReferenceAs(taskB);
                await Assert.That(vm.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(secondary);
                await Assert.That(secondary.CurrentLocation).IsSameReferenceAs(locationB);
                await Assert.That(primary.CurrentIndex).IsEqualTo(indexA);
                await Assert.That(secondary.CurrentIndex).IsEqualTo(indexB);
                await Assert.That(primary.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(historyA)).IsTrue();
                await Assert.That(secondary.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(historyB)).IsTrue();
                await Assert.That(scrollB.Offset).IsEqualTo(offsetB);
                if (cancellation == "none" && Environment.GetEnvironmentVariable("UNLIMOTION_TEST_CAPTURE_FRAMES") == "1")
                {
                    window.UpdateLayout();
                    for (var index = 0; index < 5; index++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No rendered workspace selection frame.");
                    var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY")
                        ?? throw new InvalidOperationException("No workspace screenshot directory.");
                    System.IO.Directory.CreateDirectory(directory);
                    frame.Save(System.IO.Path.Combine(directory, "workspace-selection-a-card-b.png"));
                }
            }
            finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootProjection_ClearSearchLateWorkerBatch_RestoresCanonicalNestedSelection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            var timer = new ControlledRootScheduler();
            var retries = new ControlledRootScheduler();
            var probe = new RootNotificationProbe();
            vm.EmojiSearchRefreshScheduler = timer;
            vm.RootSelectionRestoreScheduler = retries;
            vm.RootCollectionFactory = () => probe;
            vm.RootDeliveryTrace = probe.TraceBatch;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            vm.AllTasksMode = true;
            vm.DetailsAreOpen = false;
            var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
            var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
            parent.Title = "Search parent";
            child.Title = "Unique search target";
            var view = new MainControl { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var tree = view.FindControl<TreeView>("AllTasksTree")!;
                vm.Search.SearchText = "Unique search target";
                timer.RunAll();
                await Assert.That(await TestHelpers.WaitUntilAsync(
                    () => vm.CurrentAllTasksItems.Count == 1 && vm.CurrentAllTasksItems[0].TaskItem.Id == child.Id,
                    TimeSpan.FromSeconds(5))).IsTrue();
                var searchWrapper = vm.CurrentAllTasksItems.Single(item => item.TaskItem.Id == child.Id);
                tree.SelectedItem = searchWrapper;
                vm.CurrentAllTasksItem = searchWrapper;
                await Assert.That(ReferenceEquals(vm.CurrentAllTasksItem, searchWrapper)).IsTrue();
                await Assert.That(ReferenceEquals(tree.SelectedItem, searchWrapper)).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(child.Id);
                vm.CurrentTaskItem = null;
                RecordSelection("search-established");
                vm.Search.SearchText = string.Empty;
                // Keep the emoji stage's old search predicate while the UI top stage clears.
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                    vm.CurrentAllTasksItems.Any(item => item.TaskItem.Id == parent.Id) &&
                    vm.CurrentAllTasksItems.Any(item => ReferenceEquals(item, searchWrapper)), TimeSpan.FromSeconds(5))).IsTrue();
                retries.RunAll(); // Both legacy retries complete before the late worker stage.
                RecordSelection("ui-clear-before-worker");
                await Assert.That(ReferenceEquals(vm.CurrentAllTasksItem, searchWrapper)).IsTrue();
                await Assert.That(ReferenceEquals(tree.SelectedItem, searchWrapper)).IsTrue();
                probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
                probe.ClearEvents();
                await Task.Run(timer.RunAll).WaitAsync(TimeSpan.FromSeconds(10));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var nested = vm.FindTaskWrapperViewModel(child, vm.CurrentAllTasksItems);
                RecordSelection("after-late-worker");
                await Assert.That(nested).IsNotNull();
                await Assert.That(ReferenceEquals(nested, searchWrapper)).IsFalse();
                await Assert.That(ReferenceEquals(vm.CurrentAllTasksItem, nested)).IsTrue();
                await Assert.That(ReferenceEquals(tree.SelectedItem, nested)).IsTrue();
                probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
                await probe.AssertUiDeliveryAsync();

                void RecordSelection(string phase) => TestExecutionTrace.Write("emoji-selection", phase, "observed", details: new
                {
                    vm.Search.SearchText,
                    rootIds = vm.CurrentAllTasksItems.Select(item => item.Id).ToArray(),
                    currentId = vm.CurrentAllTasksItem?.Id,
                    currentIsSearchWrapper = ReferenceEquals(vm.CurrentAllTasksItem, searchWrapper),
                    treeId = (tree.SelectedItem as TaskWrapperViewModel)?.Id,
                    treeIsSearchWrapper = ReferenceEquals(tree.SelectedItem, searchWrapper)
                });
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootProjection_SealedEditorLateDiskRefresh_ChangesTitleAndEmojiOnSameTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            var original = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask3Id);
            var diskTitle = original.Title;
            original.Title = "🧰 Beta tools target";
            await Assert.That(vm.EmojiExcludeFilters.Any(filter => filter.Emoji == "🧰")).IsTrue();
            var storage = (FileStorage)vm.taskRepository.TaskTreeManager.Storage;
            storage.Watcher!.ForceUpdateFile(original.Id, global::Unlimotion.TaskTree.UpdateType.Saved);
            await Assert.That(await TestHelpers.WaitUntilAsync(() => original.Title == diskTitle,
                TimeSpan.FromSeconds(5))).IsTrue();
            var current = TestHelpers.GetTask(vm, original.Id);
            TestExecutionTrace.Write("emoji-data", "late-disk-refresh", "observed", details: new
            {
                current.Id, current.Title, current.Emoji,
                sameIdentity = ReferenceEquals(current, original), diskTitle,
                keys = vm.EmojiExcludeFilters.Select(filter => filter.Emoji).ToArray()
            });
            await Assert.That(ReferenceEquals(current, original)).IsTrue();
            await Assert.That(current.Emoji).IsEqualTo(string.Empty);
            await Assert.That(vm.EmojiExcludeFilters.Any(filter => filter.Emoji == "🧰")).IsFalse();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootReplay_UnknownResetCannotHideBadIndexBeforeKnownReset()
    {
        var probe = new RootNotificationProbe();
        Record(1, NotifyCollectionChangedAction.Reset, -1, [], null);
        Record(2, NotifyCollectionChangedAction.Add, 999, ["unexpected"], null);
        Record(3, NotifyCollectionChangedAction.Reset, -1, [], []);
        probe.RecordQuiescentSnapshot([]);
        AssertionException? observed = null;
        try { await probe.AssertUiDeliveryAsync(); }
        catch (AssertionException error) { observed = error; }
        await Assert.That(observed).IsNotNull();

        void Record(long id, NotifyCollectionChangedAction action, int index, string[] newIds, string[]? resetIds)
        {
            foreach (var phase in new[] { "begin", "end" })
                probe.Events.Enqueue(new RootNotificationProbe.Delivery(id, phase, 1, 1, true,
                    action, index, -1, newIds, [], resetIds));
        }
    }

    [Test]
    public async Task RootProjection_PendingWorkerThenDirectUiFilter_ReplaysActualSortedIdsBeforeReturn()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            var probe = new RootNotificationProbe();
            var scheduler = new ControlledRootScheduler();
            vm.RootCollectionFactory = () => probe;
            vm.RootDeliveryTrace = probe.TraceBatch;
            vm.RootUiDeliveryScheduler = scheduler;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            var first = vm.EmojiFilters.Single(filter => filter.Emoji == "❌");
            var second = vm.EmojiFilters.Single(filter => filter.Emoji == "🚀");
            var third = vm.EmojiFilters.Single(filter => filter.Emoji == "🧰");
            first.ShowTasks = true;
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            probe.ClearEvents();
            await Task.Run(() => second.ShowTasks = true).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(vm.CurrentAllTasksItems.Select(item => item.TaskItem.Id).SequenceEqual(new[] { first.Source!.Id })).IsTrue();
            third.ShowTasks = true;
            // The direct UI call must drain both accepted sorted batches without a pump.
            await Assert.That(vm.CurrentAllTasksItems.Select(item => item.TaskItem.Id)
                .SequenceEqual(new[] { second.Source!.Id, third.Source!.Id, first.Source!.Id })).IsTrue();
            scheduler.RunAll();
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            await probe.AssertUiDeliveryAsync();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootBinding_CustomWorkerContext_UsesActualSchedulerOwner_AndNullContextStaysSync()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var scheduler = new ControlledRootScheduler();
            var owner = Environment.CurrentManagedThreadId;
            int? boundOwner = null;
            // A custom context is supplied from a worker; it must not become the fast-path owner.
            var bindingStarted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.Run(() => bindingStarted.SetResult(MainWindowViewModel.BindRootProjectionOnOwner(
                id => boundOwner = id, new SynchronizationContext(), scheduler))).WaitAsync(TimeSpan.FromSeconds(10));
            var binding = await bindingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(binding.IsCompleted).IsFalse();
            scheduler.RunAll();
            await binding.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(boundOwner).IsEqualTo(owner);
            var boundWithoutContext = false;
            var synchronous = MainWindowViewModel.BindRootProjectionOnOwner(id =>
            {
                boundWithoutContext = true;
                if (id != null) throw new InvalidOperationException("Context-free binding acquired a UI owner.");
            }, null, scheduler);
            await Assert.That(boundWithoutContext && synchronous.IsCompletedSuccessfully).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootDelivery_RejectingScheduler_PreservesErrorAndTerminatesAcceptedWork()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var source = new Subject<int>();
            var original = new InvalidOperationException("Controlled scheduler rejection");
            var scheduler = new ControlledRootScheduler { Rejection = original };
            var delivered = new ConcurrentQueue<int>();
            using var subscription = MainWindowViewModel.DeliverRootProjection(source, scheduler, Environment.CurrentManagedThreadId)
                .Subscribe(delivered.Enqueue);
            var observed = await Task.Run(() =>
            {
                try { source.OnNext(1); return null; }
                catch (Exception error) { return error; }
            }).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(ReferenceEquals(observed, original)).IsTrue();
            scheduler.Rejection = null;
            source.OnNext(2);
            scheduler.RunAll();
            await Assert.That(delivered.Count).IsEqualTo(0);
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootProjection_DelayedSearchRefresh_FromUiOrWorker_HasUiAffinity(bool worker)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            var probe = new RootNotificationProbe();
            var timer = new ControlledRootScheduler();
            vm.RootCollectionFactory = () => probe;
            vm.RootDeliveryTrace = probe.TraceBatch;
            vm.EmojiSearchRefreshScheduler = timer;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            TestHelpers.GetTask(vm, MainWindowViewModelFixture.BlockedTask6Id).Title = "❌ Gamma nested target";
            vm.EmojiFilters.Single(filter => filter.Emoji == "❌").ShowTasks = true;
            timer.RunAll();
            vm.Search.SearchText = "target";
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            probe.ClearEvents();
            if (worker) await Task.Run(timer.RunAll).WaitAsync(TimeSpan.FromSeconds(10));
            else timer.RunAll();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            await Assert.That(probe.Events.Count).IsGreaterThan(0);
            await probe.AssertUiDeliveryAsync();
            await Assert.That(vm.CurrentAllTasksItems.Any(item => item.TaskItem.Id == MainWindowViewModelFixture.BlockedTask6Id)).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootDelivery_InitialNestedAndQueuedWorkerThenUi_PreservesSyncFifo()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var source = new Subject<int>();
            var scheduler = new ControlledRootScheduler();
            var delivered = new ConcurrentQueue<int>();
            var depth = 0;
            var maximumDepth = 0;
            using var subscription = MainWindowViewModel.DeliverRootProjection(
                Observable.Return(0).Concat(source).Do(value => TraceBatch("accepted", value)), scheduler, Environment.CurrentManagedThreadId)
                .Subscribe(value =>
                {
                    ++depth;
                    maximumDepth = Math.Max(maximumDepth, depth);
                    try
                    {
                        TraceBatch("published", value);
                        delivered.Enqueue(value);
                        if (value == 1) source.OnNext(2);
                    }
                    finally { --depth; }
                });
            // No RunAll/pump between the calls and these initial/direct UI assertions.
            await Assert.That(delivered.ToArray()).IsEquivalentTo(new[] { 0 });
            source.OnNext(1);
            await Assert.That(delivered.ToArray().SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
            await Assert.That(maximumDepth).IsEqualTo(1);
            await Task.Run(() => source.OnNext(3)).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(delivered.ToArray().SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
            source.OnNext(4);
            await Assert.That(delivered.ToArray().SequenceEqual(new[] { 0, 1, 2, 3, 4 })).IsTrue();
            scheduler.RunAll();
            await Assert.That(delivered.ToArray().SequenceEqual(new[] { 0, 1, 2, 3, 4 })).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootDelivery_DisposeWithPendingWorkerBatch_DoesNotPublish()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var source = new Subject<int>();
            var scheduler = new ControlledRootScheduler();
            var delivered = new ConcurrentQueue<int>();
            var subscription = MainWindowViewModel.DeliverRootProjection(source, scheduler, Environment.CurrentManagedThreadId)
                .Subscribe(delivered.Enqueue);
            try { await Task.Run(() => source.OnNext(1)).WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { subscription.Dispose(); }
            scheduler.RunAll();
            await Assert.That(delivered.Count).IsEqualTo(0);
        }, CancellationToken.None);
    }

    private static void TraceBatch(string phase, int batchId)
    {
        TestExecutionTrace.Write("emoji-batch", "root-delivery", phase,
            details: new { batchId, uiThread = Dispatcher.UIThread.CheckAccess() });
        if (phase == "published" && !Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Root batch published outside UI owner.");
    }

    [Test]
    public async Task RootDelivery_ConsumerException_IsPreservedAndStopsPendingDelivery()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var source = new Subject<int>();
            var scheduler = new ControlledRootScheduler();
            var original = new InvalidOperationException("Controlled consumer failure");
            var delivered = new List<int>();
            using var subscription = MainWindowViewModel.DeliverRootProjection(source, scheduler, Environment.CurrentManagedThreadId)
                .Subscribe(value => { delivered.Add(value); if (value == 1) throw original; });
            Exception? observed = null;
            try { source.OnNext(1); }
            catch (Exception error) { observed = error; }
            await Assert.That(ReferenceEquals(observed, original)).IsTrue();
            source.OnNext(2);
            scheduler.RunAll();
            await Assert.That(delivered.SequenceEqual(new[] { 1 })).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootDelivery_UnpumpedWorkerBurst_IsBoundedAndFailsExplicitly()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var source = new Subject<int>();
            var scheduler = new ControlledRootScheduler();
            var delivered = new ConcurrentQueue<int>();
            using var subscription = MainWindowViewModel.DeliverRootProjection(source, scheduler, Environment.CurrentManagedThreadId)
                .Subscribe(delivered.Enqueue);
            var observed = await Task.Run(() =>
            {
                try
                {
                    for (var index = 0; index <= MainWindowViewModel.MaximumPendingRootBatches; ++index) source.OnNext(index);
                    return null;
                }
                catch (InvalidOperationException error) { return error; }
            }).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(observed).IsNotNull();
            scheduler.RunAll();
            await Assert.That(delivered.Count).IsEqualTo(0);
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootProjection_SameFilterRefresh_FromUiOrWorker_HasUiAffinity(bool worker)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            var probe = new RootNotificationProbe();
            vm.RootCollectionFactory = () => probe;
            vm.RootDeliveryTrace = probe.TraceBatch;
            await vm.Connect();
            // Initial projection must already be available, before a dispatcher pump.
            await Assert.That(vm.CurrentAllTasksItems.Count).IsGreaterThan(0);
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            var first = vm.EmojiFilters.Single(filter => filter.Emoji == "❌");
            var second = vm.EmojiFilters.Single(filter => filter.Emoji == "🚀");
            first.ShowTasks = true;
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            probe.ClearEvents();

            // The producer finishes before taking a live snapshot. No waits occur in notify.
            if (worker) await Task.Run(() => second.ShowTasks = true).WaitAsync(TimeSpan.FromSeconds(10));
            else second.ShowTasks = true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            probe.RecordQuiescentSnapshot(vm.CurrentAllTasksItems);
            await Assert.That(probe.Events.Count).IsGreaterThan(0);
            await probe.AssertUiDeliveryAsync();
            await Assert.That(vm.CurrentAllTasksItems.Select(item => item.TaskItem.Id).ToArray())
                .IsEquivalentTo(new[] { first.Source!.Id, second.Source!.Id });
        }, CancellationToken.None);
    }
}

// Test-controlled timer/dispatcher: accepted work stays pending until RunAll is called.
// Calling RunAll on a worker explicitly exercises the production delayed producer.
internal sealed class ControlledRootScheduler : IScheduler
{
    private readonly ConcurrentQueue<Action> _pending = new();
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
    public Exception? Rejection { get; set; }
    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        if (Rejection != null) throw Rejection;
        var cancelled = new BooleanDisposable();
        var result = new SingleAssignmentDisposable();
        _pending.Enqueue(() => { if (!cancelled.IsDisposed) result.Disposable = action(this, state); });
        return new CompositeDisposable(cancelled, result);
    }
    public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);
    public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);
    public void RunAll()
    {
        var budget = 1000;
        while (_pending.TryDequeue(out var action))
        {
            if (--budget == 0) throw new InvalidOperationException("Controlled scheduler did not quiesce.");
            action();
        }
    }
}

// This envelope includes every subscriber, including the later ItemsControl handler.
// RED diagnostics never enumerate a live collection while a producer can still write it.
internal sealed class RootNotificationProbe : ObservableCollectionExtended<TaskWrapperViewModel>, IObservableCollection<TaskWrapperViewModel>
{
    internal sealed record Delivery(long Id, string Phase, int Depth, int ThreadDepth, bool UiThread,
        NotifyCollectionChangedAction Action, int NewIndex, int OldIndex, string[] NewIds, string[] OldIds, string[]? ResetIds);

    private readonly ThreadLocal<int> _threadDepth = new(() => 0);
    private readonly ThreadLocal<string[]?> _resetPayload = new(() => null);
    private int _suppressed;
    private string[] _replayStart = [];
    private string[]? _finalSnapshot;
    private int _depth;
    private int _firstOffUi;
    private long _id;
    private long _currentBatchId;
    private int _firstOffUiBatch;
    private readonly ConcurrentQueue<(long Id, bool UiThread)> _accepted = new();
    private readonly ConcurrentQueue<(long Id, bool UiThread)> _published = new();
    public ConcurrentQueue<Delivery> Events { get; } = new();

    public void TraceBatch(string phase, long id)
    {
        var ui = Dispatcher.UIThread.CheckAccess();
        (phase == "accepted" ? _accepted : _published).Enqueue((id, ui));
        if (phase == "published") Interlocked.Exchange(ref _currentBatchId, id);
        TestExecutionTrace.Write("emoji-batch", "root-projection", phase, details: new { batchId = id, uiThread = ui });
        if (phase == "accepted" && !ui && Interlocked.Exchange(ref _firstOffUiBatch, 1) == 0)
            TestExecutionTrace.Write("emoji-batch", "first-offUI-producer", "observed", details: new { batchId = id, stack = Environment.StackTrace });
    }

    // Freeze the caller's Load payload, not the live destination. This provides the
    // Reset checkpoint even on RED without an unsafe owner-thread enumeration.
    void IObservableCollection<TaskWrapperViewModel>.Load(IEnumerable<TaskWrapperViewModel> items)
    {
        var frozen = items.ToArray();
        var previous = _resetPayload.Value;
        _resetPayload.Value = frozen.Select(item => item.TaskItem.Id).ToArray();
        try { base.Load(frozen); }
        finally { if (Volatile.Read(ref _suppressed) == 0) _resetPayload.Value = previous; }
    }

    IDisposable INotifyCollectionChangedSuspender.SuspendNotifications()
    {
        var previous = _resetPayload.Value;
        var scope = base.SuspendNotifications();
        Volatile.Write(ref _suppressed, 1);
        return Disposable.Create(() =>
        {
            Volatile.Write(ref _suppressed, 0);
            try { scope.Dispose(); }
            finally { _resetPayload.Value = previous; }
        });
    }

    protected override void ClearItems()
    {
        var previous = _resetPayload.Value;
        _resetPayload.Value = [];
        try { base.ClearItems(); }
        finally { _resetPayload.Value = previous; }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs change)
    {
        // CollectionExtended calls this override during Load too; those inner events
        // are suppressed and do not reach consumers. Only wrap actual multicast delivery.
        if (Volatile.Read(ref _suppressed) != 0)
        {
            base.OnCollectionChanged(change);
            return;
        }
        var id = Interlocked.Increment(ref _id);
        var depth = Interlocked.Increment(ref _depth);
        var threadDepth = ++_threadDepth.Value;
        var ui = Dispatcher.UIThread.CheckAccess();
        var newIds = Ids(change.NewItems);
        var oldIds = Ids(change.OldItems);
        var resetIds = change.Action == NotifyCollectionChangedAction.Reset ? _resetPayload.Value : null;
        if (!ui && Interlocked.Exchange(ref _firstOffUi, 1) == 0)
            TestExecutionTrace.Write("emoji-notify", "first-offUI", "observed", details: new { id, stack = Environment.StackTrace });
        Record("begin");
        try { base.OnCollectionChanged(change); }
        finally
        {
            Record("end");
            --_threadDepth.Value;
            Interlocked.Decrement(ref _depth);
        }

        void Record(string phase)
        {
            var delivery = new Delivery(id, phase, depth, threadDepth, ui, change.Action,
                change.NewStartingIndex, change.OldStartingIndex, newIds, oldIds, resetIds);
            Events.Enqueue(delivery);
            TestExecutionTrace.Write("emoji-notify", "CurrentAllTasksItems", phase,
                details: new { delivery, batchId = Interlocked.Read(ref _currentBatchId), resetSnapshot = resetIds == null ? "unknown" : "frozen-load-payload" });
        }
    }

    public void ClearEvents()
    {
        _replayStart = _finalSnapshot ?? [];
        Events.Clear();
    }

    public void RecordQuiescentSnapshot(IEnumerable<TaskWrapperViewModel> items)
    {
        _finalSnapshot = items.Select(item => item.TaskItem.Id).ToArray();
        TestExecutionTrace.Write("emoji-snapshot", "CurrentAllTasksItems", "quiescent",
            details: new { lastNotificationId = Interlocked.Read(ref _id), ids = _finalSnapshot });
    }

    public async Task AssertUiDeliveryAsync()
    {
        var events = Events.ToArray();
        await Assert.That(events.All(item => item.UiThread)).IsTrue();
        await Assert.That(events.All(item => item.Depth == 1 && item.ThreadDepth == 1)).IsTrue();
        await Assert.That(events.GroupBy(item => item.Id).All(group => group.Select(item => item.Phase).SequenceEqual(new[] { "begin", "end" }))).IsTrue();
        await Assert.That(Volatile.Read(ref _depth)).IsEqualTo(0);
        await Assert.That(_accepted.Select(batch => batch.Id).SequenceEqual(_published.Select(batch => batch.Id))).IsTrue();
        await Assert.That(_published.All(batch => batch.UiThread)).IsTrue();
        List<string> replay = new(_replayStart);
        foreach (var change in events.Where(item => item.Phase == "begin"))
        {
            if (change.Action == NotifyCollectionChangedAction.Reset)
            {
                // Candidate validation requires every Reset checkpoint: no unchecked gaps.
                await Assert.That(change.ResetIds).IsNotNull();
                replay = new(change.ResetIds!);
                continue;
            }
            if (change.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Move)
            {
                await Assert.That(change.OldIndex >= 0 && change.OldIndex + change.OldIds.Length <= replay.Count).IsTrue();
                await Assert.That(replay.Skip(change.OldIndex).Take(change.OldIds.Length).SequenceEqual(change.OldIds)).IsTrue();
                replay.RemoveRange(change.OldIndex, change.OldIds.Length);
            }
            if (change.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Move)
            {
                await Assert.That(change.NewIndex >= 0 && change.NewIndex <= replay.Count).IsTrue();
                replay.InsertRange(change.NewIndex, change.NewIds);
            }
        }
        await Assert.That(_finalSnapshot).IsNotNull();
        await Assert.That(replay.SequenceEqual(_finalSnapshot!)).IsTrue();
    }

    private static string[] Ids(IList? items) => items?.Cast<TaskWrapperViewModel>().Select(item => item.TaskItem.Id).ToArray() ?? [];
}
