using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class FeedQuickTaskCaptureTests
{
    [Test]
    [Arguments("# План\n\nСделать релиз", "# План", "Сделать релиз")]
    [Arguments("Сделать релиз\n\n# План", "Сделать релиз", "# План")]
    public async Task Capture_HeadingAtEitherBoundaryIsNotSilentlyExcluded(string input, string title, string description)
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var target = new RecordingTarget();
        var result = await new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations),
            parser, mutations, target, new InMemoryFeedTaskConversionJournal())
            .CaptureAsync(new FeedTaskCaptureRequest("vault1", "heading", new DateOnly(2026, 9, 4), input,
                new AreaReference("work", "Работа"), null, ["work"]));
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(target.Tasks[0].Title).IsEqualTo(title);
        await Assert.That(target.Tasks[0].Description).IsEqualTo(description);
        var source = (await vault.ReadAsync(result.SourcePath))!.Text;
        await Assert.That(parser.Parse(source).Blocks.Any(block => block.Kind == MarkdownBlockKind.Heading)).IsFalse();
        await Assert.That(source).Contains("unlimotion://task/feed-heading");
        await Assert.That(source).Contains("unlimotion-area:work");
    }

    [Test]
    public async Task Capture_ProtectedAreaInsidePayloadFailsBeforeAnyWrite()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var target = new RecordingTarget();
        var journal = new InMemoryFeedTaskConversionJournal();
        await NotesTestSupport.CaptureAsync<InvalidOperationException>(() =>
            new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser,
                mutations, target, journal).CaptureAsync(new FeedTaskCaptureRequest("vault1", "area-input",
                new DateOnly(2026, 9, 4), "## Работа <!-- unlimotion-area:work -->\nЗадача", null, null, [])));
        await Assert.That(target.Tasks).IsEmpty();
        await Assert.That(await vault.ListMarkdownFilesAsync()).IsEmpty();
        await Assert.That(await journal.LoadAsync("vault1", "area-input")).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Capture_RestartsAfterAppendWasWrittenButAcknowledgementWasLost(bool existingDay)
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var realVault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-09-04.md";
        if (existingDay) await realVault.CreateAsync(path, "Предыдущая запись\r\n", hasUtf8Bom: true);
        var faultyVault = new LoseAppendAcknowledgementVault(realVault);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var target = new RecordingTarget();
        var request = new FeedTaskCaptureRequest("vault1", "lost-append", new DateOnly(2026, 9, 4),
            "Новая задача\n\nПодробности", null, null, []);
        await NotesTestSupport.CaptureAsync<IOException>(() => new FeedTaskCaptureService(faultyVault,
            new DailyNoteService(faultyVault, parser, mutations), parser, mutations, target,
            new FileFeedTaskConversionJournal(recovery.Path)).CaptureAsync(request));
        await Assert.That(target.Tasks).IsEmpty();
        var pending = await new FileFeedTaskConversionJournal(recovery.Path).LoadAsync("vault1", "lost-append");
        await Assert.That(pending!.CaptureIntent).IsNotNull();
        await Assert.That((await realVault.ReadAsync(path))!.Text).Contains("Подробности");
        var result = await new FeedTaskCaptureService(realVault,
            new DailyNoteService(realVault, parser, mutations), parser, mutations, target,
            new FileFeedTaskConversionJournal(recovery.Path)).CaptureAsync(request with { Date = request.Date.AddDays(1) });
        var saved = (await realVault.ReadAsync(path))!;
        await Assert.That(result.SourcePath).IsEqualTo(path);
        await Assert.That(saved.Text.Split("Новая задача").Length - 1).IsEqualTo(1);
        await Assert.That(saved.HasUtf8Bom).IsEqualTo(existingDay);
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        if (existingDay) await Assert.That(saved.Text).StartsWith("Предыдущая запись\r\n");
    }

    [Test]
    public async Task Capture_SameTextAsOlderBlock_OnlyConvertsNewRange()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-09-04.md";
        await vault.CreateAsync(path, "Повторяющийся текст\n\nСтарый контекст\n");
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        await new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser,
            mutations, new RecordingTarget(), new InMemoryFeedTaskConversionJournal())
            .CaptureAsync(new FeedTaskCaptureRequest("vault1", "same-text", new DateOnly(2026, 9, 4),
                "Повторяющийся текст", null, null, []));
        await Assert.That((await vault.ReadAsync(path))!.Text).StartsWith("Повторяющийся текст\n\nСтарый контекст\n");
    }

    [Test]
    public async Task Capture_MultipleParagraphs_ConvertsTheWholeCapture()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var daily = new DailyNoteService(vault, parser, mutations);
        var target = new RecordingTarget();
        var service = new FeedTaskCaptureService(vault, daily, parser, mutations, target,
            new InMemoryFeedTaskConversionJournal());
        var result = await service.CaptureAsync(new FeedTaskCaptureRequest("vault1", "multi",
            new DateOnly(2026, 9, 4), "Заголовок задачи\n\nВажная подробность\n\n- [ ] Подпункт\n\n",
            new AreaReference("work", "Работа"), null, ["work"]));

        await Assert.That(target.Tasks[0].Title).IsEqualTo("Заголовок задачи");
        await Assert.That(target.Tasks[0].Description).Contains("Важная подробность");
        await Assert.That(target.Tasks[0].Description).Contains("- [ ] Подпункт");
        var source = (await vault.ReadAsync(result.SourcePath))!.Text;
        await Assert.That(source).Contains("unlimotion-area:work");
        await Assert.That(source).Contains("unlimotion://task/feed-multi");
        await Assert.That(source).DoesNotContain("Важная подробность");
    }

    [Test]
    public async Task Capture_RetryCompletedOperation_DoesNotAppendOrCreateAgain()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var daily = new DailyNoteService(vault, parser, mutations);
        var target = new RecordingTarget();
        var journal = new InMemoryFeedTaskConversionJournal();
        var request = new FeedTaskCaptureRequest("vault1", "retry", new DateOnly(2026, 9, 4),
            "Единственная задача", null, null, []);
        var service = new FeedTaskCaptureService(vault, daily, parser, mutations, target, journal);
        var first = await service.CaptureAsync(request);
        var firstSource = (await vault.ReadAsync(first.SourcePath))!;
        await vault.WriteAsync(first.SourcePath, firstSource.Text + "\nНовая независимая запись\n", firstSource.Revision);
        var original = (await vault.ReadAsync(first.SourcePath))!.Text;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var next = await new FeedTaskCaptureService(vault, daily, parser, mutations, target, journal)
                .CaptureAsync(request with { Date = request.Date.AddDays(1) });
            await Assert.That(next.TaskId).IsEqualTo(first.TaskId);
            await Assert.That(next.SourcePath).IsEqualTo(first.SourcePath);
        }
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That((await vault.ReadAsync(first.SourcePath))!.Text).IsEqualTo(original);
        await Assert.That(await daily.OpenDayAsync(request.Date.AddDays(1))).IsNull();
    }

    [Test]
    public async Task Capture_RetryAfterTaskFailure_DoesNotDuplicateCapturedText()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var daily = new DailyNoteService(vault, parser, mutations);
        var journal = new InMemoryFeedTaskConversionJournal();
        var request = new FeedTaskCaptureRequest("vault1", "resume", new DateOnly(2026, 9, 4),
            "Не дублировать", null, null, []);
        await NotesTestSupport.CaptureAsync<IOException>(() =>
            new FeedTaskCaptureService(vault, daily, parser, mutations, new FailingTarget(), journal)
                .CaptureAsync(request));
        var target = new RecordingTarget();
        var result = await new FeedTaskCaptureService(vault, daily, parser, mutations, target, journal)
            .CaptureAsync(request);
        var source = (await vault.ReadAsync(result.SourcePath))!.Text;
        await Assert.That(source.Split("Не дублировать").Length - 1).IsEqualTo(1);
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Capture_CreatesTaskAndWritesLiveLinkWithoutCheckboxSyntax()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var dailyNotes = new DailyNoteService(vault, parser, new MarkdownMutationService(parser));
        var target = new RecordingTarget();
        var service = new FeedTaskCaptureService(
            vault,
            dailyNotes,
            parser,
            new MarkdownMutationService(parser),
            target,
            new InMemoryFeedTaskConversionJournal());

        var result = await service.CaptureAsync(new FeedTaskCaptureRequest(
            "vault1",
            "capture1",
            new DateOnly(2026, 8, 27),
            "Подготовить демонстрацию Ленты",
            null,
            null,
            ["work"]));

        var source = await vault.ReadAsync(result.SourcePath);
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(target.Tasks[0].Title).IsEqualTo("Подготовить демонстрацию Ленты");
        await Assert.That(target.Tasks[0].AreaIds).IsEquivalentTo(["work"]);
        await Assert.That(source!.Text).Contains("[Подготовить демонстрацию Ленты](unlimotion://task/feed-capture1)");
        await Assert.That(source.Text).DoesNotContain("- [ ]");
    }

    [Test]
    public async Task Capture_WhenTaskStorageFails_PreservesCapturedText()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var dailyNotes = new DailyNoteService(vault, parser, new MarkdownMutationService(parser));
        var service = new FeedTaskCaptureService(
            vault,
            dailyNotes,
            parser,
            new MarkdownMutationService(parser),
            new FailingTarget(),
            new InMemoryFeedTaskConversionJournal());

        _ = await NotesTestSupport.CaptureAsync<IOException>(() => service.CaptureAsync(
            new FeedTaskCaptureRequest(
                "vault1",
                "capture-failure",
                new DateOnly(2026, 8, 27),
                "Не потерять исходный текст",
                null,
                null,
                [])));

        var source = await dailyNotes.OpenDayAsync(new DateOnly(2026, 8, 27));
        await Assert.That(source!.Text).Contains("Не потерять исходный текст");
        await Assert.That(source.Text).DoesNotContain("unlimotion://task/");
    }

    private sealed class RecordingTarget : IFeedTaskCreationTarget
    {
        public List<FeedTaskDraft> Tasks { get; } = [];

        public Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Tasks.Add(draft);
            return Task.FromResult(new FeedCreatedTask(draft.TaskId, draft.Title));
        }
    }

    private sealed class FailingTarget : IFeedTaskCreationTarget
    {
        public Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default) =>
            Task.FromException<FeedCreatedTask>(new IOException("Task storage unavailable."));
    }

    private sealed class LoseAppendAcknowledgementVault(INoteVault inner) : INoteVault
    {
        public string RootPath => inner.RootPath;
        public string ResolveSafePath(string path) => inner.ResolveSafePath(path);
        public Task<VaultDocument?> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(path, cancellationToken);
        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);
        public async Task<VaultWriteResult> CreateAsync(string path, string text, bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default)
        {
            await inner.CreateAsync(path, text, hasUtf8Bom, cancellationToken);
            throw new IOException("Injected crash after durable append");
        }
        public async Task<VaultWriteResult> WriteAsync(string path, string text, string? expectedRevision,
            bool hasUtf8Bom = false, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(path, text, expectedRevision, hasUtf8Bom, cancellationToken);
            throw new IOException("Injected crash after durable append");
        }
    }
}
