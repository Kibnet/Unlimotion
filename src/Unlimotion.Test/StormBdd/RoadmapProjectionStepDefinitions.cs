using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class RoadmapProjectionStepDefinitions
{
    private const string ScenarioId = "SC-0008-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0111",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.RoadmapProjectionTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0112",
                "И",
                "поведение относится к истории ST-0008",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RoadmapProjectionTaskSetAvailable).IsTrue();
                    context.RoadmapProjectionStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0113",
                "Когда",
                "пользователь работает с дорожной картой задач",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RoadmapProjectionTaskSetAvailable).IsTrue();
                    await Assert.That(context.RoadmapProjectionStoryBehaviorConfirmed).IsTrue();

                    context.RoadmapProjectionResult =
                        await RoadmapProjectionContract.ExecuteRoadmapProjectionScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0114",
                "Тогда",
                "Roadmap строит узлы и связи из текущей модели задач.",
                supportsScenarios,
                async context =>
                {
                    var result = context.RoadmapProjectionResult;

                    await Assert.That(result).IsNotNull();
                    await RoadmapProjectionContract.AssertRoadmapProjectionScenarioResultAsync(result!);
                })
        ];
    }
}
