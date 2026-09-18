using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class FeedLegacySourceBindingTests
{
    [Test]
    public async Task VerifiedLegacyBindingPreservesOriginalBytesAndHidesOldReplaySurface()
    {
        using var directory = new TempNotesDirectory();
        var journal = new FileFeedTaskConversionJournal(directory.Path);
        var legacy = CreateLegacy();
        await journal.SaveAsync(legacy);
        var oldPath = Path.Combine(directory.Path, "vault", "transactions", "op.task.json");
        var originalBytes = await File.ReadAllBytesAsync(oldPath);
        var identity = new FeedTaskSourceIdentity("confirmed", "binding");
        var target = new LookupTarget(() => new("feed-op", "Задача"));
        var parser = new MarkdownDocumentParser();
        var service = new FeedTaskConversionService(new FileNoteVault(directory.Path), parser, new MarkdownMutationService(parser),
            target, journal, taskSourceIdentityProvider: () => identity);
        var bound = await service.BindLegacyToVerifiedSourceAsync(legacy, identity);
        await Assert.That(bound.SchemaVersion).IsEqualTo(3);
        await Assert.That(bound.ParentTaskIds).IsNull();
        await Assert.That(bound.TaskSourceIdentity).IsEqualTo(identity);
        await Assert.That(File.Exists(oldPath)).IsFalse();
        var archivedBytes = await File.ReadAllBytesAsync(Path.Combine(directory.Path, "vault", "transactions", "task-v3", "legacy-original", "op.task.json"));
        await Assert.That(archivedBytes.SequenceEqual(originalBytes)).IsTrue();
        await Assert.That((await journal.ListPendingAsync("vault")).Count).IsEqualTo(1);
        await Assert.That((await journal.LoadAsync("vault", "op"))!.SchemaVersion).IsEqualTo(3);
        await Assert.That(target.CreateCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOwnedTaskOrSourceChangeCannotBind(bool switchSource)
    {
        using var directory = new TempNotesDirectory();
        var journal = new InMemoryFeedTaskConversionJournal();
        var legacy = CreateLegacy();
        await journal.SaveAsync(legacy);
        var identity = new FeedTaskSourceIdentity("confirmed", "binding");
        var current = identity;
        var target = new LookupTarget(() =>
        {
            if (!switchSource) return null;
            current = new("another", "another-binding");
            return new("feed-op", "Задача");
        });
        var parser = new MarkdownDocumentParser();
        var service = new FeedTaskConversionService(new FileNoteVault(directory.Path), parser, new MarkdownMutationService(parser),
            target, journal, taskSourceIdentityProvider: () => current);
        await Assert.That(() => service.BindLegacyToVerifiedSourceAsync(legacy, identity)).Throws<InvalidOperationException>();
        await Assert.That((await journal.LoadAsync("vault", "op"))!.TaskSourceIdentity).IsNull();
        await Assert.That(target.CreateCalls).IsEqualTo(0);
    }

    private static FeedTaskConversionRecord CreateLegacy() => new(2, "vault", "op", FeedTaskConversionState.TaskCreated,
        "note.md", "rev", "feed-op", null, DateTimeOffset.UtcNow,
        new FeedTaskConversionRecoveryDescriptor("op", new MarkdownBlockSelection(0, 1), "hash", null, "Задача", "", false, []));

    private sealed class LookupTarget(Func<FeedCreatedTask?> lookup) : IFeedTaskCreationTarget
    {
        public bool SupportsReadOnlyLookup => true;
        public int CreateCalls { get; private set; }
        public Task<FeedCreatedTask?> FindOwnedAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(lookup());
        public Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default)
        { CreateCalls++; throw new Exception("Binding must not create a task."); }
    }
}
