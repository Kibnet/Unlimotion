using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class GitConflictResolutionStepDefinitions
{
    private const string ScenarioId = "SC-0010-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0143", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.GitConflictResolutionTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0144", "И", "поведение относится к истории ST-0010", scenarios, async context =>
            {
                await Assert.That(context.GitConflictResolutionTaskSetAvailable).IsTrue();
                context.GitConflictResolutionStoryBehaviorConfirmed = true;
            }),
            new("SD-0145", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.GitConflictResolutionTaskSetAvailable).IsTrue();
                await Assert.That(context.GitConflictResolutionStoryBehaviorConfirmed).IsTrue();
                context.GitConflictResolutionResult = await GitConflictResolutionContract.ExecuteAsync();
            }),
            new("SD-0146", "Тогда", "Разрешение конфликтов поддерживает решения на уровне файла и отдельных полей перед commit/push.", scenarios, async context =>
            {
                await Assert.That(context.GitConflictResolutionResult).IsNotNull();
                await GitConflictResolutionContract.AssertAsync(context.GitConflictResolutionResult!);
            })
        ];
    }
}
