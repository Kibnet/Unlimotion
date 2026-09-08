using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

public sealed class FeedVaultWatchRuntimeTests
{
    private const string DailyPath = "Ежедневные/2026-08-24.md";

    [Test]
    public async Task EventsStayBufferedUntilBootstrapActivation()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var original = await vault.CreateAsync(DailyPath, "before\n");
        var markdownSource = new RuntimeManualVaultWatchSource();
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            markdownSource,
            sidecarSource);
        runtime.Start();

        await WriteExternalAsync(vault, DailyPath, "after\n");
        markdownSource.Emit(new VaultRawChange(
            VaultRawChangeKind.Changed,
            vault.ResolveSafePath(DailyPath)));
        await WaitUntilAsync(() => runtime.BufferedChangeCount == 1);

        await Assert.That(runtime.IsActive).IsFalse();
        await Assert.That(sink.TotalCallbackCount).IsEqualTo(0);

        await runtime.ActivateAsync();
        var reload = await sink.WaitForReloadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(runtime.IsActive).IsTrue();
            await Assert.That(runtime.BufferedChangeCount).IsEqualTo(0);
            await Assert.That(reload.Document!.Text).IsEqualTo("after\n");
            await Assert.That(reload.Document.Revision).IsNotEqualTo(original.Revision);
        }
    }

    [Test]
    public async Task CleanMarkdownChangeRoutesToReloadCallback()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, ownWrites);
        await vault.CreateAsync(DailyPath, "before\n");
        var markdownSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            markdownSource,
            new RuntimeManualVaultWatchSource());
        runtime.Start();
        await runtime.ActivateAsync();

        await WriteExternalAsync(vault, DailyPath, "clean external\n");
        markdownSource.Emit(new VaultRawChange(
            VaultRawChangeKind.Changed,
            vault.ResolveSafePath(DailyPath)));
        var reload = await sink.WaitForReloadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(reload.Change.Scope).IsEqualTo(VaultWatchScope.Markdown);
            await Assert.That(reload.Change.RelativePath).IsEqualTo(DailyPath);
            await Assert.That(reload.Document!.Text).IsEqualTo("clean external\n");
            await Assert.That(sink.Conflicts).IsEmpty();
            await Assert.That(runtime.OwnWrites).IsSameReferenceAs(ownWrites);
        }
    }

    [Test]
    public async Task DirtyMarkdownChangeRoutesThroughConflictCoordinator()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry(retention: TimeSpan.FromMinutes(1));
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var original = await vault.CreateAsync(DailyPath, "base\n");
        var markdownSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            markdownSource,
            new RuntimeManualVaultWatchSource());
        runtime.DirtyDocuments.Set(new DirtyDocumentBuffer(
            DailyPath,
            "editor version\n",
            "editor version",
            0,
            original.Revision,
            false));
        runtime.Start();
        await runtime.ActivateAsync();

        await WriteExternalAsync(vault, DailyPath, "disk version\n");
        markdownSource.Emit(new VaultRawChange(
            VaultRawChangeKind.Changed,
            vault.ResolveSafePath(DailyPath)));
        var conflict = await sink.WaitForConflictAsync();

        using (Assert.Multiple())
        {
            await Assert.That(conflict.EditorDocumentText).IsEqualTo("editor version\n");
            await Assert.That(conflict.DiskDocument!.Text).IsEqualTo("disk version\n");
            await Assert.That(runtime.ConflictCoordinator.ActiveConflicts.Count).IsEqualTo(1);
            await Assert.That(sink.Reloads).IsEmpty();
            await Assert.That((await vault.ReadAsync(DailyPath))!.Text).IsEqualTo("disk version\n");
        }
    }

    [Test]
    public async Task AreasReviewAndDailySettingsSidecarsRouteToSeparateRefreshCallbacks()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            new RuntimeManualVaultWatchSource(),
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        var areasPath = await WriteExternalAsync(vault, ".unlimotion/areas.json", "{\"schemaVersion\":1,\"areas\":[]}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, areasPath));
        var reviewPath = await WriteExternalAsync(vault, ".unlimotion/review/device/events.jsonl", "{}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, reviewPath));
        var dailySettingsPath = await WriteExternalAsync(
            vault,
            ".unlimotion/daily-note-settings.json",
            "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy.MM.dd\"}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, dailySettingsPath));

        var areas = await sink.WaitForAreaRefreshAsync();
        var review = await sink.WaitForReviewRefreshAsync();
        var dailySettings = await sink.WaitForDailyNoteSettingsReloadAsync();
        using (Assert.Multiple())
        {
            await Assert.That(areas.SidecarArtifact).IsEqualTo(SidecarArtifactKind.Areas);
            await Assert.That(areas.RelativePath).IsEqualTo(".unlimotion/areas.json");
            await Assert.That(review.SidecarArtifact).IsEqualTo(SidecarArtifactKind.Review);
            await Assert.That(review.RelativePath).IsEqualTo(".unlimotion/review/device/events.jsonl");
            await Assert.That(dailySettings.SidecarArtifact).IsEqualTo(SidecarArtifactKind.DailyNoteSettings);
            await Assert.That(dailySettings.RelativePath).IsEqualTo(".unlimotion/daily-note-settings.json");
            await Assert.That(sink.IdentitySignals).IsEmpty();
        }
    }

    [Test]
    public async Task IdentityChangeRaisesSafeFreezeSignalAndStopsLaterRoutes()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            new RuntimeManualVaultWatchSource(),
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        var identityPath = await WriteExternalAsync(vault, ".unlimotion/vault.json", "{\"schemaVersion\":1,\"vaultId\":\"other\"}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, identityPath));
        var identity = await sink.WaitForIdentitySignalAsync();

        var areasPath = await WriteExternalAsync(vault, ".unlimotion/areas.json", "{\"schemaVersion\":1,\"areas\":[]}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, areasPath));
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        using (Assert.Multiple())
        {
            await Assert.That(runtime.IsIdentityFrozen).IsTrue();
            await Assert.That(identity.Change.SidecarArtifact).IsEqualTo(SidecarArtifactKind.VaultIdentity);
            await Assert.That(identity.Reason).IsNotEmpty();
            await Assert.That(sink.AreaRefreshes).IsEmpty();
        }
    }

    [Test]
    public async Task DelayedSameIdentityEventIsIgnoredAndLaterSidecarsStillRoute()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var identityPath = await WriteExternalAsync(
            vault,
            ".unlimotion/vault.json",
            "{\"schemaVersion\":1,\"vaultId\":\"vault-runtime-tests\"}\n");
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            new RuntimeManualVaultWatchSource(),
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, identityPath));
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var areasPath = await WriteExternalAsync(vault, ".unlimotion/areas.json", "{\"schemaVersion\":1,\"areas\":[]}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, areasPath));
        var areas = await sink.WaitForAreaRefreshAsync();

        using (Assert.Multiple())
        {
            await Assert.That(runtime.IsIdentityFrozen).IsFalse();
            await Assert.That(sink.IdentitySignals).IsEmpty();
            await Assert.That(areas.SidecarArtifact).IsEqualTo(SidecarArtifactKind.Areas);
        }
    }

    [Test]
    public async Task SidecarRescanWithExpectedIdentityRefreshesPortableStateAndContinues()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        await WriteExternalAsync(
            vault,
            ".unlimotion/vault.json",
            "{\"schemaVersion\":1,\"vaultId\":\"vault-runtime-tests\"}\n");
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            new RuntimeManualVaultWatchSource(),
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path));
        await sink.WaitForAreaRefreshAsync();
        await sink.WaitForReviewRefreshAsync();
        await sink.WaitForDailyNoteSettingsReloadAsync();

        var areasPath = await WriteExternalAsync(vault, ".unlimotion/areas.json", "{\"schemaVersion\":1,\"areas\":[]}\n");
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.Changed, areasPath));
        await WaitUntilAsync(() => sink.AreaRefreshes.Count == 2);

        using (Assert.Multiple())
        {
            await Assert.That(runtime.IsIdentityFrozen).IsFalse();
            await Assert.That(runtime.IsActive).IsTrue();
            await Assert.That(sink.IdentitySignals).IsEmpty();
            await Assert.That(sink.ReviewRefreshes.Count).IsEqualTo(1);
            await Assert.That(sink.DailyNoteSettingsReloads.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SidecarRescanWithMalformedIdentityFreezesPortableWrites()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        await WriteExternalAsync(vault, ".unlimotion/vault.json", "{not-json}\n");
        var sidecarSource = new RuntimeManualVaultWatchSource();
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        await using var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            new RuntimeManualVaultWatchSource(),
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path));
        var identity = await sink.WaitForIdentitySignalAsync();

        using (Assert.Multiple())
        {
            await Assert.That(runtime.IsIdentityFrozen).IsTrue();
            await Assert.That(identity.Change.Kind).IsEqualTo(VaultWatchChangeKind.RescanRequired);
            await Assert.That(identity.Reason).IsNotEmpty();
            await Assert.That(sink.AreaRefreshes).IsEmpty();
            await Assert.That(sink.ReviewRefreshes).IsEmpty();
        }
    }

    [Test]
    public async Task DisposeWaitsForBothWatchersAndRejectsLaterCallbacks()
    {
        using var directory = new TempNotesDirectory();
        using var recovery = new TempNotesDirectory();
        var ownWrites = new OwnWriteRegistry();
        var vault = new FileNoteVault(directory.Path, ownWrites);
        var markdownSource = new RuntimeManualVaultWatchSource(blockDispose: true);
        var sidecarSource = new RuntimeManualVaultWatchSource(blockDispose: true);
        var sink = new RecordingFeedVaultWatchRuntimeSink();
        var runtime = CreateRuntime(
            vault,
            ownWrites,
            recovery.Path,
            sink,
            markdownSource,
            sidecarSource);
        runtime.Start();
        await runtime.ActivateAsync();

        var disposing = runtime.DisposeAsync().AsTask();
        await Task.WhenAll(markdownSource.DisposeEntered.Task, sidecarSource.DisposeEntered.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            markdownSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path, IsDirectory: true));
            sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path, IsDirectory: true));
            await Assert.That(disposing.IsCompleted).IsFalse();

            markdownSource.AllowDispose.TrySetResult();
            await Task.Delay(TimeSpan.FromMilliseconds(25));
            await Assert.That(disposing.IsCompleted).IsFalse();
        }
        finally
        {
            markdownSource.AllowDispose.TrySetResult();
            sidecarSource.AllowDispose.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }

        markdownSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path, IsDirectory: true));
        sidecarSource.Emit(new VaultRawChange(VaultRawChangeKind.RescanRequired, directory.Path, IsDirectory: true));
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        using (Assert.Multiple())
        {
            await Assert.That(markdownSource.IsDisposed).IsTrue();
            await Assert.That(sidecarSource.IsDisposed).IsTrue();
            await Assert.That(sink.TotalCallbackCount).IsEqualTo(0);
        }
    }

    private static FeedVaultWatchRuntime CreateRuntime(
        INoteVault vault,
        OwnWriteRegistry ownWrites,
        string recoveryRoot,
        RecordingFeedVaultWatchRuntimeSink sink,
        IVaultWatchSource markdownSource,
        IVaultWatchSource sidecarSource)
    {
        return new FeedVaultWatchRuntime(
            "vault-runtime-tests",
            vault,
            ownWrites,
            new InMemoryDirtyDocumentRegistry(),
            recoveryRoot,
            sink,
            markdownSource,
            sidecarSource,
            TimeSpan.Zero);
    }

    private static async Task<string> WriteExternalAsync(
        INoteVault vault,
        string relativePath,
        string text)
    {
        var fullPath = vault.ResolveSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, text, new UTF8Encoding(false));
        return fullPath;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The Feed vault watch runtime did not reach the expected state.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}

internal sealed class RuntimeManualVaultWatchSource(bool blockDispose = false) : IVaultWatchSource
{
    private bool started;
    private int disposed;

    public event Action<VaultRawChange>? Change;

    public TaskCompletionSource DisposeEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource AllowDispose { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        started = true;
    }

    public void Emit(VaultRawChange change)
    {
        if (started && !IsDisposed)
        {
            Change?.Invoke(change);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        DisposeEntered.TrySetResult();
        if (blockDispose)
        {
            await AllowDispose.Task.ConfigureAwait(false);
        }

        Change = null;
    }
}

internal sealed class RecordingFeedVaultWatchRuntimeSink : IFeedVaultWatchRuntimeSink
{
    public ConcurrentQueue<DocumentReloadSignal> Reloads { get; } = new();

    public ConcurrentQueue<DocumentConflictState> Conflicts { get; } = new();

    public ConcurrentQueue<VaultWatchChange> AreaRefreshes { get; } = new();

    public ConcurrentQueue<VaultWatchChange> ReviewRefreshes { get; } = new();

    public ConcurrentQueue<VaultWatchChange> DailyNoteSettingsReloads { get; } = new();

    public ConcurrentQueue<FeedVaultIdentityFreezeSignal> IdentitySignals { get; } = new();

    public int TotalCallbackCount =>
        Reloads.Count + Conflicts.Count + AreaRefreshes.Count + ReviewRefreshes.Count + DailyNoteSettingsReloads.Count + IdentitySignals.Count;

    public ValueTask ReloadMarkdownAsync(DocumentReloadSignal signal, CancellationToken cancellationToken)
    {
        Reloads.Enqueue(signal);
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowMarkdownConflictAsync(DocumentConflictState conflict, CancellationToken cancellationToken)
    {
        Conflicts.Enqueue(conflict);
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshAreasAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        AreaRefreshes.Enqueue(change);
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshReviewAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        ReviewRefreshes.Enqueue(change);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReloadDailyNoteSettingsAsync(VaultWatchChange change, CancellationToken cancellationToken)
    {
        DailyNoteSettingsReloads.Enqueue(change);
        return ValueTask.CompletedTask;
    }

    public ValueTask FreezeForIdentityChangeAsync(
        FeedVaultIdentityFreezeSignal signal,
        CancellationToken cancellationToken)
    {
        IdentitySignals.Enqueue(signal);
        return ValueTask.CompletedTask;
    }

    public Task<DocumentReloadSignal> WaitForReloadAsync() => WaitForAsync(Reloads);

    public Task<DocumentConflictState> WaitForConflictAsync() => WaitForAsync(Conflicts);

    public Task<VaultWatchChange> WaitForAreaRefreshAsync() => WaitForAsync(AreaRefreshes);

    public Task<VaultWatchChange> WaitForReviewRefreshAsync() => WaitForAsync(ReviewRefreshes);

    public Task<VaultWatchChange> WaitForDailyNoteSettingsReloadAsync() => WaitForAsync(DailyNoteSettingsReloads);

    public Task<FeedVaultIdentityFreezeSignal> WaitForIdentitySignalAsync() => WaitForAsync(IdentitySignals);

    private static async Task<T> WaitForAsync<T>(ConcurrentQueue<T> values)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var value = default(T);
        while (!values.TryPeek(out value))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"The Feed vault watch runtime did not emit {typeof(T).Name}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        return value!;
    }
}
