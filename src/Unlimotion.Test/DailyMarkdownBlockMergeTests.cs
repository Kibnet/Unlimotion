using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;

namespace Unlimotion.Test;

public class DailyMarkdownBlockMergeTests
{
    private readonly FeedMarkdownBlockMergeService service = new(new MarkdownDocumentParser());

    [Test]
    [Arguments("Альфа\n\nБета\n", 2, "Бета", "АльфаБета\n", 5)]
    [Arguments("Альфа\r\n\r\n- [ ] Бета\r\n", 2, "- [ ] Бета", "АльфаБета\r\n", 5)]
    [Arguments("# Заголовок\n\n> Цитата\n", 2, "> Цитата", "# ЗаголовокЦитата\n", 11)]
    public async Task CreatePlan_MergesSemanticTextAndPreservesDocumentLineEnding(
        string raw,
        int currentBlockIndex,
        string editedCurrentText,
        string expected,
        int expectedCaret)
    {
        var plan = service.CreatePlan(raw, currentBlockIndex, editedCurrentText);

        await Assert.That(plan).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(plan!.UpdatedDocumentRaw).IsEqualTo(expected);
            await Assert.That(plan.CaretIndex).IsEqualTo(expectedCaret);
            await Assert.That(plan.TargetBlockIndex).IsEqualTo(0);
            await Assert.That(raw[..plan.SelectionStart]
                              + plan.ReplacementRaw
                              + raw[(plan.SelectionStart + plan.SelectionLength)..])
                .IsEqualTo(expected);
        }
    }

    [Test]
    public async Task CreatePlan_PreservesUnsavedCurrentTextAndInlineMarkdown()
    {
        const string raw = "Начало\n\n- [ ] Старое\n";

        var plan = service.CreatePlan(raw, 2, "- [ ] **Новое**");

        await Assert.That(plan).IsNotNull();
        await Assert.That(plan!.UpdatedDocumentRaw).IsEqualTo("Начало**Новое**\n");
    }

    [Test]
    public async Task CreatePlan_RejectsAreaAndTechnicalBoundaries()
    {
        const string area = "<!-- unlimotion-area: area-1 -->\n## Область\n\nТекст\n";
        const string code = "До\n\n```text\nкод\n```\n";

        var areaPlan = service.CreatePlan(area, 2, "## Область");
        var codePlan = service.CreatePlan(code, 2, "```text\nкод\n```");

        using (Assert.Multiple())
        {
            await Assert.That(areaPlan).IsNull();
            await Assert.That(codePlan).IsNull();
        }
    }
}
