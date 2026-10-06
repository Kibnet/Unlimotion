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
using Avalonia.Threading;
using DynamicData.Binding;
using TUnit.Assertions.Exceptions;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class EmojiProjectionUiAffinityTests
{
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
