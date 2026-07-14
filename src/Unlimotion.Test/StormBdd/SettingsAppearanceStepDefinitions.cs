using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class SettingsAppearanceStepDefinitions
{
    private const string ScenarioId = "SC-0012-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0151", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.SettingsAppearanceTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0152", "И", "поведение относится к истории ST-0012", scenarios, async context =>
            {
                await Assert.That(context.SettingsAppearanceTaskSetAvailable).IsTrue();
                context.SettingsAppearanceStoryBehaviorConfirmed = true;
            }),
            new("SD-0153", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.SettingsAppearanceTaskSetAvailable).IsTrue();
                await Assert.That(context.SettingsAppearanceStoryBehaviorConfirmed).IsTrue();
                context.SettingsAppearanceResult = await SettingsAppearanceContract.ExecuteAsync();
            }),
            new("SD-0154", "Тогда", "Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.", scenarios, async context =>
            {
                await Assert.That(context.SettingsAppearanceResult).IsNotNull();
                await SettingsAppearanceContract.AssertAsync(context.SettingsAppearanceResult!);
            })
        ];
    }
}
