using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class TaskStatusMappingTests
{
    [Test]
    public async Task TaskItemMold_RoundTrip_PreservesStatusHistoryAndCompletionCriteria()
    {
        var mapper = AppModelMapping.ConfigureMapping();
        var source = CreateTaskItem();

        var mold = mapper.Map<TaskItemMold>(source);
        var roundTrip = mapper.Map<TaskItem>(mold);

        await Assert.That(mold.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(mold.StatusHistory.Select(entry => entry.Status))
            .IsEquivalentTo([DomainTaskStatus.Prepared, DomainTaskStatus.InProgress]);
        await Assert.That(mold.CompletionCriteria.Single().Text).IsEqualTo("Проверить результат");
        await Assert.That(roundTrip.Status).IsEqualTo(source.Status);
        await Assert.That(roundTrip.StatusHistory.Select(entry => entry.Status))
            .IsEquivalentTo(source.StatusHistory.Select(entry => entry.Status));
        await Assert.That(roundTrip.CompletionCriteria.Single().IsSatisfied).IsTrue();
    }

    [Test]
    public async Task HubAndReceiveTaskMolds_PreserveStatusContract()
    {
        var mapper = AppModelMapping.ConfigureMapping();
        var source = CreateTaskItem();

        var hubMold = mapper.Map<TaskItemHubMold>(source);
        var received = mapper.Map<TaskItem>(new ReceiveTaskItem
        {
            Id = source.Id,
            UserId = source.UserId,
            Title = source.Title,
            Description = source.Description,
            Status = DomainTaskStatus.Archived,
            StatusHistory = source.StatusHistory.ToList(),
            CompletionCriteria = source.CompletionCriteria.ToList(),
            CreatedDateTime = source.CreatedDateTime,
            IsCanBeCompleted = source.IsCanBeCompleted,
            ContainsTasks = source.ContainsTasks.ToList(),
            ParentTasks = source.ParentTasks.ToList(),
            BlocksTasks = source.BlocksTasks.ToList(),
            BlockedByTasks = source.BlockedByTasks.ToList(),
            Version = source.Version
        });

        await Assert.That(hubMold.Status).IsEqualTo(source.Status);
        await Assert.That(hubMold.StatusHistory.Count).IsEqualTo(2);
        await Assert.That(hubMold.CompletionCriteria.Single().Id).IsEqualTo("criterion-1");
        await Assert.That(received.Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(received.StatusHistory.Count).IsEqualTo(2);
        await Assert.That(received.CompletionCriteria.Single().Text).IsEqualTo("Проверить результат");
    }

    private static TaskItem CreateTaskItem()
    {
        var preparedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var startedAt = preparedAt.AddHours(1);
        return new TaskItem
        {
            Id = "mapping-task",
            UserId = "owner",
            Title = "Mapping task",
            Description = "Mapping contract",
            Status = DomainTaskStatus.InProgress,
            StatusHistory = new List<TaskStatusHistoryEntry>
            {
                new()
                {
                    Status = DomainTaskStatus.Prepared,
                    ChangedAt = preparedAt,
                    Author = "owner"
                },
                new()
                {
                    Status = DomainTaskStatus.InProgress,
                    ChangedAt = startedAt,
                    Author = "owner"
                }
            },
            CompletionCriteria = new List<TaskCompletionCriterion>
            {
                new()
                {
                    Id = "criterion-1",
                    Text = "Проверить результат",
                    IsSatisfied = true
                }
            },
            CreatedDateTime = preparedAt.AddDays(-1),
            UpdatedDateTime = startedAt,
            ContainsTasks = ["child"],
            ParentTasks = ["parent"],
            BlocksTasks = ["blocked"],
            BlockedByTasks = ["blocker"],
            Version = 3
        };
    }
}
