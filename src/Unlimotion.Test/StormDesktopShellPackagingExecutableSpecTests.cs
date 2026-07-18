using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StormDesktopShellPackagingExecutableSpecTests
{
    [Test]
    public async Task DesktopShellPackagingScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0015-platform-shells.feature", "SC-0015-001");
        await Assert.That(scenario.Title).IsEqualTo("Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0015-001");
        await Assert.That(scenario.Tags).Contains("@rule:GR-041");
        await Assert.That(scenario.Tags).Contains("@story:ST-0015");
        await Assert.That(scenario.Tags).Contains("@test:TS-0011");
        await Assert.That(scenario.Tags).Contains("@test:TS-0015");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(DesktopShellPackagingStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0171", "SD-0172", "SD-0173", "SD-0174" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
