using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TaskGraphWorkspaceCommandStepDefinitions
{
    private const string ScenarioId = "SC-0001-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0047",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskGraphWorkspaceCommandTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0048",
                "И",
                "поведение относится к истории ST-0001",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskGraphWorkspaceCommandTaskSetAvailable).IsTrue();
                    context.TaskGraphWorkspaceCommandStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0049",
                "Когда",
                "пользователь меняет положение или связь задачи в рабочем представлении",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskGraphWorkspaceCommandTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskGraphWorkspaceCommandStoryBehaviorConfirmed).IsTrue();

                    context.TaskGraphWorkspaceCommandResult =
                        await TaskGraphWorkspaceCommandContract.ExecuteTaskGraphWorkspaceCommandScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0050",
                "Тогда",
                "Перетаскивание, команды дерева и редактор отношений позволяют прикреплять, перемещать, блокировать, обратно блокировать, клонировать, удалять и выбирать задачи из активных представлений.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskGraphWorkspaceCommandResult;

                    await Assert.That(result).IsNotNull();
                    await TaskGraphWorkspaceCommandContract.AssertTaskGraphWorkspaceCommandScenarioResultAsync(result!);
                })
        ];
    }
}
