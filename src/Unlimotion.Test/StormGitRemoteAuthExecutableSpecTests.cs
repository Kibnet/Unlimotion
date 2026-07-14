using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormGitRemoteAuthExecutableSpecTests
{
    [Test]
    public async Task GitRemoteAuthScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0010-git-backup-sync.feature", "SC-0010-002");
        await Assert.That(scenario.Title).IsEqualTo("Remote-аутентификация поддерживает SSH и token/http сценарии, включая хранение SSH key.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Remote-аутентификация поддерживает SSH и token/http сценарии, включая хранение SSH key.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0010-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-029");
        await Assert.That(scenario.Tags).Contains("@story:ST-0010");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0009");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(GitRemoteAuthStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0139", "SD-0140", "SD-0141", "SD-0142" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
