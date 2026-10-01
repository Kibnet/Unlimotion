using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Domain;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
public sealed class FeedAreaParentContractTests
{
    [Test]
    public async Task ParentRelationsArePersistedAndIdempotentBeforeReturningTask()
    {
        var storage = new InMemoryStorage();
        using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await repository.Init();
        await storage.Save(new TaskItem { Id = "root", Title = "Родитель" });
        var identity = new FeedTaskSourceIdentity("space", "binding-a");
        var target = new TaskStorageFeedTaskCreationTarget(() => repository, () => identity);
        var draft = new FeedTaskDraft("feed-op", "op", "Задача", "", false, [], ["root", "root"], identity);
        await target.CreateOrGetAsync(draft);
        await target.CreateOrGetAsync(draft);
        await Assert.That((await storage.Load(draft.TaskId))!.ParentTasks).IsEquivalentTo(["root"]);
        await Assert.That((await storage.Load("root"))!.ContainsTasks).IsEquivalentTo([draft.TaskId]);
    }

    [Test]
    public async Task MissingParentDoesNotCreateTask()
    {
        var storage = new InMemoryStorage();
        using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await repository.Init();
        var target = new TaskStorageFeedTaskCreationTarget(() => repository);
        await Assert.That(() => target.CreateOrGetAsync(new FeedTaskDraft("feed-op", "op", "Задача", "", false, [], ["missing"])))
            .Throws<InvalidOperationException>();
        await Assert.That(await storage.Load("feed-op")).IsNull();
    }

    [Test]
    public async Task SourceBoundJournalIsInvisibleToLegacyEnumerator()
    {
        using var directory = new TempNotesDirectory();
        var journal = new FileFeedTaskConversionJournal(directory.Path);
        var record = new FeedTaskConversionRecord(3, "vault", "op", FeedTaskConversionState.Pending,
            "note.md", "revision", "feed-op", null, DateTimeOffset.UtcNow,
            TaskSourceIdentity: new FeedTaskSourceIdentity("space", "binding"), ParentTaskIds: ["root"]);
        await journal.SaveAsync(record);
        await Assert.That(Directory.EnumerateFiles(System.IO.Path.Combine(directory.Path, "vault", "transactions"), "*.task.json").Count()).IsEqualTo(0);
        await Assert.That((await journal.ListPendingAsync("vault")).Count).IsEqualTo(1);
        await Assert.That((await journal.LoadAsync("vault", "op"))!.TaskSourceIdentity).IsEqualTo(record.TaskSourceIdentity);
    }

    [Test]
    public async Task WrongSourceAndLegacyRecoveryNeverMutateMarkdown()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var document = await vault.CreateAsync("note.md", "Задача\n");
        var journal = new InMemoryFeedTaskConversionJournal();
        var parser = new MarkdownDocumentParser();
        var identity = new FeedTaskSourceIdentity("space-b", "binding-b");
        var service = new FeedTaskConversionService(vault, parser, new MarkdownMutationService(parser), new RejectingTarget(), journal,
            taskSourceIdentityProvider: () => identity);
        var descriptor = new FeedTaskConversionRecoveryDescriptor("op", new MarkdownBlockSelection(0, 1), "hash", null, "Задача", "", false, []);
        foreach (var source in new FeedTaskSourceIdentity?[] { null, new("space-a", "binding-a"), new("space-b", "binding-old") })
        {
            var record = new FeedTaskConversionRecord(source is null ? 2 : 3, "vault", "op", FeedTaskConversionState.Pending,
                "note.md", document.Revision, "feed-op", null, DateTimeOffset.UtcNow, descriptor, TaskSourceIdentity: source);
            await journal.SaveAsync(record);
            await Assert.That(() => service.ResumeAsync(record)).Throws<InvalidOperationException>();
            await Assert.That((await vault.ReadAsync("note.md"))!.Text).IsEqualTo("Задача\n");
        }
    }

    private sealed class RejectingTarget : IFeedTaskCreationTarget
    {
        public Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft, System.Threading.CancellationToken cancellationToken = default) =>
            throw new Exception("No task mutation was expected.");
    }

    [Test]
    public async Task SourceSwitchAfterTaskPersistenceKeepsMarkdownAndOriginalParentsForRetry()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var source = await vault.CreateAsync("note.md", "Задача\n");
        var journal = new InMemoryFeedTaskConversionJournal();
        var parser = new MarkdownDocumentParser();
        var original = new FeedTaskSourceIdentity("space", "original-binding");
        var current = original;
        var target = new SwitchingTarget(() => current = new("space", "replacement-binding"));
        var service = new FeedTaskConversionService(vault, parser, new MarkdownMutationService(parser), target, journal,
            taskSourceIdentityProvider: () => current);
        await Assert.That(() => service.ConvertAsync(new FeedTaskConversionRequest("vault", "op", "note.md",
            source.Revision, new MarkdownBlockSelection(0, 1), [], ParentTaskIds: ["root"], TaskSourceIdentity: original)))
            .Throws<InvalidOperationException>();
        await Assert.That((await vault.ReadAsync("note.md"))!.Text).IsEqualTo("Задача\n");
        var intent = (await journal.LoadAsync("vault", "op"))!;
        await Assert.That(intent.SchemaVersion).IsEqualTo(3);
        await Assert.That(intent.ParentTaskIds).IsEquivalentTo(["root"]);
        await Assert.That(intent.TaskSourceIdentity).IsEqualTo(original);
    }

    private sealed class SwitchingTarget(Action switchSource) : IFeedTaskCreationTarget
    {
        public Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft, System.Threading.CancellationToken cancellationToken = default)
        {
            switchSource();
            return Task.FromResult(new FeedCreatedTask(draft.TaskId, draft.Title));
        }
    }
}
