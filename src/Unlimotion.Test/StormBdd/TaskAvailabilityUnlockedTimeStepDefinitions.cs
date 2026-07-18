using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskAvailabilityUnlockedTimeCaseResult(
    bool IsCanBeCompleted,
    DateTimeOffset? UnlockedDateTime,
    DateTimeOffset? StoredUnlockedDateTime,
    DateTimeOffset? BeforeCalculation);

internal sealed record TaskAvailabilityUnlockedTimeScenarioResult(
    TaskAvailabilityUnlockedTimeCaseResult BecomesAvailable,
    TaskAvailabilityUnlockedTimeCaseResult BecomesUnavailable);

internal static class TaskAvailabilityUnlockedTimeStepDefinitions
{
    private const string ScenarioId = "SC-0003-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0067",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskAvailabilityUnlockedTimeTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0068",
                "И",
                "поведение относится к истории ST-0003",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityUnlockedTimeTaskSetAvailable).IsTrue();
                    context.TaskAvailabilityUnlockedTimeStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0069",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityUnlockedTimeTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskAvailabilityUnlockedTimeStoryBehaviorConfirmed).IsTrue();

                    context.TaskAvailabilityUnlockedTimeResult = new TaskAvailabilityUnlockedTimeScenarioResult(
                        await VerifyUnlockedDateTimeSetWhenTaskBecomesAvailableAsync(),
                        await VerifyUnlockedDateTimeClearedWhenTaskBecomesBlockedAsync());
                }),
            new StormStepDefinition(
                "SD-0070",
                "Тогда",
                "UnlockedDateTime устанавливается и очищается при изменении доступности.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskAvailabilityUnlockedTimeResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.BecomesAvailable.IsCanBeCompleted).IsTrue();
                    await Assert.That(result.BecomesAvailable.UnlockedDateTime).IsNotNull();
                    await Assert.That(result.BecomesAvailable.StoredUnlockedDateTime).IsEqualTo(result.BecomesAvailable.UnlockedDateTime);
                    await Assert.That(result.BecomesAvailable.UnlockedDateTime!.Value >= result.BecomesAvailable.BeforeCalculation!.Value).IsTrue();

                    await Assert.That(result.BecomesUnavailable.IsCanBeCompleted).IsFalse();
                    await Assert.That(result.BecomesUnavailable.UnlockedDateTime).IsNull();
                    await Assert.That(result.BecomesUnavailable.StoredUnlockedDateTime).IsNull();
                })
        ];
    }

    private static async Task<TaskAvailabilityUnlockedTimeCaseResult> VerifyUnlockedDateTimeSetWhenTaskBecomesAvailableAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var task = CreateTask(
            "unlocked-time-available",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: false,
            unlockedDateTime: null);

        await storage.Save(task);
        var beforeCalculation = DateTimeOffset.UtcNow;

        await manager.CalculateAndUpdateAvailability(task);
        var saved = await storage.Load(task.Id);

        await Assert.That(saved).IsNotNull();
        return ToCaseResult(task, saved!, beforeCalculation);
    }

    private static async Task<TaskAvailabilityUnlockedTimeCaseResult> VerifyUnlockedDateTimeClearedWhenTaskBecomesBlockedAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var blocker = CreateTask(
            "unlocked-time-blocker",
            DomainTaskStatus.NotReady,
            blocksTasks: ["unlocked-time-blocked"]);
        var task = CreateTask(
            "unlocked-time-blocked",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: true,
            unlockedDateTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            blockedByTasks: [blocker.Id]);

        await storage.Save(blocker);
        await storage.Save(task);

        await manager.CalculateAndUpdateAvailability(task);
        var saved = await storage.Load(task.Id);

        await Assert.That(saved).IsNotNull();
        return ToCaseResult(task, saved!, beforeCalculation: null);
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted = true,
        DateTimeOffset? unlockedDateTime = null,
        IReadOnlyList<string>? containsTasks = null,
        IReadOnlyList<string>? parentTasks = null,
        IReadOnlyList<string>? blocksTasks = null,
        IReadOnlyList<string>? blockedByTasks = null)
    {
        var task = new TaskItem
        {
            Id = id,
            Status = status,
            IsCompleted = false,
            IsCanBeCompleted = isCanBeCompleted,
            UnlockedDateTime = unlockedDateTime,
            ContainsTasks = containsTasks?.ToList() ?? new List<string>(),
            ParentTasks = parentTasks?.ToList() ?? new List<string>(),
            BlocksTasks = blocksTasks?.ToList() ?? new List<string>(),
            BlockedByTasks = blockedByTasks?.ToList() ?? new List<string>()
        };
        task.EnsureStatusHistory();
        return task;
    }

    private static TaskAvailabilityUnlockedTimeCaseResult ToCaseResult(
        TaskItem task,
        TaskItem saved,
        DateTimeOffset? beforeCalculation)
    {
        return new TaskAvailabilityUnlockedTimeCaseResult(
            task.IsCanBeCompleted,
            task.UnlockedDateTime,
            saved.UnlockedDateTime,
            beforeCalculation);
    }
}