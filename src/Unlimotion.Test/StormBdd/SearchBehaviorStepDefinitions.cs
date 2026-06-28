using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class SearchBehaviorStepDefinitions
{
    private const string ScenarioId = "SC-0005-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0035",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.SearchBehaviorTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0036",
                "И",
                "поведение относится к истории ST-0005",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.SearchBehaviorTaskSetAvailable).IsTrue();
                    context.SearchBehaviorStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0037",
                "Когда",
                "пользователь ищет или фильтрует задачи в текущем представлении",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.SearchBehaviorTaskSetAvailable).IsTrue();
                    await Assert.That(context.SearchBehaviorStoryBehaviorConfirmed).IsTrue();

                    context.SearchBehaviorResult = await SearchBehaviorUiContract.ExecuteSearchBehaviorScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0038",
                "Тогда",
                "Текстовый поиск поддерживает обычное и fuzzy-поведение согласно настройкам.",
                supportsScenarios,
                async context =>
                {
                    var result = context.SearchBehaviorResult;

                    await Assert.That(result).IsNotNull();
                    await SearchBehaviorUiContract.AssertSearchBehaviorScenarioResultAsync(result!);
                })
        ];
    }
}
