using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class SettingsUpdateCompatibilityStepDefinitions
{
    private const string ScenarioId = "SC-0012-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0159", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.SettingsUpdateCompatibilityTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0160", "И", "поведение относится к истории ST-0012", scenarios, async context =>
            {
                await Assert.That(context.SettingsUpdateCompatibilityTaskSetAvailable).IsTrue();
                context.SettingsUpdateCompatibilityStoryBehaviorConfirmed = true;
            }),
            new("SD-0161", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.SettingsUpdateCompatibilityTaskSetAvailable).IsTrue();
                await Assert.That(context.SettingsUpdateCompatibilityStoryBehaviorConfirmed).IsTrue();
                context.SettingsUpdateCompatibilityResult = await SettingsUpdateCompatibilityContract.ExecuteAsync();
            }),
            new("SD-0162", "Тогда", "Контролы обновления и compatibility checks защищают release/update flow.", scenarios, async context =>
            {
                await Assert.That(context.SettingsUpdateCompatibilityResult).IsNotNull();
                await SettingsUpdateCompatibilityContract.AssertAsync(context.SettingsUpdateCompatibilityResult!);
            })
        ];
    }
}
