using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class LocalJsonStorageStepDefinitions
{
    private const string ScenarioId = "SC-0009-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0123", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.LocalJsonStorageTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0124", "И", "поведение относится к истории ST-0009", scenarios, async context =>
            {
                await Assert.That(context.LocalJsonStorageTaskSetAvailable).IsTrue();
                context.LocalJsonStorageStoryBehaviorConfirmed = true;
            }),
            new("SD-0125", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.LocalJsonStorageTaskSetAvailable).IsTrue();
                await Assert.That(context.LocalJsonStorageStoryBehaviorConfirmed).IsTrue();
                context.LocalJsonStorageResult = await LocalJsonStorageContract.ExecuteAsync();
            }),
            new("SD-0126", "Тогда", "Задачи сериализуются в локальные JSON-файлы в выбранной папке.", scenarios, async context =>
            {
                await Assert.That(context.LocalJsonStorageResult).IsNotNull();
                await LocalJsonStorageContract.AssertAsync(context.LocalJsonStorageResult!);
            })
        ];
    }
}
