using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;

namespace Unlimotion.Test;

public class FeedDocumentConflictTests
{
    [Test]
    public async Task CleanExternalChangeRequestsAutomaticReloadWithoutConflict()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "Ежедневные/2026-08-24.md";
        await vault.CreateAsync(relativePath, "before\n");
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "after\n", new UTF8Encoding(false));
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out _, out _, out _);

        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);

        await Assert.That(presentation.Reloads.Count).IsEqualTo(1);
        await Assert.That(presentation.Reloads.Single().Document!.Text).IsEqualTo("after\n");
        await Assert.That(presentation.Conflicts).IsEmpty();
    }

    [Test]
    public async Task DirtyExternalChangeCreatesConflictAndDoesNotOverwriteEitherVersion()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        var bundles = new MemoryDocumentConflictStore();
        await using var coordinator = CreateCoordinator(
            vault,
            presentation,
            out var dirty,
            out _,
            out _,
            conflictStore: bundles);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor block", 2, original.Revision, false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));

        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);

        var conflict = presentation.Conflicts.Single();
        await Assert.That(conflict.EditorDocumentText).IsEqualTo("editor\n");
        await Assert.That(conflict.DiskDocument!.Text).IsEqualTo("disk\n");
        await Assert.That((await vault.ReadAsync(relativePath))!.Text).IsEqualTo("disk\n");
        await Assert.That(coordinator.ActiveConflicts.Count).IsEqualTo(1);
        var bundle = bundles.Items.Single();
        await Assert.That(bundle.EditorMarkdown).IsEqualTo("editor\n");
        await Assert.That(bundle.DiskMarkdown).IsEqualTo("disk\n");
        await Assert.That(conflict.RecoveryBundlePath).IsNotEmpty();
    }

    [Test]
    public async Task DirtyExternalChangePersistsRecoveryDraftBeforeConflictIsPresented()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out var drafts, out _);
        dirty.Set(new DirtyDocumentBuffer(
            relativePath,
            "editor document\n",
            "editor block",
            2,
            original.Revision,
            false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));

        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);

        var draft = drafts.Items.Single();
        await Assert.That(draft.RelativePath).IsEqualTo(relativePath);
        await Assert.That(draft.BlockIndex).IsEqualTo(2);
        await Assert.That(draft.RawMarkdown).IsEqualTo("editor block");
        await Assert.That(draft.EditorDocumentText).IsEqualTo("editor document\n");
        await Assert.That(presentation.Conflicts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RestartRestoresUnacknowledgedConflictAndResolutionAcknowledgesIt()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var bundles = new FileDocumentConflictStore(recovery.Path);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var firstPresentation = new RecordingDocumentExternalChangeSink();
        await using (var first = CreateCoordinator(
                         vault,
                         firstPresentation,
                         out var firstDirty,
                         out _,
                         out _,
                         conflictStore: bundles))
        {
            firstDirty.Set(new DirtyDocumentBuffer(
                relativePath,
                "editor\n",
                "editor",
                0,
                original.Revision,
                false));
            await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
            await first.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);
        }

        var preserved = (await bundles.ListUnresolvedAsync("vault1")).Single();
        var restartedPresentation = new RecordingDocumentExternalChangeSink();
        await using var restarted = CreateCoordinator(
            vault,
            restartedPresentation,
            out _,
            out _,
            out _,
            conflictStore: bundles);

        var restored = await restarted.RestoreAsync(preserved);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restarted.ActiveConflicts).HasSingleItem();
        await Assert.That(restored!.EditorDocumentText).IsEqualTo("editor\n");
        await Assert.That(restored.DiskDocument!.Text).IsEqualTo("disk\n");
        await restarted.ResolveAsync(restored.ConflictId, DocumentConflictResolution.UseDisk);
        await Assert.That(await bundles.ListUnresolvedAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task ConflictIsNotPresentedWhenImmutableBundleCannotBePreserved()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "note.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(
            vault,
            presentation,
            out var dirty,
            out var drafts,
            out _,
            conflictStore: new FailingDocumentConflictStore());
        dirty.Set(new DirtyDocumentBuffer(
            relativePath,
            "editor\n",
            "editor",
            0,
            original.Revision,
            false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
        var change = await ChangedAsync(vault, relativePath);

        var failure = await NotesTestSupport.CaptureAsync<IOException>(() =>
            coordinator.HandleAsync(change, CancellationToken.None).AsTask());

        await Assert.That(failure.Message).IsEqualTo("Conflict bundle storage is unavailable.");
        await Assert.That(drafts.Items.Count).IsEqualTo(1);
        await Assert.That(presentation.Conflicts).IsEmpty();
        await Assert.That(coordinator.ActiveConflicts).IsEmpty();
    }

    [Test]
    public async Task UseEditorSnapshotsDiskAndWritesOnlyAgainstCapturedRevision()
    {
        using var directory = new TempNotesDirectory();
        var registry = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, registry);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out _, out var revisions, registry);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor block", 0, original.Revision, false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);
        var conflict = presentation.Conflicts.Single();

        var result = await coordinator.ResolveAsync(conflict.ConflictId, DocumentConflictResolution.UseEditor);

        var current = await vault.ReadAsync(relativePath);
        await Assert.That(current!.Text).IsEqualTo("editor\n");
        await Assert.That(revisions.Documents.Single().Text).IsEqualTo("disk\n");
        await Assert.That(registry.TryMatch(relativePath, current.Revision, out _)).IsTrue();
        await Assert.That(result.ResultRevision).IsEqualTo(current.Revision);
        await Assert.That(dirty.TryGet(relativePath, out _)).IsFalse();
        await Assert.That(coordinator.ActiveConflicts).IsEmpty();
    }

    [Test]
    public async Task UseDiskKeepsRejectedEditorVersionInImmutableBundleAndRecoveryDraft()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        var bundles = new MemoryDocumentConflictStore();
        await using var coordinator = CreateCoordinator(
            vault,
            presentation,
            out var dirty,
            out var drafts,
            out _,
            conflictStore: bundles);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor document\n", "active editor block", 4, original.Revision, false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);

        await coordinator.ResolveAsync(presentation.Conflicts.Single().ConflictId, DocumentConflictResolution.UseDisk);

        var draft = drafts.Items.Single();
        await Assert.That(draft.RelativePath).IsEqualTo(relativePath);
        await Assert.That(draft.BlockIndex).IsEqualTo(4);
        await Assert.That(draft.RawMarkdown).IsEqualTo("active editor block");
        await Assert.That(draft.EditorDocumentText).IsEqualTo("editor document\n");
        var bundle = bundles.Items.Single();
        await Assert.That(bundle.EditorRelativePath).IsEqualTo(relativePath);
        await Assert.That(bundle.BlockIndex).IsEqualTo(4);
        await Assert.That(bundle.EditorMarkdown).IsEqualTo("editor document\n");
        await Assert.That(bundle.DiskMarkdown).IsEqualTo("disk\n");
        await Assert.That((await vault.ReadAsync(relativePath))!.Text).IsEqualTo("disk\n");
        await Assert.That(dirty.TryGet(relativePath, out _)).IsFalse();
    }

    [Test]
    public async Task UseDiskRepersistsEditorDraftForStartupRecoveryAndOnlyAcknowledgesBundle()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var drafts = new FileFeedDraftStore(recovery.Path);
        var bundles = new FileDocumentConflictStore(recovery.Path);
        var dirty = new InMemoryDirtyDocumentRegistry();
        var presentation = new RecordingDocumentExternalChangeSink();
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        await using var coordinator = new DocumentConflictCoordinator(
            "vault1",
            vault,
            dirty,
            presentation,
            drafts,
            new MemoryRevisionStore(),
            bundles,
            new OwnWriteRegistry());
        dirty.Set(new DirtyDocumentBuffer(
            relativePath,
            "editor document\n",
            "active editor block",
            4,
            original.Revision,
            false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);
        var conflict = presentation.Conflicts.Single();

        // Resolution must persist the rejected editor buffer itself, rather than
        // relying only on the draft written when the conflict was first detected.
        await drafts.DeleteAsync("vault1", relativePath, 4);
        await coordinator.ResolveAsync(conflict.ConflictId, DocumentConflictResolution.UseDisk);

        var restartedDrafts = new FileFeedDraftStore(recovery.Path);
        var recoveredDraft = (await restartedDrafts.ListAsync("vault1")).Single();
        var restartedBundles = new FileDocumentConflictStore(recovery.Path);
        var preservedBundle = await restartedBundles.LoadAsync("vault1", conflict.ConflictId);
        await Assert.That(recoveredDraft.RelativePath).IsEqualTo(relativePath);
        await Assert.That(recoveredDraft.BlockIndex).IsEqualTo(4);
        await Assert.That(recoveredDraft.RawMarkdown).IsEqualTo("active editor block");
        await Assert.That(recoveredDraft.EditorDocumentText).IsEqualTo("editor document\n");
        await Assert.That(preservedBundle).IsNotNull();
        await Assert.That(preservedBundle!.EditorMarkdown).IsEqualTo("editor document\n");
        await Assert.That(await restartedBundles.ListUnresolvedAsync("vault1")).IsEmpty();
        await Assert.That((await vault.ReadAsync(relativePath))!.Text).IsEqualTo("disk\n");
    }

    [Test]
    public async Task SaveBothLeavesDiskAndCreatesDeterministicSafeSibling()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "Темы/Идея.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out _, out _);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor", 0, original.Revision, false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk\n", new UTF8Encoding(false));
        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);
        var conflict = presentation.Conflicts.Single();
        var expectedPath = DocumentConflictCoordinator.CreateConflictCopyPath(relativePath, conflict.EditorRevision);

        var result = await coordinator.ResolveAsync(conflict.ConflictId, DocumentConflictResolution.SaveBoth);

        await Assert.That(result.ConflictCopyRelativePath).IsEqualTo(expectedPath);
        await Assert.That(expectedPath).IsEqualTo(DocumentConflictCoordinator.CreateConflictCopyPath(relativePath, conflict.EditorRevision));
        await Assert.That(expectedPath.StartsWith("Темы/", StringComparison.Ordinal)).IsTrue();
        await Assert.That(expectedPath.Contains("..", StringComparison.Ordinal)).IsFalse();
        await Assert.That((await vault.ReadAsync(relativePath))!.Text).IsEqualTo("disk\n");
        await Assert.That((await vault.ReadAsync(expectedPath))!.Text).IsEqualTo("editor\n");
    }

    [Test]
    public async Task ResolutionIsRejectedWhenDiskChangesAgainAndConflictStaysActive()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string relativePath = "note.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out _, out _);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor", 0, original.Revision, false));
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk-one\n", new UTF8Encoding(false));
        await coordinator.HandleAsync(await ChangedAsync(vault, relativePath), CancellationToken.None);
        var conflict = presentation.Conflicts.Single();
        await File.WriteAllTextAsync(vault.ResolveSafePath(relativePath), "disk-two\n", new UTF8Encoding(false));

        var exception = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() =>
            coordinator.ResolveAsync(conflict.ConflictId, DocumentConflictResolution.UseEditor));

        await Assert.That(exception.ExpectedRevision).IsEqualTo(conflict.DiskDocument!.Revision);
        await Assert.That((await vault.ReadAsync(relativePath))!.Text).IsEqualTo("disk-two\n");
        await Assert.That(coordinator.ActiveConflicts.Count).IsEqualTo(1);
        await Assert.That(dirty.TryGet(relativePath, out _)).IsTrue();
    }

    [Test]
    public async Task ExternalRenameWithDirtyEditorTargetsRenamedDiskPathWithoutChangingGlobalState()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string oldPath = "Темы/old.md";
        const string newPath = "Темы/new.md";
        var original = await vault.CreateAsync(oldPath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out _, out _);
        dirty.Set(new DirtyDocumentBuffer(oldPath, "editor\n", "editor", 0, original.Revision, false));
        File.Move(vault.ResolveSafePath(oldPath), vault.ResolveSafePath(newPath));
        var renamed = await vault.ReadAsync(newPath);

        await coordinator.HandleAsync(new VaultWatchChange(
            VaultWatchScope.Markdown,
            VaultWatchChangeKind.Renamed,
            newPath,
            oldPath,
            renamed!.Revision), CancellationToken.None);

        var conflict = presentation.Conflicts.Single();
        await Assert.That(conflict.EditorRelativePath).IsEqualTo(oldPath);
        await Assert.That(conflict.DiskRelativePath).IsEqualTo(newPath);
        await coordinator.ResolveAsync(conflict.ConflictId, DocumentConflictResolution.UseEditor);
        await Assert.That((await vault.ReadAsync(newPath))!.Text).IsEqualTo("editor\n");
        await Assert.That(await vault.ReadAsync(oldPath)).IsNull();
    }

    [Test]
    public async Task OwnAtomicAutosaveDoesNotCreateFalseDirtyConflict()
    {
        using var directory = new TempNotesDirectory();
        var registry = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, registry);
        const string relativePath = "Ежедневные/2026-08-24.md";
        var original = await vault.CreateAsync(relativePath, "base\n");
        var presentation = new RecordingDocumentExternalChangeSink();
        await using var coordinator = CreateCoordinator(vault, presentation, out var dirty, out _, out _, registry);
        dirty.Set(new DirtyDocumentBuffer(relativePath, "editor\n", "editor", 0, original.Revision, false));
        var source = new ManualVaultWatchSource();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            registry,
            coordinator,
            TimeSpan.FromMilliseconds(5));
        watcher.Start();

        await vault.WriteAsync(relativePath, "editor\n", original.Revision);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, vault.ResolveSafePath(relativePath)));
        await Task.Delay(80);

        await Assert.That(presentation.Conflicts).IsEmpty();
        await Assert.That(coordinator.ActiveConflicts).IsEmpty();
    }

    private static DocumentConflictCoordinator CreateCoordinator(
        INoteVault vault,
        RecordingDocumentExternalChangeSink sink,
        out InMemoryDirtyDocumentRegistry dirty,
        out MemoryFeedDraftStore drafts,
        out MemoryRevisionStore revisions,
        OwnWriteRegistry? registry = null,
        IDocumentConflictStore? conflictStore = null)
    {
        dirty = new InMemoryDirtyDocumentRegistry();
        drafts = new MemoryFeedDraftStore();
        revisions = new MemoryRevisionStore();
        return new DocumentConflictCoordinator(
            "vault1",
            vault,
            dirty,
            sink,
            drafts,
            revisions,
            conflictStore ?? new MemoryDocumentConflictStore(),
            registry ?? new OwnWriteRegistry());
    }

    private static async Task<VaultWatchChange> ChangedAsync(INoteVault vault, string relativePath)
    {
        var document = await vault.ReadAsync(relativePath);
        return new VaultWatchChange(
            VaultWatchScope.Markdown,
            VaultWatchChangeKind.Changed,
            relativePath,
            null,
            document!.Revision);
    }
}

internal sealed class FailingDocumentConflictStore : IDocumentConflictStore
{
    public Task<string> PreserveAsync(
        DocumentConflictBundle conflict,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new IOException("Conflict bundle storage is unavailable."));

    public Task<DocumentConflictBundle?> LoadAsync(
        string vaultId,
        string conflictId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DocumentConflictBundle?>(null);

    public Task<IReadOnlyList<DocumentConflictBundle>> ListAsync(
        string vaultId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentConflictBundle>>([]);
}

internal sealed class RecordingDocumentExternalChangeSink : IDocumentExternalChangeSink
{
    public ConcurrentQueue<DocumentReloadSignal> Reloads { get; } = new();

    public ConcurrentQueue<DocumentConflictState> Conflicts { get; } = new();

    public ValueTask ReloadAsync(DocumentReloadSignal signal, CancellationToken cancellationToken)
    {
        Reloads.Enqueue(signal);
        return ValueTask.CompletedTask;
    }

    public ValueTask ConflictAsync(DocumentConflictState conflict, CancellationToken cancellationToken)
    {
        Conflicts.Enqueue(conflict);
        return ValueTask.CompletedTask;
    }
}

internal sealed class MemoryFeedDraftStore : IFeedDraftStore
{
    private readonly ConcurrentDictionary<string, FeedDraft> drafts = new(StringComparer.Ordinal);

    public IReadOnlyCollection<FeedDraft> Items => drafts.Values.ToArray();

    public Task SaveAsync(FeedDraft draft, CancellationToken cancellationToken = default)
    {
        drafts[Key(draft.VaultId, draft.RelativePath, draft.BlockIndex)] = draft;
        return Task.CompletedTask;
    }

    public Task<FeedDraft?> LoadAsync(
        string vaultId,
        string relativePath,
        int blockIndex,
        CancellationToken cancellationToken = default)
    {
        drafts.TryGetValue(Key(vaultId, relativePath, blockIndex), out var draft);
        return Task.FromResult(draft);
    }

    public Task<IReadOnlyList<FeedDraft>> ListAsync(
        string vaultId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FeedDraft>>(drafts.Values
            .Where(draft => string.Equals(draft.VaultId, vaultId, StringComparison.Ordinal))
            .OrderByDescending(static draft => draft.UpdatedAt)
            .ToArray());

    public Task DeleteAsync(
        string vaultId,
        string relativePath,
        int blockIndex,
        CancellationToken cancellationToken = default)
    {
        drafts.TryRemove(Key(vaultId, relativePath, blockIndex), out _);
        return Task.CompletedTask;
    }

    private static string Key(string vaultId, string relativePath, int blockIndex) =>
        $"{vaultId}:{relativePath}:{blockIndex}";
}

internal sealed class MemoryDocumentConflictStore : IDocumentConflictStore
{
    private readonly ConcurrentDictionary<string, DocumentConflictBundle> conflicts = new(StringComparer.Ordinal);

    public IReadOnlyCollection<DocumentConflictBundle> Items => conflicts.Values.ToArray();

    public Task<string> PreserveAsync(
        DocumentConflictBundle conflict,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(conflict.VaultId, conflict.ConflictId);
        if (conflicts.TryGetValue(key, out var existing)
            && existing with { CreatedAt = default } != conflict with { CreatedAt = default })
        {
            throw new IOException("Conflict bundles are immutable.");
        }

        conflicts.TryAdd(key, conflict);
        return Task.FromResult($"memory://{conflict.VaultId}/conflicts/{conflict.ConflictId}");
    }

    public Task<DocumentConflictBundle?> LoadAsync(
        string vaultId,
        string conflictId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        conflicts.TryGetValue(Key(vaultId, conflictId), out var conflict);
        return Task.FromResult(conflict);
    }

    public Task<IReadOnlyList<DocumentConflictBundle>> ListAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DocumentConflictBundle>>(conflicts.Values
            .Where(conflict => string.Equals(conflict.VaultId, vaultId, StringComparison.Ordinal))
            .OrderByDescending(static conflict => conflict.CreatedAt)
            .ToArray());
    }

    private static string Key(string vaultId, string conflictId) => $"{vaultId}:{conflictId}";
}

internal sealed class MemoryRevisionStore : IRevisionStore
{
    public ConcurrentQueue<VaultDocument> Documents { get; } = new();

    public Task SaveAsync(string vaultId, VaultDocument document, CancellationToken cancellationToken = default)
    {
        Documents.Enqueue(document);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(
        string vaultId,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
