using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class GitRemoteAuthStepDefinitions
{
    private const string ScenarioId = "SC-0010-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0139", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.GitRemoteAuthTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0140", "И", "поведение относится к истории ST-0010", scenarios, async context =>
            {
                await Assert.That(context.GitRemoteAuthTaskSetAvailable).IsTrue();
                context.GitRemoteAuthStoryBehaviorConfirmed = true;
            }),
            new("SD-0141", "Когда", "пользователь запускает или проверяет remote backup flow", scenarios, async context =>
            {
                await Assert.That(context.GitRemoteAuthTaskSetAvailable).IsTrue();
                await Assert.That(context.GitRemoteAuthStoryBehaviorConfirmed).IsTrue();
                context.GitRemoteAuthResult = await GitRemoteAuthContract.ExecuteAsync();
            }),
            new("SD-0142", "Тогда", "Remote-аутентификация поддерживает SSH и token/http сценарии, включая хранение SSH key.", scenarios, async context =>
            {
                await Assert.That(context.GitRemoteAuthResult).IsNotNull();
                await GitRemoteAuthContract.AssertAsync(context.GitRemoteAuthResult!);
            })
        ];
    }
}
