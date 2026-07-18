using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class SettingsStorageGitStepDefinitions
{
    private const string ScenarioId = "SC-0012-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0155", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.SettingsStorageGitTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0156", "И", "поведение относится к истории ST-0012", scenarios, async context =>
            {
                await Assert.That(context.SettingsStorageGitTaskSetAvailable).IsTrue();
                context.SettingsStorageGitStoryBehaviorConfirmed = true;
            }),
            new("SD-0157", "Когда", "пользователь запускает или проверяет remote backup flow", scenarios, async context =>
            {
                await Assert.That(context.SettingsStorageGitTaskSetAvailable).IsTrue();
                await Assert.That(context.SettingsStorageGitStoryBehaviorConfirmed).IsTrue();
                context.SettingsStorageGitResult = await SettingsStorageGitContract.ExecuteAsync();
            }),
            new("SD-0158", "Тогда", "Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликтов.", scenarios, async context =>
            {
                await Assert.That(context.SettingsStorageGitResult).IsNotNull();
                await SettingsStorageGitContract.AssertAsync(context.SettingsStorageGitResult!);
            })
        ];
    }
}
