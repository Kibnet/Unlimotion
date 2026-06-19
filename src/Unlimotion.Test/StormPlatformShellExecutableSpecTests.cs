using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormPlatformShellExecutableSpecTests
{
    [Test]
    public async Task PlatformShellContractScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0015-platform-shells.feature",
            "SC-0015-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Android, browser и iOS shell projects сохраняют общий UI contract без заявления runtime release support.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0015-002");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(PlatformShellStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0001", "SD-0002", "SD-0003", "SD-0004" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
