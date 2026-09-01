using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        var taskId = TryGetTaskIdBySourceFileName(e.Id, out var mappedTaskId)
            ? mappedTaskId
            : e.Id;
        RemovePendingFileChange(e.Id);
        var refresh = await WithDirectoryLockAsync(async () =>
        {
            var revisionBefore = LiveGraphRevision;
            var loaded = await Load(taskId, forced: true);
            var sourcePath = System.IO.Path.Combine(Path, e.Id);
            var physicallyAbsent = !File.Exists(sourcePath) || new FileInfo(sourcePath).Length == 0;
            return new FileRefreshResult(
                loaded,
                physicallyAbsent,
                revisionBefore,
                LiveGraphRevision);
        });

        if (refresh.RevisionBefore > 0 && refresh.RevisionBefore == refresh.RevisionAfter)
        {
            return;
        }

        if (refresh.Task == null && !refresh.PhysicallyAbsent)
        {
            // Keep the existing projection for corrupt or temporarily unreadable content.
            // The live graph records the diagnostic and blocks writes until the file is repaired.
            return;
        }

        RaiseUpdating(new TaskStorageUpdateEventArgs
        {
            Id = refresh.Task?.Id ?? taskId,
            Type = refresh.Task == null ? UpdateType.Removed : UpdateType.Saved,
            StorageRevision = refresh.RevisionAfter
        });
    }

    protected override void OnBeforeWrite(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    protected override void OnBeforeRemove(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    public override Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation) =>
        WithDirectoryLockAsync(async () =>
        {
            await EnsureLiveGraphReadyWithinWriteLockAsync();
            await DrainPendingFileChangesAsync();
            return await operation();
        });

    public Task<TaskGraphReadResult> SynchronizePendingFileChangesAsync() =>
        WithDirectoryLockAsync(async () =>
        {
            await EnsureLiveGraphReadyWithinWriteLockAsync();
            await DrainPendingFileChangesAsync();
            return await ReadGraphAsync();
        });

    private void SubscribeToWatcher(IDatabaseWatcher watcher)
    {
        if (watcher is IRawDatabaseWatcher rawWatcher)
        {
            rawWatcher.OnInvalidated += (_, _) => InvalidateLiveGraph();
            rawWatcher.OnRawUpdated += (_, args) =>
            {
                if (IsTaskFile(args.Id))
                {
                    var change = new PendingFileChange(
                        Interlocked.Increment(ref _nextPendingGeneration),
                        args.Type);
                    _pendingFileChanges.AddOrUpdate(args.Id, change, (_, _) => change);
                }
            };
        }

        watcher.OnUpdated += async (_, args) =>
        {
            try
            {
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

    private void RemovePendingFileChange(string fileName)
    {
        if (_pendingFileChanges.TryGetValue(fileName, out var pending))
        {
            RemovePendingFileChange(new KeyValuePair<string, PendingFileChange>(fileName, pending));
        }
    }

    private bool RemovePendingFileChange(KeyValuePair<string, PendingFileChange> entry) =>
        ((ICollection<KeyValuePair<string, PendingFileChange>>)_pendingFileChanges).Remove(entry);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (_dbWatcher as IDisposable)?.Dispose();
        _pendingFileChanges.Clear();
    }

    private sealed record PendingFileChange(long Generation, UpdateType Type);

    private sealed record FileRefreshResult(
        TaskItem? Task,
        bool PhysicallyAbsent,
        long RevisionBefore,
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
