using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class PlatformShellStepDefinitions
{
    private const string ScenarioId = "SC-0015-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0001",
                "Дано",
                "desktop остаётся основной release-supported оболочкой Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.DesktopReleaseSupportedShellConfirmed = true;
                    context.RuntimeReleaseSupportClaimed = false;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0002",
                "И",
                "non-desktop shells должны переиспользовать общую Avalonia task UI модель",
                supportsScenarios,
                context =>
                {
                    context.NonDesktopSharedUiModelRequired = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0003",
                "Когда",
                "maintainer проверяет platform project contracts для Android, browser и iOS",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.DesktopReleaseSupportedShellConfirmed).IsTrue();
                    await Assert.That(context.NonDesktopSharedUiModelRequired).IsTrue();
                    await PlatformShellProjectContracts.AssertAllPlatformShellProjectContractsAsync();
                    context.PlatformProjectContractsChecked = true;
                }),
            new StormStepDefinition(
                "SD-0004",
                "Тогда",
                "каждый non-desktop shell ссылается на общую UI-модель и нужный Avalonia platform package, а Android содержит native Git assets без заявления runtime release support",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.PlatformProjectContractsChecked).IsTrue();
                    await Assert.That(context.RuntimeReleaseSupportClaimed).IsFalse();
                })
        ];
    }
}
