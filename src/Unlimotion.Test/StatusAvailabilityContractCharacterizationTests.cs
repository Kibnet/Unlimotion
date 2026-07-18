using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class StatusAvailabilityContractCharacterizationTests
{
    [Test]
    [Arguments(DomainTaskStatus.Completed)]
    [Arguments(DomainTaskStatus.Archived)]
    public async Task TerminalTask_InProgressPreview_IsDisabled(DomainTaskStatus sourceStatus)
    {
        using var viewModel = CreateViewModel(sourceStatus);

        var inProgressOption = viewModel.StatusOptions.Single(
            option => option.Status == DomainTaskStatus.InProgress);

        await Assert.That(inProgressOption.IsEnabled).IsFalse();
    }

    [Test]
    public async Task CompletedTask_TransitionOptionSource_ContainsEveryNonCurrentTargetIncludingDisabled()
    {
        using var viewModel = CreateViewModel(DomainTaskStatus.Completed);

        var options = viewModel.AvailableStatusTransitionOptions.ToList();

        using (Assert.Multiple())
        {
            await Assert.That(options.Count).IsEqualTo(4);
            await Assert.That(options.Select(option => option.Status)).IsEquivalentTo(
            [
                DomainTaskStatus.NotReady,
                DomainTaskStatus.Prepared,
                DomainTaskStatus.InProgress,
                DomainTaskStatus.Archived
            ]);
            await Assert.That(options.Any(option =>
                    option.Status == DomainTaskStatus.Archived && !option.IsEnabled))
                .IsTrue();
        }
    }

    [Test]
    public async Task ArchivedTask_RestoreStatus_NormalizesPreviousInProgressToPrepared()
    {
        var inProgressAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Id = "archived-in-progress-task",
            Status = DomainTaskStatus.Archived,
            StatusHistory = new List<TaskStatusHistoryEntry>
            {
                new()
                {
                    Status = DomainTaskStatus.InProgress,
                    ChangedAt = inProgressAt,
                    Author = "owner"
                },
                new()
                {
                    Status = DomainTaskStatus.Archived,
                    ChangedAt = inProgressAt.AddHours(1),
                    Author = "owner"
                }
            }
        };

        await Assert.That(task.GetRestoreStatusAfterArchive()).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task CompletedTask_DeniedInProgressSelection_DoesNotMutateStatusOptimistically()
    {
        using var viewModel = CreateViewModel(DomainTaskStatus.Completed);
        var inProgressOption = viewModel.StatusOptions.Single(
            option => option.Status == DomainTaskStatus.InProgress);

        await viewModel.TryTransitionToStatusAsync(inProgressOption.Status);

        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
    }

    private static TaskItemViewModel CreateViewModel(DomainTaskStatus status)
    {
        return new TaskItemViewModel(
            new TaskItem
            {
                Id = $"{status}-status-contract-task",
                Status = status,
                IsCanBeCompleted = true
            },
            new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
            () => false);
    }
}
