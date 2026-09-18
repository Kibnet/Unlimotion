using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;

namespace Unlimotion.Test;

public class MarkdownLivePreviewCharacterizationTests
{
    [Test]
    public async Task ParserBlockRange_CanReplaceOneBlockWithoutChangingYamlOrNeighboringBytes()
    {
        const string raw = "---\r\ncustom:  two spaces\r\n---\r\n# День\r\n\r\nПервый *абзац*\r\n\r\n```custom\r\n<script>raw</script>\r\n```\r\nПоследний  абзац\r\n";
        var document = new MarkdownDocumentParser().Parse(raw);
        var target = document.Blocks.Single(block => block.Raw.StartsWith("Первый", StringComparison.Ordinal));

        var changed = document.ReplaceBlocks(target.Index, 1, "Изменённый **абзац**\r\n");

        await Assert.That(changed[..target.Start]).IsEqualTo(raw[..target.Start]);
        await Assert.That(changed[(target.Start + "Изменённый **абзац**\r\n".Length)..])
            .IsEqualTo(raw[(target.Start + target.Length)..]);
        await Assert.That(changed.StartsWith("---\r\ncustom:  two spaces\r\n---\r\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(changed.Contains("```custom\r\n<script>raw</script>\r\n```\r\n", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Parser_PreservesUnsupportedAndFencedContentAsExactRawSlices()
    {
        const string raw = "plugin::  value\n\n<div onclick=\"run()\">raw html</div>\n\n```dataviewjs\ndv.pages()\n```\n";
        var document = new MarkdownDocumentParser().Parse(raw);

        await Assert.That(string.Concat(document.Blocks.Select(static block => block.Raw))).IsEqualTo(raw);
        await Assert.That(document.Blocks.Single(block => block.Kind == MarkdownBlockKind.FencedCode).Raw)
            .IsEqualTo("```dataviewjs\ndv.pages()\n```\n");
        await Assert.That(document.Blocks.Single(block => block.Raw.StartsWith("<div", StringComparison.Ordinal)).Raw)
            .IsEqualTo("<div onclick=\"run()\">raw html</div>\n");
    }
}
