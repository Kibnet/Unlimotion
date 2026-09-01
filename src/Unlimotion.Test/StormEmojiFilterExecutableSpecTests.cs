using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[Property("CiMeasurementPackage", "emoji")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormEmojiFilterExecutableSpecTests
{
    [Test]
    public async Task EmojiFilterScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0005-search-and-filters.feature",
            "SC-0005-003");

        await Assert.That(scenario.Title.Contains("emoji/text", StringComparison.Ordinal)).IsTrue();
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0005-003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0006");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(EmojiFilterStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0031", "SD-0032", "SD-0033", "SD-0034" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
