using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormOutlineClipboardPasteExecutableSpecTests
{
    [Test]
    public async Task OutlineClipboardPasteScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0013-outline-clipboard.feature", "SC-0013-002");
        await Assert.That(scenario.Title).IsEqualTo("Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0013-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-038");
        await Assert.That(scenario.Tags).Contains("@story:ST-0013");
        await Assert.That(scenario.Tags).Contains("@test:TS-0001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0010");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(OutlineClipboardPasteStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0167", "SD-0168", "SD-0169", "SD-0170" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
