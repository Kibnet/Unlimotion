using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

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
    public async Task HandleTaskStatusChange_CompletedTaskWithRepeater_CreatesPreparedClone()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var task = new TaskItem
        {
            Id = "test-task",
            Status = DomainTaskStatus.Completed,
            Repeater = new RepeaterPattern
            {
                Type = RepeaterType.Daily,
                Period = 1
            },
            PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1),
            PlannedEndDateTime = DateTimeOffset.UtcNow,
            ContainsTasks = new List<string> { "child1" },
            BlocksTasks = new List<string> { "blocked1" },
            BlockedByTasks = new List<string> { "blocker1" },
            Description = "Test task",
            Title = "Test Task"
        };

        var result = await manager.HandleTaskStatusChange(task);
        var clonedTask = result.First(taskItem => taskItem.Id != task.Id && taskItem.Title == task.Title);

        await Assert.That(task.CompletedDateTime).IsNotNull();
        await Assert.That(task.ArchiveDateTime).IsNull();
        await Assert.That(result).Contains(task);
        await Assert.That(clonedTask.Id).IsNotNull();
        await Assert.That(clonedTask.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(clonedTask.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(clonedTask.Title).IsEqualTo(task.Title);
        await Assert.That(clonedTask.Description).IsEqualTo(task.Description);
        await Assert.That(clonedTask.ContainsTasks).IsEquivalentTo(task.ContainsTasks);
        await Assert.That(clonedTask.BlocksTasks).IsEquivalentTo(task.BlocksTasks);
        await Assert.That(clonedTask.BlockedByTasks).IsEquivalentTo(task.BlockedByTasks);
        await Assert.That(result).Contains(clonedTask);
    }

    [Test]
    public async Task HandleTaskStatusChange_CompletedTaskWithRepeater_ShouldSyncCloneRelationsAndAvailability()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var child = new TaskItem
        {
            Id = "child",
            Status = DomainTaskStatus.Completed,
            ParentTasks = new List<string> { "source" }
        };

        var blocker = new TaskItem
        {
            Id = "blocker",
            Status = DomainTaskStatus.Completed,
            BlocksTasks = new List<string> { "source" }
        };

        var blocked = new TaskItem
        {
            Id = "blocked",
            Status = DomainTaskStatus.NotReady,
            IsCanBeCompleted = true,
            UnlockedDateTime = DateTimeOffset.UtcNow,
            BlockedByTasks = new List<string> { "source" }
        };

        var source = new TaskItem
        {
            Id = "source",
            Status = DomainTaskStatus.Completed,
            Repeater = new RepeaterPattern
            {
                Type = RepeaterType.Daily,
                Period = 1
            },
            PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1),
            PlannedEndDateTime = DateTimeOffset.UtcNow,
            ContainsTasks = new List<string> { "child" },
            BlocksTasks = new List<string> { "blocked" },
            BlockedByTasks = new List<string> { "blocker" },
            Description = "Source description",
            Title = "Source title"
        };

        await storage.Save(child);
        await storage.Save(blocker);
        await storage.Save(blocked);
        await storage.Save(source);

        var result = await manager.HandleTaskStatusChange(source);

        var clone = result.FirstOrDefault(t => t.Id != source.Id && t.Title == source.Title);
        await Assert.That(clone).IsNotNull();

        var cloneFromStorage = await storage.Load(clone!.Id);
        var childFromStorage = await storage.Load(child.Id);
        var blockerFromStorage = await storage.Load(blocker.Id);
        var blockedFromStorage = await storage.Load(blocked.Id);

        await Assert.That(cloneFromStorage).IsNotNull();
        await Assert.That(cloneFromStorage!.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(cloneFromStorage.ContainsTasks).Contains(child.Id);
        await Assert.That(cloneFromStorage.BlockedByTasks).Contains(blocker.Id);
        await Assert.That(cloneFromStorage.BlocksTasks).Contains(blocked.Id);

        await Assert.That(childFromStorage).IsNotNull();
        await Assert.That(childFromStorage!.ParentTasks).Contains(clone.Id);

        await Assert.That(blockerFromStorage).IsNotNull();
        await Assert.That(blockerFromStorage!.BlocksTasks).Contains(clone.Id);

        await Assert.That(blockedFromStorage).IsNotNull();
        await Assert.That(blockedFromStorage!.BlockedByTasks).Contains(clone.Id);
        await Assert.That(blockedFromStorage.IsCanBeCompleted).IsFalse();

        await Assert.That(cloneFromStorage.Status).IsEqualTo(DomainTaskStatus.Prepared);
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

        viewModel.StatusOption = completedOption;

        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
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
