using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormWorkspaceNavigationTabsExecutableSpecTests
{
    [Test]
    public async Task WorkspaceNavigationTabsScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0004-workspace-navigation.feature",
            "SC-0004-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Вкладки показывают соответствующие подмножества задач и сохраняют текущий выбранный контекст.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0004-001");
        await Assert.That(scenario.Tags).Contains("@story:ST-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0011");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(WorkspaceNavigationTabsStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0075", "SD-0076", "SD-0077", "SD-0078" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
