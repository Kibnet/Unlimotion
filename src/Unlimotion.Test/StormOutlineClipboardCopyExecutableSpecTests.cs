using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormOutlineClipboardCopyExecutableSpecTests
{
    [Test]
    public async Task OutlineClipboardCopyScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0013-outline-clipboard.feature", "SC-0013-001");
        await Assert.That(scenario.Title).IsEqualTo("Копирование может вывести markdown outline и description по выбранной задаче или поддереву.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Копирование может вывести markdown outline и description по выбранной задаче или поддереву.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0013-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-037");
        await Assert.That(scenario.Tags).Contains("@story:ST-0013");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0010");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(OutlineClipboardCopyStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0163", "SD-0164", "SD-0165", "SD-0166" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
