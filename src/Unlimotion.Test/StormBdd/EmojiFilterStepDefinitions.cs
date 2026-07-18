using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class EmojiFilterStepDefinitions
{
    private const string ScenarioId = "SC-0005-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0031",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.EmojiFilterTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0032",
                "И",
                "поведение относится к истории ST-0005",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.EmojiFilterTaskSetAvailable).IsTrue();
                    context.EmojiFilterStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0033",
                "Когда",
                "пользователь ищет или фильтрует задачи в текущем представлении",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.EmojiFilterTaskSetAvailable).IsTrue();
                    await Assert.That(context.EmojiFilterStoryBehaviorConfirmed).IsTrue();

                    context.EmojiFilterResult = await EmojiFilterUiContract.ExecuteEmojiFilterScenarioAsync();
                }),
            new StormStepDefinition(
                "SD-0034",
                "Тогда",
                "Фильтр включения и исключения emoji поддерживает поиск по emoji/text и сохраняет семантику flyout.",
                supportsScenarios,
                async context =>
                {
                    var result = context.EmojiFilterResult;

                    await Assert.That(result).IsNotNull();
                    await EmojiFilterUiContract.AssertEmojiFilterScenarioResultAsync(result!);
                })
        ];
    }
}
