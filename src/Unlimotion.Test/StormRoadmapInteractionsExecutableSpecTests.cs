using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormRoadmapInteractionsExecutableSpecTests
{
    [Test]
    public async Task RoadmapInteractionsScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0008-roadmap-graph.feature", "SC-0008-003");
        await Assert.That(scenario.Title).IsEqualTo("Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласн…");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласно спекам.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0008-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-024");
        await Assert.That(scenario.Tags).Contains("@story:ST-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0006");
        await Assert.That(scenario.Tags).Contains("@test:TS-0007");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(RoadmapInteractionsStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0119", "SD-0120", "SD-0121", "SD-0122" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
