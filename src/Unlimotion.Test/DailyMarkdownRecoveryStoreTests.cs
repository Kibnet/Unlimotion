using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Recovery;

namespace Unlimotion.Test;

public class DailyMarkdownRecoveryStoreTests
{
    [Test]
    public async Task DraftCanBeRecoveredAndExplicitlyDiscardedOutsideVault()
    {
        using var appLocal = new TempNotesDirectory();
        var store = new FileFeedDraftStore(appLocal.Path);
        var draft = new FeedDraft(
            1,
            "vault1",
            "Ежедневные/2026-08-24.md",
            3,
            "base-revision",
            "несохранённый **Markdown**",
            DateTimeOffset.UtcNow,
            "# День\n\nнесохранённый **Markdown**\n",
            HasUtf8Bom: true);

        await store.SaveAsync(draft);
        var restartedStore = new FileFeedDraftStore(appLocal.Path);
        var loaded = await restartedStore.LoadAsync("vault1", "Ежедневные/2026-08-24.md", 3);
        var listed = await restartedStore.ListAsync("vault1");
        await restartedStore.DeleteAsync("vault1", "Ежедневные/2026-08-24.md", 3);
        var deleted = await restartedStore.LoadAsync("vault1", "Ежедневные/2026-08-24.md", 3);

        await Assert.That(loaded).IsEqualTo(draft);
        await Assert.That(listed).IsEquivalentTo(new[] { draft });
        await Assert.That(deleted).IsNull();
    }

    [Test]
    public async Task ConflictBundlePreservesBothVersionsAndIsImmutable()
    {
        using var appLocal = new TempNotesDirectory();
        var store = new FileDocumentConflictStore(appLocal.Path);
        var conflict = new DocumentConflictBundle(
            1,
            "vault1",
            "conflict1",
            "Ежедневные/2026-08-24.md",
            "Ежедневные/2026-08-24.md",
            3,
            "base",
            "editor-revision",
            "редактор",
            false,
            "disk-revision",
            "Obsidian",
            false,
            DateTimeOffset.UtcNow);

        var path = await store.PreserveAsync(conflict);
        var samePath = await store.PreserveAsync(conflict with { CreatedAt = conflict.CreatedAt.AddMinutes(1) });
        var restartedStore = new FileDocumentConflictStore(appLocal.Path);
        var loaded = await restartedStore.LoadAsync("vault1", "conflict1");
        var listed = await restartedStore.ListAsync("vault1");
        var unresolvedBefore = await restartedStore.ListUnresolvedAsync("vault1");
        var immutable = await NotesTestSupport.CaptureAsync<IOException>(() =>
            store.PreserveAsync(conflict with { EditorMarkdown = "другая версия" }));
        await restartedStore.AcknowledgeAsync("vault1", "conflict1");
        await restartedStore.AcknowledgeAsync("vault1", "conflict1");
        var acknowledgedStore = new FileDocumentConflictStore(appLocal.Path);
        var unresolvedAfter = await acknowledgedStore.ListUnresolvedAsync("vault1");
        var acknowledgedButPreserved = await acknowledgedStore.LoadAsync("vault1", "conflict1");
        var json = await File.ReadAllTextAsync(path);

        await Assert.That(samePath).IsEqualTo(path);
        await Assert.That(loaded).IsEqualTo(conflict);
        await Assert.That(listed).IsEquivalentTo(new[] { conflict });
        await Assert.That(unresolvedBefore).IsEquivalentTo(new[] { conflict });
        await Assert.That(unresolvedAfter).IsEmpty();
        await Assert.That(acknowledgedButPreserved).IsEqualTo(conflict);
        await Assert.That(json.Contains("редактор", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("Obsidian", StringComparison.Ordinal)).IsTrue();
        await Assert.That(immutable.Message.Contains("immutable", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task FileJournalPersistsDestinationCreatedStateForRecovery()
    {
        using var appLocal = new TempNotesDirectory();
        var store = new FileFeedOperationJournal(appLocal.Path);
        var record = new FeedOperationRecord(
            1, "vault1", "operation1", FeedOperationKind.NoteExtraction,
            FeedOperationState.DestinationCreated, "source.md", "Темы/note.md", "dest-rev", "source-rev", "note1", DateTimeOffset.UtcNow);

        await store.SaveAsync(record);
        var loaded = await store.LoadAsync("vault1", "operation1");

        await Assert.That(loaded).IsEqualTo(record);
        await Assert.That(loaded!.State).IsEqualTo(FeedOperationState.DestinationCreated);
    }

    [Test]
    public async Task KeepBothResolutionIsDurableAndRemainsPendingUntilReviewCheckpoint()
    {
        using var appLocal = new TempNotesDirectory();
        IFeedOperationJournal operationJournal = new FileFeedOperationJournal(appLocal.Path);
        IFeedTaskConversionJournal taskJournal = new FileFeedTaskConversionJournal(appLocal.Path);
        var selection = new MarkdownBlockSelection(1, 1);
        await operationJournal.SaveAsync(new FeedOperationRecord(
            2,
            "vault1",
            "note-keep-both",
            FeedOperationKind.NoteExtraction,
            FeedOperationState.DestinationCreated,
            "Ежедневные/2026-08-24.md",
            "Темы/Заметка.md",
            "destination-revision",
            null,
            "note1",
            DateTimeOffset.UtcNow,
            new FeedOperationRecoveryDescriptor(
                "note-keep-both",
                "source-revision",
                selection,
                "selection-hash",
                "destination-hash",
                "source-output-hash")));
        await taskJournal.SaveAsync(new FeedTaskConversionRecord(
            2,
            "vault1",
            "task-keep-both",
            FeedTaskConversionState.TaskCreated,
            "Ежедневные/2026-08-24.md",
            "source-revision",
            "task1",
            null,
            DateTimeOffset.UtcNow,
            new FeedTaskConversionRecoveryDescriptor(
                "task-keep-both",
                selection,
                "selection-hash",
                "source-output-hash",
                "Задача",
                string.Empty,
                false,
                [])));

        await operationJournal.ResolveKeepBothAsync("vault1", "note-keep-both");
        await taskJournal.ResolveKeepBothAsync("vault1", "task-keep-both");
        var operation = await operationJournal.LoadAsync("vault1", "note-keep-both");
        var task = await taskJournal.LoadAsync("vault1", "task-keep-both");

        await Assert.That(operation!.State).IsEqualTo(FeedOperationState.Completed);
        await Assert.That(operation.RecoveryResolution).IsEqualTo(FeedOperationRecoveryResolution.KeptBoth);
        await Assert.That(operation.ReviewApplied).IsFalse();
        await Assert.That(task!.State).IsEqualTo(FeedTaskConversionState.Completed);
        await Assert.That(task.RecoveryResolution).IsEqualTo(FeedOperationRecoveryResolution.KeptBoth);
        await Assert.That(task.ReviewApplied).IsFalse();
        await Assert.That(await operationJournal.ListPendingAsync("vault1")).HasSingleItem();
        await Assert.That(await taskJournal.ListPendingAsync("vault1")).HasSingleItem();

        await operationJournal.MarkReviewAppliedAsync("vault1", "note-keep-both");
        await taskJournal.MarkReviewAppliedAsync("vault1", "task-keep-both");
        await Assert.That(await operationJournal.ListPendingAsync("vault1")).IsEmpty();
        await Assert.That(await taskJournal.ListPendingAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task FileJournalsEnumerateOnlyUnfinishedOperationsAfterRestart()
    {
        using var appLocal = new TempNotesDirectory();
        var operationJournal = new FileFeedOperationJournal(appLocal.Path);
        await operationJournal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "pending-note", FeedOperationKind.NoteExtraction,
            FeedOperationState.DestinationCreated, "source.md", "note.md", "dest-rev", "source-rev", "note1", DateTimeOffset.UtcNow));
        await operationJournal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "completed-move", FeedOperationKind.MoveToToday,
            FeedOperationState.Completed, "source.md", "today.md", "dest-rev", "source-rev", "anchor1", DateTimeOffset.UtcNow));

        var taskJournal = new FileFeedTaskConversionJournal(appLocal.Path);
        await taskJournal.SaveAsync(new FeedTaskConversionRecord(
            1, "vault1", "pending-task", FeedTaskConversionState.TaskCreated,
            "source.md", "source-rev", "task1", null, DateTimeOffset.UtcNow));
        await taskJournal.SaveAsync(new FeedTaskConversionRecord(
            1, "vault1", "completed-task", FeedTaskConversionState.Completed,
            "source.md", "source-rev", "task2", "written-rev", DateTimeOffset.UtcNow));

        var reloadedOperations = await new FileFeedOperationJournal(appLocal.Path).ListPendingAsync("vault1");
        var reloadedTasks = await new FileFeedTaskConversionJournal(appLocal.Path).ListPendingAsync("vault1");

        await Assert.That(reloadedOperations.Select(static item => item.OperationId)).IsEquivalentTo(["pending-note"]);
        await Assert.That(reloadedTasks.Select(static item => item.OperationId)).IsEquivalentTo(["pending-task"]);
    }

    [Test]
    public async Task CompletedMarkdownStaysPendingUntilReviewCheckpointIsAppliedIdempotently()
    {
        using var appLocal = new TempNotesDirectory();
        var operationJournal = new FileFeedOperationJournal(appLocal.Path);
        await operationJournal.SaveAsync(new FeedOperationRecord(
            2, "vault1", "review-note", FeedOperationKind.NoteExtraction,
            FeedOperationState.Completed, "source.md", "note.md", "dest-rev", "source-rev", "note1",
            DateTimeOffset.UtcNow, ReviewApplied: false));

        var taskJournal = new FileFeedTaskConversionJournal(appLocal.Path);
        await taskJournal.SaveAsync(new FeedTaskConversionRecord(
            2, "vault1", "review-task", FeedTaskConversionState.Completed,
            "source.md", "expected-rev", "task1", "source-rev", DateTimeOffset.UtcNow,
            ReviewApplied: false));

        IFeedOperationJournal restartedOperations = new FileFeedOperationJournal(appLocal.Path);
        IFeedTaskConversionJournal restartedTasks = new FileFeedTaskConversionJournal(appLocal.Path);
        await Assert.That((await restartedOperations.ListPendingAsync("vault1")).Single().OperationId)
            .IsEqualTo("review-note");
        await Assert.That((await restartedTasks.ListPendingAsync("vault1")).Single().OperationId)
            .IsEqualTo("review-task");

        await restartedOperations.MarkReviewAppliedAsync("vault1", "review-note");
        await restartedOperations.MarkReviewAppliedAsync("vault1", "review-note");
        await restartedTasks.MarkReviewAppliedAsync("vault1", "review-task");
        await restartedTasks.MarkReviewAppliedAsync("vault1", "review-task");

        await Assert.That((await restartedOperations.LoadAsync("vault1", "review-note"))!.ReviewApplied).IsTrue();
        await Assert.That((await restartedTasks.LoadAsync("vault1", "review-task"))!.ReviewApplied).IsTrue();
        await Assert.That(await restartedOperations.ListPendingAsync("vault1")).IsEmpty();
        await Assert.That(await restartedTasks.ListPendingAsync("vault1")).IsEmpty();
    }
}
