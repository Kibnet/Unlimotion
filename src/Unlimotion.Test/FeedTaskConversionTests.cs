using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class FeedTaskConversionTests
{
    [Test]
    public async Task ConversionPersistsTaskFirstAndReplacesWholeSelectionWithOneLiveLink()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "## Работа <!-- unlimotion-area:work -->\n- [ ] Подготовить режим Ленты\n  Согласовать блочный разбор.\n\nОстаётся\n";
        var source = await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var selected = document.Blocks.Single(block => block.Kind == MarkdownBlockKind.TaskListItem);
        var target = new RecordingTaskTarget();
        var service = CreateService(vault, parser, target, new InMemoryFeedTaskConversionJournal());

        var result = await service.ConvertAsync(new FeedTaskConversionRequest(
            "vault1",
            "operation1",
            path,
            source.Revision,
            new MarkdownBlockSelection(selected.Index, 1),
            ["work", "project"],
            IsGoal: true));

        var updated = await vault.ReadAsync(path);
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(target.Tasks[0].TaskId).IsEqualTo("feed-operation1");
        await Assert.That(target.Tasks[0].Title).IsEqualTo("Подготовить режим Ленты");
        await Assert.That(target.Tasks[0].Description).Contains("Согласовать блочный разбор.");
        await Assert.That(target.Tasks[0].IsGoal).IsTrue();
        await Assert.That(target.Tasks[0].AreaIds).IsEquivalentTo(["work", "project"]);
        await Assert.That(updated!.Text).Contains("[Подготовить режим Ленты](unlimotion://task/feed-operation1)");
        await Assert.That(updated.Text).DoesNotContain("Согласовать блочный разбор.");
        await Assert.That(updated.Text).Contains("Остаётся");
        await Assert.That(result.TaskId).IsEqualTo("feed-operation1");
    }

    [Test]
    public async Task RetryAfterTaskWasPersistedBeforeCreatorReturnedDoesNotCreateDuplicate()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "- [ ] Одна задача\n");
        var parser = new MarkdownDocumentParser();
        var target = new RecordingTaskTarget { ThrowAfterFirstPersistence = true };
        var journal = new InMemoryFeedTaskConversionJournal();
        var service = CreateService(vault, parser, target, journal);
        var request = new FeedTaskConversionRequest(
            "vault1", "operation1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), [], false);

        _ = await NotesTestSupport.CaptureAsync<IOException>(() => service.ConvertAsync(request));
        var result = await service.ConvertAsync(request);

        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(target.Calls).IsEqualTo(2);
        await Assert.That(result.TaskId).IsEqualTo(target.Tasks[0].TaskId);
        await Assert.That((await vault.ReadAsync(request.SourcePath))!.Text.CountOccurrences("unlimotion://task/")).IsEqualTo(1);
    }

    [Test]
    public async Task RetryAfterSourceWriteBeforeCompletedCheckpointFinalizesWithoutDuplicateLink()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Задача после сбоя\n");
        var parser = new MarkdownDocumentParser();
        var target = new RecordingTaskTarget();
        var journal = new ThrowOnFirstCompletedSaveJournal();
        var service = CreateService(vault, parser, target, journal);
        var request = new FeedTaskConversionRequest(
            "vault1", "operation1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), [], false);

        _ = await NotesTestSupport.CaptureAsync<IOException>(() => service.ConvertAsync(request));
        var retry = await service.ConvertAsync(request);
        var updated = (await vault.ReadAsync(request.SourcePath))!.Text;

        await Assert.That(retry.WasAlreadyCompleted).IsTrue();
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(updated.CountOccurrences("unlimotion://task/feed-operation1")).IsEqualTo(1);
        await Assert.That((await journal.LoadAsync("vault1", "operation1"))!.State)
            .IsEqualTo(FeedTaskConversionState.Completed);
    }

    [Test]
    public async Task SourceConflictAfterTaskPersistenceKeepsTaskAndOriginalSelection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Исходная задача\n");
        var parser = new MarkdownDocumentParser();
        var target = new MutatingTaskTarget(vault, "Ежедневные/2026-08-23.md");
        var journal = new InMemoryFeedTaskConversionJournal();
        var service = CreateService(vault, parser, target, journal);

        _ = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() => service.ConvertAsync(
            new FeedTaskConversionRequest(
                "vault1", "operation1", "Ежедневные/2026-08-23.md", source.Revision,
                new MarkdownBlockSelection(0, 1), [], false)));

        var updated = await vault.ReadAsync("Ежедневные/2026-08-23.md");
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(updated!.Text).Contains("Исходная задача");
        await Assert.That(updated.Text).DoesNotContain("unlimotion://task/");
        await Assert.That((await journal.LoadAsync("vault1", "operation1"))!.State)
            .IsEqualTo(FeedTaskConversionState.TaskCreated);
    }

    [Test]
    public async Task ConversionSavesSourceRevisionBeforeReplacingSelection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Сохранить исходную версию\n";
        var source = await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var revisions = new RecordingRevisionStore();
        var service = new FeedTaskConversionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            new RecordingTaskTarget(),
            new InMemoryFeedTaskConversionJournal(),
            revisions);

        await service.ConvertAsync(new FeedTaskConversionRequest(
            "vault1", "operation1", path, source.Revision,
            new MarkdownBlockSelection(0, 1), [], false));

        await Assert.That(revisions.Documents.Count).IsEqualTo(1);
        await Assert.That(revisions.Documents[0].VaultId).IsEqualTo("vault1");
        await Assert.That(revisions.Documents[0].Document.RelativePath.Replace('\\', '/')).IsEqualTo(path);
        await Assert.That(revisions.Documents[0].Document.Revision).IsEqualTo(source.Revision);
        await Assert.That(revisions.Documents[0].Document.Text).IsEqualTo(raw);
    }

    [Test]
    public async Task RestartRecoveryReusesPersistedTaskAndOriginalOperationId()
    {
        using var directory = new TempNotesDirectory();
        using var appLocal = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string sourceRaw = "Задача после перезапуска\n";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, sourceRaw);
        var parser = new MarkdownDocumentParser();
        var target = new RecordingTaskTarget();
        var firstJournal = new FileFeedTaskConversionJournal(appLocal.Path);
        var firstService = new FeedTaskConversionService(
            vault, parser, new MarkdownMutationService(parser), target, firstJournal);
        var request = new FeedTaskConversionRequest(
            "vault1", "task-restart", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), ["work"], true, "session1");

        _ = await firstService.ConvertAsync(request);
        var replacedSource = await vault.ReadAsync(sourcePath);
        await vault.WriteAsync(sourcePath, sourceRaw, replacedSource!.Revision);
        var completedRecord = await firstJournal.LoadAsync("vault1", "task-restart");
        await firstJournal.SaveAsync(completedRecord! with
        {
            State = FeedTaskConversionState.TaskCreated,
            SourceRevision = null
        });

        var restartedJournal = new FileFeedTaskConversionJournal(appLocal.Path);
        var pending = (await restartedJournal.ListPendingAsync("vault1")).Single();
        await Assert.That(pending.RecoveryDescriptor!.ReviewSessionId).IsEqualTo("session1");
        await Assert.That(pending.RecoveryDescriptor.InputLocators!.Count).IsEqualTo(1);
        await Assert.That(pending.RecoveryDescriptor.SourceOutputLocators!.Count).IsEqualTo(1);
        var restartedService = new FeedTaskConversionService(
            vault, parser, new MarkdownMutationService(parser), target, restartedJournal);
        var recovered = await restartedService.ResumeAsync(pending);

        var reviewPending = (await restartedJournal.ListPendingAsync("vault1")).Single();
        await Assert.That(reviewPending.State).IsEqualTo(FeedTaskConversionState.Completed);
        await Assert.That(reviewPending.ReviewApplied).IsFalse();
        var completedRetry = await restartedService.ResumeAsync(reviewPending);
        await restartedService.MarkReviewAppliedAsync("vault1", "task-restart");
        await restartedService.MarkReviewAppliedAsync("vault1", "task-restart");

        await Assert.That(recovered.TaskId).IsEqualTo("feed-task-restart");
        await Assert.That(completedRetry.WasAlreadyCompleted).IsTrue();
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That(target.Tasks[0].OperationId).IsEqualTo("task-restart");
        await Assert.That((await vault.ReadAsync(sourcePath))!.Text.CountOccurrences("unlimotion://task/feed-task-restart"))
            .IsEqualTo(1);
        await Assert.That(await restartedJournal.ListPendingAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task CompletedConversionDriftRemainsPendingBeforeReviewCheckpoint()
    {
        using var directory = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, "Задача для causal review\n");
        var parser = new MarkdownDocumentParser();
        var target = new RecordingTaskTarget();
        var journal = new InMemoryFeedTaskConversionJournal();
        var service = CreateService(vault, parser, target, journal);
        var request = new FeedTaskConversionRequest(
            "vault1", "task-drift", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), [], false, "session1");

        _ = await service.ConvertAsync(request);
        var completedSource = await vault.ReadAsync(sourcePath);
        await vault.WriteAsync(
            sourcePath,
            completedSource!.Text + "Внешняя правка\n",
            completedSource.Revision);
        var pending = (await journal.ListPendingAsync("vault1")).Single();

        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(
            () => service.ResumeAsync(pending));

        var unresolved = await journal.LoadAsync("vault1", "task-drift");
        await Assert.That(unresolved!.State).IsEqualTo(FeedTaskConversionState.Completed);
        await Assert.That(unresolved.ReviewApplied).IsFalse();
        await Assert.That(unresolved.RecoveryIssue).Contains("drifted");
        await Assert.That((await vault.ReadAsync(sourcePath))!.Text).Contains("Внешняя правка");
        await Assert.That(target.Tasks.Count).IsEqualTo(1);
        await Assert.That((await journal.ListPendingAsync("vault1")).Single().OperationId)
            .IsEqualTo("task-drift");
    }

    private static FeedTaskConversionService CreateService(
        INoteVault vault,
        IMarkdownDocumentParser parser,
        IFeedTaskCreationTarget target,
        IFeedTaskConversionJournal journal) =>
        new(vault, parser, new MarkdownMutationService(parser), target, journal);

    private sealed class RecordingTaskTarget : IFeedTaskCreationTarget
    {
        public List<FeedTaskDraft> Tasks { get; } = [];

        public int Calls { get; private set; }

        public bool ThrowAfterFirstPersistence { get; init; }

        public Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var existing = Tasks.SingleOrDefault(task => task.TaskId == draft.TaskId);
            if (existing is null)
            {
                Tasks.Add(draft);
                if (ThrowAfterFirstPersistence && Calls == 1)
                {
                    throw new IOException("Simulated crash after task persistence.");
                }

                existing = draft;
            }

            return Task.FromResult(new FeedCreatedTask(existing.TaskId, existing.Title));
        }
    }

    private sealed class MutatingTaskTarget(INoteVault vault, string sourcePath) : IFeedTaskCreationTarget
    {
        public List<FeedTaskDraft> Tasks { get; } = [];

        public async Task<FeedCreatedTask> CreateOrGetAsync(
            FeedTaskDraft draft,
            CancellationToken cancellationToken = default)
        {
            var existing = Tasks.SingleOrDefault(task => task.TaskId == draft.TaskId);
            if (existing is null)
            {
                Tasks.Add(draft);
                var source = await vault.ReadAsync(sourcePath, cancellationToken);
                await vault.WriteAsync(sourcePath, source!.Text + "Внешняя правка\n", source.Revision, cancellationToken: cancellationToken);
                existing = draft;
            }

            return new FeedCreatedTask(existing.TaskId, existing.Title);
        }
    }

    private sealed class ThrowOnFirstCompletedSaveJournal : IFeedTaskConversionJournal
    {
        private FeedTaskConversionRecord? record;
        private bool didThrow;

        public Task<FeedTaskConversionRecord?> LoadAsync(
            string vaultId,
            string operationId,
            CancellationToken cancellationToken = default) => Task.FromResult(record);

        public Task SaveAsync(FeedTaskConversionRecord value, CancellationToken cancellationToken = default)
        {
            if (value.State == FeedTaskConversionState.Completed && !didThrow)
            {
                didThrow = true;
                throw new IOException("Simulated crash before completed checkpoint.");
            }

            record = value;
            return Task.CompletedTask;
        }
    }
}

internal static class FeedTaskConversionTestStringExtensions
{
    public static int CountOccurrences(this string value, string fragment)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0; index += fragment.Length)
        {
            count++;
        }

        return count;
    }
}
