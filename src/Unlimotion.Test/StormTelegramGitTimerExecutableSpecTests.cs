using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormTelegramGitTimerExecutableSpecTests
{
    [Test]
    public async Task TelegramGitTimerConflictSafetyScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0014-telegram-bot.feature",
            "SC-0014-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "Git timers пропускают pull и push во время разрешения конфликтов.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0014-002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0025");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(TelegramGitTimerStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0005", "SD-0006", "SD-0007", "SD-0008" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
