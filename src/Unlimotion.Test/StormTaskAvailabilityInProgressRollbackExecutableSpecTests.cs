using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskAvailabilityInProgressRollbackExecutableSpecTests
{
    [Test]
    public async Task TaskAvailabilityInProgressRollbackScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0003-availability-rules.feature",
            "SC-0003-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Если задача стала недоступной, недопустимые InProgress-состояния корректируются.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0003-003");
        await Assert.That(scenario.Tags).Contains("@story:ST-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskAvailabilityInProgressRollbackStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0071", "SD-0072", "SD-0073", "SD-0074" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}