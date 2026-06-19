using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class NotificationToastStepDefinitions
{
    private const string ScenarioId = "SC-0016-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0017",
                "Дано",
                "у пользователя открыт основной экран Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.NotificationMainScreenAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0018",
                "И",
                "поведение относится к истории ST-0016",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.NotificationMainScreenAvailable).IsTrue();
                    context.NotificationStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0019",
                "Когда",
                "операция сообщает ошибку через notification manager",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.NotificationMainScreenAvailable).IsTrue();
                    await Assert.That(context.NotificationStoryBehaviorConfirmed).IsTrue();

                    context.NotificationToastResult =
                        await ToastNotificationUiContract.ExecuteErrorToastScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0020",
                "Тогда",
                "пользователь видит toast с текстом ошибки",
                supportsScenarios,
                async context =>
                {
                    var result = context.NotificationToastResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.MainScreenOpened).IsTrue();
                    await Assert.That(result.ErrorOperationReported).IsTrue();
                    await Assert.That(result.ToastTextObserved).IsTrue();
                }),
            new StormStepDefinition(
                "SD-0021",
                "И",
                "пользователь может закрыть toast без перезапуска экрана",
                supportsScenarios,
                async context =>
                {
                    var result = context.NotificationToastResult;

                    await Assert.That(result).IsNotNull();
                    await ToastNotificationUiContract
                        .AssertNotificationToastScenarioResultAsync(result!);
                })
        ];
    }
}
