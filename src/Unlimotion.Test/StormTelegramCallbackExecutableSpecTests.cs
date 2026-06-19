using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTelegramCallbackExecutableSpecTests
{
    [Test]
    public async Task TelegramCallbackScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0014-telegram-bot.feature",
            "SC-0014-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Callback-действия открывают задачу, меняют статус, удаляют и показывают отношения.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0014-003");
        await Assert.That(scenario.Tags).Contains("@test:TS-0023");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TelegramCallbackStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0013", "SD-0014", "SD-0015", "SD-0016" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
