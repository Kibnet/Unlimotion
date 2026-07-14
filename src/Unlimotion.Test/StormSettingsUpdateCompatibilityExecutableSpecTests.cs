using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormSettingsUpdateCompatibilityExecutableSpecTests
{
    [Test]
    public async Task SettingsUpdateCompatibilityScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0012-settings-and-localization.feature", "SC-0012-003");
        await Assert.That(scenario.Title).IsEqualTo("Контролы обновления и compatibility checks защищают release/update flow.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Контролы обновления и compatibility checks защищают release/update flow.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0012-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-036");
        await Assert.That(scenario.Tags).Contains("@story:ST-0012");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0015");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(SettingsUpdateCompatibilityStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0159", "SD-0160", "SD-0161", "SD-0162" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
