using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskPlanningRepeaterExecutableSpecTests
{
    [Test]
    public async Task TaskPlanningRepeaterScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0006-calendar-planning.feature",
            "SC-0006-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0006-002");
        await Assert.That(scenario.Tags).Contains("@story:ST-0006");
        await Assert.That(scenario.Tags).Contains("@test:TS-0013");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(RepeaterPatternStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0091", "SD-0092", "SD-0093", "SD-0094" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
