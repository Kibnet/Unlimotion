using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskStatusMigrationExecutableSpecTests
{
    [Test]
    public async Task TaskStatusMigrationScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0002-task-lifecycle.feature",
            "SC-0002-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "История статусов и legacy-поля мигрируются без потери смысла.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0002-003");
        await Assert.That(scenario.Tags).Contains("@story:ST-0002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0014");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskStatusMigrationStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0059", "SD-0060", "SD-0061", "SD-0062" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}