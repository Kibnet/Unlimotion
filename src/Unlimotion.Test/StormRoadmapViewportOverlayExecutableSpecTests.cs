using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormRoadmapViewportOverlayExecutableSpecTests
{
    [Test]
    public async Task RoadmapViewportOverlayScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0008-roadmap-graph.feature",
            "SC-0008-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0008-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-023");
        await Assert.That(scenario.Tags).Contains("@story:ST-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0007");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(RoadmapViewportOverlayStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0115", "SD-0116", "SD-0117", "SD-0118" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
