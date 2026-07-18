using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskAvailabilityBlockerCaseResult(
    bool IsCanBeCompleted,
    DateTimeOffset? UnlockedDateTime,
    IReadOnlyList<string> DirectBlockedByTasks);

internal sealed record TaskAvailabilityBlockersScenarioResult(
    TaskAvailabilityBlockerCaseResult IncompleteChildBlocksParent,
    TaskAvailabilityBlockerCaseResult DirectIncompleteBlockerBlocksTask,
    TaskAvailabilityBlockerCaseResult InheritedIncompleteBlockerBlocksDescendant);

internal static class TaskAvailabilityBlockersStepDefinitions
{
    private const string ScenarioId = "SC-0003-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0063",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskAvailabilityBlockersTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0064",
                "И",
                "поведение относится к истории ST-0003",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityBlockersTaskSetAvailable).IsTrue();
                    context.TaskAvailabilityBlockersStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0065",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskAvailabilityBlockersTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskAvailabilityBlockersStoryBehaviorConfirmed).IsTrue();

                    context.TaskAvailabilityBlockersResult = new TaskAvailabilityBlockersScenarioResult(
                        await VerifyIncompleteChildBlocksParentAsync(),
                        await VerifyDirectIncompleteBlockerBlocksTaskAsync(),
                        await VerifyInheritedIncompleteBlockerBlocksDescendantAsync());
                }),
            new StormStepDefinition(
                "SD-0066",
                "Тогда",
                "Задача считается недоступной, если у неё есть незавершённые дочерние задачи, блокирующие задачи или блокировки в родительской цепочке.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskAvailabilityBlockersResult;

                    await Assert.That(result).IsNotNull();
                    await AssertUnavailable(result!.IncompleteChildBlocksParent);
                    await AssertUnavailable(result.DirectIncompleteBlockerBlocksTask);
                    await AssertUnavailable(result.InheritedIncompleteBlockerBlocksDescendant);
                    await Assert.That(result.InheritedIncompleteBlockerBlocksDescendant.DirectBlockedByTasks).IsEmpty();
                })
        ];
    }

    private static async Task<TaskAvailabilityBlockerCaseResult> VerifyIncompleteChildBlocksParentAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var child = CreateTask("child", DomainTaskStatus.NotReady);
        var parent = CreateTask(
            "parent",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: true,
            unlockedDateTime: DateTimeOffset.UtcNow,
            containsTasks: [child.Id]);

        await storage.Save(child);
        await storage.Save(parent);

        await manager.CalculateAndUpdateAvailability(parent);
        var saved = await storage.Load(parent.Id);

        await Assert.That(saved).IsNotNull();
        return ToCaseResult(saved!);
    }

    private static async Task<TaskAvailabilityBlockerCaseResult> VerifyDirectIncompleteBlockerBlocksTaskAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var blocker = CreateTask("blocker", DomainTaskStatus.NotReady, blocksTasks: ["blocked"]);
        var blocked = CreateTask(
            "blocked",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: true,
            unlockedDateTime: DateTimeOffset.UtcNow,
            blockedByTasks: [blocker.Id]);

        await storage.Save(blocker);
        await storage.Save(blocked);

        await manager.CalculateAndUpdateAvailability(blocked);
        var saved = await storage.Load(blocked.Id);

        await Assert.That(saved).IsNotNull();
        return ToCaseResult(saved!);
    }

    private static async Task<TaskAvailabilityBlockerCaseResult> VerifyInheritedIncompleteBlockerBlocksDescendantAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var blocker = CreateTask("blocker", DomainTaskStatus.NotReady, blocksTasks: ["parent"]);
        var parent = CreateTask(
            "parent",
            DomainTaskStatus.Prepared,
            containsTasks: ["child"],
            blockedByTasks: [blocker.Id]);
        var child = CreateTask(
            "child",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: true,
            unlockedDateTime: DateTimeOffset.UtcNow,
            parentTasks: [parent.Id]);

        await storage.Save(blocker);
        await storage.Save(parent);
        await storage.Save(child);

        await manager.CalculateAndUpdateAvailability(parent);
        var saved = await storage.Load(child.Id);

        await Assert.That(saved).IsNotNull();
        return ToCaseResult(saved!);
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

    private static TaskAvailabilityBlockerCaseResult ToCaseResult(TaskItem task)
    {
        return new TaskAvailabilityBlockerCaseResult(
            task.IsCanBeCompleted,
            task.UnlockedDateTime,
            task.BlockedByTasks.ToArray());
    }

    private static async Task AssertUnavailable(TaskAvailabilityBlockerCaseResult actual)
    {
        await Assert.That(actual.IsCanBeCompleted).IsFalse();
        await Assert.That(actual.UnlockedDateTime).IsNull();
    }
}