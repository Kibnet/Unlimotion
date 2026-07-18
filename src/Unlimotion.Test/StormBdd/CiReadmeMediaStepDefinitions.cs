using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class CiReadmeMediaStepDefinitions
{
    private const string ScenarioId = "SC-0015-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0175", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.CiReadmeMediaTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0176", "И", "поведение относится к истории ST-0015", scenarios, async context =>
            {
                await Assert.That(context.CiReadmeMediaTaskSetAvailable).IsTrue();
                context.CiReadmeMediaStoryBehaviorConfirmed = true;
            }),
            new("SD-0177", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.CiReadmeMediaTaskSetAvailable).IsTrue();
                await Assert.That(context.CiReadmeMediaStoryBehaviorConfirmed).IsTrue();
                context.CiReadmeMediaResult = await CiReadmeMediaContract.ExecuteAsync();
            }),
            new("SD-0178", "Тогда", "CI и README media automation дают smoke/regression-доказательства для UI-потоков.", scenarios, async context =>
            {
                await Assert.That(context.CiReadmeMediaResult).IsNotNull();
                await CiReadmeMediaContract.AssertAsync(context.CiReadmeMediaResult!);
            })
        ];
    }
}
