using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class WorkspaceNavigationTabsStepDefinitions
{
    private const string ScenarioId = "SC-0004-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0075",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.WorkspaceNavigationTabsTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0076",
                "И",
                "поведение относится к истории ST-0004",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceNavigationTabsTaskSetAvailable).IsTrue();
                    context.WorkspaceNavigationTabsStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0077",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceNavigationTabsTaskSetAvailable).IsTrue();
                    await Assert.That(context.WorkspaceNavigationTabsStoryBehaviorConfirmed).IsTrue();

                    context.WorkspaceNavigationTabsResult =
                        await WorkspaceNavigationTabsUiContract.ExecuteWorkspaceNavigationTabsScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0078",
                "Тогда",
                "Вкладки показывают соответствующие подмножества задач и сохраняют текущий выбранный контекст.",
                supportsScenarios,
                async context =>
                {
                    var result = context.WorkspaceNavigationTabsResult;

                    await Assert.That(result).IsNotNull();
                    await WorkspaceNavigationTabsUiContract.AssertWorkspaceNavigationTabsScenarioResultAsync(result!);
                })
        ];
    }
}
