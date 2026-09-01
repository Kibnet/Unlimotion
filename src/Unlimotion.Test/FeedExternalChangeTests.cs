using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;

namespace Unlimotion.Test;

public class FeedExternalChangeTests
{
    [Test]
    public async Task MarkdownWatcherCoalescesAndClassifiesRecursiveCrudAndRename()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.FromMilliseconds(20));
        watcher.Start();

        var firstPath = Path.Combine(directory.Path, "Темы", "Проект", "idea.md");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        await File.WriteAllTextAsync(firstPath, "one", new UTF8Encoding(false));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, firstPath));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, firstPath));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, firstPath));

        var created = await sink.WaitForAsync(change => change.RelativePath == "Темы/Проект/idea.md");
        await Assert.That(created.Kind).IsEqualTo(VaultWatchChangeKind.Created);
        await Assert.That(sink.Changes.Count(change => change.RelativePath == created.RelativePath)).IsEqualTo(1);

        await File.WriteAllTextAsync(firstPath, "two", new UTF8Encoding(false));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, firstPath));
        var changed = await sink.WaitForAsync(
            change => change.RelativePath == "Темы/Проект/idea.md" && change.Kind == VaultWatchChangeKind.Changed);
        await Assert.That(changed.Revision).IsNotEqualTo(created.Revision);

        var renamedPath = Path.Combine(directory.Path, "Темы", "Проект", "renamed.md");
        File.Move(firstPath, renamedPath);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Renamed, renamedPath, firstPath));
        var renamed = await sink.WaitForAsync(change => change.Kind == VaultWatchChangeKind.Renamed);
        await Assert.That(renamed.OldRelativePath).IsEqualTo("Темы/Проект/idea.md");
        await Assert.That(renamed.RelativePath).IsEqualTo("Темы/Проект/renamed.md");

        File.Delete(renamedPath);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Deleted, renamedPath));
        var deleted = await sink.WaitForAsync(change => change.Kind == VaultWatchChangeKind.Deleted);
        await Assert.That(deleted.RelativePath).IsEqualTo("Темы/Проект/renamed.md");
        await Assert.That(deleted.Revision).IsNull();
    }

    [Test]
    public async Task MarkdownWatcherExcludesSidecarsAndPathsOutsideVault()
    {
        using var directory = new TempNotesDirectory();
        using var outside = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.FromMilliseconds(5));
        watcher.Start();

        var sidecarMarkdown = Path.Combine(directory.Path, ".unlimotion", "hidden.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecarMarkdown)!);
        await File.WriteAllTextAsync(sidecarMarkdown, "internal");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, sidecarMarkdown));
        var outsideMarkdown = Path.Combine(outside.Path, "outside.md");
        await File.WriteAllTextAsync(outsideMarkdown, "outside");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, outsideMarkdown));

        await Task.Delay(80);
        await Assert.That(sink.Changes).IsEmpty();
    }

    [Test]
    public async Task DirectoryCreateIsIgnoredButDirectoryRenameRequestsRescan()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();

        var nestedDirectory = Path.Combine(directory.Path, "Темы", "Проект");
        Directory.CreateDirectory(nestedDirectory);
        source.Emit(new VaultRawChange(
            VaultRawChangeKind.Created,
            nestedDirectory,
            IsDirectory: true));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        await Assert.That(sink.Changes).IsEmpty();

        var renamedDirectory = Path.Combine(directory.Path, "Темы", "Архив");
        Directory.Move(nestedDirectory, renamedDirectory);
        source.Emit(new VaultRawChange(
            VaultRawChangeKind.Renamed,
            renamedDirectory,
            nestedDirectory,
            IsDirectory: true));
        var rescan = await sink.WaitForAsync(change => change.Kind == VaultWatchChangeKind.RescanRequired);
        await Assert.That(rescan.Scope).IsEqualTo(VaultWatchScope.Markdown);
    }

    [Test]
    public async Task FileNoteVaultWriteAndDeleteAreSuppressedByOperationAndResultHash()
    {
        using var directory = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            ownWrites,
            sink,
            TimeSpan.FromMilliseconds(5));
        watcher.Start();

        const string relativePath = "Ежедневные/2026-08-24.md";
        var write = await vault.CreateAsync(relativePath, "own\n");
        var fullPath = vault.ResolveSafePath(relativePath);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, fullPath));
        const string barrierRelativePath = "Ежедневные/watcher-barrier.md";
        var barrierFullPath = vault.ResolveSafePath(barrierRelativePath);
        await File.WriteAllTextAsync(barrierFullPath, "barrier\n", new UTF8Encoding(false));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, barrierFullPath));
        _ = await sink.WaitForAsync(change =>
            string.Equals(change.RelativePath, barrierRelativePath, StringComparison.Ordinal));
        await Assert.That(sink.Changes.Any(change =>
            string.Equals(change.RelativePath, relativePath, StringComparison.Ordinal))).IsFalse();
        await Assert.That(ownWrites.TryMatch(relativePath, write.Revision, out var match)).IsTrue();
        await Assert.That(match!.OperationId.Length).IsGreaterThan(0);

        await File.WriteAllTextAsync(fullPath, "external\n", new UTF8Encoding(false));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, fullPath));
        var external = await sink.WaitForAsync(change => change.Kind == VaultWatchChangeKind.Changed);
        await Assert.That(external.Revision).IsNotEqualTo(write.Revision);

        var current = await vault.ReadAsync(relativePath);
        await vault.DeleteAsync(relativePath, current!.Revision);
        source.Emit(new VaultRawChange(VaultRawChangeKind.Deleted, fullPath));
        await Task.Delay(60);
        await Assert.That(sink.Changes.Count(change =>
            string.Equals(change.RelativePath, relativePath, StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    public async Task ExhaustedReadIOExceptionEmitsRescanAndPumpContinues()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();

        var lockedPath = Path.Combine(directory.Path, "locked.md");
        await File.WriteAllTextAsync(lockedPath, "locked");
        using (var locked = new FileStream(
                   lockedPath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            source.Emit(new VaultRawChange(VaultRawChangeKind.Created, lockedPath));
            var rescan = await sink.WaitForAsync(change => change.Kind == VaultWatchChangeKind.RescanRequired);
            await Assert.That(rescan.Scope).IsEqualTo(VaultWatchScope.Markdown);
            await Assert.That(watcher.LastFailure).IsTypeOf<IOException>();
        }

        var nextPath = Path.Combine(directory.Path, "next.md");
        await File.WriteAllTextAsync(nextPath, "next");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, nextPath));
        var next = await sink.WaitForAsync(change => change.RelativePath == "next.md");
        await Assert.That(next.Kind).IsEqualTo(VaultWatchChangeKind.Created);
    }

    [Test]
    public async Task SinkFailureOnOneEventDoesNotFaultPumpOrBlockLaterEvents()
    {
        using var directory = new TempNotesDirectory();
        var source = new ManualVaultWatchSource();
        var sink = new ThrowOnceVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();

        var firstPath = Path.Combine(directory.Path, "first.md");
        await File.WriteAllTextAsync(firstPath, "first");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, firstPath));
        await sink.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondPath = Path.Combine(directory.Path, "second.md");
        await File.WriteAllTextAsync(secondPath, "second");
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, secondPath));
        var second = await sink.WaitForAsync(change => change.RelativePath == "second.md");

        await Assert.That(second.Kind).IsEqualTo(VaultWatchChangeKind.Created);
        await Assert.That(watcher.LastFailure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task DisposeWaitsForActiveCallbackAndRejectsAllLaterSourceEvents()
    {
        using var directory = new TempNotesDirectory();
        var fullPath = Path.Combine(directory.Path, "note.md");
        await File.WriteAllTextAsync(fullPath, "text");
        var source = new ManualVaultWatchSource();
        var sink = new BlockingVaultChangeSink();
        var watcher = new MarkdownVaultWatcher(
            directory.Path,
            source,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.Zero);
        watcher.Start();
        source.Emit(new VaultRawChange(VaultRawChangeKind.Created, fullPath));
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposing = watcher.DisposeAsync().AsTask();
        await Assert.That(disposing.IsCompleted).IsFalse();
        sink.Release.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        source.Emit(new VaultRawChange(VaultRawChangeKind.Changed, fullPath));
        await Task.Delay(30);

        await Assert.That(sink.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RealFileSystemWatcherObservesNestedMarkdownCreate()
    {
        using var directory = new TempNotesDirectory();
        var sink = new RecordingVaultChangeSink();
        await using var watcher = new MarkdownVaultWatcher(
            directory.Path,
            new OwnWriteRegistry(),
            sink,
            TimeSpan.FromMilliseconds(100));
        watcher.Start();

        var nestedDirectory = Path.Combine(directory.Path, "Темы", "Исследования");
        Directory.CreateDirectory(nestedDirectory);
        var fullPath = Path.Combine(nestedDirectory, "идея.md");
        await File.WriteAllTextAsync(fullPath, "Наблюдение", new UTF8Encoding(false));

        var observed = await sink.WaitForAsync(
            change => change.RelativePath == "Темы/Исследования/идея.md"
                && change.Kind is VaultWatchChangeKind.Created or VaultWatchChangeKind.Changed,
            TimeSpan.FromSeconds(10));
        await Assert.That(observed.Scope).IsEqualTo(VaultWatchScope.Markdown);
        await Assert.That(observed.Revision).IsNotNull();
    }
}

internal sealed class ManualVaultWatchSource : IVaultWatchSource
{
    private bool started;
    private bool disposed;

    public event Action<VaultRawChange>? Change;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        started = true;
    }

    public void Emit(VaultRawChange change)
    {
        if (started && !disposed)
        {
            Change?.Invoke(change);
        }
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        Change = null;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingVaultChangeSink : IVaultChangeSink
{
    private readonly ConcurrentQueue<VaultWatchChange> changes = new();
    private readonly SemaphoreSlim signal = new(0);

    public IReadOnlyCollection<VaultWatchChange> Changes => changes.ToArray();

    public ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        changes.Enqueue(change);
        signal.Release();
        return ValueTask.CompletedTask;
    }

    public async Task<VaultWatchChange> WaitForAsync(
        Func<VaultWatchChange, bool> predicate,
        TimeSpan? timeout = null)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            var match = changes.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await signal.WaitAsync(timeoutCancellation.Token);
        }
    }
}

internal sealed class BlockingVaultChangeSink : IVaultChangeSink
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public async ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        CallCount++;
        Entered.TrySetResult();
        await Release.Task;
    }
}

internal sealed class ThrowOnceVaultChangeSink : IVaultChangeSink
{
    private readonly ConcurrentQueue<VaultWatchChange> changes = new();
    private readonly SemaphoreSlim signal = new(0);
    private int calls;

    public TaskCompletionSource FailureObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask HandleAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref calls) == 1)
        {
            FailureObserved.TrySetResult();
            throw new InvalidOperationException("synthetic sink failure");
        }

        changes.Enqueue(change);
        signal.Release();
        return ValueTask.CompletedTask;
    }

    public async Task<VaultWatchChange> WaitForAsync(Func<VaultWatchChange, bool> predicate)
    {
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var match = changes.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await signal.WaitAsync(timeoutCancellation.Token);
        }
    }
}
