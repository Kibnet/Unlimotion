using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTelegramCommandExecutableSpecTests
{
    [Test]
    public async Task TelegramCommandAuthorizationScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0014-telegram-bot.feature",
            "SC-0014-001");

        await Assert.That(scenario.Title).IsEqualTo(
            "Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0014-001");
        await Assert.That(scenario.Tags).Contains("@test:TS-0022");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TelegramCommandStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0009", "SD-0010", "SD-0011", "SD-0012" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
