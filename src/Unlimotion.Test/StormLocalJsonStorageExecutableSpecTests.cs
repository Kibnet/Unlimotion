using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormLocalJsonStorageExecutableSpecTests
{
    [Test]
    public async Task LocalJsonStorageScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0009-local-storage-migration.feature", "SC-0009-001");
        await Assert.That(scenario.Title).IsEqualTo("Задачи сериализуются в локальные JSON-файлы в выбранной папке.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Задачи сериализуются в локальные JSON-файлы в выбранной папке.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0009-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-025");
        await Assert.That(scenario.Tags).Contains("@story:ST-0009");
        await Assert.That(scenario.Tags).Contains("@test:TS-0014");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(LocalJsonStorageStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0123", "SD-0124", "SD-0125", "SD-0126" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
