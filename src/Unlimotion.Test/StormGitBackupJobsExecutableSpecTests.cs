using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

public class StormGitBackupJobsExecutableSpecTests
{
    [Test]
    public async Task GitBackupJobsScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario("features/storm/st-0010-git-backup-sync.feature", "SC-0010-004");
        await Assert.That(scenario.Title).IsEqualTo("Автоматические pull/push и backup-задачи не должны терять существующие локальные или удалённые…");
        await Assert.That(scenario.RuleTitle).IsEqualTo("Автоматические pull/push и backup-задачи не должны терять существующие локальные или удалённые задачи.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0010-004");
        await Assert.That(scenario.Tags).Contains("@rule:GR-031");
        await Assert.That(scenario.Tags).Contains("@story:ST-0010");
        await Assert.That(scenario.Tags).Contains("@test:TS-0009");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var context = await new StormScenarioRunner(GitBackupJobsStepDefinitions.Create()).ExecuteAsync(scenario);
        var expected = new[] { "SD-0147", "SD-0148", "SD-0149", "SD-0150" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expected.Length);
        foreach (var id in expected)
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray()).IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
