using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;

namespace Unlimotion.Test;

public class DailyMarkdownParserTests
{
    [Test]
    public async Task Parse_PreservesRawYamlUnknownContentAndAreaIdentity()
    {
        const string raw = "---\r\ncustom: untouched\r\n---\r\n# 24 августа\r\n\r\n## Работа <!-- unlimotion-area:area1 -->\r\nplugin:: value\r\n\r\n```custom\r\n<x>\r\n```\r\n";
        var parser = new MarkdownDocumentParser();

        var document = parser.Parse(raw);

        await Assert.That(string.Concat(document.Blocks.Select(block => block.Raw))).IsEqualTo(raw);
        await Assert.That(document.Blocks[0].Kind).IsEqualTo(MarkdownBlockKind.FrontMatter);
        var pluginBlock = document.Blocks.Single(block => block.Raw.StartsWith("plugin::"));
        await Assert.That(pluginBlock.AreaId).IsEqualTo("area1");
        await Assert.That(document.Blocks.Single(block => block.Kind == MarkdownBlockKind.FencedCode).Raw).IsEqualTo("```custom\r\n<x>\r\n```\r\n");
    }

    [Test]
    public async Task Parse_EmitsEveryNestedTaskItemAsOwnSyntacticBlock()
    {
        const string raw = "- [x] Готовый родитель\n  - [ ] Незавершённый ребёнок\n    продолжение ребёнка\n  - [x] Готовый сосед\n- [ ] Незавершённый сосед\n";
        var parser = new MarkdownDocumentParser();

        var items = parser.Parse(raw).Blocks.Where(block => block.Kind == MarkdownBlockKind.TaskListItem).ToArray();

        await Assert.That(items.Length).IsEqualTo(4);
        await Assert.That(items.Select(block => block.IsTaskCompleted)).IsEquivalentTo(new bool?[] { true, false, true, false });
        await Assert.That(items[1].Raw).IsEqualTo("  - [ ] Незавершённый ребёнок\n    продолжение ребёнка\n");
        await Assert.That(items[1].ListDepth > items[0].ListDepth).IsTrue();
    }

    [Test]
    public async Task QuickCapture_AppendsUnderStableAreaWithoutReformattingOtherBlocks()
    {
        const string raw = "---\r\nx: 1\r\n---\r\n\r\nДо области\r\n\r\n## Работа <!-- unlimotion-area:a1 -->\r\nСтарое\r\n\r\n## Дом <!-- unlimotion-area:a2 -->\r\nДомашнее\r\n";
        var mutations = new MarkdownMutationService(new MarkdownDocumentParser());

        var changed = mutations.AppendQuickCapture(raw, "- [ ] Новая задача\n  детали", new AreaReference("a1", "Переименованная работа"));

        await Assert.That(changed.StartsWith("---\r\nx: 1\r\n---", System.StringComparison.Ordinal)).IsTrue();
        await Assert.That(changed.Contains("## Работа <!-- unlimotion-area:a1 -->\r\nСтарое\r\n\r\n- [ ] Новая задача\r\n  детали\r\n\r\n## Дом", System.StringComparison.Ordinal)).IsTrue();
        await Assert.That(changed.Contains("Переименованная работа", System.StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReplaceSelection_ReplacesWholeContiguousBlocksOnly()
    {
        const string raw = "Вступление\n\n- [ ] Задача\n  описание\n\nСледующий блок\n";
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var taskIndex = document.Blocks.Single(block => block.Kind == MarkdownBlockKind.TaskListItem).Index;
        var mutations = new MarkdownMutationService(parser);

        var changed = mutations.ReplaceSelection(raw, new MarkdownBlockSelection(taskIndex, 1), "[Задача](unlimotion://task/id1)");

        await Assert.That(changed.Contains("- [ ] Задача", System.StringComparison.Ordinal)).IsFalse();
        await Assert.That(changed.Contains("[Задача](unlimotion://task/id1)\n\nСледующий блок", System.StringComparison.Ordinal)).IsTrue();
        await Assert.That(changed.StartsWith("Вступление\n\n", System.StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task H1IsDocumentMetadataWhileH3RemainsAreaContent()
    {
        const string raw = "# День\n\n## Работа <!-- unlimotion-area:a1 -->\n### Подтема\n";
        var headings = new MarkdownDocumentParser().Parse(raw).Blocks
            .Where(block => block.Kind is MarkdownBlockKind.Heading)
            .ToArray();

        await Assert.That(headings.Length).IsEqualTo(2);
        await Assert.That(headings[0].HeadingLevel).IsEqualTo(1);
        await Assert.That(headings[0].IsContent).IsFalse();
        await Assert.That(headings[1].HeadingLevel).IsEqualTo(3);
        await Assert.That(headings[1].IsContent).IsTrue();
        await Assert.That(headings[1].AreaId).IsEqualTo("a1");
    }

    [Test]
    public async Task HashTagWithoutFollowingWhitespaceIsParagraphNotHeading()
    {
        const string raw = "#идея\n\n# Заголовок\n";

        var blocks = new MarkdownDocumentParser().Parse(raw).Blocks
            .Where(block => block.Kind is not MarkdownBlockKind.Blank)
            .ToArray();

        await Assert.That(blocks.Length).IsEqualTo(2);
        await Assert.That(blocks[0].Kind).IsEqualTo(MarkdownBlockKind.Paragraph);
        await Assert.That(blocks[0].Raw).IsEqualTo("#идея\n");
        await Assert.That(blocks[1].Kind).IsEqualTo(MarkdownBlockKind.Heading);
        await Assert.That(blocks[1].HeadingLevel).IsEqualTo(1);
    }

    [Test]
    public async Task QuickCaptureRejectsAreaMarkerInjectionAndFlattensAreaName()
    {
        var mutations = new MarkdownMutationService(new MarkdownDocumentParser());
        var invalid = await NotesTestSupport.Capture<ArgumentException>(() =>
            mutations.AppendQuickCapture(string.Empty, "text", new AreaReference("bad\n-->", "Area")));
        var safe = mutations.AppendQuickCapture(string.Empty, "text", new AreaReference("area1", "Line one\nLine two"));

        await Assert.That(invalid.Message.Length > 0).IsTrue();
        await Assert.That(safe.Contains("## Line one Line two <!-- unlimotion-area:area1 -->", System.StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task QuickCaptureToUnresolvedHeadingDoesNotPersistSyntheticAreaMarker()
    {
        const string raw = "## Внешний раздел\nСтарая мысль\n\n## Другой раздел\nДругое\n";
        var mutations = new MarkdownMutationService(new MarkdownDocumentParser());

        var changed = mutations.AppendQuickCapture(
            raw,
            "Новая мысль",
            new AreaReference(null, "Внешний раздел") { MatchUnmarkedByName = true });

        await Assert.That(changed.Split("## Внешний раздел", StringSplitOptions.None).Length - 1).IsEqualTo(1);
        await Assert.That(changed).Contains("Новая мысль");
        await Assert.That(changed).DoesNotContain("unlimotion-area:");
        await Assert.That(changed.IndexOf("Новая мысль", StringComparison.Ordinal))
            .IsLessThan(changed.IndexOf("## Другой раздел", StringComparison.Ordinal));
        var inserted = new MarkdownDocumentParser().Parse(changed).Blocks
            .Single(block => block.Raw.Contains("Новая мысль", StringComparison.Ordinal));
        await Assert.That(inserted.AreaId).IsNull();
        await Assert.That(inserted.AreaName).IsEqualTo("Внешний раздел");
    }

    [Test]
    public async Task StableAreaWithAmbiguousNameDoesNotGuessMarkerlessHeading()
    {
        const string raw = "## Проект\nСуществующая запись\n";
        var mutations = new MarkdownMutationService(new MarkdownDocumentParser());

        var changed = mutations.AppendQuickCapture(
            raw,
            "Запись стабильной области",
            new AreaReference("work-project", "Проект") { MatchUnmarkedByName = false });

        await Assert.That(changed).Contains("## Проект\nСуществующая запись");
        await Assert.That(changed).Contains("## Проект <!-- unlimotion-area:work-project -->");
        await Assert.That(changed).Contains("Запись стабильной области");
    }

    [Test]
    public async Task LegacyAreaReferenceJsonDefaultsToNoMarkerlessGuessing()
    {
        var area = JsonSerializer.Deserialize<AreaReference>(
            "{\"id\":\"work\",\"name\":\"Работа\"}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(area).IsNotNull();
        await Assert.That(area!.MatchUnmarkedByName).IsFalse();
    }
}
