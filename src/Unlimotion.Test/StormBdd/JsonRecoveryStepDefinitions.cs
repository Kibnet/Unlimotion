using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class JsonRecoveryStepDefinitions
{
    private const string ScenarioId = "SC-0009-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0131", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.JsonRecoveryTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0132", "И", "поведение относится к истории ST-0009", scenarios, async context =>
            {
                await Assert.That(context.JsonRecoveryTaskSetAvailable).IsTrue();
                context.JsonRecoveryStoryBehaviorConfirmed = true;
            }),
            new("SD-0133", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.JsonRecoveryTaskSetAvailable).IsTrue();
                await Assert.That(context.JsonRecoveryStoryBehaviorConfirmed).IsTrue();
                context.JsonRecoveryResult = await JsonRecoveryContract.ExecuteAsync();
            }),
            new("SD-0134", "Тогда", "Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.", scenarios, async context =>
            {
                await Assert.That(context.JsonRecoveryResult).IsNotNull();
                await JsonRecoveryContract.AssertAsync(context.JsonRecoveryResult!);
            })
        ];
    }
}
