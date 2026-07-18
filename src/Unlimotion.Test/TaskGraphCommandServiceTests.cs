using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskGraphCommandServiceTests
{
    [Test]
    public async Task PublicResultFactories_PreservePreStage2ClrSignatures()
    {
        var legacyReason = TaskOperationDeniedReason.Create(
            TaskOperationDeniedKind.StorageFailed,
            "legacy",
            null);
        var legacyDenied = TaskOperationResult.Denied(legacyReason, null);
        var succeeded = typeof(TaskOperationResult).GetMethod(
            nameof(TaskOperationResult.Succeeded),
            [
                typeof(IReadOnlyList<TaskItem>),
                typeof(TaskAvailabilityAnalysis),
                typeof(TaskAvailabilityAnalysis),
                typeof(TaskGraphValidationReport)
            ]);
        var denied = typeof(TaskOperationResult).GetMethod(
            nameof(TaskOperationResult.Denied),
            [
                typeof(TaskOperationDeniedReason),
                typeof(TaskAvailabilityAnalysis),
                typeof(TaskAvailabilityAnalysis),
                typeof(TaskGraphValidationReport)
            ]);
        var deniedReason = typeof(TaskOperationDeniedReason).GetMethod(
            nameof(TaskOperationDeniedReason.Create),
            [
                typeof(TaskOperationDeniedKind),
                typeof(string),
                typeof(string),
                typeof(DomainTaskStatus?),
                typeof(string)
            ]);
        var statusCommand = typeof(ITaskStorage).GetMethod(
            nameof(ITaskStorage.TrySetStatusAsync),
            [typeof(string), typeof(DomainTaskStatus), typeof(string)]);
        var unarchiveCommand = typeof(ITaskStorage).GetMethod(
            nameof(ITaskStorage.TryUnarchiveAsync),
            [typeof(string), typeof(string)]);
        var confirm = typeof(INotificationManagerWrapper).GetMethod(
            nameof(INotificationManagerWrapper.ConfirmAsync),
            [typeof(string), typeof(string)]);

        await Assert.That(succeeded).IsNotNull();
        await Assert.That(legacyDenied.DeniedReason).IsSameReferenceAs(legacyReason);
        await Assert.That(denied).IsNotNull();
        await Assert.That(deniedReason).IsNotNull();
        await Assert.That(statusCommand).IsNotNull();
        await Assert.That(statusCommand!.IsAbstract).IsFalse();
        await Assert.That(unarchiveCommand).IsNotNull();
        await Assert.That(unarchiveCommand!.IsAbstract).IsFalse();
        await Assert.That(confirm).IsNotNull();
        await Assert.That(confirm!.IsAbstract).IsFalse();
        await Assert.That((int)TaskOperationDeniedKind.ValidationFailed).IsEqualTo(0);
        await Assert.That((int)TaskOperationDeniedKind.TaskNotFound).IsEqualTo(1);
        await Assert.That((int)TaskOperationDeniedKind.CriterionNotFound).IsEqualTo(2);
        await Assert.That((int)TaskOperationDeniedKind.StatusTransitionDenied).IsEqualTo(3);
        await Assert.That((int)TaskOperationDeniedKind.CompletedCriteriaImmutable).IsEqualTo(4);
        await Assert.That((int)TaskOperationDeniedKind.StorageFailed).IsEqualTo(5);
        await Assert.That((int)TaskOperationDeniedKind.OutcomeUnknown).IsEqualTo(6);
        await Assert.That((int)TaskOperationDeniedKind.StatusPreconditionFailed).IsEqualTo(7);
    }

    [Test]
    [Arguments(DomainTaskStatus.NotReady)]
    [Arguments(DomainTaskStatus.Prepared)]
    [Arguments(DomainTaskStatus.InProgress)]
    [Arguments(DomainTaskStatus.Completed)]
    [Arguments(DomainTaskStatus.Archived)]
    public async Task TrySetStatus_AllDefinedSameStatusValuesAreAuthoritativeNoOps(
        DomainTaskStatus status)
    {
        var task = CreateTask("same-status", status, title: "Persisted title");
        var storage = new DiagnosticStorage([task])
        {
            ReturnStoredReferences = true
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, status);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.ChangedTasks).IsEmpty();
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status).IsEqualTo(status);
        await Assert.That(storage.ReadCount).IsEqualTo(1);
        await Assert.That(storage.GetAllEnumerationCount).IsEqualTo(0);
        await Assert.That(storage.SaveCount).IsEqualTo(0);

        result.AuthoritativeTask.Title = "Mutated result";
        var persisted = await storage.Load(task.Id);
        await Assert.That(persisted!.Title).IsEqualTo("Persisted title");
    }

    [Test]
    public async Task TrySetStatus_InvalidSameValueIsDeniedBeforeNoOp()
    {
        var invalidStatus = (DomainTaskStatus)int.MaxValue;
        var task = CreateTask("invalid-same", invalidStatus);
        var storage = new DiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, invalidStatus);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind)
            .IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(result.DeniedReason?.StatusTransitionReason)
            .IsEqualTo(TaskStatusTransitionDenialReason.InvalidTargetStatus);
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status).IsEqualTo(invalidStatus);
        await Assert.That(storage.ReadCount).IsEqualTo(1);
        await Assert.That(storage.GetAllEnumerationCount).IsEqualTo(0);
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_NoOpPreservesNullStatusHistorySlotsInAuthoritativeClone()
    {
        var task = CreateTask("null-history", DomainTaskStatus.Prepared);
        task.StatusHistory =
        [
            task.StatusHistory[0],
            null!,
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.NotReady,
                ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                Author = "legacy"
            }
        ];
        var storage = new DiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.Prepared);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.StatusHistory.Count).IsEqualTo(3);
        await Assert.That(result.AuthoritativeTask.StatusHistory[1]).IsNull();
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_SameStatusDoesNotMaskGraphValidationFailure()
    {
        var first = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "first");
        var second = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "second");
        var storage = new DiagnosticStorage([first, second]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("duplicate", DomainTaskStatus.Prepared);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind)
            .IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        await Assert.That(result.AuthoritativeTask).IsNull();
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_SameStatusDoesNotMaskGraphReadFailure()
    {
        var task = CreateTask("read-failure", DomainTaskStatus.Prepared);
        var storage = new DiagnosticStorage([task])
        {
            ThrowOnReadAfterCount = 0
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.Prepared);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind)
            .IsEqualTo(TaskOperationDeniedKind.StorageFailed);
        await Assert.That(result.AuthoritativeTask).IsNull();
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_MissingTaskPrecedesInvalidTargetValidation()
    {
        var storage = new DiagnosticStorage([]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("missing", (DomainTaskStatus)int.MaxValue);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind)
            .IsEqualTo(TaskOperationDeniedKind.TaskNotFound);
        await Assert.That(result.DeniedReason?.StatusTransitionReason).IsNull();
        await Assert.That(result.AuthoritativeTask).IsNull();
        await Assert.That(storage.ReadCount).IsEqualTo(1);
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_UndefinedPersistedSourceCanRecoverToPrepared()
    {
        var task = CreateTask("undefined-source", (DomainTaskStatus)int.MaxValue);
        var storage = new DiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.Prepared, "tester");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status)
            .IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(storage.SaveCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrySetStatus_DeniedTransitionReturnsClonedAuthoritativeTaskWithoutWrite()
    {
        var task = CreateTask("terminal", DomainTaskStatus.Completed, title: "Persisted title");
        var storage = new DiagnosticStorage([task])
        {
            ReturnStoredReferences = true
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind)
            .IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(result.DeniedReason?.StatusTransitionReason)
            .IsEqualTo(TaskStatusTransitionDenialReason.TerminalCannotStart);
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status)
            .IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(storage.ReadCount).IsEqualTo(1);
        await Assert.That(storage.GetAllEnumerationCount).IsEqualTo(0);
        await Assert.That(storage.SaveCount).IsEqualTo(0);

        result.AuthoritativeTask.Title = "Mutated result";
        var persisted = await storage.Load(task.Id);
        await Assert.That(persisted!.Title).IsEqualTo("Persisted title");
    }

    [Test]
    public async Task TrySetStatus_NonLockingAllowedTransitionUsesBoundedGraphReads()
    {
        var task = CreateTask(
            "non-locking",
            DomainTaskStatus.Prepared,
            title: "Persisted title");
        var storage = new DiagnosticStorage([task])
        {
            ReturnStoredReferences = true
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status)
            .IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(storage.ReadCount).IsEqualTo(2);
        await Assert.That(storage.GetAllEnumerationCount).IsEqualTo(1);
        await Assert.That(storage.ReadCount + storage.GetAllEnumerationCount)
            .IsLessThanOrEqualTo(3);
        await Assert.That(storage.SaveCount).IsEqualTo(1);

        result.AuthoritativeTask.Title = "Mutated result";
        var persisted = await storage.Load(task.Id);
        await Assert.That(persisted!.Title).IsEqualTo("Persisted title");
    }

    [Test]
    public async Task TrySetStatus_LockingStorageUsesOneLockAndVerifiesInsideIt()
    {
        var task = CreateTask("locking", DomainTaskStatus.Prepared);
        var storage = new LockTrackingDiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Status)
            .IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(storage.LockCallCount).IsEqualTo(1);
        await Assert.That(storage.NestedLockAttempted).IsFalse();
        await Assert.That(storage.ReadCount).IsEqualTo(2);
        await Assert.That(storage.SaveCount).IsEqualTo(1);
        await Assert.That(storage.OperationObservedOutsideLock).IsFalse();
    }

    [Test]
    public async Task TrySetStatus_DeniedTransitionDoesNotChangeFileUpdatedTimeOrHistory()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        task.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Check outcome",
            IsSatisfied = false
        });

        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var filePath = Path.Combine(temp.DirectoryPath, task.Id);
        var beforeFile = await File.ReadAllTextAsync(filePath);
        var beforeTask = await storage.Load(task.Id, forced: true);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.Completed, "tester");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(await File.ReadAllTextAsync(filePath)).IsEqualTo(beforeFile);

        var afterTask = await storage.Load(task.Id, forced: true);
        await Assert.That(afterTask).IsNotNull();
        await Assert.That(afterTask!.UpdatedDateTime).IsEqualTo(beforeTask!.UpdatedDateTime);
        await Assert.That(afterTask.StatusHistory.Count).IsEqualTo(beforeTask.StatusHistory.Count);
        await Assert.That(afterTask.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task TrySetStatus_BlockedCompleteAndInProgressReturnStructuredDenied()
    {
        using var temp = TempTaskDirectory.Create();
        var blocker = CreateTask("blocker", DomainTaskStatus.Prepared);
        blocker.BlocksTasks.Add("blocked");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        blocked.BlockedByTasks.Add("blocker");

        var storage = CreateStorage(temp.DirectoryPath);
        await SaveTasks(storage, blocker, blocked);
        var service = new TaskGraphCommandService(storage);

        var complete = await service.TrySetStatusAsync(blocked.Id, DomainTaskStatus.Completed);
        var inProgress = await service.TrySetStatusAsync(blocked.Id, DomainTaskStatus.InProgress);

        await Assert.That(complete.Success).IsFalse();
        await Assert.That(complete.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(inProgress.Success).IsFalse();
        await Assert.That(inProgress.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
    }

    [Test]
    public async Task TrySetCriterion_CompletedTaskReturnsCompletedCriteriaImmutable()
    {
        using var temp = TempTaskDirectory.Create();
        var completed = CreateTask("completed", DomainTaskStatus.Completed);
        completed.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Done",
            IsSatisfied = false
        });

        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(completed);

        var result = await new TaskGraphCommandService(storage)
            .TrySetCriterionAsync(completed.Id, "criterion", satisfied: true);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.CompletedCriteriaImmutable);
    }

    [Test]
    public async Task TrySetCriterion_MissingTaskAndCriterionReturnStructuredDenied()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var service = new TaskGraphCommandService(storage);

        var missingTask = await service.TrySetCriterionAsync("missing", "criterion", satisfied: true);
        var missingCriterion = await service.TrySetCriterionAsync(task.Id, "missing", satisfied: true);

        await Assert.That(missingTask.Success).IsFalse();
        await Assert.That(missingTask.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.TaskNotFound);
        await Assert.That(missingCriterion.Success).IsFalse();
        await Assert.That(missingCriterion.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.CriterionNotFound);
    }

    [Test]
    public async Task TrySetStatus_DuplicateIdsReturnValidationFailureWithFilePaths()
    {
        using var temp = TempTaskDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "duplicate-a"), """
        {
          "Id": "duplicate",
          "Title": "Duplicate A",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "duplicate-b"), """
        {
          "Id": "duplicate",
          "Title": "Duplicate B",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);

        var result = await new TaskGraphCommandService(CreateStorage(temp.DirectoryPath))
            .TrySetStatusAsync("duplicate", DomainTaskStatus.Completed);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate-a", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate-b", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TrySetStatus_NonDiagnosticStorageReturnsStorageFailedWithoutSaving()
    {
        var storage = new CountingStorage();

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("task", DomainTaskStatus.Completed);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StorageFailed);
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_PostMutationDiagnosticFailureReturnsOutcomeUnknown()
    {
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        var storage = new DiagnosticStorage([task])
        {
            ThrowOnReadAfterCount = 1
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
        await Assert.That(storage.SaveCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrySetStatus_WriteLockFailureReturnsStructuredStorageFailure()
    {
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        var storage = new ThrowingWriteLockStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StorageFailed);
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetCriterion_DuplicateCriterionIdsBlockWrite()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        task.CompletionCriteria =
        [
            new TaskCompletionCriterion { Id = "duplicate", Text = "First" },
            new TaskCompletionCriterion { Id = "duplicate", Text = "Second" }
        ];
        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);

        var result = await new TaskGraphCommandService(storage)
            .TrySetCriterionAsync(task.Id, "duplicate", satisfied: true);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        var persisted = await storage.Load(task.Id, forced: true);
        await Assert.That(persisted!.CompletionCriteria.All(static criterion => !criterion.IsSatisfied)).IsTrue();
    }

    [Test]
    public async Task TrySetStatus_DiagnosticStorageDuplicateTasksWithoutDuplicateIssuesBlocksWrite()
    {
        var first = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "first");
        var second = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "second");
        var storage = new DiagnosticStorage([first, second]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("duplicate", DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate id: duplicate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_RepeatingCompletionReturnsOriginalCloneAndReverseLinkTasks()
    {
        using var temp = TempTaskDirectory.Create();
        var plannedBegin = DateTimeOffset.UtcNow.AddDays(-1);
        var plannedEnd = DateTimeOffset.UtcNow;

        var child = CreateTask("child", DomainTaskStatus.Completed);
        child.ParentTasks.Add("source");
        var blocker = CreateTask("blocker", DomainTaskStatus.Completed);
        blocker.BlocksTasks.Add("source");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        blocked.BlockedByTasks.Add("source");
        var source = CreateTask("source", DomainTaskStatus.Prepared, title: "Repeating source");
        source.ContainsTasks.Add(child.Id);
        source.BlockedByTasks.Add(blocker.Id);
        source.BlocksTasks.Add(blocked.Id);
        source.Repeater = new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1
        };
        source.PlannedBeginDateTime = plannedBegin;
        source.PlannedEndDateTime = plannedEnd;

        var storage = CreateStorage(temp.DirectoryPath);
        await SaveTasks(storage, child, blocker, blocked, source);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(source.Id, DomainTaskStatus.Completed, "tester");

        await Assert.That(result.Success).IsTrue();
        var changedIds = result.ChangedTasks.Select(static task => task.Id).ToHashSet(StringComparer.Ordinal);
        var allTasks = await LoadAllTasks(storage);
        var clone = allTasks.Single(task => task.Id != source.Id && task.Title == source.Title);
        await Assert.That(changedIds).Contains(source.Id);
        await Assert.That(changedIds).Contains(clone.Id);
        await Assert.That(changedIds).Contains(child.Id);
        await Assert.That(changedIds).Contains(blocker.Id);
        await Assert.That(changedIds).Contains(blocked.Id);

        var childAfter = await storage.Load(child.Id, forced: true);
        var blockerAfter = await storage.Load(blocker.Id, forced: true);
        var blockedAfter = await storage.Load(blocked.Id, forced: true);
        await Assert.That(childAfter!.ParentTasks).Contains(clone.Id);
        await Assert.That(blockerAfter!.BlocksTasks).Contains(clone.Id);
        await Assert.That(blockedAfter!.BlockedByTasks).Contains(clone.Id);
    }

    [Test]
    [Arguments(StaleGuardScenario.Graph)]
    [Arguments(StaleGuardScenario.FutureDate)]
    [Arguments(StaleGuardScenario.CompletionCriteria)]
    [Arguments(StaleGuardScenario.TerminalStatus)]
    public async Task TrySetStatus_ReevaluatesAuthoritativeStaleGuardState(
        StaleGuardScenario scenario)
    {
        var task = CreateTask($"stale-{scenario}", DomainTaskStatus.Prepared);
        var tasks = new List<TaskItem> { task };
        var requestedStatus = DomainTaskStatus.InProgress;
        TaskStatusTransitionDenialReason expectedTransitionReason;
        TaskAvailabilityReasonKind expectedAvailabilityReason;

        switch (scenario)
        {
            case StaleGuardScenario.Graph:
                var blocker = CreateTask("stale-graph-blocker", DomainTaskStatus.Prepared);
                blocker.BlocksTasks.Add(task.Id);
                task.BlockedByTasks.Add(blocker.Id);
                tasks.Add(blocker);
                expectedTransitionReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart;
                expectedAvailabilityReason = TaskAvailabilityReasonKind.IncompleteDirectBlocker;
                break;
            case StaleGuardScenario.FutureDate:
                task.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(1);
                expectedTransitionReason = TaskStatusTransitionDenialReason.FutureDatePreventsStart;
                expectedAvailabilityReason = TaskAvailabilityReasonKind.FuturePlannedBegin;
                break;
            case StaleGuardScenario.CompletionCriteria:
                requestedStatus = DomainTaskStatus.Completed;
                task.CompletionCriteria.Add(new TaskCompletionCriterion
                {
                    Id = "stale-criterion",
                    Text = "Still pending",
                    IsSatisfied = false
                });
                expectedTransitionReason = TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete;
                expectedAvailabilityReason = TaskAvailabilityReasonKind.UnsatisfiedCriterion;
                break;
            case StaleGuardScenario.TerminalStatus:
                task.Status = DomainTaskStatus.Completed;
                task.StatusHistory[^1].Status = DomainTaskStatus.Completed;
                expectedTransitionReason = TaskStatusTransitionDenialReason.TerminalCannotStart;
                expectedAvailabilityReason = TaskAvailabilityReasonKind.AlreadyCompleted;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var storage = new DiagnosticStorage(tasks);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, requestedStatus, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.StatusTransitionReason)
                .IsEqualTo(expectedTransitionReason);
            await Assert.That(result.Before?.Reasons.Any(reason =>
                    reason.Kind == expectedAvailabilityReason))
                .IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(task.Status);
            await Assert.That(storage.SaveCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task TrySetStatus_SaveFailureReturnsOutcomeUnknownWithoutFalseSuccess()
    {
        var task = CreateTask("save-failure", DomainTaskStatus.Prepared);
        var storage = new DiagnosticStorage([task])
        {
            ThrowOnSave = true
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress, "tester");
        var persisted = await storage.Load(task.Id);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(result.Before).IsNotNull();
            await Assert.That(result.AuthoritativeTask).IsNull();
            await Assert.That(storage.SaveCount).IsEqualTo(1);
            await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Prepared);
        }
    }

    [Test]
    public async Task TryUnarchive_ResolvesTargetFromAuthoritativeHistoryAtCommandTime()
    {
        var now = DateTimeOffset.UtcNow;
        var task = CreateTask("authoritative-unarchive", DomainTaskStatus.Archived);
        task.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.InProgress,
                ChangedAt = now.AddHours(-3),
                Author = "old-cache"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Completed,
                ChangedAt = now.AddHours(-2),
                Author = "authoritative"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = now.AddHours(-1),
                Author = "authoritative"
            }
        ];
        var initialHistoryCount = task.StatusHistory.Count;
        var storage = new DiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TryUnarchiveAsync(task.Id, "tester");
        var persisted = await storage.Load(task.Id);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(persisted?.StatusHistory.Count).IsEqualTo(initialHistoryCount + 1);
            await Assert.That(persisted?.StatusHistory[^1].Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(persisted?.StatusHistory[^1].Author).IsEqualTo("tester");
            await Assert.That(storage.ReadCount).IsEqualTo(2);
            await Assert.That(storage.SaveCount).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(DomainTaskStatus.NotReady)]
    [Arguments(DomainTaskStatus.Prepared)]
    [Arguments(DomainTaskStatus.InProgress)]
    [Arguments(DomainTaskStatus.Completed)]
    public async Task TryUnarchive_AuthoritativeNonArchivedStatusFailsPreconditionWithoutWrite(
        DomainTaskStatus authoritativeStatus)
    {
        var task = CreateTask("stale-unarchive", authoritativeStatus);
        var storage = new DiagnosticStorage([task]);

        var result = await new TaskGraphCommandService(storage)
            .TryUnarchiveAsync(task.Id, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.StatusPreconditionFailed);
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(authoritativeStatus);
            await Assert.That(result.ChangedTasks).IsEmpty();
            await Assert.That(storage.ReadCount).IsEqualTo(1);
            await Assert.That(storage.SaveCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task TryUnarchive_ConcurrentLockedCommandsProduceOneWriteAndOnePreconditionFailure()
    {
        using var temp = TempTaskDirectory.Create();
        var now = DateTimeOffset.UtcNow;
        var task = CreateTask("concurrent-unarchive", DomainTaskStatus.Archived);
        task.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.InProgress,
                ChangedAt = now.AddHours(-2),
                Author = "seed"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = now.AddHours(-1),
                Author = "seed"
            }
        ];
        var initialHistoryCount = task.StatusHistory.Count;
        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var firstService = new TaskGraphCommandService(storage);
        var secondService = new TaskGraphCommandService(storage);

        var results = await Task.WhenAll(
            firstService.TryUnarchiveAsync(task.Id, "first"),
            secondService.TryUnarchiveAsync(task.Id, "second"));
        var persisted = await storage.Load(task.Id, forced: true);

        using (Assert.Multiple())
        {
            await Assert.That(results.Count(static result => result.Success)).IsEqualTo(1);
            await Assert.That(results.Count(result =>
                    result.DeniedReason?.Kind == TaskOperationDeniedKind.StatusPreconditionFailed))
                .IsEqualTo(1);
            await Assert.That(results.Sum(static result => result.ChangedTasks.Count)).IsEqualTo(1);
            await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(persisted?.StatusHistory.Count).IsEqualTo(initialHistoryCount + 1);
            await Assert.That(persisted?.StatusHistory.Count(entry =>
                    entry is not null && entry.Status == DomainTaskStatus.Prepared))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task TrySetStatus_ConcurrentLockedCommandsProduceOneWriteAndOneNoOp()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("concurrent-status", DomainTaskStatus.Prepared);
        var initialHistoryCount = task.StatusHistory.Count;
        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var firstService = new TaskGraphCommandService(storage);
        var secondService = new TaskGraphCommandService(storage);

        var results = await Task.WhenAll(
            firstService.TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress, "first"),
            secondService.TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress, "second"));
        var persisted = await storage.Load(task.Id, forced: true);

        using (Assert.Multiple())
        {
            await Assert.That(results.All(static result => result.Success)).IsTrue();
            await Assert.That(results.Sum(static result => result.ChangedTasks.Count)).IsEqualTo(1);
            await Assert.That(persisted).IsNotNull();
            await Assert.That(persisted!.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(persisted.StatusHistory.Count).IsEqualTo(initialHistoryCount + 1);
            await Assert.That(persisted.StatusHistory.Count(entry =>
                    entry is not null && entry.Status == DomainTaskStatus.InProgress))
                .IsEqualTo(1);
        }
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted = true,
        string? title = null)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        return new TaskItem
        {
            Id = id,
            UserId = "test-user",
            Title = title ?? id,
            Description = string.Empty,
            Status = status,
            IsCanBeCompleted = isCanBeCompleted,
            CreatedDateTime = createdAt,
            UnlockedDateTime = isCanBeCompleted ? createdAt : null,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = status,
                    ChangedAt = createdAt,
                    Author = "seed"
                }
            ]
        };
    }

    private static FileTaskStorage CreateStorage(string directory) => new(new FileTaskStorageOptions
    {
        Path = directory,
        PreserveUnknownJson = true,
        UseDirectoryLock = true
    });

    private static async Task SaveTasks(FileTaskStorage storage, params TaskItem[] tasks)
    {
        foreach (var task in tasks)
        {
            await storage.Save(task);
        }
    }

    private static async Task<IReadOnlyList<TaskItem>> LoadAllTasks(FileTaskStorage storage)
    {
        var tasks = new List<TaskItem>();
        await foreach (var task in storage.GetAll())
        {
            tasks.Add(task);
        }

        return tasks;
    }

    private sealed class CountingStorage : IStorage
    {
        public int SaveCount { get; private set; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            SaveCount++;
            return Task.FromResult(item);
        }

        public Task<bool> Remove(string itemId) => Task.FromResult(true);

        public Task<TaskItem?> Load(string itemId) => Task.FromResult<TaskItem?>(null);

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;
    }

    private sealed class DiagnosticStorage : IStorage, ITaskGraphDiagnosticStorage
    {
        private readonly List<TaskItem> _tasks;
        private int _readCount;

        public DiagnosticStorage(IEnumerable<TaskItem> tasks)
        {
            _tasks = tasks.Select(CloneTask).ToList();
        }

        public int SaveCount { get; private set; }
        public int ReadCount => _readCount;
        public int GetAllEnumerationCount { get; private set; }
        public int? ThrowOnReadAfterCount { get; init; }
        public bool ThrowOnSave { get; init; }
        public bool ReturnStoredReferences { get; init; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskGraphReadResult> ReadGraphAsync()
        {
            if (ThrowOnReadAfterCount.HasValue && _readCount >= ThrowOnReadAfterCount.Value)
            {
                throw new TimeoutException("diagnostic read timed out");
            }

            _readCount++;
            var tasks = ReturnStoredReferences
                ? _tasks.ToArray()
                : _tasks.Select(CloneTask).ToArray();
            return Task.FromResult(new TaskGraphReadResult(
                tasks,
                _tasks
                    .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
                    .GroupBy(static task => task.Id, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => $"memory:{group.Key}", StringComparer.Ordinal),
                Array.Empty<TaskGraphLoadError>(),
                Array.Empty<TaskGraphDuplicateIdIssue>()));
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            SaveCount++;
            if (ThrowOnSave)
            {
                throw new IOException("simulated save failure");
            }

            var clone = CloneTask(item);
            var index = _tasks.FindIndex(task => string.Equals(task.Id, item.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _tasks[index] = clone;
            }
            else
            {
                _tasks.Add(clone);
            }

            return Task.FromResult(CloneTask(clone));
        }

        public Task<bool> Remove(string itemId)
        {
            _tasks.RemoveAll(task => string.Equals(task.Id, itemId, StringComparison.Ordinal));
            return Task.FromResult(true);
        }

        public Task<TaskItem?> Load(string itemId)
        {
            var task = _tasks.LastOrDefault(task => string.Equals(task.Id, itemId, StringComparison.Ordinal));
            return Task.FromResult(task == null ? null : CloneTask(task));
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            GetAllEnumerationCount++;
            foreach (var task in _tasks)
            {
                yield return CloneTask(task);
            }

            await Task.CompletedTask;
        }

        public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            foreach (var taskItem in taskItems)
            {
                await Save(taskItem);
            }
        }

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;
    }

    private sealed class LockTrackingDiagnosticStorage :
        IStorage,
        ITaskGraphDiagnosticStorage,
        ITaskGraphWriteLock
    {
        private readonly AsyncLocal<int> _lockDepth = new();
        private readonly DiagnosticStorage _inner;

        public LockTrackingDiagnosticStorage(IEnumerable<TaskItem> tasks)
        {
            _inner = new DiagnosticStorage(tasks);
        }

        public int LockCallCount { get; private set; }
        public bool NestedLockAttempted { get; private set; }
        public bool OperationObservedOutsideLock { get; private set; }
        public int ReadCount => _inner.ReadCount;
        public int SaveCount => _inner.SaveCount;

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public async Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation)
        {
            LockCallCount++;
            if (_lockDepth.Value > 0)
            {
                NestedLockAttempted = true;
                throw new InvalidOperationException("Nested write lock acquisition is forbidden.");
            }

            _lockDepth.Value++;
            try
            {
                return await operation();
            }
            finally
            {
                _lockDepth.Value--;
            }
        }

        public Task<TaskGraphReadResult> ReadGraphAsync()
        {
            TrackLockScope();
            return _inner.ReadGraphAsync();
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            TrackLockScope();
            return _inner.Save(item);
        }

        public Task<bool> Remove(string itemId)
        {
            TrackLockScope();
            return _inner.Remove(itemId);
        }

        public Task<TaskItem?> Load(string itemId)
        {
            TrackLockScope();
            return _inner.Load(itemId);
        }

        public IAsyncEnumerable<TaskItem> GetAll()
        {
            TrackLockScope();
            return _inner.GetAll();
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            TrackLockScope();
            return _inner.BulkInsert(taskItems);
        }

        public Task<bool> Connect() => _inner.Connect();

        public Task Disconnect() => _inner.Disconnect();

        private void TrackLockScope()
        {
            OperationObservedOutsideLock |= _lockDepth.Value == 0;
        }
    }

    private sealed class ThrowingWriteLockStorage : IStorage, ITaskGraphDiagnosticStorage, ITaskGraphWriteLock
    {
        private readonly DiagnosticStorage _inner;

        public ThrowingWriteLockStorage(IEnumerable<TaskItem> tasks)
        {
            _inner = new DiagnosticStorage(tasks);
        }

        public int SaveCount => _inner.SaveCount;

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation) =>
            throw new IOException("simulated lock failure");

        public Task<TaskGraphReadResult> ReadGraphAsync() => _inner.ReadGraphAsync();

        public Task<TaskItem> Save(TaskItem item) => _inner.Save(item);

        public Task<bool> Remove(string itemId) => _inner.Remove(itemId);

        public Task<TaskItem?> Load(string itemId) => _inner.Load(itemId);

        public IAsyncEnumerable<TaskItem> GetAll() => _inner.GetAll();

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => _inner.BulkInsert(taskItems);

        public Task<bool> Connect() => _inner.Connect();

        public Task Disconnect() => _inner.Disconnect();
    }

    private static TaskItem CloneTask(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory?
            .Select(entry => entry == null
                ? null!
                : new TaskStatusHistoryEntry
                {
                    Status = entry.Status,
                    ChangedAt = entry.ChangedAt,
                    Author = entry.Author,
                    ExtensionData = entry.ExtensionData
                })
            .ToList() ?? new List<TaskStatusHistoryEntry>(),
        CompletionCriteria = task.CompletionCriteria?
            .Select(criterion => new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied,
                ExtensionData = criterion.ExtensionData
            })
            .ToList() ?? new List<TaskCompletionCriterion>(),
        ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
        ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
        BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
        BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>()
    };

    public enum StaleGuardScenario
    {
        Graph,
        FutureDate,
        CompletionCriteria,
        TerminalStatus
    }

    private sealed class TempTaskDirectory : IDisposable
    {
        private TempTaskDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TempTaskDirectory Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "unlimotion-command-service-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempTaskDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
