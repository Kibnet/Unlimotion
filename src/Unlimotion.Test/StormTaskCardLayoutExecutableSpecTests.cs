using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskCardLayoutExecutableSpecTests
{
    [Test]
    public async Task TaskCardLayoutScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0007-task-card.feature",
            "SC-0007-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Карточка задачи остаётся читаемой и управляемой в десктопных и узких компоновках.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Карточка задачи остаётся читаемой и управляемой в десктопных и узких компоновках.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0007-001");
        await Assert.That(scenario.Tags).Contains("@story:ST-0007");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0003");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskCardLayoutStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0099", "SD-0100", "SD-0101", "SD-0102" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
