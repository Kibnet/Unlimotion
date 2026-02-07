using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Unlimotion.TaskTree;

namespace Unlimotion.ViewModel
{
    public class FileDbWatcher : IDatabaseWatcher
    {
        private const string GitFolderName = ".git";
        private const string GitLockPostfix = ".lock";
        private const string GitOrigPostfix = ".orig";
        private readonly MemoryCache ignoredTasks = MemoryCache.Default;
        private readonly FileSystemWatcher watcher;
        private readonly object itLock = new();
        public event EventHandler<DbUpdatedEventArgs> OnUpdated;
        private readonly MemoryCache cache = new("EventThrottlerCache");
        private readonly TimeSpan throttlePeriod = TimeSpan.FromSeconds(1);
        private bool isEnable;
        private readonly INotificationManagerWrapper? _notificationManager;

        public void SetEnable(bool enable)
        {
            isEnable = enable;
        }

        public void ForceUpdateFile(string filename, UpdateType type)
        {
            OnUpdated?.Invoke(this, new DbUpdatedEventArgs
            {
                Id = filename,
                Type = type
            });
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

            //todo Добавить логер и логировать ошибки
            watcher.Error += OnError;
            watcher.IncludeSubdirectories = true;
            isEnable = true;
            watcher.EnableRaisingEvents = true;
        }

        public void AddIgnoredTask(string taskId)
        {
            lock (itLock)
            {
                ignoredTasks.Add(taskId, itLock,  new CacheItemPolicy { SlidingExpiration = TimeSpan.FromSeconds(60) });
                Debug.WriteLine($"{DateTimeOffset.Now}: ${taskId} is added to ignored");
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Debug.WriteLine("Error in FileWatcher");
            _notificationManager?.ErrorToast(e.GetException().Message);

        }

        private FileSystemEventHandler CreateThrottledEventHandler(
            FileSystemEventHandler handler)
        {
            return (s, e) =>
            {
                if (!isEnable)
                    return;

                var fullPath = e.FullPath;
                
                if (fullPath.Contains(GitFolderName) || fullPath.EndsWith(GitOrigPostfix))
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

            lock (itLock)
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (ignoredTasks.Contains(fileInfo.Name))
                {
                    return;
                }
            }

            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                case WatcherChangeTypes.Changed:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.Name,
                        Type = UpdateType.Saved
                    });
                    break;
                case WatcherChangeTypes.Deleted:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.Name,
                        Type = UpdateType.Removed
                    });
                    break;
            }
            Debug.WriteLine($"{DateTimeOffset.Now}: {e.FullPath} {e.ChangeType}.");
        }
        
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
    }
}