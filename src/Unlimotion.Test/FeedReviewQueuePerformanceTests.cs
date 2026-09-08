using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedReviewQueuePerformanceTests
{
    [Test]
    public async Task Feed_ReviewQueueBuild_KeepsDispatcherResponsive()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        using var directory = new FeedReviewQueueTempDirectory();
        directory.WriteDaily(
            new DateOnly(2026, 8, 24),
            "## Работа <!-- unlimotion-area:work -->\n- [ ] Проверить отзывчивость\n\nКонтекст\n");

        using var viewModel = new FeedViewModel(
            () => new DateOnly(2026, 8, 24),
            reviewDeviceId: "review-queue-performance-device");
        var buildStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildRanOnDispatcher = false;
        viewModel.ReviewQueueBuildGateAsync = async cancellationToken =>
        {
            buildRanOnDispatcher = Dispatcher.UIThread.CheckAccess();
            buildStarted.TrySetResult(true);
            if (buildRanOnDispatcher)
            {
                return;
            }

            await releaseBuild.Task.WaitAsync(cancellationToken);
        };

        var initializeTask = session.DispatchAsync(
            () => viewModel.InitializeVaultAsync(directory.Path),
            CancellationToken.None);
        try
        {
            await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var dispatcherCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => dispatcherCallback.TrySetResult());

            await dispatcherCallback.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Assert.That(buildRanOnDispatcher).IsFalse();
            await Assert.That(initializeTask.IsCompleted).IsFalse();
        }
        finally
        {
            releaseBuild.TrySetResult();
            await initializeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await Assert.That(viewModel.PendingReviewBlocks).IsEqualTo(1);
    }

    [Test]
    public async Task Feed_ReviewQueueBuild_DiscardsCanceledVaultGeneration()
    {
        using var firstDirectory = new FeedReviewQueueTempDirectory();
        using var secondDirectory = new FeedReviewQueueTempDirectory();
        firstDirectory.WriteDaily(
            new DateOnly(2026, 8, 23),
            "- [ ] Кандидат из старого vault\n");
        secondDirectory.WriteDaily(
            new DateOnly(2026, 8, 24),
            "- [ ] Первый кандидат нового vault\n- [ ] Второй кандидат нового vault\n");

        using var viewModel = new FeedViewModel(
            () => new DateOnly(2026, 8, 24),
            reviewDeviceId: "review-queue-version-device");
        viewModel.SetNotificationDispatcher(static action => action());
        var firstBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstBuildCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildNumber = 0;
        viewModel.ReviewQueueBuildGateAsync = async cancellationToken =>
        {
            if (Interlocked.Increment(ref buildNumber) != 1)
            {
                return;
            }

            firstBuildStarted.TrySetResult();
            try
            {
                await releaseFirstBuild.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                firstBuildCanceled.TrySetResult();
                throw;
            }
        };

        var firstInitialize = Task.Run(() => viewModel.InitializeVaultAsync(firstDirectory.Path));
        Task? secondInitialize = null;
        try
        {
            await firstBuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            secondInitialize = viewModel.InitializeVaultAsync(secondDirectory.Path);

            await secondInitialize.WaitAsync(TimeSpan.FromSeconds(10));
            await firstInitialize.WaitAsync(TimeSpan.FromSeconds(10));
            await firstBuildCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseFirstBuild.TrySetResult();
            if (secondInitialize is not null)
            {
                await secondInitialize.WaitAsync(TimeSpan.FromSeconds(10));
            }

            await firstInitialize.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await Assert.That(viewModel.VaultRootPath).IsEqualTo(secondDirectory.Path);
        await Assert.That(viewModel.PendingReviewBlocks).IsEqualTo(2);
        await Assert.That(viewModel.Days).HasSingleItem();
        await Assert.That(viewModel.Days[0].Date).IsEqualTo(new DateOnly(2026, 8, 24));
    }

    private sealed class FeedReviewQueueTempDirectory : IDisposable
    {
        public FeedReviewQueueTempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unlimotion-feed-review-queue-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteDaily(DateOnly date, string text)
        {
            var dailyDirectory = System.IO.Path.Combine(Path, "Ежедневные");
            Directory.CreateDirectory(dailyDirectory);
            File.WriteAllText(System.IO.Path.Combine(dailyDirectory, $"{date:yyyy-MM-dd}.md"), text);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
