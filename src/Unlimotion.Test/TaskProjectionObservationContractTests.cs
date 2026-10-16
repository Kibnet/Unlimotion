#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
public class TaskProjectionObservationContractTests
{
    [Test]
    public async Task Emoji_NonNullChangesStillRefreshTheSameBooleanResult()
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalEmoji(source).Subscribe(values.Add);
        source.Emoji = "B"; source.Raise(nameof(Probe.Emoji));
        source.Emoji = null; source.Raise(nameof(Probe.Emoji));
        source.Emoji = ""; source.Raise(nameof(Probe.Emoji));
        source.Raise(nameof(Probe.Emoji));
        source.Raise("Unrelated");
        await Assert.That(values.SequenceEqual(new[] { false, false, true, false })).IsTrue();
    }

    [Test]
    public async Task Root_DateAndStatusChangesRefreshEvenWhenFinalBooleanMatches()
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalRoot(source).Subscribe(values.Add);
        source.UnlockedDateTime = DateTimeOffset.UnixEpoch; source.Raise(nameof(Probe.UnlockedDateTime));
        source.Status = DomainTaskStatus.Archived; source.Raise(nameof(Probe.Status));
        source.IsCanBeCompleted = false; source.Raise(nameof(Probe.IsCanBeCompleted));
        source.Status = DomainTaskStatus.Prepared; source.Raise(nameof(Probe.Status));
        source.IsCanBeCompleted = true; source.Raise(nameof(Probe.IsCanBeCompleted));
        source.Raise(nameof(Probe.Status));
        source.Raise("Unrelated");
        Console.WriteLine("root=" + string.Join(",", values));
        await Assert.That(values.SequenceEqual(new[] { true, true, false, false, false, true })).IsTrue();
    }

    [Test]
    public async Task Root_SilentOtherFieldWaitsForItsOwnNotification()
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalRoot(source).Subscribe(values.Add);
        source.Status = DomainTaskStatus.Archived;
        source.UnlockedDateTime = DateTimeOffset.UnixEpoch;
        source.Raise(nameof(Probe.UnlockedDateTime));
        source.Raise(nameof(Probe.Status));
        Console.WriteLine("delayed=" + string.Join(",", values));
        await Assert.That(values.SequenceEqual(new[] { true, true, false })).IsTrue();
    }

    [Test]
    [Arguments((string?)null)]
    [Arguments("")]
    public async Task Root_BroadNotificationRecomputesChangedProperties(string? propertyName)
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalRoot(source).Subscribe(values.Add);
        source.Status = DomainTaskStatus.Archived;
        source.IsCanBeCompleted = false;
        source.Raise(propertyName);
        Console.WriteLine("broad=" + string.Join(",", values));
        await Assert.That(values.SequenceEqual(new[] { true, false, false })).IsTrue();
    }

    [Test]
    public async Task Root_ReentrantMutationKeepsSynchronousDelivery()
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalRoot(source).Subscribe(value =>
        {
            values.Add(value);
            if (values.Count != 1) return;
            source.Status = DomainTaskStatus.Archived;
            source.Raise(nameof(Probe.Status));
        });
        await Assert.That(values.SequenceEqual(new[] { true, false })).IsTrue();
    }

    [Test]
    public async Task Emoji_InitialDeliveryPrecedesEventAttachment()
    {
        var source = new Probe();
        var values = new List<bool>();
        using var subscription = OriginalEmoji(source).Subscribe(value =>
        {
            values.Add(value);
            if (values.Count != 1) return;
            source.Emoji = "B";
            source.Raise(nameof(Probe.Emoji));
        });
        await Assert.That(values.SequenceEqual(new[] { false })).IsTrue();
    }

    [Test]
    public async Task Dispose_DetachesAndNewSubscriptionReadsCurrentValue()
    {
        var source = new Probe();
        var oldValues = new List<bool>();
        var subscription = OriginalEmoji(source).Subscribe(oldValues.Add);
        subscription.Dispose();
        source.Emoji = null; source.Raise(nameof(Probe.Emoji));
        var newValues = new List<bool>();
        using (OriginalEmoji(source).Subscribe(newValues.Add)) { }
        await Assert.That(oldValues.SequenceEqual(new[] { false })).IsTrue();
        await Assert.That(newValues.SequenceEqual(new[] { true })).IsTrue();
        await Assert.That(source.HandlerCount).IsEqualTo(0);
    }

    [Test]
    public async Task WorkerNotification_IsDeliveredOnTheEmitterThread()
    {
        var source = new Probe();
        var threads = new List<int>();
        using var subscription = OriginalEmoji(source).Subscribe(_ => threads.Add(Environment.CurrentManagedThreadId));
        var emitter = await Task.Run(() =>
        {
            source.Emoji = "B"; source.Raise(nameof(Probe.Emoji));
            return Environment.CurrentManagedThreadId;
        });
        await Assert.That(threads.Count).IsEqualTo(2);
        await Assert.That(threads[1]).IsEqualTo(emitter);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetterFailure_IsObservableAndDoesNotKeepHandlers(bool afterInitial)
    {
        var source = new Probe { ThrowOnRead = !afterInitial };
        var trace = new List<string>();
        Exception? synchronousFailure = null;
        IDisposable? subscription = null;
        try
        {
            try
            {
                subscription = OriginalEmoji(source).Subscribe(value => trace.Add("value:" + value), ex =>
                {
                    // The expression observer wraps getter failures through reflection.
                    var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    trace.Add("error:" + cause.GetType().Name + ":" + cause.Message);
                });
                if (afterInitial)
                {
                    source.ThrowOnRead = true;
                    source.Raise(nameof(Probe.Emoji));
                }
            }
            catch (Exception ex) { synchronousFailure = ex; }
            Console.WriteLine(string.Join("\n", trace));
            await Assert.That(synchronousFailure).IsNull();
            var expected = afterInitial
                ? new[] { "value:False", "error:InvalidOperationException:getter fault" }
                : new[] { "error:InvalidOperationException:getter fault" };
            await Assert.That(trace.SequenceEqual(expected)).IsTrue();
            // Check automatic termination before explicit cleanup can hide a leaked handler.
            await Assert.That(source.HandlerCount).IsEqualTo(0);
        }
        finally { subscription?.Dispose(); }
    }

    [Test]
    public async Task ReentrantDispose_StopsLaterEvents()
    {
        var source = new Probe();
        var values = new List<bool>();
        IDisposable? subscription = null;
        try
        {
            subscription = OriginalEmoji(source).Subscribe(value =>
            {
                values.Add(value);
                if (values.Count == 2) subscription!.Dispose();
            });
            source.Emoji = "B"; source.Raise(nameof(Probe.Emoji));
            source.Emoji = null; source.Raise(nameof(Probe.Emoji));
            await Assert.That(values.SequenceEqual(new[] { false, false })).IsTrue();
            await Assert.That(source.HandlerCount).IsEqualTo(0);
        }
        finally { subscription?.Dispose(); }
    }

    [Test]
    public async Task ConcurrentDispose_DoesNotWaitForAnAlreadyEnteredConsumer()
    {
        var source = new Probe();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var subscription = OriginalEmoji(source).Subscribe(_ =>
        {
            if (Interlocked.Increment(ref calls) != 2) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Consumer was not released.");
        });
        var producer = Task.Run(() => { source.Emoji = "B"; source.Raise(nameof(Probe.Emoji)); });
        try
        {
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();
            // An already-entered callback may finish; completed Dispose stops subsequent events.
            await Task.Run(subscription.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(source.HandlerCount).IsEqualTo(0);
            source.Emoji = null; source.Raise(nameof(Probe.Emoji));
            release.Set();
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(calls).IsEqualTo(2);
        }
        finally
        {
            release.Set();
            subscription.Dispose();
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task ConsumerFailure_PropagatesAndAutomaticallyDetaches()
    {
        var source = new Probe();
        var calls = 0;
        var errors = 0;
        var failure = new InvalidOperationException("consumer fault");
        using var subscription = OriginalEmoji(source).Subscribe(_ =>
        {
            if (++calls == 2) throw failure;
        }, _ => errors++);
        Exception? caught = null;
        try { source.Emoji = "B"; source.Raise(nameof(Probe.Emoji)); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(ReferenceEquals(caught, failure)).IsTrue();
        await Assert.That(errors).IsEqualTo(0);
        await Assert.That(source.HandlerCount).IsEqualTo(0);
    }

    private static IObservable<bool> OriginalEmoji(Probe source) =>
        source.WhenAny(task => task.Emoji, change => change.Value == null);

    private static IObservable<bool> OriginalRoot(Probe source) =>
        source.WhenAny(task => task.IsCanBeCompleted, task => task.Status, task => task.UnlockedDateTime,
            (can, status, unlocked) => can.Value && status.Value != DomainTaskStatus.Archived);

    private sealed class Probe : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        private string? _emoji = "A";
        public int HandlerCount { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _changed += value; HandlerCount++; }
            remove { _changed -= value; HandlerCount--; }
        }
        public string? Emoji
        {
            get => ThrowOnRead ? throw new InvalidOperationException("getter fault") : _emoji;
            set => _emoji = value;
        }
        public bool ThrowOnRead { get; set; }
        public bool IsCanBeCompleted { get; set; } = true;
        public DomainTaskStatus Status { get; set; } = DomainTaskStatus.Prepared;
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public void Raise(string? propertyName) => _changed?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
