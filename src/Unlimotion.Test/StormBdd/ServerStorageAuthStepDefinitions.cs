using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class ServerStorageAuthStepDefinitions
{
    private const string ScenarioId = "SC-0011-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0022",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.ServerStorageTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0023",
                "И",
                "поведение относится к истории ST-0011",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    context.ServerStorageStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0024",
                "Когда",
                "пользователь использует серверное хранилище",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    await Assert.That(context.ServerStorageStoryBehaviorConfirmed).IsTrue();

                    context.ServerStorageAuthResult =
                        await ServerStorageAuthContract.ExecuteLoginRegisterRefreshScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0025",
                "Тогда",
                "Клиент поддерживает login/register/refresh-token flow для серверного хранилища.",
                supportsScenarios,
                async context =>
                {
                    var result = context.ServerStorageAuthResult;

                    await Assert.That(result).IsNotNull();
                    await ServerStorageAuthContract
                        .AssertLoginRegisterRefreshScenarioResultAsync(result!);
                })
        ];
    }
}
