using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unlimotion.Services;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion;

public class FileStorage : global::Unlimotion.Storage.FileTaskStorage, IDisposable
{
    private readonly IDatabaseWatcher? _dbWatcher;
    private readonly ConcurrentDictionary<string, PendingFileChange> _pendingFileChanges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingWatcherUpdate> _pendingWatcherUpdates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConfirmedOwnWrite> _confirmedOwnWrites =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _watcherUpdateGate = new(1, 1);
    private long _nextPendingGeneration;
    private bool _disposed;

    public FileStorage(string path, bool watcher = false, INotificationManagerWrapper? notificationManager = null)
        : base(new global::Unlimotion.Storage.FileTaskStorageOptions { Path = PreparePath(path) })
    {
        if (!watcher)
        {
            return;
        }

        _dbWatcher = new FileDbWatcher(Path, notificationManager);
        SubscribeToWatcher(_dbWatcher);
    }

    protected FileStorage(string path, IDatabaseWatcher watcher)
        : base(new global::Unlimotion.Storage.FileTaskStorageOptions { Path = PreparePath(path) })
    {
        _dbWatcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        SubscribeToWatcher(_dbWatcher);
    }

    public IDatabaseWatcher? Watcher => _dbWatcher;

    protected virtual async Task OnUpdatingAsync(TaskStorageUpdateEventArgs e)
    {
        _pendingWatcherUpdates.TryGetValue(e.Id, out var pendingWatcherUpdate);
        var taskId = pendingWatcherUpdate?.TaskId ??
            (TryGetTaskIdBySourceFileName(e.Id, out var mappedTaskId) ? mappedTaskId : e.Id);
        var refresh = await WithDirectoryLockAsync(async () =>
        {
            // Keep the raw entry pending until the directory lock is held. Otherwise a
            // command queued ahead of this delayed callback can observe an empty queue,
            // mark the invalidated graph current, and consume stale task content.
            await RefreshPendingFileChangesWithinWriteLockAsync();
            // A precise refresh can replace the source-file mapping when an external edit
            // changes the task Id. Resolve the mapping again before the forced load so an
            // aliased file is still loaded by its actual source path.
            var refreshedTaskId = TryGetTaskIdBySourceFileName(e.Id, out var refreshedMappedTaskId)
                ? refreshedMappedTaskId
                : taskId;
            var loadedTask = await Load(refreshedTaskId, forced: true);
            var graph = await ReadGraphAsync();
            var sourcePath = System.IO.Path.Combine(Path, e.Id);
            var physicallyAbsent = !File.Exists(sourcePath) || new FileInfo(sourcePath).Length == 0;
            refreshedTaskId = loadedTask?.Id ?? taskId;
            return new FileRefreshResult(
                graph.TasksById.GetValueOrDefault(refreshedTaskId),
                graph.TasksById.GetValueOrDefault(taskId),
                physicallyAbsent,
                graph.Revision);
        });

        if (refresh.Task == null && !refresh.PhysicallyAbsent)
        {
            // Keep the existing projection for corrupt or temporarily unreadable content.
            // The live graph records the diagnostic and blocks writes until the file is repaired.
            return;
        }

        if (refresh.Task != null && !string.Equals(taskId, refresh.Task.Id, StringComparison.Ordinal))
        {
            // A source file can legally change its domain Id. Another source can take over the
            // old identity before this delayed callback runs; republish that surviving snapshot
            // instead of removing it from the UI.
            RaiseUpdating(new FileStorageUpdateEventArgs
            {
                Id = taskId,
                Type = refresh.PreviousIdentityTask == null ? UpdateType.Removed : UpdateType.Saved,
                StorageRevision = refresh.RevisionAfter,
                Snapshot = refresh.PreviousIdentityTask == null
                    ? null
                    : TaskItemSnapshot.Clone(refresh.PreviousIdentityTask)
            });
        }

        RaiseUpdating(new FileStorageUpdateEventArgs
        {
            Id = refresh.Task?.Id ?? taskId,
            Type = refresh.Task == null ? UpdateType.Removed : UpdateType.Saved,
            StorageRevision = refresh.RevisionAfter,
            Snapshot = refresh.Task == null ? null : TaskItemSnapshot.Clone(refresh.Task)
        });

        if (pendingWatcherUpdate != null)
        {
            RemovePendingWatcherUpdate(e.Id, pendingWatcherUpdate);
        }
    }

    protected override void OnBeforeWrite(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    protected override void OnWritePrepared(string taskId, string filePath, string content)
    {
        ConfirmOwnWrite(System.IO.Path.GetFileName(filePath), content);
    }

    internal async Task WriteOwnedMigrationFileAsync(string filePath, string content)
    {
        ConfirmOwnWrite(System.IO.Path.GetFileName(filePath), content);
        try
        {
            await AtomicWriteAllTextAsync(filePath, content);
        }
        finally
        {
            CompleteOwnWrite(System.IO.Path.GetFileName(filePath));
        }
    }

    private void ConfirmOwnWrite(string fileName, string content)
    {
        _confirmedOwnWrites[fileName] = new ConfirmedOwnWrite(
            SHA256.HashData(Encoding.UTF8.GetBytes(content)),
            DateTimeOffset.UtcNow.AddSeconds(5));
    }

    protected override void OnWriteFinished(string taskId, string filePath) =>
        CompleteOwnWrite(System.IO.Path.GetFileName(filePath));

    protected override void OnAfterWritePersisted(string taskId, string filePath) =>
        CompleteOwnWrite(System.IO.Path.GetFileName(filePath));

    private void CompleteOwnWrite(string fileName)
    {
        if (_confirmedOwnWrites.TryGetValue(fileName, out var confirmation))
        {
            lock (confirmation.Sync)
            {
                confirmation.IsWriteInProgress = false;
            }
        }
    }

    protected override void OnBeforeRemove(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    public override Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation) =>
        WithDirectoryLockAsync(async () =>
        {
            await RefreshPendingFileChangesWithinWriteLockAsync();
            return await operation();
        });

    public Task<TaskGraphReadResult> SynchronizePendingFileChangesAsync() =>
        WithDirectoryLockAsync(async () =>
        {
            await RefreshPendingFileChangesWithinWriteLockAsync();
            return await ReadGraphAsync();
        });

    public Task RefreshPendingFileChangesAsync() =>
        WithDirectoryLockAsync(async () =>
        {
            await RefreshPendingFileChangesWithinWriteLockAsync();
        });

    public long CapturePendingWatcherGeneration() => Interlocked.Read(ref _nextPendingGeneration);

    public long EnableWatcherAndCaptureDisabledGeneration()
    {
        var generation = 0L;
        _dbWatcher?.SetEnable(
            true,
            () => generation = CapturePendingWatcherGeneration());
        return generation;
    }

    public void DiscardPendingWatcherUpdatesThrough(long generation)
    {
        foreach (var entry in _pendingWatcherUpdates)
        {
            if (entry.Value.Generation <= generation)
            {
                RemovePendingWatcherUpdate(entry.Key, entry.Value);
            }
        }
    }

    public async Task WaitForRawWatcherQuiescenceAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var generation = CapturePendingWatcherGeneration();
            await Task.Delay(200);
            if (CapturePendingWatcherGeneration() == generation)
            {
                return;
            }
        }
    }

    private void SubscribeToWatcher(IDatabaseWatcher watcher)
    {
        if (watcher is IRawDatabaseWatcher rawWatcher)
        {
            rawWatcher.OnInvalidated += (_, _) => InvalidateLiveGraph();
            rawWatcher.OnRawUpdated += (_, args) =>
            {
                if (IsTaskFile(args.Id))
                {
                    if (IsConfirmedOwnWrite(args.Id))
                    {
                        return;
                    }

                    var change = new PendingFileChange(
                        Interlocked.Increment(ref _nextPendingGeneration),
                        args.Type);
                    // Publish the precise work and advance the invalidation generation under
                    // the same lock used to accept a refreshed graph. A synchronizer therefore
                    // cannot observe the entry before the generation that owns it.
                    PublishKnownFileChange(() =>
                    {
                        var taskId = TryGetTaskIdBySourceFileName(args.Id, out var mappedTaskId)
                            ? mappedTaskId
                            : args.Id;
                        _pendingFileChanges.AddOrUpdate(args.Id, change, (_, _) => change);
                        _pendingWatcherUpdates.AddOrUpdate(
                            args.Id,
                            new PendingWatcherUpdate(change.Generation, taskId),
                            (_, existing) => new PendingWatcherUpdate(change.Generation, existing.TaskId));
                    });
                }
            };
        }

        watcher.OnUpdated += async (_, args) =>
        {
            var entered = false;
            try
            {
                await _watcherUpdateGate.WaitAsync();
                entered = true;
                if (_disposed)
                {
                    return;
                }

                await OnUpdatingAsync(new TaskStorageUpdateEventArgs
                {
                    Id = args.Id,
                    Type = args.Type
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to process file storage update for '{args.Id}': {ex}");
            }
            finally
            {
                if (entered)
                {
                    _watcherUpdateGate.Release();
                }
            }
        };
    }

    private async Task DrainPendingFileChangesAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var pending = _pendingFileChanges.ToArray();
            if (pending.Length == 0)
            {
                return;
            }

            foreach (var entry in pending)
            {
                if (!RemovePendingFileChange(entry))
                {
                    continue;
                }

                var fileName = entry.Key;
                var taskId = TryGetTaskIdBySourceFileName(fileName, out var mappedTaskId)
                    ? mappedTaskId
                    : fileName;
                await Load(taskId, forced: true);
            }
        }

        if (!_pendingFileChanges.IsEmpty)
        {
            throw new IOException("Task files keep changing while preparing a graph command.");
        }
    }

    private async Task RefreshPendingFileChangesWithinWriteLockAsync()
    {
        if (!HasLiveGraphSnapshot && !LiveGraphRequiresFullReload)
        {
            await DrainPendingFileChangesAsync();
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (LiveGraphRequiresFullReload)
            {
                await EnsureLiveGraphReadyWithinWriteLockAsync();
            }

            var invalidationGeneration = CaptureLiveGraphInvalidationGeneration();
            await DrainPendingFileChangesAsync();
            if (TryMarkKnownFileChangesApplied(invalidationGeneration))
            {
                return;
            }

            if (!LiveGraphRequiresFullReload)
            {
                continue;
            }
        }

        // A global watcher invalidation, recovery, or duplicate-bearing graph cannot be
        // reconciled from per-file events. Fall back to the authoritative directory scan.
        await EnsureLiveGraphReadyWithinWriteLockAsync();
        var finalInvalidationGeneration = CaptureLiveGraphInvalidationGeneration();
        await DrainPendingFileChangesAsync();
        if (!TryMarkKnownFileChangesApplied(finalInvalidationGeneration))
        {
            throw new IOException("Task files keep changing while finalizing the live graph.");
        }
    }

    private bool RemovePendingFileChange(KeyValuePair<string, PendingFileChange> entry) =>
        ((ICollection<KeyValuePair<string, PendingFileChange>>)_pendingFileChanges).Remove(entry);

    private bool RemovePendingWatcherUpdate(string fileName, PendingWatcherUpdate update) =>
        ((ICollection<KeyValuePair<string, PendingWatcherUpdate>>)_pendingWatcherUpdates).Remove(
            new KeyValuePair<string, PendingWatcherUpdate>(fileName, update));

    private bool IsConfirmedOwnWrite(string fileName)
    {
        if (!_confirmedOwnWrites.TryGetValue(fileName, out var confirmation))
        {
            return false;
        }

        lock (confirmation.Sync)
        {
            // FileSystemWatcher can raise Changed/Renamed while File.Replace still exposes
            // the displaced or temporarily absent target. The completed write is classified
            // by its exact hash; guarded writes independently verify the displaced source.
            if (confirmation.IsWriteInProgress)
            {
                return true;
            }
        }

        if (confirmation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            RemoveConfirmedOwnWrite(fileName, confirmation);
            return false;
        }

        var filePath = System.IO.Path.Combine(Path, fileName);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = File.Open(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var matches = SHA256.HashData(stream).AsSpan().SequenceEqual(confirmation.Hash);
                if (!matches)
                {
                    RemoveConfirmedOwnWrite(fileName, confirmation);
                }

                return matches;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(1);
            }
            catch (IOException)
            {
                RemoveConfirmedOwnWrite(fileName, confirmation);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                RemoveConfirmedOwnWrite(fileName, confirmation);
                return false;
            }
        }

        return false;
    }

    private bool RemoveConfirmedOwnWrite(string fileName, ConfirmedOwnWrite confirmation) =>
        ((ICollection<KeyValuePair<string, ConfirmedOwnWrite>>)_confirmedOwnWrites).Remove(
            new KeyValuePair<string, ConfirmedOwnWrite>(fileName, confirmation));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (_dbWatcher as IDisposable)?.Dispose();
        _pendingFileChanges.Clear();
        _pendingWatcherUpdates.Clear();
        _confirmedOwnWrites.Clear();
    }

    private sealed record PendingFileChange(long Generation, UpdateType Type);

    private sealed record PendingWatcherUpdate(long Generation, string TaskId);

    private sealed class ConfirmedOwnWrite(byte[] hash, DateTimeOffset expiresAt)
    {
        public byte[] Hash { get; } = hash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public object Sync { get; } = new();
        public bool IsWriteInProgress { get; set; } = true;
    }

    private sealed record FileRefreshResult(
        TaskItem? Task,
        TaskItem? PreviousIdentityTask,
        bool PhysicallyAbsent,
        long RevisionAfter);

    private static string PreparePath(string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "Tasks"
            : path;

        try
        {
            Directory.CreateDirectory(normalizedPath);
            return normalizedPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(L10n.Format("FileStorageNoAccess", normalizedPath), ex);
        }
    }
}

internal sealed class FileStorageUpdateEventArgs : TaskStorageUpdateEventArgs
{
    public TaskItem? Snapshot { get; init; }
}
