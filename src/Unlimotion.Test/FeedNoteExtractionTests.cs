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

public class FeedNoteExtractionTests
{
    [Test]
    public async Task ExtractionIsDestinationFirstAndReplacesWholeSelectionWithSafeWikiLink()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string raw = "## Работа <!-- unlimotion-area:work -->\nПолезный большой фрагмент\nвторая строка\n\nОстаётся в дне\n";
        var created = await vault.CreateAsync(sourcePath, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var selectedIndex = document.Blocks.First(block => block.Kind == MarkdownBlockKind.Paragraph).Index;
        var service = new FeedNoteExtractionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            new InMemoryFeedOperationJournal());
        var request = new NoteExtractionRequest(
            "vault1", "extract1", sourcePath, created.Revision,
            new MarkdownBlockSelection(selectedIndex, 1),
            "Темы", "Архитектура | [v2]", "note1", ["work"]);

        var result = await service.ExtractAsync(request);
        var source = await vault.ReadAsync(sourcePath);
        var note = await vault.ReadAsync(result.DestinationPath);

        await Assert.That(result.DestinationPath.StartsWith("Темы/", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.DestinationPath.IndexOfAny(['|', '[', ']', '#']) < 0).IsTrue();
        await Assert.That(note).IsNotNull();
        await Assert.That(note!.Text.Contains("unlimotion-id: note1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(note.Text.Contains("Полезный большой фрагмент\nвторая строка", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source!.Text.Contains("[[Темы/", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Text.Contains("|Архитектура", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Text.Contains("<!-- unlimotion-note:note1 -->", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Text.Contains("Полезный большой фрагмент", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Text.Contains("Остаётся в дне", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CompletedExtractionRetryReturnsSameNoteWithoutDuplicate()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string raw = "Фрагмент\n";
        var created = await vault.CreateAsync(sourcePath, raw);
        var parser = new MarkdownDocumentParser();
        var journal = new InMemoryFeedOperationJournal();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), journal);
        var request = new NoteExtractionRequest(
            "vault1", "extract1", sourcePath, created.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Заметка", "note1", [], "session1");

        var first = await service.ExtractAsync(request);
        var retry = await service.ExtractAsync(request);
        var markdownFiles = await vault.ListMarkdownFilesAsync();
        var reviewPending = (await journal.ListPendingAsync("vault1")).Single();
        var completedRetry = await service.ResumeAsync(reviewPending);
        await service.MarkReviewAppliedAsync("vault1", "extract1");
        await service.MarkReviewAppliedAsync("vault1", "extract1");

        await Assert.That(retry.WasAlreadyCompleted).IsTrue();
        await Assert.That(completedRetry.WasAlreadyCompleted).IsTrue();
        await Assert.That(reviewPending.State).IsEqualTo(FeedOperationState.Completed);
        await Assert.That(reviewPending.ReviewApplied).IsFalse();
        await Assert.That(retry.DestinationPath).IsEqualTo(first.DestinationPath);
        await Assert.That(markdownFiles.Count(path => path.StartsWith("Темы/", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(await journal.ListPendingAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task CollisionUsesDeterministicNumericSuffixAndDoesNotOverwriteExistingNote()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Темы/Заметка.md", "Существующая\n");
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Извлечь\n");
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), new InMemoryFeedOperationJournal());

        var result = await service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract2", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Заметка", "note2", []));

        await Assert.That(result.DestinationPath).IsEqualTo("Темы/Заметка 2.md");
        await Assert.That((await vault.ReadAsync("Темы/Заметка.md"))!.Text).IsEqualTo("Существующая\n");
    }

    [Test]
    public async Task TaskAndWikiSerializersKeepUserTextOutOfTargets()
    {
        var task = FeedLinkSerializer.Task("task_1", "a] (b)\\c\nnext");
        var note = FeedLinkSerializer.Note("Темы/Safe.md", "unsafe | alias", "note1");
        var traversal = await NotesTestSupport.Capture<ArgumentException>(() =>
            FeedLinkSerializer.ChooseAvailableNotePath("../outside", "Title", _ => false));

        await Assert.That(task).IsEqualTo("[a\\] \\(b\\)\\\\c next](unlimotion://task/task_1)");
        await Assert.That(note).IsEqualTo("[[Темы/Safe]] <!-- unlimotion-note:note1 -->");
        await Assert.That(traversal.Message.Length > 0).IsTrue();
    }

    [Test]
    public async Task PendingJournalResumesDestinationCreatedBeforeJournalCheckpoint()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Фрагмент\n");
        const string destinationPath = "Темы/Recovered.md";
        await vault.CreateAsync(destinationPath, "---\nunlimotion-id: note1\nunlimotion-areas: []\n---\n\n# Recovered\n\nФрагмент\n");
        var journal = new InMemoryFeedOperationJournal();
        await journal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "extract1", FeedOperationKind.NoteExtraction, FeedOperationState.Pending,
            "Ежедневные/2026-08-23.md", destinationPath, null, source.Revision, "note1", DateTimeOffset.UtcNow));
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), journal);

        var result = await service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Recovered", "note1", []));

        await Assert.That(result.DestinationPath).IsEqualTo(destinationPath);
        await Assert.That((await vault.ListMarkdownFilesAsync()).Count(path => path == destinationPath)).IsEqualTo(1);
        await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-23.md"))!.Text.Contains("unlimotion-note:note1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidMetadataIsRejectedBeforeAnyDestinationWriteAndEmptyAreasUseInlineArray()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Фрагмент\n");
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), new InMemoryFeedOperationJournal());
        var invalid = await NotesTestSupport.CaptureAsync<ArgumentException>(() => service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Title", "bad\nid", ["work\n- injected"] )));

        await Assert.That(invalid.Message.Length > 0).IsTrue();
        await Assert.That((await vault.ListMarkdownFilesAsync()).Count).IsEqualTo(1);

        var valid = await service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract2", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Title", "note2", []));
        var note = (await vault.ReadAsync(valid.DestinationPath))!.Text;
        await Assert.That(note.Contains("unlimotion-areas: []", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MismatchedLoadedJournalIsRejected()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Фрагмент\n");
        var journal = new InMemoryFeedOperationJournal();
        await journal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "extract1", FeedOperationKind.MoveToToday, FeedOperationState.Pending,
            "another.md", "destination.md", null, source.Revision, "wrong", DateTimeOffset.UtcNow));
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), journal);

        var failure = await NotesTestSupport.CaptureAsync<InvalidDataException>(() => service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Title", "note1", [])));

        await Assert.That(failure.Message.Contains("does not match", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DestinationCreatedJournalFinalizesWhenSourceWasAlreadyReplacedBeforeCrash()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var destination = await vault.CreateAsync(
            "Темы/Recovered.md",
            "---\nunlimotion-id: note1\nunlimotion-areas: []\n---\n\n# Recovered\n\nФрагмент\n");
        var original = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Фрагмент\n");
        await vault.WriteAsync(
            "Ежедневные/2026-08-23.md",
            "[[Темы/Recovered|Recovered]] <!-- unlimotion-note:note1 -->\n",
            original.Revision);
        var journal = new InMemoryFeedOperationJournal();
        await journal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "extract1", FeedOperationKind.NoteExtraction, FeedOperationState.DestinationCreated,
            "Ежедневные/2026-08-23.md", "Темы/Recovered.md", destination.Revision, original.Revision, "note1", DateTimeOffset.UtcNow));
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(vault, parser, new MarkdownMutationService(parser), journal);

        var result = await service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract1", "Ежедневные/2026-08-23.md", original.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Recovered", "note1", []));

        await Assert.That(result.WasAlreadyCompleted).IsTrue();
        await Assert.That((await journal.LoadAsync("vault1", "extract1"))!.State).IsEqualTo(FeedOperationState.Completed);
    }

    [Test]
    public async Task ExtractionSavesSourceRevisionBeforeReplacingSelection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string raw = "Полезный фрагмент\n";
        var source = await vault.CreateAsync(sourcePath, raw);
        var parser = new MarkdownDocumentParser();
        var revisions = new RecordingRevisionStore();
        var service = new FeedNoteExtractionService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            new InMemoryFeedOperationJournal(),
            revisions);

        await service.ExtractAsync(new NoteExtractionRequest(
            "vault1", "extract1", sourcePath, source.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Заметка", "note1", []));

        await Assert.That(revisions.Documents.Count).IsEqualTo(1);
        await Assert.That(revisions.Documents[0].VaultId).IsEqualTo("vault1");
        await Assert.That(revisions.Documents[0].Document.RelativePath.Replace('\\', '/')).IsEqualTo(sourcePath);
        await Assert.That(revisions.Documents[0].Document.Revision).IsEqualTo(source.Revision);
        await Assert.That(revisions.Documents[0].Document.Text).IsEqualTo(raw);
    }

    [Test]
    public async Task RestartRecoveryRefusesToReplaceSourceWhenCreatedDestinationDrifted()
    {
        using var directory = new TempNotesDirectory();
        using var appLocal = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string sourceRaw = "Не потерять этот фрагмент\n";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, sourceRaw);
        var parser = new MarkdownDocumentParser();
        var firstJournal = new FileFeedOperationJournal(appLocal.Path);
        var firstService = new FeedNoteExtractionService(
            vault, parser, new MarkdownMutationService(parser), firstJournal);
        var request = new NoteExtractionRequest(
            "vault1", "extract-restart", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Recovery", "note-restart", [], "session1");

        var completed = await firstService.ExtractAsync(request);
        var replacedSource = await vault.ReadAsync(sourcePath);
        await vault.WriteAsync(sourcePath, sourceRaw, replacedSource!.Revision);
        var completedRecord = await firstJournal.LoadAsync("vault1", "extract-restart");
        await firstJournal.SaveAsync(completedRecord! with
        {
            State = FeedOperationState.DestinationCreated,
            SourceRevision = original.Revision
        });
        var destination = await vault.ReadAsync(completed.DestinationPath);
        await vault.WriteAsync(completed.DestinationPath, destination!.Text + "Внешняя правка\n", destination.Revision);

        var restartedJournal = new FileFeedOperationJournal(appLocal.Path);
        var pending = (await restartedJournal.ListPendingAsync("vault1")).Single();
        await Assert.That(pending.RecoveryDescriptor!.ReviewSessionId).IsEqualTo("session1");
        await Assert.That(pending.RecoveryDescriptor.InputLocators!.Count).IsEqualTo(1);
        await Assert.That(pending.RecoveryDescriptor.SourceOutputLocators!.Count).IsEqualTo(1);
        var restartedService = new FeedNoteExtractionService(
            vault, parser, new MarkdownMutationService(parser), restartedJournal);
        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(
            () => restartedService.ResumeAsync(pending));

        await Assert.That((await vault.ReadAsync(sourcePath))!.Text).IsEqualTo(sourceRaw);
        await Assert.That((await restartedJournal.LoadAsync("vault1", "extract-restart"))!.State)
            .IsEqualTo(FeedOperationState.DestinationCreated);
    }

    [Test]
    public async Task CompletedDestinationDriftRemainsPendingBeforeReviewCheckpoint()
    {
        using var directory = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, "Фрагмент для causal review\n");
        var parser = new MarkdownDocumentParser();
        var journal = new InMemoryFeedOperationJournal();
        var service = new FeedNoteExtractionService(
            vault, parser, new MarkdownMutationService(parser), journal);
        var request = new NoteExtractionRequest(
            "vault1", "extract-completed-drift", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), "Темы", "Causal", "note-causal", [], "session1");

        var completed = await service.ExtractAsync(request);
        var completedSource = await vault.ReadAsync(sourcePath);
        var destination = await vault.ReadAsync(completed.DestinationPath);
        await vault.WriteAsync(
            completed.DestinationPath,
            destination!.Text + "Внешняя правка\n",
            destination.Revision);
        var pending = (await journal.ListPendingAsync("vault1")).Single();

        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(
            () => service.ResumeAsync(pending));

        var unresolved = await journal.LoadAsync("vault1", "extract-completed-drift");
        await Assert.That(unresolved!.State).IsEqualTo(FeedOperationState.Completed);
        await Assert.That(unresolved.ReviewApplied).IsFalse();
        await Assert.That(unresolved.RecoveryIssue).Contains("destination changed");
        await Assert.That((await vault.ReadAsync(sourcePath))!.Text).IsEqualTo(completedSource!.Text);
        await Assert.That((await vault.ReadAsync(completed.DestinationPath))!.Text).Contains("Внешняя правка");
        await Assert.That((await journal.ListPendingAsync("vault1")).Single().OperationId)
            .IsEqualTo("extract-completed-drift");
    }

    [Test]
    public async Task DestinationIsRecheckedAfterCheckpointBeforeSourceReplacement()
    {
        using var directory = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string destinationPath = "Темы/Recovery race.md";
        const string sourceRaw = "Фрагмент в окне гонки\n";
        var innerVault = new FileNoteVault(directory.Path);
        var original = await innerVault.CreateAsync(sourcePath, sourceRaw);
        var gate = new DestinationMutationGate();
        var vault = new MutatingDestinationVault(innerVault, destinationPath, gate);
        var journal = new ArmingOperationJournal(new InMemoryFeedOperationJournal(), gate);
        var parser = new MarkdownDocumentParser();
        var service = new FeedNoteExtractionService(
            vault, parser, new MarkdownMutationService(parser), journal);

        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(() => service.ExtractAsync(
            new NoteExtractionRequest(
                "vault1", "extract-race", sourcePath, original.Revision,
                new MarkdownBlockSelection(0, 1), "Темы", "Recovery race", "note-race", [])));

        await Assert.That((await innerVault.ReadAsync(sourcePath))!.Text).IsEqualTo(sourceRaw);
        await Assert.That((await journal.LoadAsync("vault1", "extract-race"))!.State)
            .IsEqualTo(FeedOperationState.DestinationCreated);
    }

    private sealed class DestinationMutationGate
    {
        public bool IsArmed { get; set; }
    }

    private sealed class ArmingOperationJournal(
        IFeedOperationJournal inner,
        DestinationMutationGate gate) : IFeedOperationJournal
    {
        public Task<FeedOperationRecord?> LoadAsync(
            string vaultId,
            string operationId,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(vaultId, operationId, cancellationToken);

        public async Task SaveAsync(
            FeedOperationRecord record,
            CancellationToken cancellationToken = default)
        {
            await inner.SaveAsync(record, cancellationToken);
            if (record.State == FeedOperationState.DestinationCreated && record.RecoveryIssue is null)
            {
                gate.IsArmed = true;
            }
        }

        public Task<IReadOnlyList<FeedOperationRecord>> ListPendingAsync(
            string vaultId,
            CancellationToken cancellationToken = default) =>
            inner.ListPendingAsync(vaultId, cancellationToken);
    }

    private sealed class MutatingDestinationVault(
        INoteVault inner,
        string destinationPath,
        DestinationMutationGate gate) : INoteVault
    {
        public string RootPath => inner.RootPath;

        public async Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            var current = await inner.ReadAsync(relativePath, cancellationToken);
            if (gate.IsArmed
                && current is not null
                && string.Equals(relativePath.Replace('\\', '/'), destinationPath, StringComparison.Ordinal))
            {
                gate.IsArmed = false;
                await inner.WriteAsync(
                    relativePath,
                    current.Text + "Внешняя правка\n",
                    current.Revision,
                    current.HasUtf8Bom,
                    cancellationToken);
                current = await inner.ReadAsync(relativePath, cancellationToken);
            }

            return current;
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

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

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
