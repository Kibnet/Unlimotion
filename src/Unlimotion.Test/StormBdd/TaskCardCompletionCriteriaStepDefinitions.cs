using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskCardCompletionCriteriaStepDefinitions
{
    private const string ScenarioId = "SC-0007-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0107",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskCardCompletionCriteriaTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0108",
                "И",
                "поведение относится к истории ST-0007",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardCompletionCriteriaTaskSetAvailable).IsTrue();
                    context.TaskCardCompletionCriteriaStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0109",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardCompletionCriteriaTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskCardCompletionCriteriaStoryBehaviorConfirmed).IsTrue();

                    context.TaskCardCompletionCriteriaResult =
                        await TaskCardCompletionCriteriaContract.ExecuteTaskCardCompletionCriteriaScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0110",
                "Тогда",
                "Критерии завершения можно добавлять, изменять и блокировать после завершения задачи.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskCardCompletionCriteriaResult;

                    await Assert.That(result).IsNotNull();
                    await TaskCardCompletionCriteriaContract.AssertTaskCardCompletionCriteriaScenarioResultAsync(result!);
                })
        ];
    }
}
