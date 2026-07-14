using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskPlanningWantedImportanceExecutableSpecTests
{
    [Test]
    public async Task TaskPlanningWantedImportanceScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0006-calendar-planning.feature",
            "SC-0006-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0006-003");
        await Assert.That(scenario.Tags).Contains("@story:ST-0006");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0003");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Tags).Contains("@test:TS-0013");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(WantedImportanceStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0095", "SD-0096", "SD-0097", "SD-0098" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
