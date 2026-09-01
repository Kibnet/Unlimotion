using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using ReactiveUI;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskStatusTransitionTests
{
    [Test]
    public async Task HandleTaskStatusChange_CompletedTask_AddsCompletedHistoryEntry()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Completed
        };

        var result = await manager.HandleTaskStatusChange(task);

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(task.CompletedDateTime).IsNotNull();
        await Assert.That(task.ArchiveDateTime).IsNull();
        await Assert.That(task.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(result).Contains(task);
    }

    [Test]
    public async Task HandleTaskStatusChange_NotReadyTask_LeavesStatusDatesEmpty()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.NotReady
        };

        var result = await manager.HandleTaskStatusChange(task);

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.NotReady);
        await Assert.That(task.CompletedDateTime).IsNull();
        await Assert.That(task.ArchiveDateTime).IsNull();
        await Assert.That(result).Contains(task);
    }

    [Test]
    public async Task HandleTaskStatusChange_ArchivedTask_AddsArchiveHistoryEntry()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Archived
        };

        var result = await manager.HandleTaskStatusChange(task);

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(task.ArchiveDateTime).IsNotNull();
        await Assert.That(task.CompletedDateTime).IsNull();
        await Assert.That(task.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(result).Contains(task);
    }

    [Test]
    public async Task HandleTaskStatusChange_CompletedRepeater_ClonesContainedDagAndInternalRelations()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var plannedBegin = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        var futureRightBegin = DateTimeOffset.UtcNow.AddDays(30);
        var externalBlocker = new TaskItem
        {
            Id = "external-blocker",
            Title = "external blocker",
            Status = DomainTaskStatus.Completed,
            BlocksTasks = ["source"]
        };
        var externalBlocked = new TaskItem
        {
            Id = "external-blocked",
            Title = "external blocked",
            Status = DomainTaskStatus.NotReady,
            BlockedByTasks = ["source"]
        };
        var sharedLeaf = new TaskItem
        {
            Id = "shared-leaf",
            Title = "shared leaf",
            Status = DomainTaskStatus.Completed,
            ParentTasks = ["left", "right"],
            PlannedBeginDateTime = plannedBegin.AddHours(3),
            PlannedEndDateTime = plannedBegin.AddHours(4),
            CompletionCriteria =
            [
                new TaskCompletionCriterion
                {
                    Id = "old-criterion",
                    Text = "Verify leaf",
                    IsSatisfied = true
                }
            ]
        };
        var left = new TaskItem
        {
            Id = "left",
            Title = "left",
            Status = DomainTaskStatus.Completed,
            ParentTasks = ["source"],
            ContainsTasks = [sharedLeaf.Id],
            BlocksTasks = ["right"],
            PlannedBeginDateTime = plannedBegin.AddHours(1)
        };
        var right = new TaskItem
        {
            Id = "right",
            Title = "right",
            Status = DomainTaskStatus.Completed,
            ParentTasks = ["source"],
            ContainsTasks = [sharedLeaf.Id],
            BlockedByTasks = ["left"],
            PlannedBeginDateTime = futureRightBegin
        };
        var source = new TaskItem
        {
            Id = "source",
            Title = "source",
            Description = "Source description",
            Status = DomainTaskStatus.Prepared,
            IsCanBeCompleted = true,
            Repeater = new RepeaterPattern
            {
                Type = RepeaterType.Daily,
                Period = 1,
                AfterComplete = false,
                Pattern = [1, 3]
            },
            PlannedBeginDateTime = plannedBegin,
            PlannedEndDateTime = plannedBegin.AddHours(8),
            ContainsTasks = [left.Id, right.Id],
            BlocksTasks = [externalBlocked.Id],
            BlockedByTasks = [externalBlocker.Id]
        };

        await storage.Save(externalBlocker);
        await storage.Save(externalBlocked);
        await storage.Save(sharedLeaf);
        await storage.Save(left);
        await storage.Save(right);
        await storage.Save(source);
        source.Status = DomainTaskStatus.Completed;

        var result = await manager.HandleTaskStatusChange(source);
        var cloneRoot = result.Single(item => item.Id != source.Id && item.Title == source.Title);
        var cloneLeft = result.Single(item => item.Title == left.Title && item.Id != left.Id);
        var cloneRight = result.Single(item => item.Title == right.Title && item.Id != right.Id);
        var cloneLeaf = result.Single(item => item.Title == sharedLeaf.Title && item.Id != sharedLeaf.Id);

        using (Assert.Multiple())
        {
            await Assert.That(cloneRoot.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(cloneRoot.StatusHistory).Count().IsEqualTo(1);
            await Assert.That(cloneRoot.StatusHistory[0].Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(cloneRoot.Repeater).IsNotNull();
            await Assert.That(cloneRoot.Repeater!.Type).IsEqualTo(RepeaterType.Daily);
            await Assert.That(cloneRoot.Repeater).IsNotSameReferenceAs(source.Repeater);
            await Assert.That(cloneRoot.ContainsTasks).IsEquivalentTo([cloneLeft.Id, cloneRight.Id]);
            await Assert.That(cloneRoot.BlocksTasks).IsEmpty();
            await Assert.That(cloneRoot.BlockedByTasks).IsEmpty();
            await Assert.That(cloneRoot.IsCanBeCompleted).IsFalse();
            await Assert.That(cloneRoot.UnlockedDateTime).IsNull();
            await Assert.That(cloneRoot.CompletedDateTime).IsNull();
            await Assert.That(cloneRoot.ArchiveDateTime).IsNull();

            await Assert.That(cloneLeft.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneRight.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneLeaf.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneLeft.StatusHistory).Count().IsEqualTo(1);
            await Assert.That(cloneRight.StatusHistory).Count().IsEqualTo(1);
            await Assert.That(cloneLeaf.StatusHistory).Count().IsEqualTo(1);
            await Assert.That(cloneLeft.StatusHistory[0].Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneRight.StatusHistory[0].Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneLeaf.StatusHistory[0].Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cloneLeft.IsCanBeCompleted).IsFalse();
            await Assert.That(cloneRight.IsCanBeCompleted).IsFalse();
            await Assert.That(cloneLeaf.IsCanBeCompleted).IsFalse();
            await Assert.That(cloneLeft.UnlockedDateTime).IsNull();
            await Assert.That(cloneRight.UnlockedDateTime).IsNull();
            await Assert.That(cloneLeaf.UnlockedDateTime).IsNull();
            await Assert.That(cloneLeaf.CompletedDateTime).IsNull();
            await Assert.That(cloneLeaf.ArchiveDateTime).IsNull();
            await Assert.That(cloneLeaf.ParentTasks).IsEquivalentTo([cloneLeft.Id, cloneRight.Id]);
            await Assert.That(cloneLeft.ContainsTasks).IsEquivalentTo([cloneLeaf.Id]);
            await Assert.That(cloneRight.ContainsTasks).IsEquivalentTo([cloneLeaf.Id]);
            await Assert.That(cloneLeft.BlocksTasks).IsEquivalentTo([cloneRight.Id]);
            await Assert.That(cloneRight.BlockedByTasks).IsEquivalentTo([cloneLeft.Id]);

            await Assert.That(cloneRoot.PlannedBeginDateTime).IsEqualTo(plannedBegin.AddDays(1));
            await Assert.That(cloneRoot.PlannedEndDateTime).IsEqualTo(plannedBegin.AddDays(1).AddHours(8));
            await Assert.That(cloneLeft.PlannedEndDateTime).IsNull();
            await Assert.That(cloneRight.PlannedBeginDateTime).IsEqualTo(futureRightBegin.AddDays(1));
            await Assert.That(cloneLeaf.PlannedBeginDateTime).IsEqualTo(sharedLeaf.PlannedBeginDateTime!.Value.AddDays(1));
            await Assert.That(cloneLeaf.PlannedEndDateTime).IsEqualTo(sharedLeaf.PlannedEndDateTime!.Value.AddDays(1));
            await Assert.That(cloneLeaf.CompletionCriteria).Count().IsEqualTo(1);
            await Assert.That(cloneLeaf.CompletionCriteria[0].Id).IsNotEqualTo("old-criterion");
            await Assert.That(cloneLeaf.CompletionCriteria[0].Text).IsEqualTo("Verify leaf");
            await Assert.That(cloneLeaf.CompletionCriteria[0].IsSatisfied).IsFalse();
        }

        cloneRoot.Repeater!.Period = 7;
        cloneRoot.Repeater.Pattern.Add(6);
        await Assert.That(source.Repeater!.Period).IsEqualTo(1);
        await Assert.That(source.Repeater.Pattern).IsEquivalentTo([1, 3]);

        var externalBlockerAfter = await storage.Load(externalBlocker.Id);
        var externalBlockedAfter = await storage.Load(externalBlocked.Id);
        await Assert.That(externalBlockerAfter!.BlocksTasks).IsEquivalentTo([source.Id]);
        await Assert.That(externalBlockedAfter!.BlockedByTasks).IsEquivalentTo([source.Id]);
    }

    [Test]
    public async Task UpdateTask_InProgressTaskWithFutureBegin_RollsBackToPrepared()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.InProgress,
            PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(1)
        };

        await storage.Save(task);

        task.Title = "v2";
        await manager.UpdateTask(task);

        var saved = await storage.Load(task.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(saved.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task UpdateTask_InProgressTaskWithUnavailableFlag_RollsBackToPrepared()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.InProgress,
            IsCanBeCompleted = false
        };
        task.EnsureStatusHistory();
        await storage.Save(task);

        task.Title = "v2";
        await manager.UpdateTask(task);

        var saved = await storage.Load(task.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(saved.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(saved.StatusHistory.Last().Author).IsEqualTo("System");
    }

    [Test]
    public async Task HandleTaskStatusChange_InProgressTaskWithUnsatisfiedCriteria_IsAllowed()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var existing = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Prepared,
            IsCanBeCompleted = true,
            CompletionCriteria =
            [
                new TaskCompletionCriterion
                {
                    Text = "Проверить результат",
                    IsSatisfied = false
                }
            ]
        };
        existing.EnsureStatusHistory("owner");
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.InProgress,
            IsCanBeCompleted = true,
            CompletionCriteria = existing.CompletionCriteria
        };

        await manager.UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(saved.StartedDateTime).IsNotNull();
        await Assert.That(saved.CompletedDateTime).IsNull();
        await Assert.That(saved.StatusHistory.Select(entry => entry.Status))
            .IsEquivalentTo([DomainTaskStatus.Prepared, DomainTaskStatus.InProgress]);
    }

    [Test]
    public async Task HandleTaskStatusChange_CompletedTaskWithUnsatisfiedCriteria_IsRejected()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var existing = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Prepared,
            CompletionCriteria = new List<TaskCompletionCriterion>
            {
                new()
                {
                    Text = "Проверить результат",
                    IsSatisfied = false
                }
            }
        };
        existing.EnsureStatusHistory();
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Completed,
            CompletionCriteria = existing.CompletionCriteria
        };

        var result = await manager.UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(saved.CompletedDateTime).IsNull();
        await Assert.That(result.Single(task => task.Id == existing.Id).Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task HandleTaskStatusChange_CompletedTaskToArchived_IsRejected()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var existing = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Completed
        };
        existing.EnsureStatusHistory("owner");
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Archived
        };

        var result = await manager.UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(saved.ArchiveDateTime).IsNull();
        await Assert.That(result.Single(task => task.Id == existing.Id).Status).IsEqualTo(DomainTaskStatus.Completed);
    }

    [Test]
    public async Task HandleTaskStatusChange_ArchivedTaskToCompleted_IsRejected()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var existing = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Archived
        };
        existing.EnsureStatusHistory("owner");
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Completed
        };

        var result = await manager.UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(saved.CompletedDateTime).IsNull();
        await Assert.That(result.Single(task => task.Id == existing.Id).Status).IsEqualTo(DomainTaskStatus.Archived);
    }

    [Test]
    public async Task HandleTaskStatusChange_JumpToCompleted_AppendsOnlyRequestedStatus()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var existing = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.NotReady
        };
        existing.EnsureStatusHistory("owner");
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Completed
        };

        await manager.UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.StatusHistory.Select(entry => entry.Status))
            .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Completed]);
        await Assert.That(saved.StatusHistory.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TaskItemViewModel_StatusOptions_DisablesCompletedWhenCriteriaUnsatisfied()
    {
        var storage = new InMemoryStorage();
        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Prepared,
            CompletionCriteria =
            [
                new TaskCompletionCriterion
                {
                    Text = "Проверить результат",
                    IsSatisfied = false
                }
            ]
        };

        var viewModel = new TaskItemViewModel(
            task,
            new UnifiedTaskStorage(new TaskTreeManager(storage)),
            () => false);
        var completedOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed);
        var inProgressOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.InProgress);

        await Assert.That(completedOption.IsEnabled).IsFalse();
        await Assert.That(inProgressOption.IsEnabled).IsTrue();

        await viewModel.TryTransitionToStatusAsync(completedOption.Status);

        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task TaskItemViewModel_StatusOptions_DisablesCompletedWhenArchived()
    {
        var storage = new InMemoryStorage();
        var viewModel = new TaskItemViewModel(
            new TaskItem
            {
                Id = "test-task",
                Status = DomainTaskStatus.Archived,
                IsCanBeCompleted = true
            },
            new UnifiedTaskStorage(new TaskTreeManager(storage)),
            () => false);
        var completedOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed);
        var preparedOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.Prepared);

        await Assert.That(completedOption.IsEnabled).IsFalse();
        await Assert.That(completedOption.ToolTip).IsNotNull();
        await Assert.That(completedOption.ToolTip).IsNotEqualTo(completedOption.Title);
        await Assert.That(preparedOption.IsEnabled).IsTrue();

        await viewModel.TryTransitionToStatusAsync(completedOption.Status);

        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Archived);
    }

    [Test]
    public async Task TaskItemViewModel_StatusOptions_EnablesCompletedWhenCriterionBecomesSatisfied()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var storage = new InMemoryStorage();
        using var taskStorage = new UnifiedTaskStorage(new TaskTreeManager(storage));
        TaskItemViewModel viewModel = null!;
        TaskStatusOption completedOption = null!;
        try
        {
            await session.DispatchAsync(async () =>
            {
                var criterion = new TaskCompletionCriterion
                {
                    Text = "Проверить результат",
                    IsSatisfied = false
                };
                var task = new TaskItem
                {
                    Id = "test-task",
                    Status = DomainTaskStatus.Prepared,
                    IsCanBeCompleted = true,
                    CompletionCriteria = [criterion]
                };
                await storage.Save(task);
                viewModel = new TaskItemViewModel(
                    task,
                    taskStorage,
                    () => false);
                completedOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed);

                await Assert.That(completedOption.IsEnabled).IsFalse();

                viewModel.CompletionCriteria.Single().IsSatisfied = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(await TestHelpers.WaitUntilAsync(
                        () =>
                        {
                            Dispatcher.UIThread.RunJobs();
                            return completedOption.IsEnabled;
                        },
                        TimeSpan.FromSeconds(2)))
                    .IsTrue();

                await storage.Save(viewModel.Model);
            }, CancellationToken.None);

            await viewModel.TryTransitionToStatusAsync(completedOption.Status);

            await session.DispatchAsync(async () =>
            {
                await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task TaskItemViewModel_InProgressElapsed_UsesTotalHoursAndRefreshesPeriodically()
    {
        var previousInterval = TaskItemViewModel.InProgressElapsedRefreshInterval;
        var previousScheduler = TaskItemViewModel.InProgressElapsedRefreshScheduler;
        TaskItemViewModel.InProgressElapsedRefreshInterval = TimeSpan.FromMilliseconds(10);
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        try
        {
            await session.DispatchAsync(async () =>
            {
                TaskItemViewModel.InProgressElapsedRefreshScheduler = RxSchedulers.MainThreadScheduler;
                var startedAt = DateTimeOffset.Now.AddHours(-26).AddMinutes(-5);
                using var viewModel = new TaskItemViewModel(
                    new TaskItem
                    {
                        Id = "elapsed-task",
                        Title = "Elapsed task",
                        Status = DomainTaskStatus.InProgress,
                        StatusHistory =
                        [
                            new TaskStatusHistoryEntry
                            {
                                Status = DomainTaskStatus.InProgress,
                                ChangedAt = startedAt,
                                Author = "owner"
                            }
                        ]
                    },
                    new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                    () => false);

                var notificationCount = 0;
                var notificationWasOnUiThread = 0;
                ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(TaskItemViewModel.InProgressElapsed))
                    {
                        if (Dispatcher.UIThread.CheckAccess())
                        {
                            Interlocked.Exchange(ref notificationWasOnUiThread, 1);
                        }

                        Interlocked.Increment(ref notificationCount);
                    }
                };

                await Assert.That(viewModel.InProgressElapsed.StartsWith("26:", StringComparison.Ordinal)).IsTrue();
                await Assert.That(viewModel.InProgressElapsed.StartsWith("02:", StringComparison.Ordinal)).IsFalse();

                var refreshed = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        return Volatile.Read(ref notificationCount) > 0;
                    },
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(10));

                await Assert.That(refreshed).IsTrue();
                await Assert.That(Volatile.Read(ref notificationWasOnUiThread)).IsEqualTo(1);
            }, CancellationToken.None);
        }
        finally
        {
            TaskItemViewModel.InProgressElapsedRefreshInterval = previousInterval;
            TaskItemViewModel.InProgressElapsedRefreshScheduler = previousScheduler;
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task TaskItemViewModel_StatusOptions_DisablesInProgressWhenPlannedBeginIsFuture()
    {
        var storage = new InMemoryStorage();
        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Prepared,
            PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(1)
        };

        var viewModel = new TaskItemViewModel(
            task,
            new UnifiedTaskStorage(new TaskTreeManager(storage)),
            () => false);
        var preparedOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.Prepared);
        var inProgressOption = viewModel.StatusOptions.Single(option => option.Status == DomainTaskStatus.InProgress);

        await Assert.That(preparedOption.IsEnabled).IsTrue();
        await Assert.That(inProgressOption.IsEnabled).IsFalse();
    }

    [Test]
    public async Task TaskItemViewModel_CompletedTask_DisablesCompletionCriteriaEditing()
    {
        var storage = new InMemoryStorage();
        var viewModel = new TaskItemViewModel(
            new TaskItem
            {
                Id = "test-task",
                Status = DomainTaskStatus.Completed
            },
            new UnifiedTaskStorage(new TaskTreeManager(storage)),
            () => false);

        await Assert.That(viewModel.CanEditCompletionCriteria).IsFalse();
        await Assert.That(viewModel.AddCompletionCriterionCommand.CanExecute(null)).IsFalse();
    }
}
