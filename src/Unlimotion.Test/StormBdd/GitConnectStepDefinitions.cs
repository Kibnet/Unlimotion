using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class GitConnectStepDefinitions
{
    private const string ScenarioId = "SC-0010-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0135", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.GitConnectTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0136", "И", "поведение относится к истории ST-0010", scenarios, async context =>
            {
                await Assert.That(context.GitConnectTaskSetAvailable).IsTrue();
                context.GitConnectStoryBehaviorConfirmed = true;
            }),
            new("SD-0137", "Когда", "пользователь запускает или проверяет remote backup flow", scenarios, async context =>
            {
                await Assert.That(context.GitConnectTaskSetAvailable).IsTrue();
                await Assert.That(context.GitConnectStoryBehaviorConfirmed).IsTrue();
                context.GitConnectResult = await GitConnectContract.ExecuteAsync();
            }),
            new("SD-0138", "Тогда", "Настройки позволяют предварительно проверить и подключить Git-репозиторий, а также подготовить remote.", scenarios, async context =>
            {
                await Assert.That(context.GitConnectResult).IsNotNull();
                await GitConnectContract.AssertAsync(context.GitConnectResult!);
            })
        ];
    }
}
