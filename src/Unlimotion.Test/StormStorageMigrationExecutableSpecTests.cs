using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormStorageMigrationExecutableSpecTests
{
    [Test]
    public async Task StorageMigrationScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0009-local-storage-migration.feature", "SC-0009-002");
        await Assert.That(scenario.Title).IsEqualTo("Миграции восстанавливают reverse links, status model и availability при загрузке.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Миграции восстанавливают reverse links, status model и availability при загрузке.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0009-002");
        await Assert.That(scenario.Tags).Contains("@rule:GR-026");
        await Assert.That(scenario.Tags).Contains("@story:ST-0009");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0014");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(StorageMigrationStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0127", "SD-0128", "SD-0129", "SD-0130" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
