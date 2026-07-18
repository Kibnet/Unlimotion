using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskCreationGraphStepDefinitions
{
    private const string ScenarioId = "SC-0001-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0039",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskCreationGraphTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0040",
                "И",
                "поведение относится к истории ST-0001",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCreationGraphTaskSetAvailable).IsTrue();
                    context.TaskCreationGraphStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0041",
                "Когда",
                "пользователь создаёт или добавляет задачу через доступное действие интерфейса",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskCreationGraphTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskCreationGraphStoryBehaviorConfirmed).IsTrue();

                    context.TaskCreationGraphResult = await TaskCreationGraphUiContract.ExecuteTaskCreationGraphScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0042",
                "Тогда",
                "задачу можно создать в корне, рядом с выбранной задачей, как заблокированного соседа или внутри выбранной задачи.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskCreationGraphResult;

                    await Assert.That(result).IsNotNull();
                    await TaskCreationGraphUiContract.AssertTaskCreationGraphScenarioResultAsync(result!);
                })
        ];
    }
}
