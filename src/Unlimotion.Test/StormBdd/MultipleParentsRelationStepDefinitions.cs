using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class MultipleParentsRelationStepDefinitions
{
    private const string ScenarioId = "SC-0001-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0043",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.MultipleParentsRelationTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0044",
                "И",
                "поведение относится к истории ST-0001",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.MultipleParentsRelationTaskSetAvailable).IsTrue();
                    context.MultipleParentsRelationStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0045",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.MultipleParentsRelationTaskSetAvailable).IsTrue();
                    await Assert.That(context.MultipleParentsRelationStoryBehaviorConfirmed).IsTrue();

                    context.MultipleParentsRelationResult =
                        await MultipleParentsRelationContract.ExecuteMultipleParentsRelationScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0046",
                "Тогда",
                "Задача может иметь несколько родителей, а обратные связи parent-child остаются синхронизированными.",
                supportsScenarios,
                async context =>
                {
                    var result = context.MultipleParentsRelationResult;

                    await Assert.That(result).IsNotNull();
                    await MultipleParentsRelationContract.AssertMultipleParentsRelationScenarioResultAsync(result!);
                })
        ];
    }
}
