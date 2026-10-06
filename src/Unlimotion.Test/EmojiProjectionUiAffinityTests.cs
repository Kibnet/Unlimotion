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
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class EmojiProjectionUiAffinityTests
{
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
            vm.EmojiSearchRefreshScheduler = timer;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            TestHelpers.GetTask(vm, MainWindowViewModelFixture.BlockedTask6Id).Title = "❌ Gamma nested target";
            vm.EmojiFilters.Single(filter => filter.Emoji == "❌").ShowTasks = true;
            timer.RunAll();
            vm.Search.SearchText = "target";
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
            await vm.Connect();
            // Initial projection must already be available, before a dispatcher pump.
            await Assert.That(vm.CurrentAllTasksItems.Count).IsGreaterThan(0);
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(item => item.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            var first = vm.EmojiFilters.Single(filter => filter.Emoji == "❌");
            var second = vm.EmojiFilters.Single(filter => filter.Emoji == "🚀");
            first.ShowTasks = true;
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
    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
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
internal sealed class RootNotificationProbe : ObservableCollectionExtended<TaskWrapperViewModel>
{
    internal sealed record Delivery(long Id, string Phase, int Depth, int ThreadDepth, bool UiThread,
        NotifyCollectionChangedAction Action, int NewIndex, int OldIndex, string[] NewIds, string[] OldIds);

    private readonly ThreadLocal<int> _threadDepth = new(() => 0);
    private int _depth;
    private int _firstOffUi;
    private long _id;
    public ConcurrentQueue<Delivery> Events { get; } = new();

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs change)
    {
        var id = Interlocked.Increment(ref _id);
        var depth = Interlocked.Increment(ref _depth);
        var threadDepth = ++_threadDepth.Value;
        var ui = Dispatcher.UIThread.CheckAccess();
        var newIds = Ids(change.NewItems);
        var oldIds = Ids(change.OldItems);
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
                change.NewStartingIndex, change.OldStartingIndex, newIds, oldIds);
            Events.Enqueue(delivery);
            TestExecutionTrace.Write("emoji-notify", "CurrentAllTasksItems", phase,
                details: new { delivery, resetSnapshot = "unknown-until-quiescence" });
        }
    }

    public void ClearEvents() => Events.Clear();

    public void RecordQuiescentSnapshot(IEnumerable<TaskWrapperViewModel> items) =>
        TestExecutionTrace.Write("emoji-snapshot", "CurrentAllTasksItems", "quiescent",
            details: new { lastNotificationId = Interlocked.Read(ref _id), ids = items.Select(item => item.TaskItem.Id).ToArray() });

    public async Task AssertUiDeliveryAsync()
    {
        var events = Events.ToArray();
        await Assert.That(events.All(item => item.UiThread)).IsTrue();
        await Assert.That(events.All(item => item.Depth == 1 && item.ThreadDepth == 1)).IsTrue();
        await Assert.That(events.GroupBy(item => item.Id).All(group => group.Select(item => item.Phase).SequenceEqual(new[] { "begin", "end" }))).IsTrue();
        await Assert.That(Volatile.Read(ref _depth)).IsEqualTo(0);
    }

    private static string[] Ids(IList? items) => items?.Cast<TaskWrapperViewModel>().Select(item => item.TaskItem.Id).ToArray() ?? [];
}
