using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskStatusSupportExecutableSpecTests
{
    [Test]
    public async Task TaskStatusSupportScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0002-task-lifecycle.feature",
            "SC-0002-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Поддерживаются статусы NotReady, Prepared, InProgress, Completed и Archived.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0002-001");
        await Assert.That(scenario.Tags).Contains("@story:ST-0002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskStatusSupportStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0051", "SD-0052", "SD-0053", "SD-0054" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
