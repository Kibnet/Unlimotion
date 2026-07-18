using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskGraphWorkspaceCommandExecutableSpecTests
{
    [Test]
    public async Task TaskGraphWorkspaceCommandScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0001-task-graph.feature",
            "SC-0001-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Перетаскивание, команды дерева и редактор отношений позволяют прикреплять, перемещать, блокиров…");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0001-003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskGraphWorkspaceCommandStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0047", "SD-0048", "SD-0049", "SD-0050" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
