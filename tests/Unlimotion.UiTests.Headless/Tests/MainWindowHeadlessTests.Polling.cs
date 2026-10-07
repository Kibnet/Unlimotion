using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Threading;
using TUnit.Assertions;
using TUnit.Core;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed partial class MainWindowHeadlessTests
{
    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task FileOnlyPolling_PumpsFireAndForgetUiContinuation()
    {
        var directory = Path.GetDirectoryName(parityConfigurationPath)
            ?? throw new InvalidOperationException("The isolated fixture configuration is missing.");
        var file = Path.Combine(directory, $"ui-polling-{Guid.NewGuid():N}.txt");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? operation = null;
        var startedOnUi = false;
        var completedOnUi = false;
        var startingThread = 0;
        var completionThread = 0;

        async Task WriteAfterUiContinuationAsync()
        {
            await Task.Delay(75);
            // The initial Dispatch's exit RunJobs cannot finish this operation,
            // even when a slow first render took longer than the delay above.
            await release.Task;
            completedOnUi = Dispatcher.UIThread.CheckAccess();
            completionThread = Environment.CurrentManagedThreadId;
            File.WriteAllText(file, "UI continuation completed");
        }

        try
        {
            await HeadlessRuntime.Session.Dispatch(() =>
            {
                startedOnUi = Dispatcher.UIThread.CheckAccess();
                startingThread = Environment.CurrentManagedThreadId;
                operation = WriteAfterUiContinuationAsync();
                // Deliberately do not await the operation inside this Dispatch.
            }, CancellationToken.None).ContinueWith(task => task.GetAwaiter().GetResult(),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            release.SetResult(true);

            // No Dispatch/RunJobs in the value factory: this is the exact
            // filesystem-only polling used after confirmation in UX03/UX04.
            WaitUntil(() => File.Exists(file), timeout: TimeSpan.FromSeconds(5),
                timeoutMessage: "Filesystem-only polling starved the queued UI continuation.");

            await Assert.That(File.ReadAllText(file)).IsEqualTo("UI continuation completed");
            await Assert.That(startedOnUi).IsTrue();
            await Assert.That(completedOnUi).IsTrue();
            await Assert.That(completionThread).IsEqualTo(startingThread);
            await Assert.That(operation!.IsCompletedSuccessfully).IsTrue();
        }
        finally
        {
            release.TrySetResult(true);
            if (operation is not null)
                await RunParityUiAsync(async () => { await operation; return true; });
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
