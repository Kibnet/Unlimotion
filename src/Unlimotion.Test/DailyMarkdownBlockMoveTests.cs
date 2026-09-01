using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class DailyMarkdownBlockMoveTests
{
    [Test]
    public async Task Move_NonContiguousBlocksAcrossAreas_PreservesSourceOrderAndRawText()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        const string path = "Ежедневные/2026.08.27.md";
        const string raw = "# День\r\n\r\n## Работа <!-- unlimotion-area:work -->\r\n\r\nАльфа *raw*\r\n\r\nБета\r\n\r\nГамма [x](https://example.org)\r\n\r\n## Личное <!-- unlimotion-area:personal -->\r\n\r\nДельта\r\n";
        await vault.CreateAsync(path, raw, hasUtf8Bom: true);
        var source = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var document = parser.Parse(source.Text);
        var selected = new[]
        {
            Locate(path, document, "Альфа *raw*"),
            Locate(path, document, "Гамма [x]")
        };
        var insertBefore = Locate(path, document, "Дельта");

        var result = await new FeedMarkdownBlockMoveService(vault, parser).MoveAsync(
            new FeedMarkdownBlockMoveRequest(path, source.Revision, selected, insertBefore));

        var updated = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var content = parser.Parse(updated.Text).Blocks.Where(static block => block.IsContent).ToArray();
        var alpha = Array.FindIndex(content, block => block.Raw.Contains("Альфа *raw*", StringComparison.Ordinal));
        var gamma = Array.FindIndex(content, block => block.Raw.Contains("Гамма [x]", StringComparison.Ordinal));
        var delta = Array.FindIndex(content, block => block.Raw.Contains("Дельта", StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(alpha).IsGreaterThanOrEqualTo(0);
            await Assert.That(gamma).IsEqualTo(alpha + 1);
            await Assert.That(delta).IsEqualTo(gamma + 1);
            await Assert.That(content[alpha].AreaId).IsEqualTo("personal");
            await Assert.That(content[gamma].AreaId).IsEqualTo("personal");
            await Assert.That(updated.HasUtf8Bom).IsTrue();
            await Assert.That(updated.NewLine).IsEqualTo("\r\n");
            await Assert.That(updated.Text).Contains("Альфа *raw*\r\n\r\nГамма [x](https://example.org)\r\n");
            await Assert.That(result.OutputLocators.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Move_WithStaleRevision_RejectsEntireOperation()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        const string path = "Daily/2026-08-27.md";
        await vault.CreateAsync(path, "Один\n\nДва\n");
        var stale = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var locator = Locate(path, parser.Parse(stale.Text), "Один");
        await vault.WriteAsync(path, "Один\n\nДва\n\nВнешнее изменение\n", stale.Revision);
        var beforeAttempt = await vault.ReadAsync(path) ?? throw new InvalidOperationException();

        _ = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() =>
            new FeedMarkdownBlockMoveService(vault, parser).MoveAsync(
                new FeedMarkdownBlockMoveRequest(path, stale.Revision, [locator])));

        var afterAttempt = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        await Assert.That(afterAttempt.Text).IsEqualTo(beforeAttempt.Text);
        await Assert.That(afterAttempt.Revision).IsEqualTo(beforeAttempt.Revision);
    }

    [Test]
    public async Task Move_DropInsideSelection_DoesNotWrite()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        const string path = "Daily/2026-08-27.md";
        await vault.CreateAsync(path, "Один\n\nДва\n");
        var source = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var locator = Locate(path, parser.Parse(source.Text), "Один");

        _ = await NotesTestSupport.CaptureAsync<InvalidOperationException>(() =>
            new FeedMarkdownBlockMoveService(vault, parser).MoveAsync(
                new FeedMarkdownBlockMoveRequest(path, source.Revision, [locator], locator)));

        var unchanged = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        await Assert.That(unchanged.Text).IsEqualTo(source.Text);
        await Assert.That(unchanged.Revision).IsEqualTo(source.Revision);
    }

    [Test]
    public async Task Move_GeneratedTaskLink_IsRejectedWithoutMutation()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        const string path = "Daily/2026-08-27.md";
        await vault.CreateAsync(path, "[Задача](unlimotion://task/task1)\n\nОбычный блок\n");
        var source = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var locator = Locate(path, parser.Parse(source.Text), "unlimotion://task/task1");

        _ = await NotesTestSupport.CaptureAsync<InvalidOperationException>(() =>
            new FeedMarkdownBlockMoveService(vault, parser).MoveAsync(
                new FeedMarkdownBlockMoveRequest(path, source.Revision, [locator])));

        var unchanged = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        await Assert.That(unchanged.Text).IsEqualTo(source.Text);
    }

    [Test]
    public async Task Move_AreaHeadingMovesTheWholeSection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        const string path = "Daily/2026-08-27.md";
        const string raw = "# День\n\n## Работа <!-- unlimotion-area:work -->\n\nРабочий текст\n\n- [ ] Рабочая задача\n\n## Дом <!-- unlimotion-area:home -->\n\nДомашний текст\n";
        await vault.CreateAsync(path, raw);
        var source = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        var document = parser.Parse(source.Text);
        var work = LocateAny(path, document, block => block.Kind == MarkdownBlockKind.AreaHeading && block.AreaId == "work");
        var home = LocateAny(path, document, block => block.Kind == MarkdownBlockKind.AreaHeading && block.AreaId == "home");

        var result = await new FeedMarkdownBlockMoveService(vault, parser).MoveAsync(
            new FeedMarkdownBlockMoveRequest(path, source.Revision, [home], work));

        var updated = await vault.ReadAsync(path) ?? throw new InvalidOperationException();
        using (Assert.Multiple())
        {
            await Assert.That(updated.Text.IndexOf("unlimotion-area:home", StringComparison.Ordinal))
                .IsLessThan(updated.Text.IndexOf("unlimotion-area:work", StringComparison.Ordinal));
            await Assert.That(updated.Text.IndexOf("Домашний текст", StringComparison.Ordinal))
                .IsLessThan(updated.Text.IndexOf("unlimotion-area:work", StringComparison.Ordinal));
            await Assert.That(updated.Text.IndexOf("Рабочий текст", StringComparison.Ordinal))
                .IsGreaterThan(updated.Text.IndexOf("unlimotion-area:work", StringComparison.Ordinal));
            await Assert.That(result.OutputBlockIndices.Count).IsEqualTo(2);
        }
    }

    private static BlockLocator Locate(string path, MarkdownDocument document, string contains)
    {
        var block = document.Blocks.Single(candidate => candidate.IsContent
            && candidate.Raw.Contains(contains, StringComparison.Ordinal));
        return FeedReviewQueue.CoveredLocators(
            path,
            document,
            new MarkdownBlockSelection(block.Index, 1)).Single();
    }

    private static BlockLocator LocateAny(
        string path,
        MarkdownDocument document,
        Func<MarkdownBlock, bool> predicate)
    {
        var block = document.Blocks.Single(predicate);
        return new BlockLocator(
            path,
            block.AreaId ?? block.AreaName,
            block.Kind,
            block.ContentHash,
            0);
    }
}
