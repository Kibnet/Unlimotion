using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormNotificationToastExecutableSpecTests
{
    [Test]
    public async Task NotificationErrorToastScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0016-notification-error-ux.feature",
            "SC-0016-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Ошибка операции показывается в toast и закрывается пользователем.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0016-001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0021");
        await Assert.That(scenario.Steps.Count).IsEqualTo(5);

        var runner = new StormScenarioRunner(NotificationToastStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0017", "SD-0018", "SD-0019", "SD-0020", "SD-0021" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда", "И"]);
    }
}
