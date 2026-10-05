using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.UiTests.Headless.Infrastructure;

namespace Unlimotion.UiTests.Headless.Tests;

[NotInParallel("DesktopUi")]
public sealed class HeadlessSessionFactoryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Dispatch_UsesPublishedWorker_AndDisposeJoinsIt()
    {
        for (var cycle = 0; cycle < 8; cycle++)
        {
            var session = HeadlessSessionFactory.StartNew(typeof(Application));
            var worker = Get<Task>(session, "_dispatchTask");
            var cancellation = Get<CancellationTokenSource>(session, "_cancellationTokenSource");
            try
            {
                var currentTask = session.Dispatch(() =>
                {
                    Dispatcher.UIThread.VerifyAccess();
                    return Task.CurrentId;
                }, default).WaitAsync(Timeout).GetAwaiter().GetResult();
                await Assert.That(currentTask).IsEqualTo(worker.Id);
            }
            finally { await session.DisposeAsync().AsTask().WaitAsync(Timeout); }
            await Assert.That(worker.IsCompletedSuccessfully).IsTrue();
            await Assert.That(() => cancellation.Token).Throws<ObjectDisposedException>();
        }
    }

    [Test]
    public async Task Dispose_WaitsForExecutingAction_AndPreservesExecutionContext()
    {
        var session = HeadlessSessionFactory.StartNew(typeof(Application));
        var worker = Get<Task>(session, "_dispatchTask");
        var queue = Get<BlockingCollection<(Action, ExecutionContext?)>>(session, "_queue");
        var entered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var marker = new AsyncLocal<string?> { Value = "captured" };
        queue.Add((() =>
        {
            entered.SetResult(marker.Value);
            if (!release.Wait(Timeout)) throw new TimeoutException("Worker action was not released.");
        }, ExecutionContext.Capture()));
        marker.Value = "caller";
        Task? disposal = null;
        try
        {
            await Assert.That(await entered.Task.WaitAsync(Timeout)).IsEqualTo("captured");
            disposal = session.DisposeAsync().AsTask();
            await Assert.That(disposal.IsCompleted).IsFalse();
        }
        finally
        {
            release.Set();
            await (disposal ?? session.DisposeAsync().AsTask()).WaitAsync(Timeout);
        }
        await Assert.That(worker.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task Dispose_PropagatesWorkerNullReference_InsteadOfSuppressingIt()
    {
        var session = HeadlessSessionFactory.StartNew(typeof(Application));
        var worker = Get<Task>(session, "_dispatchTask");
        var cancellation = Get<CancellationTokenSource>(session, "_cancellationTokenSource");
        var queue = Get<BlockingCollection<(Action, ExecutionContext?)>>(session, "_queue");
        var sentinel = new NullReferenceException("Worker fault negative control.");
        queue.Add((() => throw sentinel, null));
        try
        {
            Exception? observed = null;
            try { await worker.WaitAsync(Timeout); }
            catch (NullReferenceException exception) { observed = exception; }
            await Assert.That(observed).IsSameReferenceAs(sentinel);
            observed = null;
            try { await session.DisposeAsync().AsTask().WaitAsync(Timeout); }
            catch (NullReferenceException exception) { observed = exception; }
            await Assert.That(observed).IsSameReferenceAs(sentinel);
        }
        finally
        {
            // The deliberate worker fault makes framework disposal propagate before
            // CTS disposal. Only this negative control owns the residual resources.
            await cancellation.CancelAsync();
            queue.CompleteAdding();
            cancellation.Dispose();
            queue.Dispose();
        }
    }

    private static T Get<T>(HeadlessUnitTestSession session, string name) where T : class =>
        typeof(HeadlessUnitTestSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(session) as T
        ?? throw new NotSupportedException($"Pinned Headless field '{name}' is unavailable.");
}
