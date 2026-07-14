using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormTaskPlanningDatesExecutableSpecTests
{
    [Test]
    public async Task TaskPlanningDatesScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0006-calendar-planning.feature",
            "SC-0006-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0006-001");
        await Assert.That(scenario.Tags).Contains("@story:ST-0006");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0003");
        await Assert.That(scenario.Tags).Contains("@constraint:CN-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0005");
        await Assert.That(scenario.Tags).Contains("@test:TS-0013");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TaskPlanningDatesStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0087", "SD-0088", "SD-0089", "SD-0090" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
