using System;
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
}
