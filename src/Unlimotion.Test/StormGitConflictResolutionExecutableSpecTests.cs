using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormGitConflictResolutionExecutableSpecTests
{
    [Test]
    public async Task GitConflictResolutionScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0010-git-backup-sync.feature", "SC-0010-003");
        await Assert.That(scenario.Title).IsEqualTo("Разрешение конфликтов поддерживает решения на уровне файла и отдельных полей перед commit/push.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Разрешение конфликтов поддерживает решения на уровне файла и отдельных полей перед commit/push.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0010-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-030");
        await Assert.That(scenario.Tags).Contains("@story:ST-0010");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0009");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(GitConflictResolutionStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0143", "SD-0144", "SD-0145", "SD-0146" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
