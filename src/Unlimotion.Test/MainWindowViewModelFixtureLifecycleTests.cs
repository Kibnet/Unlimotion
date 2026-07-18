using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class MainWindowViewModelFixtureLifecycleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task CleanTasksAsync_ConcurrentCallersWaitForInFlightSaveBeforeDirectoryDeletion()
    {
        var fixture = new MainWindowViewModelFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandExceptions = new ConcurrentQueue<Exception>();
        ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit>? controlledCommand = null;
        IDisposable? commandExceptionSubscription = null;
        Task? firstCleanup = null;
        Task? secondCleanup = null;
        Task? disposeCleanup = null;
        Task? pendingSave = null;
        Exception? pendingSaveFailure = null;
        Exception? cleanupFailure = null;
        bool cleanupCompletedBeforeRelease = true;
        bool sameCleanTask = false;
        bool disposeJoinedSameOperation = false;
        bool tasksDirectoryExistedBeforeRelease = false;
        bool taskFileExistedBeforeRelease = false;
        bool pendingSaveWasIncomplete = false;
        bool tasksDirectoryExistsAfterCleanup = true;
        bool fixtureDirectoryExistsAfterCleanup = true;
        int invocationCountAfterSeal = 0;

        try
        {
            var mainWindow = fixture.MainWindowViewModelTest;
            await mainWindow.Connect();
            var repository = mainWindow.taskRepository
                ?? throw new InvalidOperationException("Task repository was not initialized.");
            var task = repository.Tasks.Items.Single(item => item.Id == MainWindowViewModelFixture.RootTask1Id);
            var invocationCount = 0;

            controlledCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult();
                await release.Task;
                await repository.Update(task);
            });
            commandExceptionSubscription = controlledCommand.ThrownExceptions.Subscribe(commandExceptions.Enqueue);
            task.SaveItemCommand = controlledCommand;

            task.Status = NextStatus(task.Status);
            await started.Task.WaitAsync(TestTimeout);
            pendingSave = await WaitForPendingSaveRegistrationAsync(task);
            pendingSaveWasIncomplete = !pendingSave.IsCompleted;

            firstCleanup = fixture.CleanTasksAsync();
            secondCleanup = fixture.CleanTasksAsync();
            disposeCleanup = fixture.DisposeAsync().AsTask();

            sameCleanTask = ReferenceEquals(firstCleanup, secondCleanup);
            disposeJoinedSameOperation = ReferenceEquals(firstCleanup, disposeCleanup);
            cleanupCompletedBeforeRelease = firstCleanup.IsCompleted;
            tasksDirectoryExistedBeforeRelease = Directory.Exists(fixture.DefaultTasksFolderPath);
            taskFileExistedBeforeRelease = File.Exists(Path.Combine(fixture.DefaultTasksFolderPath, task.Id));

            if (!cleanupCompletedBeforeRelease)
            {
                task.Status = NextStatus(task.Status);
            }

            invocationCountAfterSeal = Volatile.Read(ref invocationCount);
            release.TrySetResult();

            pendingSaveFailure = await ObserveFailureAsync(pendingSave);
            cleanupFailure = await ObserveFailureAsync(firstCleanup);
            await ObserveFailureAsync(secondCleanup);
            await ObserveFailureAsync(disposeCleanup);
            tasksDirectoryExistsAfterCleanup = Directory.Exists(fixture.DefaultTasksFolderPath);
            fixtureDirectoryExistsAfterCleanup = Directory.Exists(fixture.FixtureDirectoryPath);
        }
        finally
        {
            release.TrySetResult();
            if (pendingSave != null)
            {
                await ObserveFailureAsync(pendingSave);
            }

            var finalCleanup = firstCleanup ?? fixture.CleanTasksAsync();
            await ObserveFailureAsync(finalCleanup);
            if (secondCleanup != null)
            {
                await ObserveFailureAsync(secondCleanup);
            }

            if (disposeCleanup != null)
            {
                await ObserveFailureAsync(disposeCleanup);
            }

            commandExceptionSubscription?.Dispose();
            controlledCommand?.Dispose();
        }

        await Assert.That(cleanupCompletedBeforeRelease)
            .IsFalse()
            .Because("cleanup completed before controlled save release");
        await Assert.That(sameCleanTask).IsTrue();
        await Assert.That(disposeJoinedSameOperation).IsTrue();
        await Assert.That(tasksDirectoryExistedBeforeRelease).IsTrue();
        await Assert.That(taskFileExistedBeforeRelease).IsTrue();
        await Assert.That(pendingSaveWasIncomplete).IsTrue();
        await Assert.That(invocationCountAfterSeal).IsEqualTo(1);
        await Assert.That(pendingSaveFailure).IsNull();
        await Assert.That(cleanupFailure).IsNull();
        await Assert.That(commandExceptions).IsEmpty();
        await Assert.That(tasksDirectoryExistsAfterCleanup).IsFalse();
        await Assert.That(fixtureDirectoryExistsAfterCleanup).IsFalse();
    }

    [Test]
    public async Task CleanTasksAsync_RepeatedCallAfterSaveAndDeleteFailuresReturnsSameAggregate()
    {
        var fixture = new MainWindowViewModelFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSaveFailure = new InvalidOperationException("controlled save one failed");
        var secondSaveFailure = new InvalidOperationException("controlled save two failed");
        var injectedDeleteFailure = new IOException("controlled delete failed");
        var commandExceptions = new ConcurrentQueue<Exception>();
        ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit>? firstCommand = null;
        ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit>? secondCommand = null;
        IDisposable? firstExceptionSubscription = null;
        IDisposable? secondExceptionSubscription = null;
        Task? firstPendingSave = null;
        Task? secondPendingSave = null;
        Task? firstCleanup = null;
        Task? concurrentCleanup = null;
        Task? repeatedCleanup = null;
        Task? disposeCleanup = null;
        Exception? cleanupFailure = null;
        Exception? repeatedCleanupFailure = null;
        AggregateException? flattenedCleanupTaskException = null;
        Exception? residualDirectoryCleanupFailure = null;
        var fixtureDirectoryExistedAfterCleanupFailure = false;
        var deleteAttempts = 0;

        try
        {
            var mainWindow = fixture.MainWindowViewModelTest;
            await mainWindow.Connect();
            var repository = mainWindow.taskRepository
                ?? throw new InvalidOperationException("Task repository was not initialized.");
            var firstTask = repository.Tasks.Items.Single(item => item.Id == MainWindowViewModelFixture.RootTask1Id);
            var secondTask = repository.Tasks.Items.Single(item => item.Id == MainWindowViewModelFixture.RootTask2Id);

            firstCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                firstStarted.TrySetResult();
                await release.Task;
                throw firstSaveFailure;
            });
            secondCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                secondStarted.TrySetResult();
                await release.Task;
                throw secondSaveFailure;
            });
            firstExceptionSubscription = firstCommand.ThrownExceptions.Subscribe(commandExceptions.Enqueue);
            secondExceptionSubscription = secondCommand.ThrownExceptions.Subscribe(commandExceptions.Enqueue);
            firstTask.SaveItemCommand = firstCommand;
            secondTask.SaveItemCommand = secondCommand;

            firstTask.Status = NextStatus(firstTask.Status);
            secondTask.Status = NextStatus(secondTask.Status);
            await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TestTimeout);
            firstPendingSave = await WaitForPendingSaveRegistrationAsync(firstTask);
            secondPendingSave = await WaitForPendingSaveRegistrationAsync(secondTask);

            fixture.DeleteFailureInjector = (operation, path) =>
            {
                if (operation != "delete fixture directory" ||
                    !string.Equals(path, fixture.FixtureDirectoryPath, StringComparison.Ordinal))
                {
                    return null;
                }

                Interlocked.Increment(ref deleteAttempts);
                return injectedDeleteFailure;
            };

            firstCleanup = fixture.CleanTasksAsync();
            concurrentCleanup = fixture.CleanTasksAsync();
            release.TrySetResult();

            await ObserveFailureAsync(firstPendingSave);
            await ObserveFailureAsync(secondPendingSave);
            cleanupFailure = await ObserveFailureAsync(firstCleanup);
            await ObserveFailureAsync(concurrentCleanup);
            flattenedCleanupTaskException = firstCleanup.Exception?.Flatten();
            fixtureDirectoryExistedAfterCleanupFailure = Directory.Exists(fixture.FixtureDirectoryPath);

            repeatedCleanup = fixture.CleanTasksAsync();
            disposeCleanup = fixture.DisposeAsync().AsTask();
            repeatedCleanupFailure = await ObserveFailureAsync(repeatedCleanup);
            await ObserveFailureAsync(disposeCleanup);
        }
        finally
        {
            release.TrySetResult();
            if (firstPendingSave != null)
            {
                await ObserveFailureAsync(firstPendingSave);
            }

            if (secondPendingSave != null)
            {
                await ObserveFailureAsync(secondPendingSave);
            }

            var finalCleanup = firstCleanup ?? fixture.CleanTasksAsync();
            await ObserveFailureAsync(finalCleanup);
            if (concurrentCleanup != null)
            {
                await ObserveFailureAsync(concurrentCleanup);
            }

            if (repeatedCleanup != null)
            {
                await ObserveFailureAsync(repeatedCleanup);
            }

            if (disposeCleanup != null)
            {
                await ObserveFailureAsync(disposeCleanup);
            }

            firstExceptionSubscription?.Dispose();
            secondExceptionSubscription?.Dispose();
            firstCommand?.Dispose();
            secondCommand?.Dispose();

            fixture.DeleteFailureInjector = null;
            if (Directory.Exists(fixture.FixtureDirectoryPath))
            {
                try
                {
                    Directory.Delete(fixture.FixtureDirectoryPath, true);
                }
                catch (Exception exception)
                {
                    residualDirectoryCleanupFailure = exception;
                }
            }
        }

        await Assert.That(firstCleanup).IsSameReferenceAs(concurrentCleanup);
        await Assert.That(firstCleanup).IsSameReferenceAs(repeatedCleanup);
        await Assert.That(firstCleanup).IsSameReferenceAs(disposeCleanup);
        await Assert.That(repeatedCleanupFailure).IsSameReferenceAs(cleanupFailure);
        await Assert.That(deleteAttempts).IsEqualTo(3);
        await Assert.That(cleanupFailure).IsAssignableTo<AggregateException>();
        await Assert.That(flattenedCleanupTaskException).IsNotNull();
        await Assert.That(fixtureDirectoryExistedAfterCleanupFailure).IsTrue();

        var failures = flattenedCleanupTaskException!.InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(3);
        await Assert.That(failures.Count(exception => exception.Message == firstSaveFailure.Message)).IsEqualTo(1);
        await Assert.That(failures.Count(exception => exception.Message == secondSaveFailure.Message)).IsEqualTo(1);

        var deleteFailure = failures.Single(exception =>
            exception is IOException &&
            exception.Message.Contains("delete fixture directory", StringComparison.Ordinal) &&
            exception.Message.Contains(fixture.FixtureDirectoryPath, StringComparison.Ordinal));
        await Assert.That(deleteFailure.InnerException).IsSameReferenceAs(injectedDeleteFailure);
        await Assert.That(commandExceptions.All(exception =>
                ReferenceEquals(exception, firstSaveFailure) || ReferenceEquals(exception, secondSaveFailure)))
            .IsTrue();
        await Assert.That(residualDirectoryCleanupFailure).IsNull();
    }

    [Test]
    public async Task CleanTasksAsync_SnapshotBarrierFailurePreservesOwnedPaths()
    {
        var fixture = new MainWindowViewModelFixture();
        var barrierFailure = new InvalidOperationException("controlled snapshot barrier failed");
        Task? firstCleanup = null;
        Task? repeatedCleanup = null;
        Task? disposeCleanup = null;
        Exception? cleanupFailure = null;
        Exception? repeatedCleanupFailure = null;
        Exception? residualDirectoryCleanupFailure = null;
        var deleteAttempts = 0;
        var configExistedAfterFailure = false;
        var tasksDirectoryExistedAfterFailure = false;
        var fixtureDirectoryExistedAfterFailure = false;

        try
        {
            await fixture.MainWindowViewModelTest.Connect();
            fixture.TaskItemsSnapshotProvider = _ => throw barrierFailure;
            fixture.DeleteFailureInjector = (_, _) =>
            {
                Interlocked.Increment(ref deleteAttempts);
                return null;
            };

            firstCleanup = fixture.CleanTasksAsync();
            cleanupFailure = await ObserveFailureAsync(firstCleanup);

            repeatedCleanup = fixture.CleanTasksAsync();
            disposeCleanup = fixture.DisposeAsync().AsTask();
            repeatedCleanupFailure = await ObserveFailureAsync(repeatedCleanup);
            await ObserveFailureAsync(disposeCleanup);

            configExistedAfterFailure = File.Exists(fixture.ConfigPath);
            tasksDirectoryExistedAfterFailure = Directory.Exists(fixture.DefaultTasksFolderPath);
            fixtureDirectoryExistedAfterFailure = Directory.Exists(fixture.FixtureDirectoryPath);
        }
        finally
        {
            var finalCleanup = firstCleanup ?? fixture.CleanTasksAsync();
            await ObserveFailureAsync(finalCleanup);
            if (repeatedCleanup != null)
            {
                await ObserveFailureAsync(repeatedCleanup);
            }

            if (disposeCleanup != null)
            {
                await ObserveFailureAsync(disposeCleanup);
            }

            fixture.DeleteFailureInjector = null;
            if (Directory.Exists(fixture.FixtureDirectoryPath))
            {
                try
                {
                    Directory.Delete(fixture.FixtureDirectoryPath, true);
                }
                catch (Exception exception)
                {
                    residualDirectoryCleanupFailure = exception;
                }
            }
        }

        await Assert.That(firstCleanup).IsSameReferenceAs(repeatedCleanup);
        await Assert.That(firstCleanup).IsSameReferenceAs(disposeCleanup);
        await Assert.That(cleanupFailure).IsSameReferenceAs(barrierFailure);
        await Assert.That(repeatedCleanupFailure).IsSameReferenceAs(cleanupFailure);
        await Assert.That(deleteAttempts).IsEqualTo(0);
        await Assert.That(configExistedAfterFailure).IsTrue();
        await Assert.That(tasksDirectoryExistedAfterFailure).IsTrue();
        await Assert.That(fixtureDirectoryExistedAfterFailure).IsTrue();
        await Assert.That(residualDirectoryCleanupFailure).IsNull();
    }

    [Test]
    public async Task CleanTasksAsync_UnconnectedFixtureDeletesOwnedPathsOnce()
    {
        var fixture = new MainWindowViewModelFixture();
        Task? firstCleanup = null;
        Task? concurrentCleanup = null;
        Task? repeatedCleanup = null;
        Task? disposeCleanup = null;
        Exception? cleanupFailure = null;
        Exception? repeatedCleanupFailure = null;
        var deleteAttempts = 0;
        var repositoryWasNull = fixture.MainWindowViewModelTest.taskRepository == null;
        var configExistedBeforeCleanup = File.Exists(fixture.ConfigPath);
        var tasksDirectoryExistedBeforeCleanup = Directory.Exists(fixture.DefaultTasksFolderPath);
        var fixtureDirectoryExistedBeforeCleanup = Directory.Exists(fixture.FixtureDirectoryPath);
        var deleteAttemptsAfterFirstCleanup = 0;
        var deleteAttemptsAfterRepeatedCleanup = 0;

        fixture.DeleteFailureInjector = (_, _) =>
        {
            Interlocked.Increment(ref deleteAttempts);
            return null;
        };

        try
        {
            firstCleanup = fixture.CleanTasksAsync();
            concurrentCleanup = fixture.CleanTasksAsync();
            disposeCleanup = fixture.DisposeAsync().AsTask();
            cleanupFailure = await ObserveFailureAsync(firstCleanup);
            await ObserveFailureAsync(concurrentCleanup);
            await ObserveFailureAsync(disposeCleanup);
            deleteAttemptsAfterFirstCleanup = Volatile.Read(ref deleteAttempts);

            repeatedCleanup = fixture.CleanTasksAsync();
            repeatedCleanupFailure = await ObserveFailureAsync(repeatedCleanup);
            deleteAttemptsAfterRepeatedCleanup = Volatile.Read(ref deleteAttempts);
        }
        finally
        {
            var finalCleanup = firstCleanup ?? fixture.CleanTasksAsync();
            await ObserveFailureAsync(finalCleanup);
            if (concurrentCleanup != null)
            {
                await ObserveFailureAsync(concurrentCleanup);
            }

            if (repeatedCleanup != null)
            {
                await ObserveFailureAsync(repeatedCleanup);
            }

            if (disposeCleanup != null)
            {
                await ObserveFailureAsync(disposeCleanup);
            }
        }

        await Assert.That(repositoryWasNull).IsTrue();
        await Assert.That(configExistedBeforeCleanup).IsTrue();
        await Assert.That(tasksDirectoryExistedBeforeCleanup).IsTrue();
        await Assert.That(fixtureDirectoryExistedBeforeCleanup).IsTrue();
        await Assert.That(firstCleanup).IsSameReferenceAs(concurrentCleanup);
        await Assert.That(firstCleanup).IsSameReferenceAs(repeatedCleanup);
        await Assert.That(firstCleanup).IsSameReferenceAs(disposeCleanup);
        await Assert.That(cleanupFailure).IsNull();
        await Assert.That(repeatedCleanupFailure).IsNull();
        await Assert.That(deleteAttemptsAfterFirstCleanup).IsEqualTo(3);
        await Assert.That(deleteAttemptsAfterRepeatedCleanup).IsEqualTo(deleteAttemptsAfterFirstCleanup);
        await Assert.That(File.Exists(fixture.ConfigPath)).IsFalse();
        await Assert.That(Directory.Exists(fixture.DefaultTasksFolderPath)).IsFalse();
        await Assert.That(Directory.Exists(fixture.FixtureDirectoryPath)).IsFalse();
    }

    private static DomainTaskStatus NextStatus(DomainTaskStatus current) =>
        current == DomainTaskStatus.InProgress
            ? DomainTaskStatus.Prepared
            : DomainTaskStatus.InProgress;

    private static async Task<Task> WaitForPendingSaveRegistrationAsync(TaskItemViewModel task)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TestTimeout)
        {
            var pendingSave = task.WaitForPendingSavesAsync();
            if (!pendingSave.IsCompleted)
            {
                return pendingSave;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Pending save was not registered for task '{task.Id}'.");
    }

    private static async Task<Exception?> ObserveFailureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TestTimeout);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
