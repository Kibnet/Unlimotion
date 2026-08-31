using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[Property("CiMeasurementPackage", "search")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormSearchBehaviorExecutableSpecTests
{
    [Test]
    public async Task SearchBehaviorScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0005-search-and-filters.feature",
            "SC-0005-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Текстовый поиск поддерживает обычное и fuzzy-поведение согласно настройкам.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0005-001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0006");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(SearchBehaviorStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0035", "SD-0036", "SD-0037", "SD-0038" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
