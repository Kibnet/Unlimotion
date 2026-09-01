using System;
using System.Collections.Generic;
using System.IO;
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
}
