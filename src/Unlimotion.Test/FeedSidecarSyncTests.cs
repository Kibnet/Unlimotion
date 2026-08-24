using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;

namespace Unlimotion.Test;

public class FeedSidecarSyncTests
{
    [Test]
    public async Task DedicatedWatcherClassifiesIdentityAreasAndRecursiveReviewArtifacts()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new SidecarVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.FromMilliseconds(10));
        watcher.Start();

        var identity = await CreateAsync(directory.Path, ".unlimotion/vault.json", "{\"vaultId\":\"one\"}");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, identity));
        var areas = await CreateAsync(directory.Path, ".unlimotion/areas.json", "[]");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, areas));
        var review = await CreateAsync(directory.Path, ".unlimotion/review/2026/event.json", "{}");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, review));
        var ordinaryNote = await CreateAsync(directory.Path, "Ежедневные/2026-08-24.md", "text");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, ordinaryNote));

        var identityChange = await sink.WaitForAsync(change => change.SidecarArtifact == SidecarArtifactKind.VaultIdentity);
        var areasChange = await sink.WaitForAsync(change => change.SidecarArtifact == SidecarArtifactKind.Areas);
        var reviewChange = await sink.WaitForAsync(change => change.SidecarArtifact == SidecarArtifactKind.Review);

        await Assert.That(identityChange.Scope).IsEqualTo(VaultWatchScope.Sidecar);
        await Assert.That(identityChange.RelativePath).IsEqualTo(".unlimotion/vault.json");
        await Assert.That(areasChange.RelativePath).IsEqualTo(".unlimotion/areas.json");
        await Assert.That(reviewChange.RelativePath).IsEqualTo(".unlimotion/review/2026/event.json");
        await Assert.That(sink.Changes.Any(change => change.RelativePath == "Ежедневные/2026-08-24.md")).IsFalse();
    }

    [Test]
    public async Task SidecarOwnWriteIsSuppressedButDifferentExternalRevisionIsDelivered()
    {
        using var directory = new TempNotesDirectory();
        var registry = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, registry);
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new SidecarVaultWatcher(
            directory.Path,
            source,
            registry,
            sink,
            TimeSpan.FromMilliseconds(5));
        watcher.Start();

        var own = await vault.CreateAsync(".unlimotion/areas.json", "[]\n");
        var fullPath = vault.ResolveSafePath(".unlimotion/areas.json");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, fullPath));
        await Task.Delay(60);
        await Assert.That(sink.Changes).IsEmpty();
        await Assert.That(registry.TryMatch(".unlimotion/areas.json", own.Revision, out _)).IsTrue();

        await File.WriteAllTextAsync(fullPath, "[{\"id\":\"external\"}]\n", new UTF8Encoding(false));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, fullPath));
        var external = await sink.WaitForAsync(change => change.SidecarArtifact == SidecarArtifactKind.Areas);
        await Assert.That(external.Revision).IsNotEqualTo(own.Revision);
    }

    [Test]
    public async Task ReviewAtomicTempIsIgnoredUntilRenamedToPortableJson()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new SidecarVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();

        var reviewDirectory = Path.Combine(directory.Path, ".unlimotion", "review", "events");
        Directory.CreateDirectory(reviewDirectory);
        var tempPath = Path.Combine(reviewDirectory, ".event.json.operation.tmp");
        await File.WriteAllTextAsync(tempPath, "{}\n");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, tempPath));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        await Assert.That(sink.Changes).IsEmpty();

        var finalPath = Path.Combine(reviewDirectory, "event.json");
        File.Move(tempPath, finalPath);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Renamed, finalPath, tempPath));
        var review = await sink.WaitForAsync(
            change => change.RelativePath?.EndsWith("event.json", StringComparison.Ordinal) == true);
        await Assert.That(review.SidecarArtifact).IsEqualTo(SidecarArtifactKind.Review);
    }

    [Test]
    public async Task RenameOutOfPortableSidecarSetBecomesDeleteForOriginalArtifact()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new SidecarVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();

        var original = await CreateAsync(directory.Path, ".unlimotion/areas.json", "[]");
        var backup = Path.Combine(directory.Path, ".unlimotion", "areas.backup");
        File.Move(original, backup);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Renamed, backup, original));

        var change = await sink.WaitForAsync(item => item.SidecarArtifact == SidecarArtifactKind.Areas);
        await Assert.That(change.Kind).IsEqualTo(VaultWatchChangeKind.Deleted);
        await Assert.That(change.RelativePath).IsEqualTo(".unlimotion/areas.json");
    }

    private static async Task<string> CreateAsync(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, contents, new UTF8Encoding(false));
        return fullPath;
    }
}
