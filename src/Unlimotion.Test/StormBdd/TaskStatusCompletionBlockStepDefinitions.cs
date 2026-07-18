using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskStatusCompletionBlockScenarioResult(
    DomainTaskStatus UnavailableTaskStatus,
    DateTimeOffset? UnavailableTaskCompletedDateTime,
    DomainTaskStatus UnsatisfiedCriteriaStatus,
    DateTimeOffset? UnsatisfiedCriteriaCompletedDateTime,
    bool CompletedOptionEnabledWithUnsatisfiedCriterion,
    bool CompletedAvailableWithUnsatisfiedCriterion,
    DomainTaskStatus ViewModelStatusAfterDisabledCompletedSelection);

internal static class TaskStatusCompletionBlockStepDefinitions
{
    private const string ScenarioId = "SC-0002-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0055",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskStatusCompletionBlockTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0056",
                "И",
                "поведение относится к истории ST-0002",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusCompletionBlockTaskSetAvailable).IsTrue();
                    context.TaskStatusCompletionBlockStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0057",
                "Когда",
                "пользователь меняет статус задачи или проверяет доступные переходы",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusCompletionBlockTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskStatusCompletionBlockStoryBehaviorConfirmed).IsTrue();

                    var unavailableResult = await VerifyUnavailableTaskCannotBeCompletedAsync();
                    var unsatisfiedCriteriaResult = await VerifyUnsatisfiedCriteriaCannotBeCompletedAsync();
                    var viewModelResult = VerifyUnsatisfiedCriteriaCompletedOptionIsUnavailable();

                    context.TaskStatusCompletionBlockResult = new TaskStatusCompletionBlockScenarioResult(
                        unavailableResult.Status,
                        unavailableResult.CompletedDateTime,
                        unsatisfiedCriteriaResult.Status,
                        unsatisfiedCriteriaResult.CompletedDateTime,
                        viewModelResult.CompletedOptionEnabled,
                        viewModelResult.CompletedAvailable,
                        viewModelResult.StatusAfterSelection);
                }),
            new StormStepDefinition(
                "SD-0058",
                "Тогда",
                "Переход в Completed блокируется, если задача недоступна или критерии завершения не выполнены.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskStatusCompletionBlockResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.UnavailableTaskStatus).IsEqualTo(DomainTaskStatus.Prepared);
                    await Assert.That(result.UnavailableTaskCompletedDateTime).IsNull();
                    await Assert.That(result.UnsatisfiedCriteriaStatus).IsEqualTo(DomainTaskStatus.Prepared);
                    await Assert.That(result.UnsatisfiedCriteriaCompletedDateTime).IsNull();
                    await Assert.That(result.CompletedOptionEnabledWithUnsatisfiedCriterion).IsFalse();
                    await Assert.That(result.CompletedAvailableWithUnsatisfiedCriterion).IsFalse();
                    await Assert.That(result.ViewModelStatusAfterDisabledCompletedSelection)
                        .IsEqualTo(DomainTaskStatus.Prepared);
                })
        ];
    }

    private static async Task<(DomainTaskStatus Status, DateTimeOffset? CompletedDateTime)>
        VerifyUnavailableTaskCannotBeCompletedAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);

        var child = new TaskItem
        {
            Id = "blocked-complete-child",
            Status = DomainTaskStatus.NotReady
        };
        child.EnsureStatusHistory();
        await storage.Save(child);

        var existing = new TaskItem
        {
            Id = "blocked-complete-parent",
            Status = DomainTaskStatus.Prepared,
            ContainsTasks = [child.Id]
        };
        existing.EnsureStatusHistory();
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Completed,
            ContainsTasks = [child.Id]
        };

        var affectedTasks = await manager.UpdateTask(change);
        var saved = await storage.Load(existing.Id);

        await Assert.That(saved).IsNotNull();
        await Assert.That(affectedTasks.Single(task => task.Id == existing.Id).Status)
            .IsEqualTo(DomainTaskStatus.Prepared);
        return (saved!.Status, saved.CompletedDateTime);
    }

    private static async Task<(DomainTaskStatus Status, DateTimeOffset? CompletedDateTime)>
        VerifyUnsatisfiedCriteriaCannotBeCompletedAsync()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var criterion = new TaskCompletionCriterion
        {
            Text = "Проверить результат",
            IsSatisfied = false
        };
        var existing = new TaskItem
        {
            Id = "blocked-complete-criteria",
            Status = DomainTaskStatus.Prepared,
            CompletionCriteria = [criterion]
        };
        existing.EnsureStatusHistory();
        await storage.Save(existing);

        var change = new TaskItem
        {
            Id = existing.Id,
            Status = DomainTaskStatus.Completed,
            CompletionCriteria = existing.CompletionCriteria
        };

        var affectedTasks = await manager.UpdateTask(change);
        var saved = await storage.Load(existing.Id);

        await Assert.That(saved).IsNotNull();
        await Assert.That(affectedTasks.Single(task => task.Id == existing.Id).Status)
            .IsEqualTo(DomainTaskStatus.Prepared);
        return (saved!.Status, saved.CompletedDateTime);
    }

    private static (
        bool CompletedOptionEnabled,
        bool CompletedAvailable,
        DomainTaskStatus StatusAfterSelection)
        VerifyUnsatisfiedCriteriaCompletedOptionIsUnavailable()
    {
        var task = new TaskItemViewModel(
            new TaskItem
            {
                Id = "blocked-complete-viewmodel",
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
            },
            new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
            () => false);
        var completedOption = task.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed);
        var completedAvailable = task.AvailableStatusTransitionOptions
            .Any(option => option.Status == DomainTaskStatus.Completed && option.IsEnabled);

        task.StatusOption = completedOption;

        return (completedOption.IsEnabled, completedAvailable, task.Status);
    }
}
