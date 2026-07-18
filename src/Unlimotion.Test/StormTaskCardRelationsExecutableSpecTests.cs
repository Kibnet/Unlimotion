using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskCardRelationsExecutableSpecTests
{
    [Test]
    public async Task TaskCardRelationsScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0007-task-card.feature",
            "SC-0007-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Блоки отношений позволяют просматривать и менять parents, containing, blocked и blocked-by связ…");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Блоки отношений позволяют просматривать и менять parents, containing, blocked и blocked-by связи.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0007-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-020");
        await Assert.That(scenario.Tags).Contains("@story:ST-0007");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskCardRelationsStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0103", "SD-0104", "SD-0105", "SD-0106" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
