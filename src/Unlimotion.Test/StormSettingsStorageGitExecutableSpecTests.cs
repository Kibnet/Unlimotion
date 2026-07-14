using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormSettingsStorageGitExecutableSpecTests
{
    [Test]
    public async Task SettingsStorageGitScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0012-settings-and-localization.feature", "SC-0012-002");
        await Assert.That(scenario.Title).IsEqualTo("Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликт…");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликтов.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0012-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-035");
        await Assert.That(scenario.Tags).Contains("@story:ST-0012");
        await Assert.That(scenario.Tags).Contains("@test:TS-0008");
        await Assert.That(scenario.Tags).Contains("@test:TS-0009");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(SettingsStorageGitStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0155", "SD-0156", "SD-0157", "SD-0158" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
