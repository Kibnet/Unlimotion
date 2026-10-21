using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Unlimotion;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class SafeHeadlessUnitTestSessionTests
{
    [Test]
    public async Task DispatchAsync_WorkerFailureIsPropagatedInsteadOfHanging()
    {
        var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(App));
        var expected = new InvalidOperationException("Injected Headless worker failure");
        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actual = null;
        try
        {
            var queue = (BlockingCollection<(Action, ExecutionContext?)>)typeof(Avalonia.Headless.HeadlessUnitTestSession)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            queue.Add((() => { workerEntered.TrySetResult(); throw expected; }, null));
            await workerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await session.DispatchAsync(() => Task.CompletedTask, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception error) { actual = error; }
        }
        finally
        {
            try { await session.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expected)) { }
        }
        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Dispatch_AwaitsAsyncCallbackBeforeReturning()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchTask = session.Dispatch(async () =>
        {
            actionStarted.SetResult();
            await releaseAction.Task;
        }, CancellationToken.None);

        try
        {
            await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            await Assert.That(dispatchTask.IsCompleted).IsFalse();
        }
        finally
        {
            releaseAction.TrySetResult();
            await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task DispatchAsync_PreservesUiThreadAcrossAwait()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));

        await session.DispatchAsync(async () =>
        {
            await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();

            await Task.Delay(50);

            await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task Dispatch_GenericResultPreservesUiAffinityAndValue()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        var result = await session.Dispatch(async () =>
        {
            await Task.Yield();
            await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
            return 42;
        }, CancellationToken.None);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task DispatchAsync_PreservesCallbackExceptionAndCancellation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        var expected = new InvalidOperationException("Callback failure");
        Exception? actual = null;
        try { await session.DispatchAsync(() => Task.FromException(expected), CancellationToken.None); }
        catch (Exception error) { actual = error; }
        await Assert.That(actual).IsSameReferenceAs(expected);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = session.DispatchAsync(() => Task.FromCanceled(cancellation.Token), CancellationToken.None);
        try { await cancelled; }
        catch (OperationCanceledException) { }
        await Assert.That(cancelled.IsCanceled).IsTrue();
    }
}
