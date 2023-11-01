using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Unlimotion.ViewModel.Models;
using Timer = System.Timers.Timer;

namespace Unlimotion.ViewModel
{
    public class FileDbWatcher : IDatabaseWatcher
    {
        private readonly string _path;
        private readonly HashSet<TaskUpdateEvent> _updatedTasks = new();
        private List<string> _ignoredTasks = new();
        private FileSystemWatcher _watcher;
        private CancellationTokenSource _ct;
        private readonly object _utLock = new object();
        private readonly object _itLock = new object();
        public bool IsEnabled { get; private set; }
        public event EventHandler<DbUpdatedEventArgs>? OnDatabaseUpdated;

        public FileDbWatcher(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _path = String.Empty;
                return;
            }
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException("Directory does not exist: " + path);
            }
            _path = path;
        }

        public async Task Start()
        {
            if (string.IsNullOrEmpty(_path) || IsEnabled) return;
            _watcher = new FileSystemWatcher(_path);

            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

            _watcher.Changed += OnChanged;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Disposed += (s, e) =>
            {
                _ct.Cancel();
            };
            //todo Добавить логер и логировать ошибки
            _watcher.Error += OnError;
            _watcher.IncludeSubdirectories = true;
            _watcher.EnableRaisingEvents = true;

            _ct = new();

            while (!_ct.Token.IsCancellationRequested)
            {
                GenerateEvent();
                await Task.Delay(1000);
            }

            _watcher?.Dispose();
            IsEnabled = false;
        }

        public void Stop()
        {
            _ct?.Cancel();
        }

        public void AddIgnoredTask(string taskId) {
            lock (_itLock) {
                _ignoredTasks.Add(taskId);
            }
        }

        public void RemoveIgnoredTask(string taskId) {
            lock (_itLock) {
                var ignoredTask = _ignoredTasks.FirstOrDefault(t => t == taskId);
                if (ignoredTask != null) _ignoredTasks.Remove(ignoredTask);
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Debug.WriteLine("Ошибка в файлВачере!!!");
        }

        private void AddTaskToCollection(TaskUpdateEvent taskUpdateEvent)
        {
            if (_ignoredTasks.Contains(taskUpdateEvent.Id)) return;
            lock (_utLock)
            {
                _updatedTasks.Add(taskUpdateEvent);
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                AddTaskToCollection(new TaskUpdateEvent(e.FullPath!, UpdateType.TaskChanged,
                    File.GetLastWriteTime(e.FullPath)));
            }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            AddTaskToCollection(new TaskUpdateEvent(e.FullPath!, UpdateType.TaskDeleted));
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            AddTaskToCollection(new TaskUpdateEvent(e.FullPath!, UpdateType.TaskCreated));
        }

        private void GenerateEvent()
        {
            lock (_utLock)
            {
                OnDatabaseUpdated?.Invoke(this, new DbUpdatedEventArgs() { UpdatedTasks = _updatedTasks.ToList() });
                _updatedTasks.Clear();
            }
        }
    }
}