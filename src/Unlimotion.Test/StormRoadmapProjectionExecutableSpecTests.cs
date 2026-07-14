using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormRoadmapProjectionExecutableSpecTests
{
    [Test]
    public async Task RoadmapProjectionScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0008-roadmap-graph.feature",
            "SC-0008-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Roadmap строит узлы и связи из текущей модели задач.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Roadmap строит узлы и связи из текущей модели задач.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0008-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-022");
        await Assert.That(scenario.Tags).Contains("@story:ST-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0007");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(RoadmapProjectionStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0111", "SD-0112", "SD-0113", "SD-0114" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
