using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class WantedImportanceStepDefinitions
{
    private const string ScenarioId = "SC-0006-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0095",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.WantedImportanceTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0096",
                "И",
                "поведение относится к истории ST-0006",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WantedImportanceTaskSetAvailable).IsTrue();
                    context.WantedImportanceStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0097",
                "Когда",
                "пользователь ищет или фильтрует задачи в текущем представлении",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WantedImportanceTaskSetAvailable).IsTrue();
                    await Assert.That(context.WantedImportanceStoryBehaviorConfirmed).IsTrue();

                    context.WantedImportanceResult =
                        await WantedImportanceUiContract.ExecuteWantedImportanceScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0098",
                "Тогда",
                "Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.",
                supportsScenarios,
                async context =>
                {
                    var result = context.WantedImportanceResult;

                    await Assert.That(result).IsNotNull();
                    await WantedImportanceUiContract.AssertWantedImportanceScenarioResultAsync(result!);
                })
        ];
    }
}
