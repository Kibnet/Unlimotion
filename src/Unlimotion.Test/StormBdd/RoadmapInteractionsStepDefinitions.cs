using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class RoadmapInteractionsStepDefinitions
{
    private const string ScenarioId = "SC-0008-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0119", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.RoadmapInteractionsTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0120", "И", "поведение относится к истории ST-0008", scenarios, async context =>
            {
                await Assert.That(context.RoadmapInteractionsTaskSetAvailable).IsTrue();
                context.RoadmapInteractionsStoryBehaviorConfirmed = true;
            }),
            new("SD-0121", "Когда", "пользователь ищет или фильтрует задачи в текущем представлении", scenarios, async context =>
            {
                await Assert.That(context.RoadmapInteractionsTaskSetAvailable).IsTrue();
                await Assert.That(context.RoadmapInteractionsStoryBehaviorConfirmed).IsTrue();
                context.RoadmapInteractionsResult = await RoadmapInteractionsContract.ExecuteAsync();
            }),
            new("SD-0122", "Тогда", "Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласно спекам.", scenarios, async context =>
            {
                await Assert.That(context.RoadmapInteractionsResult).IsNotNull();
                await RoadmapInteractionsContract.AssertAsync(context.RoadmapInteractionsResult!);
            })
        ];
    }
}
