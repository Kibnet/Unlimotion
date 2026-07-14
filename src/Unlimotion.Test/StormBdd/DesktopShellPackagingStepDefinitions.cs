using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class DesktopShellPackagingStepDefinitions
{
    private const string ScenarioId = "SC-0015-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0171", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.DesktopShellPackagingTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0172", "И", "поведение относится к истории ST-0015", scenarios, async context =>
            {
                await Assert.That(context.DesktopShellPackagingTaskSetAvailable).IsTrue();
                context.DesktopShellPackagingStoryBehaviorConfirmed = true;
            }),
            new("SD-0173", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.DesktopShellPackagingTaskSetAvailable).IsTrue();
                await Assert.That(context.DesktopShellPackagingStoryBehaviorConfirmed).IsTrue();
                context.DesktopShellPackagingResult = await DesktopShellPackagingContract.ExecuteAsync();
            }),
            new("SD-0174", "Тогда", "Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.", scenarios, async context =>
            {
                await Assert.That(context.DesktopShellPackagingResult).IsNotNull();
                await DesktopShellPackagingContract.AssertAsync(context.DesktopShellPackagingResult!);
            })
        ];
    }
}
