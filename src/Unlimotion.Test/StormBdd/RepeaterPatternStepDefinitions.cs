using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class RepeaterPatternStepDefinitions
{
    private const string ScenarioId = "SC-0006-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0091",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.RepeaterPatternTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0092",
                "И",
                "поведение относится к истории ST-0006",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RepeaterPatternTaskSetAvailable).IsTrue();
                    context.RepeaterPatternStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0093",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RepeaterPatternTaskSetAvailable).IsTrue();
                    await Assert.That(context.RepeaterPatternStoryBehaviorConfirmed).IsTrue();

                    context.RepeaterPatternResult =
                        await RepeaterPatternScenarioContract.ExecuteRepeaterPatternScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0094",
                "Тогда",
                "RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.",
                supportsScenarios,
                async context =>
                {
                    var result = context.RepeaterPatternResult;

                    await Assert.That(result).IsNotNull();
                    await RepeaterPatternScenarioContract.AssertRepeaterPatternScenarioResultAsync(result!);
                })
        ];
    }
}
