using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class TelegramGitTimerStepDefinitions
{
    private const string ScenarioId = "SC-0014-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0005",
                "Дано",
                "у пользователя включены Telegram Git timers",
                supportsScenarios,
                context =>
                {
                    context.TelegramGitTimersEnabled = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0006",
                "И",
                "в Git backup идет разрешение конфликтов",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramGitTimersEnabled).IsTrue();
                    context.TelegramGitConflictResolutionInProgress = true;
                }),
            new StormStepDefinition(
                "SD-0007",
                "Когда",
                "срабатывают pull и push timer события Telegram bot",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TelegramGitTimersEnabled).IsTrue();
                    await Assert.That(context.TelegramGitConflictResolutionInProgress).IsTrue();

                    context.TelegramGitTimerResult =
                        TelegramGitTimerConflictSafetyContract.TriggerPullAndPushTimers(
                            conflictResolutionInProgress: true);
                }),
            new StormStepDefinition(
                "SD-0008",
                "Тогда",
                "бот не выполняет pull и commit/push до завершения разрешения конфликтов.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TelegramGitTimerResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.ConflictChecks).IsEqualTo(2);
                    await Assert.That(result.PullCalls).IsEqualTo(0);
                    await Assert.That(result.PushCalls).IsEqualTo(0);
                    await Assert.That(result.PushMessages).IsEmpty();
                })
        ];
    }
}
