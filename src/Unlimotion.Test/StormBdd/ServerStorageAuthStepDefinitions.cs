using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class ServerStorageAuthStepDefinitions
{
    private const string AuthScenarioId = "SC-0011-001";
    private const string CrudRealtimeScenarioId = "SC-0011-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var sharedServerStorageScenarios = new HashSet<string>(StringComparer.Ordinal)
        {
            AuthScenarioId,
            CrudRealtimeScenarioId
        };
        var authScenario = new HashSet<string>(StringComparer.Ordinal) { AuthScenarioId };
        var crudRealtimeScenario = new HashSet<string>(StringComparer.Ordinal) { CrudRealtimeScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0022",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                sharedServerStorageScenarios,
                context =>
                {
                    context.ServerStorageTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0023",
                "И",
                "поведение относится к истории ST-0011",
                sharedServerStorageScenarios,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    context.ServerStorageStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0024",
                "Когда",
                "пользователь использует серверное хранилище",
                sharedServerStorageScenarios,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    await Assert.That(context.ServerStorageStoryBehaviorConfirmed).IsTrue();
                }),
            new StormStepDefinition(
                "SD-0025",
                "Тогда",
                "Клиент поддерживает login/register/refresh-token flow для серверного хранилища.",
                authScenario,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    await Assert.That(context.ServerStorageStoryBehaviorConfirmed).IsTrue();

                    context.ServerStorageAuthResult =
                        await ServerStorageAuthContract.ExecuteLoginRegisterRefreshScenarioAsync();
                    var result = context.ServerStorageAuthResult;
                    await Assert.That(result).IsNotNull();
                    await ServerStorageAuthContract
                        .AssertLoginRegisterRefreshScenarioResultAsync(result!);
                }),
            new StormStepDefinition(
                "SD-0026",
                "Тогда",
                "CRUD операций задач выполняется через аутентифицированные ServiceStack endpoints, а SignalR-подключение может доставлять обновления между клиентами.",
                crudRealtimeScenario,
                async context =>
                {
                    await Assert.That(context.ServerStorageTaskSetAvailable).IsTrue();
                    await Assert.That(context.ServerStorageStoryBehaviorConfirmed).IsTrue();

                    context.ServerStorageCrudRealtimeResult =
                        await ServerStorageCrudRealtimeContract.ExecuteCrudRealtimeScenarioAsync();
                    var result = context.ServerStorageCrudRealtimeResult;
                    await Assert.That(result).IsNotNull();
                    await ServerStorageCrudRealtimeContract
                        .AssertCrudRealtimeScenarioResultAsync(result!);
                })
        ];
    }
}
