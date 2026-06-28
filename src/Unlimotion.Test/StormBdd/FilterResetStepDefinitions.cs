using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class FilterResetStepDefinitions
{
    private const string ScenarioId = "SC-0005-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0027",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.FilterResetTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0028",
                "И",
                "поведение относится к истории ST-0005",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.FilterResetTaskSetAvailable).IsTrue();
                    context.FilterResetStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0029",
                "Когда",
                "пользователь меняет статус задачи или проверяет доступные переходы",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.FilterResetTaskSetAvailable).IsTrue();
                    await Assert.That(context.FilterResetStoryBehaviorConfirmed).IsTrue();

                    context.FilterResetResult =
                        await FilterResetUiContract.ExecuteFilterResetScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0030",
                "Тогда",
                "Фильтры статуса, дат, длительности и wanted применяются вместе и могут быть сброшены.",
                supportsScenarios,
                async context =>
                {
                    var result = context.FilterResetResult;

                    await Assert.That(result).IsNotNull();
                    await FilterResetUiContract.AssertFilterResetScenarioResultAsync(result!);
                })
        ];
    }
}