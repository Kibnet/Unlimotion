using AppAutomation.Avalonia.Headless.Session;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Tests;

[NotInParallel("DesktopUi")]
public sealed class HeadlessSessionStorageLifecycleTests
{
    private const int DelayedWatcherCycleCount = 8;
    private static readonly TimeSpan EventDeliveryTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ThrottleBoundary = TimeSpan.FromMilliseconds(1_500);

    [Test]
    public async Task DelayedWatcherEvent_AfterDispose_DoesNotCrashHost()
    {
        var currentTaskId = UnlimotionAppLaunchHost.GetCurrentTaskId(
            UnlimotionAutomationScenario.ReadmeDemo,
            "en");

        for (var cycle = 0; cycle < DelayedWatcherCycleCount; cycle++)
        {
            StorageCapture? captured = null;
            var options = UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                UnlimotionAutomationScenario.ReadmeDemo,
                language: "en",
                afterViewModelPrepared: vm => captured = CaptureStorage(vm));
            var session = DesktopAppSession.Launch(options);
            var state = RequireCapture(captured);
            var taskFilePath = Path.Combine(state.FileStorage.Path, currentTaskId);
            if (!File.Exists(taskFilePath))
            {
                throw new InvalidOperationException($"Seeded task file '{taskFilePath}' was not found.");
            }

            var updateObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<TaskStorageUpdateEventArgs> updateProbe = (_, args) =>
            {
                if (args.Type == UpdateType.Removed &&
                    string.Equals(args.Id, currentTaskId, StringComparison.Ordinal))
                {
                    updateObserved.TrySetResult(true);
                }
            };

            state.FileStorage.Updating += updateProbe;
            try
            {
                File.AppendAllText(taskFilePath, Environment.NewLine);
                File.Delete(taskFilePath);

                session.Dispose();

                await Task.Delay(TimeSpan.FromMilliseconds(25));
                state.Watcher.ForceUpdateFile(currentTaskId, UpdateType.Removed);
                await updateObserved.Task.WaitAsync(EventDeliveryTimeout);
            }
            finally
            {
                state.FileStorage.Updating -= updateProbe;
                session.Dispose();
            }
        }

        await Task.Delay(ThrottleBoundary);

        using var controlSession = LaunchControlSession(out var controlViewModelPrepared);
        await Assert.That(controlViewModelPrepared).IsTrue();
    }

    [Test]
    public async Task LaunchFailure_AfterStorageCreation_PreservesPrimaryExceptionAndCleansSession()
    {
        var previousDefaultIsExpanded = TaskWrapperViewModel.DefaultIsExpanded;
        TaskWrapperViewModel.DefaultIsExpanded = false;
        var sentinel = new InvalidOperationException("Headless lifecycle sentinel.");
        StorageCapture? captured = null;
        DesktopAppSession? unexpectedSession = null;
        Exception? observedException = null;

        try
        {
            try
            {
                unexpectedSession = DesktopAppSession.Launch(
                    UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                        UnlimotionAutomationScenario.ReadmeDemo,
                        language: "en",
                        afterViewModelPrepared: vm =>
                        {
                            captured = CaptureStorage(vm);
                            throw sentinel;
                        }));
            }
            catch (Exception exception)
            {
                observedException = exception;
            }
            finally
            {
                unexpectedSession?.Dispose();
            }

            var state = RequireCapture(captured);
            using (Assert.Multiple())
            {
                await Assert.That(observedException).IsSameReferenceAs(sentinel);
                await Assert.That(TaskWrapperViewModel.DefaultIsExpanded).IsFalse();
                await Assert.That(Directory.Exists(state.RootPath)).IsFalse();
            }

            await AssertStorageDisposedAsync(state.UnifiedStorage);

            using var controlSession = LaunchControlSession(out var controlViewModelPrepared);
            await Assert.That(controlViewModelPrepared).IsTrue();
        }
        finally
        {
            TaskWrapperViewModel.DefaultIsExpanded = previousDefaultIsExpanded;
            BestEffortDelete(captured?.RootPath);
        }
    }

    [Test]
    public async Task DisposeCallback_CalledTwice_IsIdempotentAndAllowsControlSession()
    {
        StorageCapture? captured = null;
        var options = UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            afterViewModelPrepared: vm => captured = CaptureStorage(vm));
        var session = DesktopAppSession.Launch(options);
        var state = RequireCapture(captured);
        var disposeCallback = options.DisposeCallback
            ?? throw new InvalidOperationException("Headless launch options did not expose a dispose callback.");

        var firstCleanupFailure = CaptureFailure(disposeCallback);
        var secondCleanupFailure = CaptureFailure(disposeCallback);
        var sessionDisposeFailure = CaptureFailure(session.Dispose);

        try
        {
            using (Assert.Multiple())
            {
                await Assert.That(firstCleanupFailure).IsNull();
                await Assert.That(secondCleanupFailure).IsNull();
                await Assert.That(sessionDisposeFailure).IsNull();
                await Assert.That(Directory.Exists(state.RootPath)).IsFalse();
            }

            await AssertStorageDisposedAsync(state.UnifiedStorage);

            using var controlSession = LaunchControlSession(out var controlViewModelPrepared);
            await Assert.That(controlViewModelPrepared).IsTrue();
        }
        finally
        {
            BestEffortDelete(state.RootPath);
        }
    }

    private static StorageCapture CaptureStorage(MainWindowViewModel viewModel)
    {
        var unifiedStorage = viewModel.taskRepository as UnifiedTaskStorage
            ?? throw new InvalidOperationException("Headless ViewModel did not expose UnifiedTaskStorage.");
        var fileStorage = unifiedStorage.TaskTreeManager.Storage as FileStorage
            ?? throw new InvalidOperationException("Headless task repository did not use FileStorage.");
        var watcher = fileStorage.Watcher
            ?? throw new InvalidOperationException("Headless FileStorage did not enable its watcher.");
        var rootPath = Path.GetDirectoryName(fileStorage.Path)
            ?? throw new InvalidOperationException($"Unable to resolve launch root for '{fileStorage.Path}'.");

        return new StorageCapture(unifiedStorage, fileStorage, watcher, rootPath);
    }

    private static StorageCapture RequireCapture(StorageCapture? capture)
    {
        return capture ?? throw new InvalidOperationException("Headless storage was not captured.");
    }

    private static DesktopAppSession LaunchControlSession(out bool viewModelPrepared)
    {
        var prepared = false;
        var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                afterViewModelPrepared: _ => prepared = true));
        viewModelPrepared = prepared;
        return session;
    }

    private static async Task AssertStorageDisposedAsync(UnifiedTaskStorage storage)
    {
        await Assert.That(
                async () => await storage.TryUnarchiveAsync("lifecycle-dispose-probe"))
            .Throws<ObjectDisposedException>();
    }

    private static Exception? CaptureFailure(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void BestEffortDelete(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        try
        {
            Directory.Delete(rootPath, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the lifecycle assertion that already failed.
        }
    }

    private sealed record StorageCapture(
        UnifiedTaskStorage UnifiedStorage,
        FileStorage FileStorage,
        IDatabaseWatcher Watcher,
        string RootPath);
}
