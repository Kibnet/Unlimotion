using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormWorkspaceTreeCommandsExecutableSpecTests
{
    [Test]
    public async Task WorkspaceTreeCommandsScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0004-workspace-navigation.feature",
            "SC-0004-003");

        await Assert.That(scenario.Title).IsEqualTo(
            "Команды дерева поддерживают раскрытие, сворачивание, выбор, удаление, копирование и вставку в р…");
        await Assert.That(scenario.RuleTitle).IsEqualTo(
            "Команды дерева поддерживают раскрытие, сворачивание, выбор, удаление, копирование и вставку в рабочих представлениях.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0004-003");
        await Assert.That(scenario.Tags).Contains("@story:ST-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0004");
        await Assert.That(scenario.Tags).Contains("@test:TS-0011");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(WorkspaceTreeCommandsStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0083", "SD-0084", "SD-0085", "SD-0086" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
