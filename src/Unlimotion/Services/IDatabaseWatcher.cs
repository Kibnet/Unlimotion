using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Unlimotion.Services;

namespace Unlimotion.Services;
public interface IDatabaseWatcher {
    event EventHandler<DbUpdatedEventArgs>? OnDatabaseUpdated;
    void Start();
}

public class DbUpdatedEventArgs : EventArgs {
    public List<UpdatedTask> UpdatedTasks { get; set;}
}

public enum UpdatingTaskType {
    TaskCreated,
    TaskDeleted, 
    TaskChanged
}

public class UpdatedTask{
    public DateTime UpdatedDateTime { get; }
    public string Id { get; }
    public UpdatingTaskType UpdatingType { get; }
    public UpdatedTask(string id, UpdatingTaskType updatingType) {
        Id = id;
        UpdatingType = updatingType;
        UpdatedDateTime = DateTime.Now;
    }

    public UpdatedTask(string id, UpdatingTaskType updatingType, DateTime updatedDateTime) {
        Id = id;
        UpdatingType = updatingType;
        UpdatedDateTime = updatedDateTime;
    }
}

public class FileDbWatcher : IDatabaseWatcher {
    private string _path;
    private readonly ConcurrentBag<UpdatedTask> _updatedTasks = new();
    private Timer _generateEventTimer = new Timer();
    
    public event EventHandler<DbUpdatedEventArgs>? OnDatabaseUpdated;

    public FileDbWatcher(string path, long generateEventInterval = 1000) {
        if (!Directory.Exists(path)) {
            throw new DirectoryNotFoundException("Directory does not exist: " + path);
        }
        _path = path;
        _generateEventTimer.Interval = generateEventInterval;
        _generateEventTimer.AutoReset = true;
        _generateEventTimer.Elapsed += GenerateEvent;
    }

    public void Start() {
        using var watcher = new FileSystemWatcher(_path);
        watcher.NotifyFilter = NotifyFilters.LastWrite;

        watcher.Changed += OnChanged;
        watcher.Created += OnCreated;
        watcher.Deleted += OnDeleted;
        watcher.Disposed += (s, e) => {
            _generateEventTimer.Stop();
            _generateEventTimer.Dispose();
        };
        //todo Добавить логер и логировать ошибки
        //watcher.Error += OnError;

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;
        _generateEventTimer.Start();
    }

    private void OnChanged(object sender, FileSystemEventArgs e) {
        if (e.ChangeType == WatcherChangeTypes.Changed) {
            _updatedTasks.Add(new UpdatedTask(e.Name!, UpdatingTaskType.TaskChanged,
                File.GetLastWriteTime(e.FullPath)));
            ResetTimer();
        }
    }
    
    private void OnDeleted(object sender, FileSystemEventArgs e) {
        _updatedTasks.Add(new UpdatedTask(e.Name!, UpdatingTaskType.TaskDeleted));
        ResetTimer();
    }

    private void OnCreated(object sender, FileSystemEventArgs e) {
        _updatedTasks.Add(new UpdatedTask(e.Name!, UpdatingTaskType.TaskCreated));
        ResetTimer();
    }
    
    private void GenerateEvent(object? sender, ElapsedEventArgs e) {
        OnDatabaseUpdated?.Invoke(this, new DbUpdatedEventArgs() { UpdatedTasks = _updatedTasks.ToList() });
    }

    private void ResetTimer() {
        if (_generateEventTimer.Enabled) {
            _generateEventTimer.Stop();
            _generateEventTimer.Start();
        }
    }
}
