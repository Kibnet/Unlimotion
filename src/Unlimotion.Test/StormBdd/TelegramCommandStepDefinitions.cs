using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TelegramCommandStepDefinitions
{
    private const string ScenarioId = "SC-0014-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0009",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TelegramCommandTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0010",
                "И",
                "поведение относится к истории ST-0014",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramCommandTaskSetAvailable).IsTrue();
                    context.TelegramCommandStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0011",
                "Когда",
                "пользователь обращается к задачам через Telegram bot",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramCommandTaskSetAvailable).IsTrue();
                    await Assert.That(context.TelegramCommandStoryBehaviorConfirmed).IsTrue();

                    context.TelegramCommandAuthorizationResult =
                        await TelegramCommandAuthorizationContract.ExecuteSupportedCommandsScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0012",
                "Тогда",
                "Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TelegramCommandAuthorizationResult;

                    await Assert.That(result).IsNotNull();
                    await TelegramCommandAuthorizationContract
                        .AssertSupportedCommandsScenarioResultAsync(result!);
                })
        ];
    }
}
