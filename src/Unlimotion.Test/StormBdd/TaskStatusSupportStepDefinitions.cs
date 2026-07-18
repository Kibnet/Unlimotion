using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskStatusSupportScenarioResult(
    IReadOnlyList<DomainTaskStatus> DomainStatuses,
    IReadOnlyList<DomainTaskStatus> ViewModelOptionStatuses,
    IReadOnlyList<DomainTaskStatus> FilterStatuses);

internal static class TaskStatusSupportStepDefinitions
{
    private const string ScenarioId = "SC-0002-001";

    private static readonly DomainTaskStatus[] ExpectedStatuses =
    [
        DomainTaskStatus.NotReady,
        DomainTaskStatus.Prepared,
        DomainTaskStatus.InProgress,
        DomainTaskStatus.Completed,
        DomainTaskStatus.Archived
    ];

    private static readonly string ExpectedStatusKey = CreateStatusKey(ExpectedStatuses);

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0051",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskStatusSupportTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0052",
                "И",
                "поведение относится к истории ST-0002",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusSupportTaskSetAvailable).IsTrue();
                    context.TaskStatusSupportStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0053",
                "Когда",
                "пользователь меняет статус задачи или проверяет доступные переходы",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusSupportTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskStatusSupportStoryBehaviorConfirmed).IsTrue();

                    var domainStatuses = Enum.GetValues<DomainTaskStatus>();
                    var viewModelOptionStatuses = TaskStatusOption.All
                        .Select(option => option.Status)
                        .ToArray();
                    var filterStatuses = TaskStatusFilter.GetDefinitions()
                        .Select(filter => filter.Status)
                        .ToArray();

                    await Assert.That(CreateStatusKey(domainStatuses)).IsEqualTo(ExpectedStatusKey);
                    await Assert.That(CreateStatusKey(viewModelOptionStatuses)).IsEqualTo(ExpectedStatusKey);
                    await Assert.That(CreateStatusKey(filterStatuses)).IsEqualTo(ExpectedStatusKey);

                    context.TaskStatusSupportResult = new TaskStatusSupportScenarioResult(
                        domainStatuses,
                        viewModelOptionStatuses,
                        filterStatuses);
                }),
            new StormStepDefinition(
                "SD-0054",
                "Тогда",
                "приложение поддерживает статусы NotReady, Prepared, InProgress, Completed и Archived.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskStatusSupportResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(CreateStatusKey(result!.DomainStatuses)).IsEqualTo(ExpectedStatusKey);
                    await Assert.That(CreateStatusKey(result.ViewModelOptionStatuses)).IsEqualTo(ExpectedStatusKey);
                    await Assert.That(CreateStatusKey(result.FilterStatuses)).IsEqualTo(ExpectedStatusKey);
                })
        ];
    }

    private static string CreateStatusKey(IEnumerable<DomainTaskStatus> statuses)
    {
        return string.Join("|", statuses.Select(status => $"{status}:{(int)status}"));
    }
}
