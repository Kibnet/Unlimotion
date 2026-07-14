using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskCardRelationsStepDefinitions
{
    private const string ScenarioId = "SC-0007-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0103",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskCardRelationsTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0104",
                "И",
                "поведение относится к истории ST-0007",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardRelationsTaskSetAvailable).IsTrue();
                    context.TaskCardRelationsStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0105",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardRelationsTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskCardRelationsStoryBehaviorConfirmed).IsTrue();

                    context.TaskCardRelationsResult =
                        await TaskCardRelationsContract.ExecuteTaskCardRelationsScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0106",
                "Тогда",
                "Блоки отношений позволяют просматривать и менять parents, containing, blocked и blocked-by связи.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskCardRelationsResult;

                    await Assert.That(result).IsNotNull();
                    await TaskCardRelationsContract.AssertTaskCardRelationsScenarioResultAsync(result!);
                })
        ];
    }
}
