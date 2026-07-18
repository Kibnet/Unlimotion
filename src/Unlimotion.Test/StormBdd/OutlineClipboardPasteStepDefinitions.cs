using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal static class OutlineClipboardPasteStepDefinitions
{
    private const string ScenarioId = "SC-0013-002";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var scenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };
        return
        [
            new("SD-0167", "Дано", "у пользователя открыт актуальный набор задач Unlimotion", scenarios, context =>
            {
                context.OutlineClipboardPasteTaskSetAvailable = true;
                return Task.CompletedTask;
            }),
            new("SD-0168", "И", "поведение относится к истории ST-0013", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardPasteTaskSetAvailable).IsTrue();
                context.OutlineClipboardPasteStoryBehaviorConfirmed = true;
            }),
            new("SD-0169", "Когда", "пользователь выполняет действие, описанное в критерии приёмки", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardPasteTaskSetAvailable).IsTrue();
                await Assert.That(context.OutlineClipboardPasteStoryBehaviorConfirmed).IsTrue();
                context.OutlineClipboardPasteResult = await OutlineClipboardPasteContract.ExecuteAsync();
            }),
            new("SD-0170", "Тогда", "Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.", scenarios, async context =>
            {
                await Assert.That(context.OutlineClipboardPasteResult).IsNotNull();
                await OutlineClipboardPasteContract.AssertAsync(context.OutlineClipboardPasteResult!);
            })
        ];
    }
}
