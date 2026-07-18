using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskCardLayoutStepDefinitions
{
    private const string ScenarioId = "SC-0007-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0099",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskCardLayoutTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0100",
                "И",
                "поведение относится к истории ST-0007",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardLayoutTaskSetAvailable).IsTrue();
                    context.TaskCardLayoutStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0101",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCardLayoutTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskCardLayoutStoryBehaviorConfirmed).IsTrue();

                    context.TaskCardLayoutResult =
                        await TaskCardLayoutUiContract.ExecuteTaskCardLayoutScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0102",
                "Тогда",
                "Карточка задачи остаётся читаемой и управляемой в десктопных и узких компоновках.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskCardLayoutResult;

                    await Assert.That(result).IsNotNull();
                    await TaskCardLayoutUiContract.AssertTaskCardLayoutScenarioResultAsync(result!);
                })
        ];
    }
}
