using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Caching;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.ViewModel
{
    public class FileDbWatcher : IDatabaseWatcher
    {
        private readonly string _path;
        private const string GitFolderName = ".git";
        private MemoryCache ignoredTasks = MemoryCache.Default;
        private MemoryCache changedTasks = MemoryCache.Default;
        private FileSystemWatcher _watcher;
        object _itLock = new object();
        public event EventHandler<DbUpdatedEventArgs> OnUpdated;

        public FileDbWatcher(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException("Directory does not exist: " + path);
            }
            _watcher = new FileSystemWatcher(path);

            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

            var throttle = CreateThrottledEventHandler(OnChanged, TimeSpan.FromSeconds(1));
            _watcher.Changed += throttle;
            _watcher.Created += throttle;
            _watcher.Deleted += throttle;

            //todo Добавить логер и логировать ошибки
            _watcher.Error += OnError;
            _watcher.IncludeSubdirectories = true;
            _watcher.EnableRaisingEvents = true;
        }

        public void AddIgnoredTask(string taskId)
        {
            lock (_itLock)
            {
                ignoredTasks.Add(taskId, _itLock, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromSeconds(5) });
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Debug.WriteLine("Error in FileWatcher");
        }

        private FileSystemEventHandler CreateThrottledEventHandler(
            FileSystemEventHandler handler,
            TimeSpan throttle)
        {
            return (s, e) =>
            {
                if (changedTasks.Contains(e.FullPath))
                {
                    return;
                }

                if (e.FullPath.Contains(GitFolderName))
                {
                    return;
                }

                changedTasks.Add(e.FullPath, _itLock, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromSeconds(1) });
                Task.Delay(throttle).ContinueWith(_ => handler(s, e));
            };
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            lock (_itLock)
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (ignoredTasks.Contains(fileInfo.FullName))
                {
                    ignoredTasks.Remove(fileInfo.FullName);
                    return;
                }
            }

            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                case WatcherChangeTypes.Changed:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.FullPath,
                        Type = UpdateType.Saved
                    });
                    break;
                case WatcherChangeTypes.Deleted:
                    OnUpdated?.Invoke(this, new DbUpdatedEventArgs
                    {
                        Id = e.FullPath,
                        Type = UpdateType.Removed
                    });
                    break;
            }
        }
    }
}