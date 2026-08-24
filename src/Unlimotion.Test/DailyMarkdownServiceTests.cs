using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class DailyMarkdownServiceTests
{
    [Test]
    public async Task ListDaysReturnsOnlyDailyFilesNewestFirst()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Ежедневные/2026-08-22.md", "Старое\n");
        await vault.CreateAsync("Ежедневные/2026-08-24.md", "Новое\n");
        await vault.CreateAsync("Темы/Заметка.md", "Постоянная\n");
        await vault.CreateAsync(".unlimotion/internal.md", "Не показывать\n");
        var parser = new MarkdownDocumentParser();
        var service = new DailyNoteService(vault, parser, new MarkdownMutationService(parser));

        var days = await service.ListDaysAsync();

        await Assert.That(days.Select(day => day.Date)).IsEquivalentTo(new[] { new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 22) });
        await Assert.That(days[0].Date).IsEqualTo(new DateOnly(2026, 8, 24));
        await Assert.That(days[0].ContentBlockCount).IsEqualTo(1);
    }

    [Test]
    public async Task ListDaysPageReadsOnlyRequestedChronologyWindow()
    {
        using var directory = new TempNotesDirectory();
        var inner = new FileNoteVault(directory.Path);
        var firstDay = new DateOnly(2026, 1, 1);
        for (var offset = 0; offset < 40; offset++)
        {
            var day = firstDay.AddDays(offset);
            await inner.CreateAsync(DailyNoteService.GetRelativePath(day), $"Запись {day:yyyy-MM-dd}\n");
        }

        await inner.CreateAsync("Темы/Справка.md", "Не входит в хронологию\n");
        var vault = new CountingNoteVault(inner);
        var parser = new MarkdownDocumentParser();
        var service = new DailyNoteService(vault, parser, new MarkdownMutationService(parser));

        var page = await service.ListDaysPageAsync(skip: 14, take: 14);

        await Assert.That(page.TotalCount).IsEqualTo(40);
        await Assert.That(page.Days.Count).IsEqualTo(14);
        await Assert.That(page.Days[0].Date).IsEqualTo(firstDay.AddDays(25));
        await Assert.That(page.Days[^1].Date).IsEqualTo(firstDay.AddDays(12));
        await Assert.That(vault.ReadCount).IsEqualTo(14);
        await Assert.That(vault.ListCount).IsEqualTo(1);
    }

    [Test]
    public async Task AppendCaptureCreatesDailyContractAndThenUsesOptimisticRevision()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var service = new DailyNoteService(vault, parser, new MarkdownMutationService(parser));

        var first = await service.AppendCaptureAsync(
            new DateOnly(2026, 8, 24),
            "Первая мысль",
            new AreaReference("work", "Работа"));
        var second = await service.AppendCaptureAsync(
            new DateOnly(2026, 8, 24),
            "Вторая мысль",
            new AreaReference("work", "Работа"),
            first.Revision);

        await Assert.That(first.RelativePath.Replace('\\', '/')).IsEqualTo("Ежедневные/2026-08-24.md");
        await Assert.That(second.Text.Contains("## Работа <!-- unlimotion-area:work -->", StringComparison.Ordinal)).IsTrue();
        await Assert.That(second.Text.Contains("Первая мысль", StringComparison.Ordinal)).IsTrue();
        await Assert.That(second.Text.Contains("Вторая мысль", StringComparison.Ordinal)).IsTrue();
    }

    private sealed class CountingNoteVault(INoteVault inner) : INoteVault
    {
        private int readCount;
        private int listCount;

        public string RootPath => inner.RootPath;

        public int ReadCount => readCount;

        public int ListCount => listCount;

        public Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref readCount);
            return inner.ReadAsync(relativePath, cancellationToken);
        }

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref listCount);
            return inner.ListMarkdownFilesAsync(cancellationToken);
        }

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }
}
