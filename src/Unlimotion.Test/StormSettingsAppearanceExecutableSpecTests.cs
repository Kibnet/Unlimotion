using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormSettingsAppearanceExecutableSpecTests
{
    [Test]
    public async Task SettingsAppearanceScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0012-settings-and-localization.feature", "SC-0012-001");
        await Assert.That(scenario.Title).IsEqualTo("Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0012-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-034");
        await Assert.That(scenario.Tags).Contains("@story:ST-0012");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0012");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(SettingsAppearanceStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0151", "SD-0152", "SD-0153", "SD-0154" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
