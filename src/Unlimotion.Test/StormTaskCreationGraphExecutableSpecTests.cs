using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskCreationGraphExecutableSpecTests
{
    [Test]
    public async Task TaskCreationGraphScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0001-task-graph.feature",
            "SC-0001-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Задачу можно создать в корне, рядом с выбранной задачей, как заблокированного соседа или внутри…");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0001-001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskCreationGraphStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0039", "SD-0040", "SD-0041", "SD-0042" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
