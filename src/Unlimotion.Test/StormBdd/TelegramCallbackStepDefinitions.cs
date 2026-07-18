using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TelegramCallbackStepDefinitions
{
    private const string ScenarioId = "SC-0014-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0013",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TelegramCallbackTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0014",
                "И",
                "пользователь входит в allowed users Telegram bot",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramCallbackTaskSetAvailable).IsTrue();
                    context.TelegramCallbackAllowedUserConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0015",
                "Когда",
                "пользователь выполняет callback-действие Telegram bot для выбранной задачи",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramCallbackTaskSetAvailable).IsTrue();
                    await Assert.That(context.TelegramCallbackAllowedUserConfirmed).IsTrue();

                    context.TelegramCallbackResult =
                        await TelegramCallbackCoverageContract.ExecuteSupportedCallbackScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0016",
                "Тогда",
                "бот открывает задачу, меняет статус, удаляет задачу, создаёт prompt для sub/sibling и показывает relation lists без раскрытия данных неразрешённым пользователям.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TelegramCallbackResult;

                    await Assert.That(result).IsNotNull();
                    await TelegramCallbackCoverageContract
                        .AssertSupportedCallbackScenarioResultAsync(result!);
                })
        ];
    }
}
