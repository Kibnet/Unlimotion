using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class WorkspaceBreadcrumbsLastOpenedStepDefinitions
{
    private const string ScenarioId = "SC-0004-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0079",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.WorkspaceBreadcrumbsLastOpenedTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0080",
                "И",
                "поведение относится к истории ST-0004",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceBreadcrumbsLastOpenedTaskSetAvailable).IsTrue();
                    context.WorkspaceBreadcrumbsLastOpenedStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0081",
                "Когда",
                "пользователь выполняет действие, описанное в критерии приёмки",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.WorkspaceBreadcrumbsLastOpenedTaskSetAvailable).IsTrue();
                    await Assert.That(context.WorkspaceBreadcrumbsLastOpenedStoryBehaviorConfirmed).IsTrue();

                    context.WorkspaceBreadcrumbsLastOpenedResult =
                        await WorkspaceBreadcrumbsLastOpenedUiContract
                            .ExecuteWorkspaceBreadcrumbsLastOpenedScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0082",
                "Тогда",
                "Breadcrumbs и last-opened контекст помогают вернуться к недавно открытым задачам.",
                supportsScenarios,
                async context =>
                {
                    var result = context.WorkspaceBreadcrumbsLastOpenedResult;

                    await Assert.That(result).IsNotNull();
                    await WorkspaceBreadcrumbsLastOpenedUiContract
                        .AssertWorkspaceBreadcrumbsLastOpenedScenarioResultAsync(result!);
                })
        ];
    }
}
