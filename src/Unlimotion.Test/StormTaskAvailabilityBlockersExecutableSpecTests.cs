using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTaskAvailabilityBlockersExecutableSpecTests
{
    [Test]
    public async Task TaskAvailabilityBlockersScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0003-availability-rules.feature",
            "SC-0003-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Задача считается недоступной, если у неё есть незавершённые дочерние задачи, блокирующие задачи…");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0003-001");
        await Assert.That(scenario.Tags).Contains("@story:ST-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskAvailabilityBlockersStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0063", "SD-0064", "SD-0065", "SD-0066" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}