using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class StorageMigrationStepDefinitions
{
    private const string ScenarioId = "SC-0009-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0127", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.StorageMigrationTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0128", "И", "поведение относится к истории ST-0009", scenarios, async context =>
            {
                await Assert.That(context.StorageMigrationTaskSetAvailable).IsTrue();
                context.StorageMigrationStoryBehaviorConfirmed = true;
            }),
            new("SD-0129", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.StorageMigrationTaskSetAvailable).IsTrue();
                await Assert.That(context.StorageMigrationStoryBehaviorConfirmed).IsTrue();
                context.StorageMigrationResult = await StorageMigrationContract.ExecuteAsync();
            }),
            new("SD-0130", "Тогда", "Миграции восстанавливают reverse links, status model и availability при загрузке.", scenarios, async context =>
            {
                await Assert.That(context.StorageMigrationResult).IsNotNull();
                await StorageMigrationContract.AssertAsync(context.StorageMigrationResult!);
            })
        ];
    }
}
