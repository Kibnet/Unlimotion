using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Caching;
using L10n = Unlimotion.ViewModel.Localization.Localization;
using System.Threading.Tasks;
using Unlimotion.TaskTree;

namespace Unlimotion.ViewModel
{
    public class FileDbWatcher : IDatabaseWatcher, IRawDatabaseWatcher, IDisposable
    {
        private const string GitFolderName = ".git";
        private const string GitLockPostfix = ".lock";
        private const string GitOrigPostfix = ".orig";
        private readonly FileSystemWatcher? watcher;
        public event EventHandler<DbUpdatedEventArgs>? OnUpdated;
        public event EventHandler<DbUpdatedEventArgs>? OnRawUpdated;
        public event EventHandler? OnInvalidated;
        private readonly MemoryCache cache = new("EventThrottlerCache");
        private readonly TimeSpan throttlePeriod = TimeSpan.FromSeconds(1);
        private bool isEnable;
        private bool isDisposed;
    private readonly INotificationManagerWrapper? _notificationManager;
    private readonly object itLockEnable = new();

        public void SetEnable(bool enable)
        {
            lock (itLockEnable)
            {
                if (isDisposed)
                    return;
                isEnable = enable;
            }
        }

        public void ForceUpdateFile(string filename, UpdateType type)
        {
            if (isDisposed)
                return;
            var args = new DbUpdatedEventArgs
            {
                Id = filename,
                Type = type
            };
            OnRawUpdated?.Invoke(this, args);
            OnUpdated?.Invoke(this, args);
        }

        public FileDbWatcher(string path, INotificationManagerWrapper? notificationManager = null)
        {
            _notificationManager = notificationManager;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException("Directory does not exist: " + path);
            }
            watcher = new FileSystemWatcher(path);

            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

            var throttle = CreateThrottledEventHandler(OnChanged);
            watcher.Changed += throttle;
            watcher.Created += throttle;
            watcher.Deleted += throttle;
            watcher.Renamed += (sender, args) =>
            {
                RegisterRawUpdate(args.OldName, UpdateType.Removed);
                RegisterRawUpdate(args.Name, UpdateType.Saved);
                throttle(sender, new FileSystemEventArgs(
                    WatcherChangeTypes.Deleted,
                    Path.GetDirectoryName(args.OldFullPath) ?? string.Empty,
                    args.OldName ?? string.Empty));
                throttle(sender, new FileSystemEventArgs(
                    WatcherChangeTypes.Created,
                    Path.GetDirectoryName(args.FullPath) ?? string.Empty,
                    args.Name ?? string.Empty));
            };

            //todo Добавить логер и логировать ошибки
            watcher.Error += OnError;
            watcher.IncludeSubdirectories = false;
            isEnable = true;
            watcher.EnableRaisingEvents = true;
        }

        public void AddIgnoredTask(string taskId)
        {
            // Own writes are deduplicated by the storage's confirmed content snapshot.
            // Do not suppress by file name: an external edit can follow immediately.
            Debug.WriteLine($"{DateTimeOffset.Now}: ${taskId} was written by this storage");
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            if (isDisposed)
                return;
            Debug.WriteLine("Error in FileWatcher");
            OnInvalidated?.Invoke(this, EventArgs.Empty);
            _notificationManager?.ErrorToast(L10n.Format("FileWatcherError", e.GetException().Message));

        }

        private FileSystemEventHandler CreateThrottledEventHandler(
            FileSystemEventHandler handler)
        {
            return (s, e) =>
            {
                var fullPath = e.FullPath;
                
                if (fullPath.Contains(GitFolderName) ||
                    fullPath.EndsWith(GitOrigPostfix) ||
                    IsStorageServiceArtifact(e.Name))
                    return;

                RegisterRawUpdate(
                    e.Name,
                    e.ChangeType == WatcherChangeTypes.Deleted ? UpdateType.Removed : UpdateType.Saved);

                if (!isEnable)
                    return;
                
                if (fullPath.EndsWith(GitLockPostfix)) 
                    fullPath = e.FullPath.Replace(GitLockPostfix, "");
                
                if (cache.Get(fullPath) != null) 
                    cache.Set(fullPath, fullPath, GetCachePolicy(() => handler(s, e)));
                else 
                    cache.Add(fullPath, fullPath, GetCachePolicy(() => handler(s, e)));
            };
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!isEnable)
                return;

            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                case WatcherChangeTypes.Changed:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.Name ?? string.Empty,
                        Type = UpdateType.Saved
                    });
                    break;
                case WatcherChangeTypes.Deleted:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.Name ?? string.Empty,
                        Type = UpdateType.Removed
                    });
                    break;
            }
            Debug.WriteLine($"{DateTimeOffset.Now}: {e.FullPath} {e.ChangeType}.");
        }

        private void RegisterRawUpdate(string? fileName, UpdateType type)
        {
            if (isDisposed || string.IsNullOrWhiteSpace(fileName) || IsStorageServiceArtifact(fileName))
            {
                return;
            }

            OnRawUpdated?.Invoke(this, new DbUpdatedEventArgs
            {
                Id = fileName,
                Type = type
            });
        }

        private static bool IsStorageServiceArtifact(string? fileName) =>
            string.IsNullOrWhiteSpace(fileName) ||
            fileName.Equals(".unlimotion.lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".report", StringComparison.OrdinalIgnoreCase);
        
        private CacheItemPolicy GetCachePolicy(Action handler)
        {
            return new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.Now.Add(throttlePeriod),
                RemovedCallback = args =>
                {
                    if (args.RemovedReason != CacheEntryRemovedReason.Expired) return;
                    Task.Run(handler);
                }
            };
        }

        public void Dispose()
        {
            lock (itLockEnable)
            {
                if (isDisposed)
                    return;

                isDisposed = true;
                isEnable = false;
            }

            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            cache.Dispose();
        }
    }
}
