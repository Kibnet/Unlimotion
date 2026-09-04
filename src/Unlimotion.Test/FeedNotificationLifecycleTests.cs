using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Notes.Recovery;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedNotificationLifecycleTests
{
    [Test]
    public async Task EditorDispose_PersistsDraftWithoutWaitingForBlockedUiPublication()
    {
        var context = new HeldContext();
        var store = new DelayedDraftStore();
        var closing = Task.Run(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var editor = new MarkdownLivePreviewEditorViewModel();
                editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Not committed during close"));
                editor.ConfigureDraftPersistence("vault", store);
                editor.Load(new MarkdownLiveDocumentSnapshot("До\n", "revision", false, "note.md"));
                editor.BeginEdit(editor.Blocks.Single());
                editor.ActiveBlock!.EditorText = "Сохранить при закрытии";
                editor.Dispose();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
        var completedWithoutUi = false;
        try
        {
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            store.Release.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(2));
            completedWithoutUi = true;
        }
        catch (TimeoutException) { }
        finally
        {
            store.Release.TrySetResult();
            context.Release(); // watchdog: a regression must fail, not strand the test process.
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await Assert.That(completedWithoutUi).IsTrue();
        await Assert.That(store.Saved!.RawMarkdown).IsEqualTo("Сохранить при закрытии");
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
        private readonly object sync = new();
        private bool released;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (sync)
            {
                if (!released) { queue.Enqueue((callback, state)); return; }
            }
            callback(state);
        }
        public void Release()
        {
            lock (sync) released = true;
            while (queue.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    private sealed class DelayedDraftStore : IFeedDraftStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FeedDraft? Saved { get; private set; }
        public async Task SaveAsync(FeedDraft draft, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            Saved = draft;
        }
        public Task<FeedDraft?> LoadAsync(string vaultId, string relativePath, int blockIndex, CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task<IReadOnlyList<FeedDraft>> ListAsync(string vaultId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FeedDraft>>(Saved is null ? [] : [Saved]);
        public Task DeleteAsync(string vaultId, string relativePath, int blockIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_StopsReviewRefreshLoopWhoseUiPublicationWasQueued()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var feed = new FeedViewModel();
            feed.SetNotificationDispatcher(_ => { });
            var refresh = typeof(FeedViewModel).GetMethod("RefreshReviewSummaryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)refresh.Invoke(feed, [CancellationToken.None])!;
            await Assert.That(pending.IsCompleted).IsFalse();
            feed.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }, CancellationToken.None);
    }

    [Test]
    public async Task Dispose_CompletesQueuedNotificationWithoutApplyingIt()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var feed = new FeedViewModel();
            Action? queued = null;
            var calls = 0;
            feed.SetNotificationDispatcher(action => queued = action);
            var method = typeof(FeedViewModel).GetMethod("DispatchNotificationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)method.Invoke(feed, [(Action)(() => calls++)])!;
            await Assert.That(pending.IsCompleted).IsFalse();
            feed.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            queued!();
            await Assert.That(calls).IsEqualTo(0);
        }, CancellationToken.None);
    }
}
