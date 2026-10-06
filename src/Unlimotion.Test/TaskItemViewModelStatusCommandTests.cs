using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using ReactiveUI;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskItemViewModelStatusCommandTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TaskNotFoundDuringStatusCommand_BlocksDirtyEditorDrainAndLifecycleSave(bool sealDuringCommand)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("status-missing-dirty", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (id, status, author) =>
        {
            entered.SetResult();
            await release.Task;
            storage.RemoveWithoutNotification(id);
            return TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.TaskNotFound, "confirmed missing", id, status));
        };
        using var vm = new TaskItemViewModel(task, storage, () => true);
        var transition = vm.TryTransitionToStatusAsync(DomainTaskStatus.Completed);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Title = "copyable edit while status is pending";
        vm.Description = "copyable local description";
        var seal = sealDuringCommand ? vm.SealPendingSaves() : Task.CompletedTask;
        release.SetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
        await seal.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));
        using (Assert.Multiple())
        {
            await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.TaskNotFound);
            await Assert.That(vm.IsMissingFromStorage).IsTrue();
            await Assert.That(vm.CanChangeTaskStatus).IsFalse();
            await Assert.That(vm.Title).IsEqualTo("copyable edit while status is pending");
            await Assert.That(vm.Description).IsEqualTo("copyable local description");
            await Assert.That(storage.UpdateCount).IsEqualTo(0);
            await Assert.That(storage.Contains(task.Id)).IsFalse();
        }
    }

    [Test]
    public async Task Reload_UnknownThenFailedReadCannotUndoPersistedStatusOnEditOrSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-uncertain-persisted", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.StatusHandler = (id, status, author) =>
        {
            storage.CreateSuccess(id, status, author);
            return Task.FromResult(TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.OutcomeUnknown, "verification failed", id, status)));
        };
        storage.ReloadHandler = _ => Task.FromResult(TaskReloadResult.Failed());
        using var vm = new TaskItemViewModel(task, storage, () => true);
        await vm.TryTransitionToStatusAsync(DomainTaskStatus.Completed);
        vm.Title = "draft after unknown outcome";
        await vm.ReloadTaskAsync();
        await Assert.That(vm.CanChangeTaskStatus).IsFalse();
        await Assert.That(vm.ArchiveCommand.CanExecute(null)).IsFalse();
        await Assert.That(async () => await vm.SealPendingSaves()).Throws<InvalidOperationException>();
        await Assert.That(storage.UpdateCount).IsEqualTo(0);
        await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(storage.Snapshot(task.Id).StatusHistory.Count).IsEqualTo(2);
        await Assert.That(vm.Title).IsEqualTo("draft after unknown outcome");
    }

    [Test]
    public async Task Reload_UserAutosaveMaturingDuringReadResumesAfterLoaded()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-deferred-save", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var release = new TaskCompletionSource<TaskReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.ReloadHandler = _ => release.Task;
        using var vm = new TaskItemViewModel(task, storage, () => true);
        var reload = vm.ReloadTaskAsync();
        vm.Title = "user edit while reading";
        await Task.Delay(TaskItemViewModel.DefaultThrottleTime + TimeSpan.FromSeconds(2));
        await Assert.That(storage.UpdateCount).IsEqualTo(0);
        release.SetResult(TaskReloadResult.Loaded(task with { Status = DomainTaskStatus.NotReady }));
        await reload;
        await vm.WaitForPendingSavesAsync();
        await Assert.That(storage.UpdateCount).IsEqualTo(1);
        await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("user edit while reading");
        await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.NotReady);
    }

    [Test]
    public async Task Reload_DelayedMissingDecisionIsAwaitedBeforeLifecycleFinalSave()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-missing-seal", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var release = new TaskCompletionSource<TaskReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.ReloadHandler = _ => release.Task;
        using var vm = new TaskItemViewModel(task, storage, () => true);
        vm.Title = "unsaved draft";
        var read = vm.ReloadTaskAsync();
        var seal = vm.SealPendingSaves();
        await Assert.That(seal.IsCompleted).IsFalse();
        await Assert.That(storage.UpdateCount).IsEqualTo(0);
        release.SetResult(TaskReloadResult.Missing());
        await Task.WhenAll(read, seal).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(vm.IsMissingFromStorage).IsTrue();
        await Assert.That(vm.Title).IsEqualTo("unsaved draft");
        await Assert.That(storage.UpdateCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reload_PreservesDirtyFieldsAndEditsDuringReadWithoutWriting()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-dirty", DomainTaskStatus.Prepared);
        task.CompletionCriteria = [new TaskCompletionCriterion { Id = "local-criterion", Text = "original criterion" }];
        storage.Seed(task);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.ReloadHandler = async _ =>
        {
            entered.SetResult();
            await release.Task;
            var snapshot = storage.Snapshot(task.Id);
            snapshot.Status = DomainTaskStatus.NotReady;
            snapshot.Title = "external title";
            snapshot.Description = "external description";
            return TaskReloadResult.Loaded(snapshot);
        };
        // This case verifies that reading does not produce a write. The separate
        // maturing-autosave case covers user saves that become due during a read.
        using var vm = CreateViewModelWithDeferredAutosave(task, storage);
        vm.Title = "local title";
        vm.Importance = 42;
        vm.Wanted = false;
        var plannedBegin = DateTime.Now.AddDays(2);
        vm.PlannedBeginDateTime = plannedBegin;
        vm.PlannedEndDateTime = plannedBegin.AddMinutes(37);
        vm.PlannedDuration = TimeSpan.FromMinutes(37);
        vm.Repeater = new RepeaterPatternViewModel { Type = RepeaterType.Daily, Period = 3 };
        var reload = vm.ReloadTaskAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CompletionCriteria.Single().Text = "keep this criterion";
        vm.Description = "typed during read";
        var denied = await vm.TryTransitionToStatusAsync(DomainTaskStatus.Completed);
        await Assert.That(vm.CanReloadTask).IsFalse();
        await Assert.That(denied.Success).IsFalse();
        release.SetResult();
        await reload;
        using (Assert.Multiple())
        {
            await Assert.That(vm.Title).IsEqualTo("local title");
            await Assert.That(vm.Description).IsEqualTo("typed during read");
            await Assert.That(vm.Importance).IsEqualTo(42);
            await Assert.That(vm.PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(37));
            await Assert.That(vm.PlannedBeginDateTime).IsEqualTo(plannedBegin);
            await Assert.That(vm.PlannedEndDateTime).IsEqualTo(plannedBegin.AddMinutes(37));
            await Assert.That(vm.Wanted).IsFalse();
            await Assert.That(vm.Repeater!.Period).IsEqualTo(3);
            await Assert.That(vm.CompletionCriteria.Single().Text).IsEqualTo("keep this criterion");
            await Assert.That(vm.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(vm.CanReloadTask).IsTrue();
            await Assert.That(storage.UpdateCount).IsEqualTo(0);
            await Assert.That(storage.StatusCalls).IsEmpty();
        }
        await vm.SaveItemCommand.Execute().ToTask();
        await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("local title");
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("typed during read");
        await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.NotReady);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reload_WaitsForRunningAutosaveEvenIfItFails(bool failSave)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-running-save", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UpdateHandler = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            if (failSave) throw new InvalidOperationException("controlled save failure");
        };
        using var vm = new TaskItemViewModel(task, storage, () => true);
        // Adding a criterion starts the existing immediate, tracked autosave producer.
        vm.CompletionCriteria.Add(new TaskCompletionCriterion { Id = "pending", Text = "pending criterion" });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reload = vm.ReloadTaskAsync();
        await Assert.That(storage.ReloadCount).IsEqualTo(0);
        release.SetResult();
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.ReloadCount).IsEqualTo(1);
        await Assert.That(storage.UpdateCount).IsEqualTo(1);
        await Assert.That(vm.CompletionCriteria.Single().Text).IsEqualTo("pending criterion");
    }

    [Test]
    public async Task Reload_MissingPreservesTextAndCannotResurrectOnSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-missing", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.ReloadHandler = _ => Task.FromResult(TaskReloadResult.Missing());
        using var vm = new TaskItemViewModel(task, storage, () => true);
        vm.Title = "copy this draft";
        await vm.ReloadTaskAsync();
        vm.Description = "copy this description";
        await vm.SaveItemCommand.Execute().ToTask();
        await vm.SealPendingSaves();
        using (Assert.Multiple())
        {
            await Assert.That(vm.IsMissingFromStorage).IsTrue();
            await Assert.That(vm.Title).IsEqualTo("copy this draft");
            await Assert.That(vm.Description).IsEqualTo("copy this description");
            await Assert.That(vm.CanChangeTaskStatus).IsFalse();
            await Assert.That(storage.UpdateCount).IsEqualTo(0);
            await Assert.That(vm.TaskOperationDetails).IsEqualTo("TaskNotFound");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reload_LateResultAfterDisposalCannotUpdateCard(bool missing)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-disposed", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var release = new TaskCompletionSource<TaskReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.ReloadHandler = _ => release.Task;
        var vm = new TaskItemViewModel(task, storage, () => false);
        var read = vm.ReloadTaskAsync();
        vm.Dispose();
        release.SetResult(missing ? TaskReloadResult.Missing()
            : TaskReloadResult.Loaded(task with { Status = DomainTaskStatus.Completed }));
        var result = await read;
        await Assert.That(result.Outcome).IsEqualTo(TaskReloadOutcome.Failed);
        await Assert.That(vm.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task Reload_UnknownOutcomeReadsActualStatusWithoutRetryOrDuplicateHistory()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("reload-unknown", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.StatusHandler = (id, status, author) =>
        {
            storage.CreateSuccess(id, status, author);
            return Task.FromResult(TaskOperationResult.Denied(TaskOperationDeniedReason.Create(
                TaskOperationDeniedKind.OutcomeUnknown, "secret exception", id, status)));
        };
        using var vm = new TaskItemViewModel(task, storage, () => false);
        await vm.TryTransitionToStatusAsync(DomainTaskStatus.Completed);
        await Assert.That(vm.TaskOperationDetails).IsEqualTo("OutcomeUnknown");
        await vm.ReloadTaskAsync();
        await Assert.That(vm.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(vm.TaskOperationError).IsEmpty();
        await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        await Assert.That(vm.StatusHistory.Count).IsEqualTo(storage.Snapshot(task.Id).StatusHistory.Count);
    }

    [Test]
    [Arguments(TaskOperationDeniedKind.ValidationFailed, "TaskStatusGraphInvalid")]
    [Arguments(TaskOperationDeniedKind.ExecutionStateDenied, "TaskStatusAgentActive")]
    [Arguments(TaskOperationDeniedKind.DescriptionMarkerConflict, "TaskStatusMarkerInvalid")]
    public async Task StatusFailure_ReportsSafeSpecificCause(TaskOperationDeniedKind kind, string key)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("specific-denial", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.StatusHandler = (id, status, _) => Task.FromResult(TaskOperationResult.Denied(
            TaskOperationDeniedReason.Create(kind, "password=secret", id, status)));
        using var vm = new TaskItemViewModel(task, storage, () => false);
        await vm.TryTransitionToStatusAsync(DomainTaskStatus.Completed);
        await Assert.That(vm.TaskOperationError).IsEqualTo(L10n.Get(key));
        await Assert.That(vm.TaskOperationDetails).IsEqualTo(kind.ToString());
        await Assert.That(vm.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task RawStorageHydration_PreservesNewerPendingFieldAndAppliesAuthoritativeState()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("raw-hydration-late-editor", DomainTaskStatus.Prepared);
        storage.Seed(task);
        using var viewModel = new TaskItemViewModel(task, storage, () => true)
        {
            PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromDays(1)
        };
        viewModel.Title = "first persisted title";
        await viewModel.FlushPendingEditorChangesAsync();
        var olderSnapshot = storage.Snapshot(task.Id) with
        {
            Description = "authoritative description",
            Status = DomainTaskStatus.Completed,
            Importance = 8,
            ContainsTasks = ["authoritative-child"],
            ParentTasks = ["authoritative-parent"]
        };
        viewModel.Title = "newer pending title";
        await Assert.That(viewModel.HasPendingEditableChanges).IsTrue();

        // Ordinary storage acknowledgements use the raw overload, not a graph revision.
        viewModel.Update(olderSnapshot);

        await Assert.That(viewModel.Title).IsEqualTo("newer pending title");
        await Assert.That(viewModel.Description).IsEqualTo("authoritative description");
        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(viewModel.Importance).IsEqualTo(8);
        await Assert.That(viewModel.Contains.SequenceEqual(["authoritative-child"])).IsTrue();
        await Assert.That(viewModel.Parents.SequenceEqual(["authoritative-parent"])).IsTrue();
        await Assert.That(viewModel.HasPendingEditableChanges).IsTrue();
        await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("first persisted title");

        await viewModel.FlushPendingEditorChangesAsync();
        await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("newer pending title");
        await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
    }

    [Test]
    public async Task EditorPersistenceReadiness_DetectsLateEditAfterAnotherTaskWaitsForStorage()
    {
        using var storage = new ScriptedTaskStorage();
        var firstTask = CreateTask("scope-first", DomainTaskStatus.Prepared);
        var secondTask = CreateTask("scope-second", DomainTaskStatus.Prepared);
        storage.Seed(firstTask, secondTask);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        storage.UpdateHandler = async snapshot =>
        {
            writes++;
            if (snapshot.Id == secondTask.Id)
            {
                secondStarted.TrySetResult();
                await releaseSecond.Task;
            }
        };
        using var first = new TaskItemViewModel(firstTask, storage, () => true);
        using var second = new TaskItemViewModel(secondTask, storage, () => true);
        await Assert.That(first.HasPendingEditorPersistence).IsFalse();
        first.Description = "initial first draft";
        second.Description = "second draft";
        await first.FlushPendingEditorChangesAsync();
        await Assert.That(first.HasPendingEditorPersistence).IsFalse();
        var secondFlush = second.FlushPendingEditorChangesAsync();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(second.HasPendingEditorPersistence).IsTrue();
        first.Description = "late edit while second waits";
        releaseSecond.TrySetResult();
        await secondFlush.WaitAsync(TimeSpan.FromSeconds(5));
        var writesBeforeRead = writes;
        await Assert.That(first.HasPendingEditorPersistence).IsTrue();
        await Assert.That(second.HasPendingEditorPersistence).IsFalse();
        await Assert.That(writes).IsEqualTo(writesBeforeRead);
        await first.FlushPendingEditorChangesAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(first.HasPendingEditorPersistence).IsFalse();
        await Assert.That(storage.Snapshot(firstTask.Id).Description).IsEqualTo("late edit while second waits");
    }

    [Test]
    public async Task EditorFlush_PersistsFastThrottledDraftAndRemainsReversible()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("fast-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var writes = 0;
        storage.UpdateHandler = _ => { writes++; return Task.CompletedTask; };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Description = "draft before throttle";
        await Assert.That(viewModel.HasPendingEditableChanges).IsTrue();
        await viewModel.WaitForPendingSavesAsync();
        await Assert.That(writes).IsEqualTo(0);
        await viewModel.FlushPendingEditorChangesAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("draft before throttle");
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
        viewModel.Description = "later draft";
        await viewModel.FlushPendingEditorChangesAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("later draft");
        await Assert.That(writes).IsEqualTo(2);
        await viewModel.FlushPendingEditorChangesAsync();
        await Assert.That(writes).IsEqualTo(2);
    }

    [Test]
    public async Task EditorFlush_CoalescesCallersAndSerializesConcurrentAutosaveAndStatus()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("concurrent-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var activeWrites = 0;
        var maximumWrites = 0;
        storage.UpdateHandler = async _ =>
        {
            var active = Interlocked.Increment(ref activeWrites);
            maximumWrites = Math.Max(maximumWrites, active);
            try
            {
                if (Interlocked.Increment(ref writes) == 1)
                {
                    started.TrySetResult();
                    await release.Task;
                }
            }
            finally { Interlocked.Decrement(ref activeWrites); }
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Description = "initial draft";
        var first = viewModel.FlushPendingEditorChangesAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = viewModel.FlushPendingEditorChangesAsync();
        await Assert.That(second).IsSameReferenceAs(first);
        viewModel.AreaIds.Add("area-during-flush"); // Starts the tracked immediate autosave.
        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");
        viewModel.Description = "latest draft during flush";
        await Assert.That(first.IsCompleted).IsFalse();
        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That((await transition.WaitAsync(TimeSpan.FromSeconds(5))).Success).IsTrue();
        await Assert.That(maximumWrites).IsEqualTo(1);
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("latest draft during flush");
        await Assert.That(storage.Snapshot(task.Id).AreaIds).Contains("area-during-flush");
        await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
    }

    [Test]
    public async Task EditorFlush_FailurePreservesDraftAndNextAttemptCanRetry()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("retry-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.UpdateHandler = _ => throw new IOException("controlled editor flush failure");
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Description = "retained failed draft";
        await Assert.That(async () => await viewModel.FlushPendingEditorChangesAsync())
            .Throws<IOException>();
        await Assert.That(viewModel.Description).IsEqualTo("retained failed draft");
        await Assert.That(viewModel.HasPendingEditableChanges).IsTrue();
        storage.UpdateHandler = _ => Task.CompletedTask;
        await viewModel.FlushPendingEditorChangesAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("retained failed draft");
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
    }

    [Test]
    public async Task EditorFlush_WaitsForExistingStatusProducerWithoutEditableDraft()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("status-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (id, status, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(id, status, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
        var flush = viewModel.FlushPendingEditorChangesAsync();
        await Assert.That(flush.IsCompleted).IsFalse();
        await Assert.That(viewModel.HasPendingEditorPersistence).IsTrue();
        release.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That((await transition).Success).IsTrue();
        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(viewModel.HasPendingEditorPersistence).IsFalse();
    }

    [Test]
    public async Task EditorFlush_ReportsPendingProducerFailureButDoesNotPoisonRetry()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("producer-retry-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UpdateHandler = async _ =>
        {
            started.TrySetResult();
            await release.Task;
            throw new IOException("controlled pending producer failure");
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Description = "dirty producer draft";
        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var flush = viewModel.FlushPendingEditorChangesAsync();
        release.TrySetResult();
        await Assert.That(async () => await flush.WaitAsync(TimeSpan.FromSeconds(5))).Throws<IOException>();
        await Assert.That((await transition).Success).IsFalse();
        await Assert.That(viewModel.HasPendingEditableChanges).IsTrue();
        storage.UpdateHandler = _ => Task.CompletedTask;
        await viewModel.FlushPendingEditorChangesAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("dirty producer draft");
        await Assert.That(viewModel.HasPendingEditableChanges).IsFalse();
    }

    [Test]
    public async Task EditorFlush_IsIncludedInLifecycleSealWithoutCircularWait()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("seal-flush", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UpdateHandler = async _ => { started.TrySetResult(); await release.Task; };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Description = "final draft";
        var flush = viewModel.FlushPendingEditorChangesAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var seal = viewModel.SealPendingSaves();
        await Assert.That(seal.IsCompleted).IsFalse();
        release.TrySetResult();
        await Task.WhenAll(flush, seal).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description).IsEqualTo("final draft");
    }

    [Test]
    public async Task StatusOperation_IsIncludedInSealAndNewOperationIsRejectedAfterSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("lifecycle", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => false);

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pendingSnapshot = viewModel.WaitForPendingSavesAsync();
        var sealedSnapshot = viewModel.SealPendingSaves();
        var rejected = await viewModel.TryTransitionToStatusAsync(DomainTaskStatus.NotReady, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(pendingSnapshot.IsCompleted).IsFalse();
            await Assert.That(sealedSnapshot.IsCompleted).IsFalse();
            await Assert.That(rejected.Success).IsFalse();
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }

        release.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
        await sealedSnapshot.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task StatusOptionSetter_RoutesThroughTrackedCommandWithoutOptimisticMutation()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("status-option-setter", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => false);

        viewModel.StatusOption = viewModel.StatusOptions.Single(option =>
            option.Status == DomainTaskStatus.InProgress);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }

        release.TrySetResult();
        await viewModel.WaitForPendingSavesAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task StatusOptionSetter_ArchiveConfirmationCascadesOnlyActiveContainedTasks()
    {
        using var storage = new ScriptedTaskStorage();
        var parentTask = CreateTask("status-option-archive-parent", DomainTaskStatus.Prepared);
        var notReadyChildTask = CreateTask("status-option-archive-not-ready", DomainTaskStatus.NotReady);
        var preparedChildTask = CreateTask("status-option-archive-prepared", DomainTaskStatus.Prepared);
        var inProgressChildTask = CreateTask("status-option-archive-in-progress", DomainTaskStatus.InProgress);
        var completedChildTask = CreateTask("status-option-archive-completed", DomainTaskStatus.Completed);
        var archivedChildTask = CreateArchivedTask(
            "status-option-archive-archived",
            DomainTaskStatus.Prepared,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        storage.Seed(
            parentTask,
            notReadyChildTask,
            preparedChildTask,
            inProgressChildTask,
            completedChildTask,
            archivedChildTask);

        var notifications = new NotificationManagerWrapperMock { AskResult = true };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var notReadyChild = new TaskItemViewModel(notReadyChildTask, storage, () => false);
        using var preparedChild = new TaskItemViewModel(preparedChildTask, storage, () => false);
        using var inProgressChild = new TaskItemViewModel(inProgressChildTask, storage, () => false);
        using var completedChild = new TaskItemViewModel(completedChildTask, storage, () => false);
        using var archivedChild = new TaskItemViewModel(archivedChildTask, storage, () => false);
        parent.ApplyRelations(
            [notReadyChild, preparedChild, inProgressChild, completedChild, archivedChild],
            [],
            [],
            []);

        parent.StatusOption = parent.StatusOptions.Single(option => option.Status == DomainTaskStatus.Archived);
        await parent.WaitForPendingSavesAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
            await Assert.That(notifications.LastAskHeader).IsEqualTo(L10n.Get("ArchiveContainedTasksHeader"));
            await Assert.That(notifications.LastAskMessage)
                .IsEqualTo(L10n.Format("ArchiveContainedTasksMessage", 3, parent.Title));
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id, notReadyChild.Id, preparedChild.Id, inProgressChild.Id]);
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(notReadyChild.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(preparedChild.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(inProgressChild.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(completedChild.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(archivedChild.Status).IsEqualTo(DomainTaskStatus.Archived);
        }
    }

    [Test]
    public async Task StatusOptionSetter_ArchiveDeclinedLeavesContainedTasksUnchanged()
    {
        using var storage = new ScriptedTaskStorage();
        var parentTask = CreateTask("status-option-archive-decline-parent", DomainTaskStatus.Prepared);
        var childTask = CreateTask("status-option-archive-decline-child", DomainTaskStatus.InProgress);
        storage.Seed(parentTask, childTask);

        var notifications = new NotificationManagerWrapperMock { AskResult = false };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(childTask, storage, () => false);
        parent.ApplyRelations([child], [], [], []);

        parent.StatusOption = parent.StatusOptions.Single(option => option.Status == DomainTaskStatus.Archived);
        await parent.WaitForPendingSavesAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.InProgress);
        }
    }

    [Test]
    public async Task SelectStatusOption_CurrentArchivedStatusDoesNotUnarchive()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateArchivedTask(
            "status-option-current-archived",
            DomainTaskStatus.Prepared,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        storage.Seed(task);
        using var viewModel = new TaskItemViewModel(task, storage, () => false);

        await viewModel.TrySelectStatusOptionAsync(DomainTaskStatus.Archived);
        await viewModel.WaitForPendingSavesAsync().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(storage.StatusCalls).IsEmpty();
            await Assert.That(storage.UnarchiveCalls).IsEmpty();
        }
    }

    [Test]
    public async Task DelayedStatusResult_DoesNotOverwriteNewerStorageGeneration()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("generation-order", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author) with { StorageRevision = 10 };
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => false);

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = TaskItemSnapshot.Clone(task);
        newer.Status = DomainTaskStatus.Completed;
        viewModel.Update(newer, storageRevision: 11);

        release.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EditDuringStatusCommand_IsMergedAndDrainedBeforeCompletion(bool sealDuringCommand)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("editor-race", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Before command";

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.Completed, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Title = "Edited while command is pending";
        viewModel.Repeater = new RepeaterPatternViewModel
        {
            Type = RepeaterType.Daily,
            Period = 1
        };
        var sealedSaves = sealDuringCommand ? viewModel.SealPendingSaves() : Task.CompletedTask;

        release.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
        await sealedSaves.WaitAsync(TimeSpan.FromSeconds(5));
        var persisted = storage.Snapshot(task.Id);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(viewModel.Title).IsEqualTo("Edited while command is pending");
            await Assert.That(viewModel.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
            await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(persisted.Title).IsEqualTo("Edited while command is pending");
            await Assert.That(persisted.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
        }
    }

    [Test]
    public async Task EditorFlushFailureBeforeStatusCommand_ReturnsControlledFailureAndRemainsRetryable()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("editor-preflush-failure", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.UpdateHandler = _ => Task.FromException(new InvalidOperationException("controlled editor save failure"));
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Unsaved title";

        var result = await viewModel.TryTransitionToStatusAsync(
            DomainTaskStatus.Completed,
            "tester").WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StorageFailed);
            await Assert.That(storage.StatusCalls).IsEmpty();
            await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo(task.Title);
        }

        storage.UpdateHandler = null;
        await viewModel.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("Unsaved title");

        viewModel.Description = "New edit after successful retry";
        await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Snapshot(task.Id).Description)
            .IsEqualTo("New edit after successful retry");
    }

    [Test]
    public async Task SealPendingSaves_FlushesThrottledEditorRevisionBeforeTeardown()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("sealed-throttled-editor", DomainTaskStatus.Prepared);
        storage.Seed(task);
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Saved by lifecycle seal";

        await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(storage.Snapshot(task.Id).Title)
            .IsEqualTo("Saved by lifecycle seal");
    }

    [Test]
    public async Task SealPendingSaves_WhenFinalEditorFlushFails_FaultsTeardown()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("sealed-throttled-editor-failure", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.UpdateHandler = _ => Task.FromException(new InvalidOperationException("controlled seal save failure"));
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Cannot be discarded";

        var sealedSaves = viewModel.SealPendingSaves();

        await Assert.That(async () =>
                await sealedSaves.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LifecycleSeal_DelayedFailedSaveCleanupRequiresPersistedRetry(bool retry)
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("seal-delayed-failed-save-cleanup", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UpdateHandler = async _ =>
        {
            await releaseSave.Task;
            throw new InvalidOperationException("controlled autosave failure");
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Draft that must be persisted";
        var context = new DelayedSaveCleanupContext();
        var previousContext = SynchronizationContext.Current;
        var failedSave = viewModel.SaveItemCommand.Execute().ToTask();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            // Capture only cleanup; the real save can finish while this context stays paused.
            viewModel.TrackPendingSave(failedSave);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            releaseSave.TrySetResult();
            await Assert.That(async () => await failedSave.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<InvalidOperationException>();
            await Assert.That(failedSave.IsFaulted).IsTrue();
            // ObserveSaveCompletionAsync is queued but has not removed the failed task.
            await Assert.That(viewModel.WaitForPendingSavesAsync().IsFaulted).IsTrue();
            if (retry)
            {
                storage.UpdateHandler = null;
                await viewModel.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("Draft that must be persisted");
                await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                await Assert.That(async () => await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5)))
                    .Throws<InvalidOperationException>();
                await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo(task.Title);
            }
        }
        finally
        {
            releaseSave.TrySetResult();
            while (context.ExecuteOne(TimeSpan.Zero)) { }
        }
    }

    private sealed class DelayedSaveCleanupContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add((callback, state));
        public bool ExecuteOne(TimeSpan timeout)
        {
            if (!callbacks.TryTake(out var work, timeout)) return false;
            var previousContext = Current;
            try
            {
                SetSynchronizationContext(this);
                work.Callback(work.State);
            }
            finally { SetSynchronizationContext(previousContext); }
            return true;
        }
    }

    [Test]
    public async Task EditorFlushFailureAfterSuccessfulStatusCommand_PreservesConfirmedResultAndRemainsRetryable()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("editor-postflush-failure", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Before command";

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.Completed, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Title = "Edited while command is pending";
        storage.UpdateHandler = _ => Task.FromException(new InvalidOperationException("controlled final editor save failure"));

        release.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
        var persistedBeforeRetry = storage.Snapshot(task.Id);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(viewModel.Title).IsEqualTo("Edited while command is pending");
            await Assert.That(persistedBeforeRetry.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(persistedBeforeRetry.Title).IsEqualTo("Before command");
        }

        storage.UpdateHandler = null;
        await viewModel.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));
        var persistedAfterRetry = storage.Snapshot(task.Id);
        using (Assert.Multiple())
        {
            await Assert.That(persistedAfterRetry.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(persistedAfterRetry.Title).IsEqualTo("Edited while command is pending");
        }
    }

    [Test]
    public async Task EditorFlushFailureAfterSuccessfulStatusCommand_FaultsConcurrentLifecycleSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("editor-postflush-seal-failure", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            started.TrySetResult();
            await release.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        using var viewModel = new TaskItemViewModel(task, storage, () => true);
        viewModel.Title = "Before command";

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.Completed, "tester");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Title = "Unsaved edit during sealed command";
        storage.UpdateHandler = _ => Task.FromException(new InvalidOperationException("controlled sealed editor save failure"));
        var sealedSaves = viewModel.SealPendingSaves();

        release.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(viewModel.Title).IsEqualTo("Unsaved edit during sealed command");
            await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("Before command");
        }

        await Assert.That(async () =>
                await sealedSaves.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RetryThatPersistsFailedRevisionBeforeProducerCompletion_AllowsLifecycleSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("editor-retry-before-producer-completion", DomainTaskStatus.Prepared);
        storage.Seed(task);
        var statusStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStatus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            statusStarted.TrySetResult();
            await releaseStatus.Task;
            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        var notifications = new BlockingErrorNotificationManager();
        using var viewModel = new TaskItemViewModel(task, storage, () => true)
        {
            NotificationManager = notifications
        };
        viewModel.Title = "Before command";

        var transition = viewModel.TryTransitionToStatusAsync(DomainTaskStatus.Completed, "tester");
        await statusStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Title = "Retry saves this edit";
        storage.UpdateHandler = _ => Task.FromException(new InvalidOperationException("controlled final editor save failure"));
        releaseStatus.TrySetResult();
        await notifications.ErrorToastEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        storage.UpdateHandler = null;
        await viewModel.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
        notifications.ReleaseErrorToast.TrySetResult();
        var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.SealPendingSaves().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(storage.Snapshot(task.Id).Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(storage.Snapshot(task.Id).Title).IsEqualTo("Retry saves this edit");
        }
    }

    [Test]
    public async Task PendingArchiveConfirmation_IsCancelledAndDrainedByLifecycleSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("sealed-parent", DomainTaskStatus.InProgress, now.AddHours(-2));
        var childTask = CreateArchivedTask("sealed-child", DomainTaskStatus.InProgress, now.AddHours(-1));
        storage.Seed(parentTask, childTask);
        var confirmationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationSettled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new NotificationManagerWrapperMock
        {
            ConfirmHandler = async (_, _) =>
            {
                confirmationStarted.TrySetResult();
                try
                {
                    return await lateConfirmation.Task;
                }
                finally
                {
                    confirmationSettled.TrySetResult();
                }
            }
        };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(childTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        var archiveWorkflow = ExecuteArchiveCommandAsync(parent);
        await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sealedSnapshot = parent.SealPendingSaves();

        await Task.WhenAll(archiveWorkflow, sealedSnapshot).WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(notifications.ErrorMessages).IsEmpty();
            await Assert.That(notifications.SuccessMessages).IsEmpty();
        }

        lateConfirmation.TrySetResult(true);
        await confirmationSettled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(notifications.ErrorMessages).IsEmpty();
            await Assert.That(notifications.SuccessMessages).IsEmpty();
        }
    }

    [Test]
    public async Task LifecycleSeal_WaitsForCascadeChildAdmittedBeforeSeal()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("drain-parent", DomainTaskStatus.InProgress, now.AddHours(-2));
        var childTask = CreateArchivedTask("drain-child", DomainTaskStatus.InProgress, now.AddHours(-1));
        storage.Seed(parentTask, childTask);
        var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            if (string.Equals(taskId, childTask.Id, StringComparison.Ordinal))
            {
                childStarted.TrySetResult();
                await releaseChild.Task;
            }

            return storage.CreateSuccess(taskId, requestedStatus, author);
        };
        var notifications = new NotificationManagerWrapperMock { AskResult = true };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(childTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        var archiveWorkflow = ExecuteArchiveCommandAsync(parent);
        await childStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sealedSnapshot = parent.SealPendingSaves();

        await Assert.That(sealedSnapshot.IsCompleted).IsFalse();

        releaseChild.TrySetResult();
        await Task.WhenAll(archiveWorkflow, sealedSnapshot).WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(",", storage.StatusCalls.Select(static call => call.TaskId)))
                .IsEqualTo(string.Join(",", [parent.Id, child.Id]));
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(notifications.ErrorMessages).IsEmpty();
            await Assert.That(notifications.SuccessMessages).IsEmpty();
        }
    }

    [Test]
    public async Task Unarchive_TransitionsParentBeforeSingleConfirmationAndProcessesEveryChildInOrder()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("parent", DomainTaskStatus.InProgress, now.AddHours(-4));
        var firstTask = CreateArchivedTask("first", DomainTaskStatus.InProgress, now.AddHours(-3));
        var secondTask = CreateArchivedTask("second", DomainTaskStatus.NotReady, now.AddHours(-2));
        var thirdTask = CreateArchivedTask("third", DomainTaskStatus.Completed, now.AddHours(-1));
        storage.Seed(parentTask, firstTask, secondTask, thirdTask);
        storage.StatusHandler = (taskId, requestedStatus, author) =>
        {
            if (string.Equals(taskId, secondTask.Id, StringComparison.Ordinal))
            {
                return Task.FromResult(storage.CreateFailure(taskId, requestedStatus));
            }

            return Task.FromResult(storage.CreateSuccess(taskId, requestedStatus, author));
        };

        var notifications = new NotificationManagerWrapperMock();
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var first = new TaskItemViewModel(firstTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var second = new TaskItemViewModel(secondTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var third = new TaskItemViewModel(thirdTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([first, second, third], [], [], []);

        var confirmationObservedParentSuccess = false;
        notifications.ConfirmHandler = (_, _) =>
        {
            confirmationObservedParentSuccess =
                parent.Status == DomainTaskStatus.Prepared &&
                storage.StatusCalls.Select(static call => call.TaskId).SequenceEqual([parent.Id]);
            return Task.FromResult(true);
        };

        var archiveCommand = (ReactiveCommand<Unit, Unit>)parent.ArchiveCommand;
        await archiveCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(confirmationObservedParentSuccess).IsTrue();
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
            await Assert.That(string.Join(",", storage.StatusCalls.Select(static call => call.TaskId)))
                .IsEqualTo(string.Join(",", [parent.Id, first.Id, second.Id, third.Id]));
            await Assert.That(storage.UnarchiveCalls)
                .IsEquivalentTo([parent.Id, first.Id, second.Id, third.Id]);
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(first.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(second.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(third.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(notifications.LastErrorMessage)
                .IsEqualTo(L10n.Format("TaskStatusCascadeSummary", 2, 1));
            await Assert.That(storage.Snapshot(parent.Id).StatusHistory.Count)
                .IsEqualTo(parentTask.StatusHistory.Count + 1);
            await Assert.That(storage.Snapshot(first.Id).StatusHistory.Count)
                .IsEqualTo(firstTask.StatusHistory.Count + 1);
            await Assert.That(storage.Snapshot(second.Id).StatusHistory.Count)
                .IsEqualTo(secondTask.StatusHistory.Count);
            await Assert.That(storage.Snapshot(third.Id).StatusHistory.Count)
                .IsEqualTo(thirdTask.StatusHistory.Count + 1);
        }
    }

    [Test]
    public async Task Unarchive_StaleCachedHistoryUsesStorageResolvedTargetsForParentAndChild()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var cachedParent = CreateArchivedTask("stale-parent", DomainTaskStatus.InProgress, now.AddHours(-4));
        var cachedChild = CreateArchivedTask("stale-child", DomainTaskStatus.Completed, now.AddHours(-3));
        var authoritativeParent = CreateArchivedTask(
            cachedParent.Id,
            DomainTaskStatus.Completed,
            now.AddHours(-2));
        var authoritativeChild = CreateArchivedTask(
            cachedChild.Id,
            DomainTaskStatus.InProgress,
            now.AddHours(-1));
        storage.Seed(authoritativeParent, authoritativeChild);

        var notifications = new NotificationManagerWrapperMock { AskResult = true };
        using var parent = new TaskItemViewModel(cachedParent, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(cachedChild, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(string.Join(",", storage.UnarchiveCalls))
                .IsEqualTo(string.Join(",", [parent.Id, child.Id]));
            await Assert.That(storage.StatusCalls.Select(static call => call.Status))
                .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Prepared]);
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Unarchive_AuthoritativeParentNoLongerArchivedStopsCascadeAndHydratesStatus()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var cachedParent = CreateArchivedTask("changed-parent", DomainTaskStatus.InProgress, now.AddHours(-3));
        var cachedChild = CreateArchivedTask("unchanged-child", DomainTaskStatus.InProgress, now.AddHours(-2));
        storage.Seed(
            CreateTask(cachedParent.Id, DomainTaskStatus.Completed),
            cachedChild);
        var notifications = new NotificationManagerWrapperMock { AskResult = true };
        using var parent = new TaskItemViewModel(cachedParent, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(cachedChild, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(storage.UnarchiveCalls).IsEquivalentTo([parent.Id]);
            await Assert.That(storage.StatusCalls).IsEmpty();
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(0);
            await Assert.That(notifications.LastErrorMessage)
                .IsEqualTo(L10n.Get("TaskStatusSourceChanged"));
        }
    }

    [Test]
    public async Task Unarchive_WithoutChildren_TransitionsParentWithoutConfirmation()
    {
        using var storage = new ScriptedTaskStorage();
        var parentTask = CreateArchivedTask(
            "parent-without-children",
            DomainTaskStatus.InProgress,
            DateTimeOffset.UtcNow.AddHours(-1));
        storage.Seed(parentTask);
        var notifications = new NotificationManagerWrapperMock { AskResult = true };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(0);
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
        }
    }

    [Test]
    public async Task Unarchive_WithoutNotificationManager_LeavesChildrenArchived()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("parent-null-manager", DomainTaskStatus.Prepared, now.AddHours(-2));
        var childTask = CreateArchivedTask("child-null-manager", DomainTaskStatus.InProgress, now.AddHours(-1));
        storage.Seed(parentTask, childTask);
        using var parent = new TaskItemViewModel(parentTask, storage, () => false);
        using var child = new TaskItemViewModel(childTask, storage, () => false);
        parent.ApplyRelations([child], [], [], []);

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
        }
    }

    [Test]
    public async Task Unarchive_WhenConfirmationIsDeclined_LeavesChildrenArchived()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("parent-decline", DomainTaskStatus.InProgress, now.AddHours(-2));
        var childTask = CreateArchivedTask("child-decline", DomainTaskStatus.InProgress, now.AddHours(-1));
        storage.Seed(parentTask, childTask);
        var notifications = new NotificationManagerWrapperMock { AskResult = false };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(childTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
        }
    }

    [Test]
    public async Task Unarchive_WhenConfirmationFails_LeavesChildrenArchivedAndReportsFailure()
    {
        using var storage = new ScriptedTaskStorage();
        var now = DateTimeOffset.UtcNow;
        var parentTask = CreateArchivedTask("parent-confirm-failure", DomainTaskStatus.Prepared, now.AddHours(-2));
        var childTask = CreateArchivedTask("child-confirm-failure", DomainTaskStatus.Prepared, now.AddHours(-1));
        storage.Seed(parentTask, childTask);
        var notifications = new NotificationManagerWrapperMock
        {
            ConfirmHandler = (_, _) => Task.FromException<bool>(new InvalidOperationException("dialog unavailable"))
        };
        using var parent = new TaskItemViewModel(parentTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        using var child = new TaskItemViewModel(childTask, storage, () => false)
        {
            NotificationManager = notifications
        };
        parent.ApplyRelations([child], [], [], []);

        await ExecuteArchiveCommandAsync(parent);

        using (Assert.Multiple())
        {
            await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
            await Assert.That(notifications.LastErrorMessage)
                .IsEqualTo(L10n.Format("TaskStatusCascadeConfirmationFailed", "dialog unavailable"));
            await Assert.That(storage.StatusCalls.Select(static call => call.TaskId))
                .IsEquivalentTo([parent.Id]);
        }
    }

    [Test]
    public async Task StalePreview_CommandDenialHydratesAuthoritativeStatusWithoutOptimisticMutation()
    {
        using var storage = new ScriptedTaskStorage();
        var localTask = CreateTask("stale-preview", DomainTaskStatus.Prepared);
        storage.Seed(localTask);
        using var viewModel = new TaskItemViewModel(localTask, storage, () => false);
        var preview = viewModel.StatusOptions.Single(
            option => option.Status == DomainTaskStatus.InProgress);
        await Assert.That(preview.IsEnabled).IsTrue();

        var authoritative = CreateArchivedTask(
            localTask.Id,
            DomainTaskStatus.Prepared,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        storage.Seed(authoritative);
        storage.StatusHandler = (taskId, requestedStatus, _) =>
            Task.FromResult(storage.CreateFailure(taskId, requestedStatus));

        var result = await viewModel.TryTransitionToStatusAsync(DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(preview.IsEnabled).IsFalse();
            await Assert.That(viewModel.StatusHistory.Count).IsEqualTo(authoritative.StatusHistory.Count);
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task StorageFailureWithoutSnapshotKeepsCachedStatusAndUsesHonestRetryCopy()
    {
        using var storage = new ScriptedTaskStorage();
        var task = CreateTask("storage-failure-no-snapshot", DomainTaskStatus.Prepared);
        storage.Seed(task);
        storage.StatusHandler = (taskId, requestedStatus, _) => Task.FromResult(
            TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StorageFailed,
                    "preflight read failed",
                    taskId,
                    requestedStatus)));
        var notifications = new NotificationManagerWrapperMock();
        using var viewModel = new TaskItemViewModel(task, storage, () => false)
        {
            NotificationManager = notifications
        };

        var result = await viewModel.TryTransitionToStatusAsync(
            DomainTaskStatus.InProgress,
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.AuthoritativeTask).IsNull();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(viewModel.StatusHistory.Count).IsEqualTo(task.StatusHistory.Count);
            await Assert.That(notifications.LastErrorMessage)
                .IsEqualTo(L10n.Get("TaskStatusSaveFailed"));
        }
    }

    [Test]
    public async Task StaleGraphDenial_UsesAuthoritativeContainedDirectAndInheritedReasonsInEnglishAndRussian()
    {
        var previousLocalization = LocalizationService.Current;
        var culture = CultureSnapshot.Capture();
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;
            var languages = new[]
            {
                new DiagnosticLanguage(
                    LocalizationService.EnglishLanguage,
                    "Complete all contained tasks before starting or completing this task.",
                    "Complete this task's direct blockers before starting or completing it.",
                    "Complete blockers inherited from parent tasks before starting or completing this task."),
                new DiagnosticLanguage(
                    LocalizationService.RussianLanguage,
                    "Сначала выполните все вложенные задачи.",
                    "Сначала выполните прямые блокирующие задачи.",
                    "Сначала выполните блокирующие задачи, унаследованные от родительских задач.")
            };
            foreach (var language in languages)
            {
                localization.SetLanguage(language.Language);
                var diagnosticScenarios = new[]
                {
                    new
                    {
                        Name = "contained",
                        Reasons = new[] { TaskAvailabilityReasonKind.IncompleteContainedTask },
                        Expected = language.ContainedReason,
                        DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                        RequestedStatus = DomainTaskStatus.InProgress
                    },
                    new
                    {
                        Name = "direct",
                        Reasons = new[] { TaskAvailabilityReasonKind.IncompleteDirectBlocker },
                        Expected = language.DirectBlockerReason,
                        DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                        RequestedStatus = DomainTaskStatus.InProgress
                    },
                    new
                    {
                        Name = "inherited",
                        Reasons = new[] { TaskAvailabilityReasonKind.IncompleteInheritedBlocker },
                        Expected = language.InheritedBlockerReason,
                        DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                        RequestedStatus = DomainTaskStatus.InProgress
                    },
                    new
                    {
                        Name = "direct-over-inherited",
                        Reasons = new[]
                        {
                            TaskAvailabilityReasonKind.IncompleteInheritedBlocker,
                            TaskAvailabilityReasonKind.IncompleteDirectBlocker
                        },
                        Expected = language.DirectBlockerReason,
                        DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                        RequestedStatus = DomainTaskStatus.InProgress
                    },
                    new
                    {
                        Name = "contained-over-all-completion",
                        Reasons = new[]
                        {
                            TaskAvailabilityReasonKind.IncompleteInheritedBlocker,
                            TaskAvailabilityReasonKind.IncompleteDirectBlocker,
                            TaskAvailabilityReasonKind.IncompleteContainedTask
                        },
                        Expected = language.ContainedReason,
                        DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForCompletion,
                        RequestedStatus = DomainTaskStatus.Completed
                    }
                };

                foreach (var scenario in diagnosticScenarios)
                {
                    using var storage = new ScriptedTaskStorage();
                    var task = CreateTask($"stale-{language.Language}-{scenario.Name}", DomainTaskStatus.Prepared);
                    storage.Seed(task);
                    var notifications = new NotificationManagerWrapperMock();
                    using var viewModel = new TaskItemViewModel(task, storage, () => false)
                    {
                        NotificationManager = notifications
                    };
                    await Assert.That(viewModel.StatusOptions.Single(option =>
                        option.Status == scenario.RequestedStatus).IsEnabled).IsTrue();
                    storage.StatusHandler = (taskId, requestedStatus, _) => Task.FromResult(
                        TaskOperationResult.DeniedWithAuthoritativeTask(
                            TaskOperationDeniedReason.CreateWithStatusTransition(
                                TaskOperationDeniedKind.StatusTransitionDenied,
                                "authoritative graph denial",
                                statusTransitionReason: scenario.DenialReason,
                                taskId: taskId,
                                requestedStatus: requestedStatus),
                            authoritativeTask: storage.Snapshot(taskId),
                            before: new TaskAvailabilityAnalysis
                            {
                                TaskId = taskId,
                                Status = DomainTaskStatus.Prepared,
                                IsCanBeCompleted = false,
                                Reasons = scenario.Reasons
                                    .Select(reasonKind => new TaskAvailabilityReason
                                    {
                                        Kind = reasonKind,
                                        SubjectId = $"subject-{reasonKind}",
                                        Details = "authoritative"
                                    })
                                    .ToArray()
                            }));

                    var result = await viewModel.TryTransitionToStatusAsync(
                        scenario.RequestedStatus,
                        "tester");

                    using (Assert.Multiple())
                    {
                        await Assert.That(result.Success).IsFalse();
                        await Assert.That(notifications.LastErrorMessage).IsEqualTo(scenario.Expected);
                        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
                    }
                }
            }
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
            culture.Restore();
        }
    }

    [Test]
    public async Task StatusPreview_DistinguishesAndPrioritizesContainedDirectAndInheritedBlockers()
    {
        var previousLocalization = LocalizationService.Current;
        var culture = CultureSnapshot.Capture();
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            localization.SetLanguage(LocalizationService.EnglishLanguage);
            LocalizationService.Current = localization;

            using var storage = new ScriptedTaskStorage();
            var taskModel = CreateTask("preview-blocked", DomainTaskStatus.Prepared);
            taskModel.IsCanBeCompleted = false;
            var containedTaskModel = CreateTask("preview-contained", DomainTaskStatus.InProgress);
            var directBlockerModel = CreateTask("preview-direct", DomainTaskStatus.InProgress);
            var parentModel = CreateTask("preview-parent", DomainTaskStatus.Prepared);
            var inheritedBlockerModel = CreateTask("preview-inherited", DomainTaskStatus.InProgress);
            storage.Seed(taskModel, containedTaskModel, directBlockerModel, parentModel, inheritedBlockerModel);

            using var task = new TaskItemViewModel(taskModel, storage, () => false);
            using var containedTask = new TaskItemViewModel(containedTaskModel, storage, () => false);
            using var directBlocker = new TaskItemViewModel(directBlockerModel, storage, () => false);
            using var parent = new TaskItemViewModel(parentModel, storage, () => false);
            using var inheritedBlocker = new TaskItemViewModel(inheritedBlockerModel, storage, () => false);

            parent.ApplyRelations([], [], [], [inheritedBlocker]);

            task.ApplyRelations([containedTask], [], [], []);
            var containedReason = task.StatusOptions.Single(option =>
                option.Status == DomainTaskStatus.InProgress).ToolTip;

            task.ApplyRelations([], [parent], [], [directBlocker]);
            var directOverInheritedReason = task.StatusOptions.Single(option =>
                option.Status == DomainTaskStatus.InProgress).ToolTip;

            task.ApplyRelations([containedTask], [parent], [], [directBlocker]);
            var containedOverAllReason = task.StatusOptions.Single(option =>
                option.Status == DomainTaskStatus.InProgress).ToolTip;

            task.ApplyRelations([], [parent], [], []);
            var inheritedReason = task.StatusOptions.Single(option =>
                option.Status == DomainTaskStatus.InProgress).ToolTip;

            using (Assert.Multiple())
            {
                await Assert.That(containedReason)
                    .IsEqualTo("Complete all contained tasks before starting or completing this task.");
                await Assert.That(directOverInheritedReason)
                    .IsEqualTo("Complete this task's direct blockers before starting or completing it.");
                await Assert.That(containedOverAllReason)
                    .IsEqualTo("Complete all contained tasks before starting or completing this task.");
                await Assert.That(inheritedReason)
                    .IsEqualTo("Complete blockers inherited from parent tasks before starting or completing this task.");
            }
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
            culture.Restore();
        }
    }

    private static Task ExecuteArchiveCommandAsync(TaskItemViewModel task) =>
        ((ReactiveCommand<Unit, Unit>)task.ArchiveCommand)
        .Execute()
        .ToTask();

    private static TaskItem CreateTask(string id, DomainTaskStatus status) => new()
    {
        Id = id,
        Title = id,
        Status = status,
        IsCanBeCompleted = true,
        StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = status,
                ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Author = "tester"
            }
        ]
    };

    private sealed record DiagnosticLanguage(
        string Language,
        string ContainedReason,
        string DirectBlockerReason,
        string InheritedBlockerReason);

    private static TaskItem CreateArchivedTask(
        string id,
        DomainTaskStatus previousStatus,
        DateTimeOffset previousAt) => new()
        {
            Id = id,
            Title = id,
            Status = DomainTaskStatus.Archived,
            IsCanBeCompleted = true,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = previousStatus,
                    ChangedAt = previousAt,
                    Author = "tester"
                },
                new TaskStatusHistoryEntry
                {
                    Status = DomainTaskStatus.Archived,
                    ChangedAt = previousAt.AddMinutes(1),
                    Author = "tester"
                }
            ]
        };

    private static TaskItemViewModel CreateViewModelWithDeferredAutosave(TaskItem task, ITaskStorage storage)
    {
        var previousThrottle = TaskItemViewModel.DefaultThrottleTime;
        try
        {
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMinutes(1);
            return new TaskItemViewModel(task, storage, () => true);
        }
        finally { TaskItemViewModel.DefaultThrottleTime = previousThrottle; }
    }

    private sealed class ScriptedTaskStorage : ITaskStorage, IDisposable
    {
        private readonly Dictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);

        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

        public ITaskRelationsIndex Relations { get; } = new TaskRelationsIndex();

        public TaskTreeManager TaskTreeManager { get; } = new(new InMemoryStorage());

        public List<(string TaskId, DomainTaskStatus Status, string? Author)> StatusCalls { get; } = [];

        public List<string> UnarchiveCalls { get; } = [];

        public Func<string, DomainTaskStatus, string?, Task<TaskOperationResult>>? StatusHandler { get; set; }

        public Func<TaskItem, Task>? UpdateHandler { get; set; }

        public Func<string, Task<TaskReloadResult>>? ReloadHandler { get; set; }
        public int UpdateCount { get; private set; }
        public int ReloadCount { get; private set; }
        public Task<TaskReloadResult> ReloadTaskAsync(string id)
        {
            ReloadCount++;
            return ReloadHandler?.Invoke(id) ?? Task.FromResult(TaskReloadResult.Loaded(Snapshot(id)));
        }

        public event EventHandler<EventArgs>? Initiated;

        public void Seed(params TaskItem[] tasks)
        {
            foreach (var task in tasks)
            {
                _tasks[task.Id] = Clone(task);
            }
        }

        public TaskItem Snapshot(string taskId) => Clone(_tasks[taskId]);

        public bool Contains(string taskId) => _tasks.ContainsKey(taskId);

        public void RemoveWithoutNotification(string taskId) => _tasks.Remove(taskId);

        public async Task<TaskOperationResult> TrySetStatusAsync(
            string taskId,
            DomainTaskStatus requestedStatus,
            string? author = null)
        {
            StatusCalls.Add((taskId, requestedStatus, author));
            if (StatusHandler != null)
            {
                return await StatusHandler(taskId, requestedStatus, author);
            }

            return CreateSuccess(taskId, requestedStatus, author);
        }

        public Task<TaskOperationResult> TryUnarchiveAsync(
            string taskId,
            string? author = null)
        {
            UnarchiveCalls.Add(taskId);
            var authoritative = _tasks[taskId];
            if (authoritative.Status != DomainTaskStatus.Archived)
            {
                return Task.FromResult(TaskOperationResult.DeniedWithAuthoritativeTask(
                    TaskOperationDeniedReason.Create(
                        TaskOperationDeniedKind.StatusPreconditionFailed,
                        "authoritative task is no longer archived",
                        taskId),
                    Clone(authoritative)));
            }

            return TrySetStatusAsync(
                taskId,
                authoritative.GetRestoreStatusAfterArchive(),
                author);
        }

        public TaskOperationResult CreateSuccess(
            string taskId,
            DomainTaskStatus requestedStatus,
            string? author)
        {
            var updated = Clone(_tasks[taskId]);
            updated.SetStatus(
                requestedStatus,
                DateTimeOffset.UtcNow,
                TaskItem.NormalizeAuthor(author));
            _tasks[taskId] = Clone(updated);
            return TaskOperationResult.Succeeded([Clone(updated)], null, null, null, Clone(updated));
        }

        public TaskOperationResult CreateFailure(string taskId, DomainTaskStatus requestedStatus) =>
            TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.StorageFailed,
                    "controlled status failure",
                    taskId,
                    requestedStatus),
                authoritativeTask: Clone(_tasks[taskId]));

        public Task Init()
        {
            Initiated?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) => Update(change.Model);

        public async Task<TaskItemViewModel> Update(TaskItem change)
        {
            UpdateCount++;
            if (UpdateHandler is not null)
            {
                await UpdateHandler(Clone(change));
            }

            _tasks[change.Id] = Clone(change);
            return null!;
        }

        public Task<TaskItemViewModel> Clone(
            TaskItemViewModel change,
            params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(
            TaskItemViewModel change,
            TaskItemViewModel[] additionalParents,
            TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();

        public void Dispose()
        {
            Tasks.Dispose();
        }

        private static TaskItem Clone(TaskItem task) => task with
        {
            StatusHistory = task.StatusHistory?
                .Select(static entry => entry == null
                    ? null!
                    : new TaskStatusHistoryEntry
                    {
                        Status = entry.Status,
                        ChangedAt = entry.ChangedAt,
                        Author = entry.Author,
                        ExtensionData = entry.ExtensionData
                    })
                .ToList() ?? [],
            CompletionCriteria = task.CompletionCriteria?
                .Select(static criterion => new TaskCompletionCriterion
                {
                    Id = criterion.Id,
                    Text = criterion.Text,
                    IsSatisfied = criterion.IsSatisfied,
                    ExtensionData = criterion.ExtensionData
                })
                .ToList() ?? [],
            ContainsTasks = task.ContainsTasks?.ToList() ?? [],
            ParentTasks = task.ParentTasks?.ToList() ?? [],
            BlocksTasks = task.BlocksTasks?.ToList() ?? [],
            BlockedByTasks = task.BlockedByTasks?.ToList() ?? []
        };
    }

    private sealed class BlockingErrorNotificationManager : INotificationManagerWrapper
    {
        public TaskCompletionSource ErrorToastEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseErrorToast { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Ask(string header, string message, Action yesAction, Action? noAction = null) =>
            noAction?.Invoke();

        public Task<bool> ConfirmTaskOutlinePasteAsync(TaskOutlinePastePreview preview) =>
            Task.FromResult(false);

        public void ErrorToast(string message)
        {
            ErrorToastEntered.TrySetResult();
            ReleaseErrorToast.Task.GetAwaiter().GetResult();
        }

        public void SuccessToast(string message)
        {
        }
    }

    private sealed class FakeSystemCultureProvider(string cultureName) : ILocalizationSystemCultureProvider
    {
        public CultureInfo SystemUICulture { get; } = CultureInfo.GetCultureInfo(cultureName);
    }

    private sealed class CultureSnapshot
    {
        private readonly CultureInfo _currentCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _currentUiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        public static CultureSnapshot Capture() => new();

        public void Restore()
        {
            CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
            CultureInfo.CurrentCulture = _currentCulture;
            CultureInfo.CurrentUICulture = _currentUiCulture;
        }
    }
}
