using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskPlanningDatesStepDefinitions
{
    private const string ScenarioId = "SC-0006-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0087",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskPlanningDatesTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0088",
                "И",
                "поведение относится к истории ST-0006",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskPlanningDatesTaskSetAvailable).IsTrue();
                    context.TaskPlanningDatesStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0089",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskPlanningDatesTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskPlanningDatesStoryBehaviorConfirmed).IsTrue();

                    context.TaskPlanningDatesResult =
                        await TaskPlanningDatesUiContract.ExecuteTaskPlanningDatesScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0090",
                "Тогда",
                "Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskPlanningDatesResult;

                    await Assert.That(result).IsNotNull();
                    await TaskPlanningDatesUiContract.AssertTaskPlanningDatesScenarioResultAsync(result!);
                })
        ];
    }
}
