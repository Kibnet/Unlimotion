using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormFilterResetExecutableSpecTests
{
    [Test]
    public async Task FilterResetScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0005-search-and-filters.feature",
            "SC-0005-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Фильтры статуса, дат, длительности и wanted применяются вместе и могут быть сброшены.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0005-002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0006");
        await Assert.That(scenario.Tags).Contains("@test:TS-0013");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(FilterResetStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0027", "SD-0028", "SD-0029", "SD-0030" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}