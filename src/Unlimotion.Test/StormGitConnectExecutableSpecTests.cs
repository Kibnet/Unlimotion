using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormGitConnectExecutableSpecTests
{
    [Test]
    public async Task GitConnectScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0010-git-backup-sync.feature", "SC-0010-001");
        await Assert.That(scenario.Title).IsEqualTo("Настройки позволяют предварительно проверить и подключить Git-репозиторий, а также подготовить…");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Настройки позволяют предварительно проверить и подключить Git-репозиторий, а также подготовить remote.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0010-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-028");
        await Assert.That(scenario.Tags).Contains("@story:ST-0010");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0009");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(GitConnectStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0135", "SD-0136", "SD-0137", "SD-0138" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
