using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormCiReadmeMediaExecutableSpecTests
{
    [Test]
    public async Task CiReadmeMediaScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0015-platform-shells.feature", "SC-0015-003");
        await Assert.That(scenario.Title).IsEqualTo("CI и README media automation дают smoke/regression-доказательства для UI-потоков.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("CI и README media automation дают smoke/regression-доказательства для UI-потоков.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0015-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-043");
        await Assert.That(scenario.Tags).Contains("@story:ST-0015");
        await Assert.That(scenario.Tags).Contains("@test:TS-0011");
        await Assert.That(scenario.Tags).Contains("@test:TS-0015");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(CiReadmeMediaStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0175", "SD-0176", "SD-0177", "SD-0178" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
