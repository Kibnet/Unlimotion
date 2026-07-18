using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskAvailabilityInProgressRollbackScenarioResult(
    DomainTaskStatus Status,
    bool IsCanBeCompleted,
    DateTimeOffset? UnlockedDateTime,
    DomainTaskStatus LatestHistoryStatus,
    string? LatestHistoryAuthor);

internal static class TaskAvailabilityInProgressRollbackStepDefinitions
{
    private const string ScenarioId = "SC-0003-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0071",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskAvailabilityInProgressRollbackTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0072",
                "И",
                "поведение относится к истории ST-0003",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityInProgressRollbackTaskSetAvailable).IsTrue();
                    context.TaskAvailabilityInProgressRollbackStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0073",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityInProgressRollbackTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskAvailabilityInProgressRollbackStoryBehaviorConfirmed).IsTrue();

                    context.TaskAvailabilityInProgressRollbackResult = await VerifyInProgressRollsBackWhenTaskBecomesUnavailableAsync();
                }),
            new StormStepDefinition(
                "SD-0074",
                "Тогда",
                "Если задача стала недоступной, недопустимые InProgress-состояния корректируются.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskAvailabilityInProgressRollbackResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.IsCanBeCompleted).IsFalse();
                    await Assert.That(result.UnlockedDateTime).IsNull();
                    await Assert.That(result.Status).IsEqualTo(DomainTaskStatus.Prepared);
                    await Assert.That(result.LatestHistoryStatus).IsEqualTo(DomainTaskStatus.Prepared);
                    await Assert.That(result.LatestHistoryAuthor).IsEqualTo("System");
                })
        ];
    }

    private static async Task<TaskAvailabilityInProgressRollbackScenarioResult> VerifyInProgressRollsBackWhenTaskBecomesUnavailableAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var blocker = CreateTask(
            "in-progress-blocker",
            DomainTaskStatus.NotReady,
            blocksTasks: ["in-progress-blocked"]);
        var task = CreateTask(
            "in-progress-blocked",
            DomainTaskStatus.InProgress,
            isCanBeCompleted: true,
            unlockedDateTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            blockedByTasks: [blocker.Id]);
        task.EnsureStatusHistory("owner");

        await storage.Save(blocker);
        await storage.Save(task);

        await manager.CalculateAndUpdateAvailability(task);
        var saved = await storage.Load(task.Id);

        await Assert.That(saved).IsNotNull();
        var latestHistory = saved!.StatusHistory.OrderBy(entry => entry.ChangedAt).Last();
        return new TaskAvailabilityInProgressRollbackScenarioResult(
            saved.Status,
            saved.IsCanBeCompleted,
            saved.UnlockedDateTime,
            latestHistory.Status,
            latestHistory.Author);
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted = true,
        DateTimeOffset? unlockedDateTime = null,
        IReadOnlyList<string>? blocksTasks = null,
        IReadOnlyList<string>? blockedByTasks = null)
    {
        return new TaskItem
        {
            Id = id,
            Status = status,
            IsCanBeCompleted = isCanBeCompleted,
            UnlockedDateTime = unlockedDateTime,
            BlocksTasks = blocksTasks?.ToList() ?? new List<string>(),
            BlockedByTasks = blockedByTasks?.ToList() ?? new List<string>(),
            ContainsTasks = new List<string>(),
            ParentTasks = new List<string>()
        };
    }
}