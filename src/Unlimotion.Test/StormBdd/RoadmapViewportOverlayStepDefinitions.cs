using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class RoadmapViewportOverlayStepDefinitions
{
    private const string ScenarioId = "SC-0008-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0115",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.RoadmapViewportOverlayTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0116",
                "И",
                "поведение относится к истории ST-0008",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RoadmapViewportOverlayTaskSetAvailable).IsTrue();
                    context.RoadmapViewportOverlayStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0117",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.RoadmapViewportOverlayTaskSetAvailable).IsTrue();
                    await Assert.That(context.RoadmapViewportOverlayStoryBehaviorConfirmed).IsTrue();

                    context.RoadmapViewportOverlayResult =
                        await RoadmapViewportOverlayContract.ExecuteRoadmapViewportOverlayScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0118",
                "Тогда",
                "Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.",
                supportsScenarios,
                async context =>
                {
                    var result = context.RoadmapViewportOverlayResult;

                    await Assert.That(result).IsNotNull();
                    await RoadmapViewportOverlayContract.AssertRoadmapViewportOverlayScenarioResultAsync(result!);
                })
        ];
    }
}
