using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class GitBackupJobsStepDefinitions
{
    private const string ScenarioId = "SC-0010-004";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0147", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.GitBackupJobsTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0148", "И", "поведение относится к истории ST-0010", scenarios, async context =>
            {
                await Assert.That(context.GitBackupJobsTaskSetAvailable).IsTrue();
                context.GitBackupJobsStoryBehaviorConfirmed = true;
            }),
            new("SD-0149", "Когда", "пользователь запускает или проверяет remote backup flow", scenarios, async context =>
            {
                await Assert.That(context.GitBackupJobsTaskSetAvailable).IsTrue();
                await Assert.That(context.GitBackupJobsStoryBehaviorConfirmed).IsTrue();
                context.GitBackupJobsResult = await GitBackupJobsContract.ExecuteAsync();
            }),
            new("SD-0150", "Тогда", "Автоматические pull/push и backup-задачи не должны терять существующие локальные или удалённые задачи.", scenarios, async context =>
            {
                await Assert.That(context.GitBackupJobsResult).IsNotNull();
                await GitBackupJobsContract.AssertAsync(context.GitBackupJobsResult!);
            })
        ];
    }
}
