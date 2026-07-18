using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class WorkspaceTreeCommandsStepDefinitions
{
    private const string ScenarioId = "SC-0004-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0083",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.WorkspaceTreeCommandsTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0084",
                "И",
                "поведение относится к истории ST-0004",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceTreeCommandsTaskSetAvailable).IsTrue();
                    context.WorkspaceTreeCommandsStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0085",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceTreeCommandsTaskSetAvailable).IsTrue();
                    await Assert.That(context.WorkspaceTreeCommandsStoryBehaviorConfirmed).IsTrue();

                    context.WorkspaceTreeCommandsResult =
                        await WorkspaceTreeCommandsUiContract.ExecuteWorkspaceTreeCommandsScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0086",
                "Тогда",
                "Команды дерева поддерживают раскрытие, сворачивание, выбор, удаление, копирование и вставку в рабочих представлениях.",
                supportsScenarios,
                async context =>
                {
                    var result = context.WorkspaceTreeCommandsResult;

                    await Assert.That(result).IsNotNull();
                    await WorkspaceTreeCommandsUiContract.AssertWorkspaceTreeCommandsScenarioResultAsync(result!);
                })
        ];
    }
}
