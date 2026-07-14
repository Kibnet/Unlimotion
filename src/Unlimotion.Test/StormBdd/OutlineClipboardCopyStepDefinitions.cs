using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class OutlineClipboardCopyStepDefinitions
{
    private const string ScenarioId = "SC-0013-001";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0163", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.OutlineClipboardCopyTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0164", "И", "поведение относится к истории ST-0013", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardCopyTaskSetAvailable).IsTrue();
                context.OutlineClipboardCopyStoryBehaviorConfirmed = true;
            }),
            new("SD-0165", "Когда", "пользователь копирует или вставляет outline задач", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardCopyTaskSetAvailable).IsTrue();
                await Assert.That(context.OutlineClipboardCopyStoryBehaviorConfirmed).IsTrue();
                context.OutlineClipboardCopyResult = await OutlineClipboardCopyContract.ExecuteAsync();
            }),
            new("SD-0166", "Тогда", "Копирование может вывести markdown outline и description по выбранной задаче или поддереву.", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardCopyResult).IsNotNull();
                await OutlineClipboardCopyContract.AssertAsync(context.OutlineClipboardCopyResult!);
            })
        ];
    }
}
