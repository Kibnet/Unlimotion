using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedCaptureRecoveryUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VaultSwitch_KeepsPendingRecoveryAndCaptureDraftInTheirOwnSpace(bool recoverOnReturn)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var first = new TempNotesDirectory();
            using var second = new TempNotesDirectory();
            var journal = new InMemoryFeedTaskConversionJournal();
            var target = new RecoverableTarget { Fail = true };
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 4),
                taskJournalFactory: _ => journal, operationJournalFactory: _ => new InMemoryFeedOperationJournal())
            { TaskCreationTarget = target };
            await feed.InitializeVaultAsync(first.Path);
            feed.QuickCaptureText = "Незавершённая задача пространства A";
            await feed.CaptureTaskAsync();
            await feed.RefreshAsync();
            await Assert.That(feed.HasPendingRecoveries).IsTrue();
            await feed.InitializeVaultAsync(second.Path);
            await Assert.That(feed.QuickCaptureText).IsEmpty();
            await Assert.That(feed.HasPendingRecoveries).IsFalse();
            await Assert.That(feed.Days).IsEmpty();
            feed.QuickCaptureText = "Собственный черновик B";
            target.Fail = !recoverOnReturn;
            await feed.InitializeVaultAsync(first.Path);
            if (!recoverOnReturn)
            {
                await Assert.That(feed.QuickCaptureText).IsEqualTo("Незавершённая задача пространства A");
                await Assert.That(feed.HasPendingRecoveries).IsTrue();
                await feed.CaptureTaskAsync();
                await Assert.That(target.AttemptIds.Distinct().Count()).IsEqualTo(1);
                target.Fail = false;
                await feed.CaptureTaskAsync();
            }
            await Assert.That(feed.QuickCaptureText).IsEmpty();
            await feed.CaptureTaskAsync();
            await Assert.That(target.AttemptIds.Distinct().Count()).IsEqualTo(1);
            await Assert.That(target.CreatedIds.Count).IsEqualTo(1);
            var saved = (await new FileNoteVault(first.Path).ReadAsync("Ежедневные/2026-09-04.md"))!.Text;
            await Assert.That(saved.Split("unlimotion://task/").Length - 1).IsEqualTo(1);
            await feed.InitializeVaultAsync(second.Path);
            await Assert.That(feed.QuickCaptureText).IsEqualTo("Собственный черновик B");
            await Assert.That(feed.HasPendingRecoveries).IsFalse();
            await Assert.That(await new FileNoteVault(second.Path).ListMarkdownFilesAsync()).IsEmpty();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RetainedWriteChangedExternally_RestoresAccessibleDraft_AndOnlyExplicitSaveAppends()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var identity = await new VaultIdentityService(vault).GetOrCreateAsync();
            IFeedTaskConversionJournal journal = new FileFeedTaskConversionJournal(recovery.Path);
            var parser = new MarkdownDocumentParser();
            var mutations = new MarkdownMutationService(parser);
            var target = new RecoverableTarget { Fail = true };
            await NotesTestSupport.CaptureAsync<IOException>(() =>
                new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser, mutations, target, journal)
                    .CaptureAsync(new FeedTaskCaptureRequest(identity.VaultId, "retained-conflict", new DateOnly(2026, 9, 4),
                        "Незаменимый черновик", null, null, [])));
            var record = (await journal.ListPendingAsync(identity.VaultId)).Single();
            await journal.ResolveKeepBothAsync(identity.VaultId, record.OperationId);
            record = (await journal.LoadAsync(identity.VaultId, record.OperationId))!;
            const string destination = "Ежедневные/2026-09-05.md";
            await journal.SaveAsync(record with
            {
                RecoveredCaptureWrite = new FeedRecoveredCaptureWrite(destination, "Незаменимый черновик\n", null, false,
                    "Незаменимый черновик", null)
            });
            await vault.CreateAsync(destination, "Внешняя версия после утраты acknowledgement\n");
            using (var feed = new FeedViewModel(() => new DateOnly(2026, 9, 5), taskJournalFactory: _ => journal,
                operationJournalFactory: _ => new InMemoryFeedOperationJournal()) { TaskCreationTarget = target })
            {
                await feed.InitializeVaultAsync(directory.Path);
                await Assert.That(feed.QuickCaptureText).IsEqualTo("Незаменимый черновик");
                await Assert.That(feed.HasPendingRecoveries).IsTrue();
                await Assert.That((await vault.ReadAsync(destination))!.Text).DoesNotContain("Незаменимый черновик");
                await feed.CaptureAsync();
                await Assert.That(feed.HasError).IsFalse();
                await Assert.That(feed.HasPendingRecoveries).IsFalse();
            }
            using var restarted = new FeedViewModel(() => new DateOnly(2026, 9, 6), taskJournalFactory: _ => journal,
                operationJournalFactory: _ => new InMemoryFeedOperationJournal()) { TaskCreationTarget = target };
            await restarted.InitializeVaultAsync(directory.Path);
            await Assert.That(restarted.HasPendingRecoveries).IsFalse();
            var saved = (await vault.ReadAsync(destination))!.Text;
            await Assert.That(saved).Contains("Внешняя версия");
            await Assert.That(saved.Split("Незаменимый черновик").Length - 1).IsEqualTo(1);
            await Assert.That(target.AttemptIds.Count).IsEqualTo(1);
            await Assert.That(await vault.ReadAsync("Ежедневные/2026-09-06.md")).IsNull();
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task KeepPendingCapture_AfterExternalEdit_AllowsNewCaptureAndSurvivesRestart(bool crashAfterKeepDecision)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var journal = new FileFeedTaskConversionJournal(recovery.Path);
            var target = new RecoverableTarget { Fail = true };
            using (var feed = new FeedViewModel(() => new DateOnly(2026, 9, 4),
                taskJournalFactory: _ => journal, operationJournalFactory: _ => new InMemoryFeedOperationJournal())
                { TaskCreationTarget = target })
            {
                await feed.InitializeVaultAsync(directory.Path);
                feed.QuickCaptureText = "Исходная запись";
                await feed.CaptureTaskAsync();
                var item = feed.PendingRecoveries.Single();
                var manifest = await new VaultIdentityService(new FileNoteVault(directory.Path)).GetOrCreateAsync();
                var record = (await journal.ListPendingAsync(manifest!.VaultId)).Single();
                var sourcePath = Path.Combine(directory.Path, record.SourcePath);
                await File.AppendAllTextAsync(sourcePath, "\nВнешняя правка\n");
                await Assert.That(item.CanKeepBoth).IsTrue();
                if (crashAfterKeepDecision)
                    await ((IFeedTaskConversionJournal)journal).ResolveKeepBothAsync(record.VaultId, record.OperationId);
                else
                {
                    item.KeepBothCommand.Execute(null);
                    for (var attempt = 0; feed.IsBusy && attempt < 200; attempt++) await Task.Delay(10);
                    await Assert.That(feed.HasPendingRecoveries).IsFalse();
                    await Assert.That(feed.QuickCaptureText).IsEmpty();
                }
            }
            var attemptsBeforeRestart = target.AttemptIds.Count;
            target.Fail = false;
            using var restored = new FeedViewModel(() => new DateOnly(2026, 9, 4),
                taskJournalFactory: _ => new FileFeedTaskConversionJournal(recovery.Path),
                operationJournalFactory: _ => new InMemoryFeedOperationJournal()) { TaskCreationTarget = target };
            await restored.InitializeVaultAsync(directory.Path);
            await Assert.That(restored.HasPendingRecoveries).IsFalse();
            await Assert.That(target.AttemptIds.Count).IsEqualTo(attemptsBeforeRestart);
            restored.QuickCaptureText = "Другая задача";
            await restored.CaptureTaskAsync();
            await Assert.That(restored.HasError).IsFalse();
            await Assert.That(restored.Days.Single().Text).Contains("Исходная запись");
            await Assert.That(restored.Days.Single().Text).Contains("Внешняя правка");
            await Assert.That(target.AttemptIds.Count).IsEqualTo(attemptsBeforeRestart + 1);
        }, CancellationToken.None);
    }

    [Test]
    public async Task CaptureRetry_ReusesTheDraftOperation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var journal = new InMemoryFeedTaskConversionJournal();
            var target = new RecoverableTarget { Fail = true };
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 4),
                taskJournalFactory: _ => journal, operationJournalFactory: _ => new InMemoryFeedOperationJournal())
            { TaskCreationTarget = target };
            await feed.InitializeVaultAsync(directory.Path);
            feed.QuickCaptureText = "Единственный черновик";
            await feed.CaptureTaskAsync();
            await Assert.That(feed.HasError).IsTrue();
            await Assert.That(feed.QuickCaptureText).IsEqualTo("Единственный черновик");
            target.Fail = false;
            await feed.CaptureTaskAsync();
            await Assert.That(feed.HasError).IsFalse();
            await Assert.That(feed.HasQuickCaptureCreatedTask).IsTrue();
            await Assert.That(feed.QuickCaptureText).IsEmpty();
            await Assert.That(target.AttemptIds.Distinct().Count()).IsEqualTo(1);
            await Assert.That(feed.Days.Single().Text.Split("Единственный черновик").Length - 1).IsEqualTo(1);
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    public async Task KeptCaptureMissingFromDisk_RestoresDraftAcrossRestartsWithoutOverwritingNewText(int daysLater, bool identicalOldBlock)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var identity = await new VaultIdentityService(vault).GetOrCreateAsync();
            if (identicalOldBlock)
                await vault.CreateAsync("Ежедневные/2026-09-04.md", "Исходный незаменимый текст\n");
            var parser = new MarkdownDocumentParser();
            var mutations = new MarkdownMutationService(parser);
            IFeedTaskConversionJournal journal = new FileFeedTaskConversionJournal(recovery.Path);
            var target = new RecoverableTarget { Fail = true };
            await NotesTestSupport.CaptureAsync<IOException>(() =>
                new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser, mutations, target, journal)
                    .CaptureAsync(new FeedTaskCaptureRequest(identity.VaultId, "lost-append", new DateOnly(2026, 9, 4),
                        "Исходный незаменимый текст", null, null, [])));
            var record = (await journal.ListPendingAsync(identity.VaultId)).Single();
            var source = (await vault.ReadAsync(record.SourcePath))!;
            var externalText = identicalOldBlock ? "Исходный незаменимый текст\n" : "Более новая внешняя версия\n";
            await vault.WriteAsync(record.SourcePath, externalText, source.Revision);
            await journal.ResolveKeepBothAsync(identity.VaultId, record.OperationId);
            for (var restart = 0; restart < 2; restart++)
            {
                using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 4).AddDays(daysLater), taskJournalFactory: _ => journal,
                    operationJournalFactory: _ => new InMemoryFeedOperationJournal()) { TaskCreationTarget = target };
                await feed.InitializeVaultAsync(directory.Path);
                await Assert.That(feed.QuickCaptureText).IsEqualTo("Исходный незаменимый текст");
                await Assert.That(feed.HasPendingRecoveries).IsTrue();
                await Assert.That((await vault.ReadAsync(record.SourcePath))!.Text).IsEqualTo(externalText);
                if (restart == 1)
                {
                    await feed.CaptureAsync();
                    await Assert.That(feed.HasPendingRecoveries).IsFalse();
                    await Assert.That(await journal.ListPendingAsync(identity.VaultId)).IsEmpty();
                    var destination = $"Ежедневные/{new DateOnly(2026, 9, 4).AddDays(daysLater):yyyy-MM-dd}.md";
                    await Assert.That((await vault.ReadAsync(destination))!.Text).Contains("Исходный незаменимый текст");
                }
            }
            await Assert.That(target.AttemptIds.Count).IsEqualTo(1);
        }, CancellationToken.None);
    }

    [Test]
    public async Task Restart_RecoversQuickTaskWithoutAReviewSession()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            await vault.CreateAsync(VaultIdentityService.ManifestPath,
                "{\"schemaVersion\":1,\"vaultId\":\"capture-vault\"}\n");
            var parser = new MarkdownDocumentParser();
            var mutations = new MarkdownMutationService(parser);
            var target = new RecoverableTarget { Fail = true };
            await NotesTestSupport.CaptureAsync<IOException>(() =>
                new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser,
                    mutations, target, new FileFeedTaskConversionJournal(recovery.Path))
                    .CaptureAsync(new FeedTaskCaptureRequest("capture-vault", "restart",
                        new DateOnly(2026, 9, 4), "Восстановленная задача", null, null, [])));
            target.Fail = false;
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 4),
                taskJournalFactory: _ => new FileFeedTaskConversionJournal(recovery.Path),
                operationJournalFactory: _ => new InMemoryFeedOperationJournal())
            { TaskCreationTarget = target };
            await feed.InitializeVaultAsync(directory.Path);
            await Assert.That(feed.IsVaultInitialized).IsTrue();
            await Assert.That(feed.HasPendingRecoveries).IsFalse();
            await Assert.That(feed.HasError).IsFalse();
            await Assert.That(feed.Days.Single().Text).Contains("unlimotion://task/feed-restart");
            await Assert.That(await new FileFeedTaskConversionJournal(recovery.Path)
                .ListPendingAsync("capture-vault")).IsEmpty();
        }, CancellationToken.None);
    }

    private sealed class RecoverableTarget : IFeedTaskCreationTarget
    {
        public bool SupportsReadOnlyLookup => true;
        public Task<FeedCreatedTask?> FindOwnedAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default)
            => Task.FromResult<FeedCreatedTask?>(null);
        public bool Fail { get; set; }
        public List<string> AttemptIds { get; } = [];
        public List<string> CreatedIds { get; } = [];
        public Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft,
            CancellationToken cancellationToken = default)
        {
            AttemptIds.Add(draft.TaskId);
            if (!Fail) CreatedIds.Add(draft.TaskId);
            return Fail ? Task.FromException<FeedCreatedTask>(new IOException("Injected task failure"))
                : Task.FromResult(new FeedCreatedTask(draft.TaskId, draft.Title));
        }
    }
}
