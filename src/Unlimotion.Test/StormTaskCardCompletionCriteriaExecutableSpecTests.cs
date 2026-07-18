using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskCardCompletionCriteriaExecutableSpecTests
{
    [Test]
    public async Task TaskCardCompletionCriteriaScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0007-task-card.feature",
            "SC-0007-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Критерии завершения можно добавлять, изменять и блокировать после завершения задачи.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Критерии завершения можно добавлять, изменять и блокировать после завершения задачи.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0007-003");
        await Assert.That(scenario.Tags).Contains("@rule:GR-021");
        await Assert.That(scenario.Tags).Contains("@story:ST-0007");
        await Assert.That(scenario.Tags).Contains("@test:TS-0003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskCardCompletionCriteriaStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0107", "SD-0108", "SD-0109", "SD-0110" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
