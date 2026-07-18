using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormJsonRecoveryExecutableSpecTests
{
    [Test]
    public async Task JsonRecoveryScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0009-local-storage-migration.feature", "SC-0009-003");
        await Assert.That(scenario.Title).IsEqualTo("Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0009-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-027");
        await Assert.That(scenario.Tags).Contains("@story:ST-0009");
        await Assert.That(scenario.Tags).Contains("@test:TS-0014");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(JsonRecoveryStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0131", "SD-0132", "SD-0133", "SD-0134" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
