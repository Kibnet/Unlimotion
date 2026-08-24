using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class FeedMoveToTodayTests
{
    [Test]
    public async Task MoveIsDestinationFirstLeavesStableSourceLinkAndAnchor()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string raw = "## Работа <!-- unlimotion-area:work -->\n- [ ] Перенести задачу\n  подробности\n\nОставить здесь\n";
        var sourceWrite = await vault.CreateAsync(sourcePath, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var taskIndex = document.Blocks.Single(block => block.Kind == MarkdownBlockKind.TaskListItem).Index;
        var journal = new InMemoryFeedOperationJournal();
        var service = new FeedMoveToTodayService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            journal);
        var request = new MoveToTodayRequest(
            "vault1", "move1", sourcePath, sourceWrite.Revision,
            new MarkdownBlockSelection(taskIndex, 1),
            new DateOnly(2026, 8, 24), new AreaReference("work", "Работа"), null, "session1");

        var result = await service.MoveAsync(request);
        var source = (await vault.ReadAsync(sourcePath))!.Text;
        var destination = (await vault.ReadAsync(result.DestinationPath))!.Text;

        await Assert.That(result.Anchor).IsEqualTo("unlimotion-move-move1");
        await Assert.That(source.Contains("[[Ежедневные/2026-08-24#^unlimotion-move-move1|", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("Перенесено на 2026-08-24", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("Перенести задачу", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("Оставить здесь", StringComparison.Ordinal)).IsTrue();
        await Assert.That(destination.Contains("- [ ] Перенести задачу", StringComparison.Ordinal)).IsTrue();
        await Assert.That(destination.Contains("^unlimotion-move-move1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(destination.Contains("## Работа <!-- unlimotion-area:work -->", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.DeferredFromSessionId).IsEqualTo("session1");
        var operation = await journal.LoadAsync("vault1", "move1");
        await Assert.That(operation!.RecoveryDescriptor!.ReviewSessionId).IsEqualTo("session1");
        await Assert.That(operation.RecoveryDescriptor.InputLocators!.Count).IsEqualTo(1);
        await Assert.That(operation.RecoveryDescriptor.SourceOutputLocators!.Count).IsEqualTo(1);
        await Assert.That(operation.RecoveryDescriptor.DestinationOutputLocators!.Count).IsEqualTo(2);
        await Assert.That(operation.State).IsEqualTo(FeedOperationState.Completed);
        await Assert.That(operation.ReviewApplied).IsFalse();
        var reviewPending = (await journal.ListPendingAsync("vault1")).Single();
        var completedRetry = await service.ResumeAsync(reviewPending);
        await service.MarkReviewAppliedAsync("vault1", "move1");
        await service.MarkReviewAppliedAsync("vault1", "move1");
        await Assert.That(completedRetry.WasAlreadyCompleted).IsTrue();
        await Assert.That(await journal.ListPendingAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task CompletedMoveRetryDoesNotDuplicateDestinationOrSourceLink()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Перенести\n");
        var parser = new MarkdownDocumentParser();
        var service = new FeedMoveToTodayService(vault, parser, new MarkdownMutationService(parser), new InMemoryFeedOperationJournal());
        var request = new MoveToTodayRequest(
            "vault1", "move1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, null, "session1");

        var first = await service.MoveAsync(request);
        var retry = await service.MoveAsync(request);
        var sourceText = (await vault.ReadAsync("Ежедневные/2026-08-23.md"))!.Text;
        var destinationText = (await vault.ReadAsync(first.DestinationPath))!.Text;

        await Assert.That(retry.WasAlreadyCompleted).IsTrue();
        await Assert.That(Count(sourceText, "#^unlimotion-move-move1")).IsEqualTo(1);
        await Assert.That(Count(destinationText, "^unlimotion-move-move1")).IsEqualTo(1);
    }

    [Test]
    public async Task ExistingDestinationRevisionMustMatchBeforeAnyWrite()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Перенести\n");
        await vault.CreateAsync("Ежедневные/2026-08-24.md", "Изменено извне\n");
        var parser = new MarkdownDocumentParser();
        var service = new FeedMoveToTodayService(vault, parser, new MarkdownMutationService(parser), new InMemoryFeedOperationJournal());

        var conflict = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() => service.MoveAsync(new MoveToTodayRequest(
            "vault1", "move1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, "stale", "session1")));

        await Assert.That(conflict.RelativePath).IsEqualTo("Ежедневные/2026-08-24.md");
        await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-23.md"))!.Text).IsEqualTo("Перенести\n");
        await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-24.md"))!.Text).IsEqualTo("Изменено извне\n");
    }

    [Test]
    public async Task PendingJournalResumesAnchorWrittenBeforeJournalCheckpoint()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Перенести\n");
        const string destinationPath = "Ежедневные/2026-08-24.md";
        await vault.CreateAsync(destinationPath, "Перенести\n^unlimotion-move-move1\n");
        var journal = new InMemoryFeedOperationJournal();
        await journal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "move1", FeedOperationKind.MoveToToday, FeedOperationState.Pending,
            "Ежедневные/2026-08-23.md", destinationPath, null, source.Revision,
            "unlimotion-move-move1", DateTimeOffset.UtcNow));
        var parser = new MarkdownDocumentParser();
        var service = new FeedMoveToTodayService(vault, parser, new MarkdownMutationService(parser), journal);

        var result = await service.MoveAsync(new MoveToTodayRequest(
            "vault1", "move1", "Ежедневные/2026-08-23.md", source.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, null, "session1"));
        var destination = (await vault.ReadAsync(destinationPath))!.Text;

        await Assert.That(result.WasAlreadyCompleted).IsFalse();
        await Assert.That(Count(destination, "^unlimotion-move-move1")).IsEqualTo(1);
        await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-23.md"))!.Text.Contains("#^unlimotion-move-move1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DestinationCreatedJournalFinalizesWhenSourceLinkWasAlreadyWrittenBeforeCrash()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var destination = await vault.CreateAsync("Ежедневные/2026-08-24.md", "Перенести\n^unlimotion-move-move1\n");
        var original = await vault.CreateAsync("Ежедневные/2026-08-23.md", "Перенести\n");
        var expectedLink = FeedLinkSerializer.MovedBlock(new DateOnly(2026, 8, 24), "unlimotion-move-move1") + "\n";
        await vault.WriteAsync("Ежедневные/2026-08-23.md", expectedLink, original.Revision);
        var journal = new InMemoryFeedOperationJournal();
        await journal.SaveAsync(new FeedOperationRecord(
            1, "vault1", "move1", FeedOperationKind.MoveToToday, FeedOperationState.DestinationCreated,
            "Ежедневные/2026-08-23.md", "Ежедневные/2026-08-24.md", destination.Revision, original.Revision,
            "unlimotion-move-move1", DateTimeOffset.UtcNow));
        var parser = new MarkdownDocumentParser();
        var service = new FeedMoveToTodayService(vault, parser, new MarkdownMutationService(parser), journal);

        var result = await service.MoveAsync(new MoveToTodayRequest(
            "vault1", "move1", "Ежедневные/2026-08-23.md", original.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, null, "session1"));

        await Assert.That(result.WasAlreadyCompleted).IsTrue();
        await Assert.That((await journal.LoadAsync("vault1", "move1"))!.State).IsEqualTo(FeedOperationState.Completed);
    }

    [Test]
    public async Task MoveSavesSourceAndExistingDestinationBeforeChangingEitherFile()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string destinationPath = "Ежедневные/2026-08-24.md";
        const string sourceRaw = "Перенести безопасно\n";
        const string destinationRaw = "Уже записано сегодня\n";
        var source = await vault.CreateAsync(sourcePath, sourceRaw);
        var destination = await vault.CreateAsync(destinationPath, destinationRaw);
        var parser = new MarkdownDocumentParser();
        var revisions = new RecordingRevisionStore();
        var service = new FeedMoveToTodayService(
            vault,
            parser,
            new MarkdownMutationService(parser),
            new InMemoryFeedOperationJournal(),
            revisions);

        await service.MoveAsync(new MoveToTodayRequest(
            "vault1", "move1", sourcePath, source.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null,
            destination.Revision, "session1"));

        await Assert.That(revisions.Documents.Count).IsEqualTo(2);
        var sourceRevision = revisions.Documents.Single(item => item.Document.RelativePath.Replace('\\', '/') == sourcePath);
        var destinationRevision = revisions.Documents.Single(item => item.Document.RelativePath.Replace('\\', '/') == destinationPath);
        await Assert.That(sourceRevision.Document.Revision).IsEqualTo(source.Revision);
        await Assert.That(sourceRevision.Document.Text).IsEqualTo(sourceRaw);
        await Assert.That(destinationRevision.Document.Revision).IsEqualTo(destination.Revision);
        await Assert.That(destinationRevision.Document.Text).IsEqualTo(destinationRaw);
    }

    [Test]
    public async Task RestartRecoveryRefusesToReplaceSourceWhenMoveDestinationDrifted()
    {
        using var directory = new TempNotesDirectory();
        using var appLocal = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        const string sourceRaw = "Перенести без потери\n";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, sourceRaw);
        var parser = new MarkdownDocumentParser();
        var firstJournal = new FileFeedOperationJournal(appLocal.Path);
        var firstService = new FeedMoveToTodayService(
            vault, parser, new MarkdownMutationService(parser), firstJournal);
        var request = new MoveToTodayRequest(
            "vault1", "move-restart", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, null, "session1");

        var completed = await firstService.MoveAsync(request);
        var replacedSource = await vault.ReadAsync(sourcePath);
        await vault.WriteAsync(sourcePath, sourceRaw, replacedSource!.Revision);
        var completedRecord = await firstJournal.LoadAsync("vault1", "move-restart");
        await firstJournal.SaveAsync(completedRecord! with
        {
            State = FeedOperationState.DestinationCreated,
            SourceRevision = original.Revision
        });
        var destination = await vault.ReadAsync(completed.DestinationPath);
        await vault.WriteAsync(completed.DestinationPath, destination!.Text + "Внешняя правка\n", destination.Revision);

        var restartedJournal = new FileFeedOperationJournal(appLocal.Path);
        var pending = (await restartedJournal.ListPendingAsync("vault1")).Single();
        var restartedService = new FeedMoveToTodayService(
            vault, parser, new MarkdownMutationService(parser), restartedJournal);
        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(
            () => restartedService.ResumeAsync(pending));

        await Assert.That((await vault.ReadAsync(sourcePath))!.Text).IsEqualTo(sourceRaw);
        await Assert.That((await restartedJournal.LoadAsync("vault1", "move-restart"))!.State)
            .IsEqualTo(FeedOperationState.DestinationCreated);
    }

    [Test]
    public async Task CompletedMoveDestinationDriftRemainsPendingBeforeReviewCheckpoint()
    {
        using var directory = new TempNotesDirectory();
        const string sourcePath = "Ежедневные/2026-08-23.md";
        var vault = new FileNoteVault(directory.Path);
        var original = await vault.CreateAsync(sourcePath, "Перенести для causal review\n");
        var parser = new MarkdownDocumentParser();
        var journal = new InMemoryFeedOperationJournal();
        var service = new FeedMoveToTodayService(
            vault, parser, new MarkdownMutationService(parser), journal);
        var request = new MoveToTodayRequest(
            "vault1", "move-completed-drift", sourcePath, original.Revision,
            new MarkdownBlockSelection(0, 1), new DateOnly(2026, 8, 24), null, null, "session1");

        var completed = await service.MoveAsync(request);
        var completedSource = await vault.ReadAsync(sourcePath);
        var destination = await vault.ReadAsync(completed.DestinationPath);
        await vault.WriteAsync(
            completed.DestinationPath,
            destination!.Text + "Внешняя правка\n",
            destination.Revision);
        var pending = (await journal.ListPendingAsync("vault1")).Single();

        _ = await NotesTestSupport.CaptureAsync<FeedOperationRecoveryConflictException>(
            () => service.ResumeAsync(pending));

        var unresolved = await journal.LoadAsync("vault1", "move-completed-drift");
        await Assert.That(unresolved!.State).IsEqualTo(FeedOperationState.Completed);
        await Assert.That(unresolved.ReviewApplied).IsFalse();
        await Assert.That(unresolved.RecoveryIssue).Contains("journaled payload");
        await Assert.That((await vault.ReadAsync(sourcePath))!.Text).IsEqualTo(completedSource!.Text);
        await Assert.That((await vault.ReadAsync(completed.DestinationPath))!.Text).Contains("Внешняя правка");
        await Assert.That((await journal.ListPendingAsync("vault1")).Single().OperationId)
            .IsEqualTo("move-completed-drift");
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}

internal sealed class RecordingRevisionStore : IRevisionStore
{
    public List<(string VaultId, VaultDocument Document)> Documents { get; } = [];

    public Task SaveAsync(
        string vaultId,
        VaultDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Documents.Add((vaultId, document));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(
        string vaultId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}
