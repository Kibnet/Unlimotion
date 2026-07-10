using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests
{
    [Test]
    public async Task WorkspaceBreadcrumbsLastOpenedScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0004-workspace-navigation.feature",
            "SC-0004-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Breadcrumbs и last-opened контекст помогают вернуться к недавно открытым задачам.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0004-002");
        await Assert.That(scenario.Tags).Contains("@story:ST-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0011");
        await Assert.That(scenario.Tags).Contains("@test:TS-0016");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(WorkspaceBreadcrumbsLastOpenedStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0079", "SD-0080", "SD-0081", "SD-0082" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
