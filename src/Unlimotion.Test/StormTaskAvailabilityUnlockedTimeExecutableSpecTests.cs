using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskAvailabilityUnlockedTimeExecutableSpecTests
{
    [Test]
    public async Task TaskAvailabilityUnlockedTimeScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0003-availability-rules.feature",
            "SC-0003-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "UnlockedDateTime устанавливается и очищается при изменении доступности.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0003-002");
        await Assert.That(scenario.Tags).Contains("@story:ST-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0014");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskAvailabilityUnlockedTimeStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0067", "SD-0068", "SD-0069", "SD-0070" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}