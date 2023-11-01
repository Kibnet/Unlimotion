using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Splat;
using System.ComponentModel;
using System.IO;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.ViewModel
{
    public interface ITaskRepository
    {
        SourceCache<TaskItemViewModel, string> Tasks { get; }
        void Init();
        Task Remove(string itemId);
        Task Save(TaskItem item);
        IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots();
        TaskItemViewModel Clone(TaskItem clone, params TaskItemViewModel[] destinations);
    }

    public class TaskRepository : ITaskRepository
    {
        private readonly ITaskStorage _taskStorage;
        private readonly IDatabaseWatcher? _dbWatcher;
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
        IObservable<Func<TaskItemViewModel, bool>> rootFilter;
        private Dictionary<string, HashSet<string>> blockedById { get; set; }
        public TaskRepository(ITaskStorage taskStorage)
        {
            _taskStorage = taskStorage;
        }
        public TaskRepository(ITaskStorage taskStorage, IDatabaseWatcher? dbWatcher = null)
        {
            _taskStorage = taskStorage;
            _dbWatcher = dbWatcher;
        }

        public async Task Remove(string itemId)
        {
            Tasks.Remove(itemId);
            await _taskStorage.Remove(itemId);
            if (blockedById.TryGetValue(itemId, out var hashSet))
            {
                blockedById.Remove(itemId);
                foreach (var blockedId in hashSet)
                {
                    var task = GetById(blockedId);
                    if (task != null)
                    {
                        task.Blocks.Remove(itemId);
                        await Save(task.Model);
                    }
                }
            }
        }
        
        public async Task Save(TaskItem item)
        {
            await _taskStorage.Save(item);
        }

        public void Init() => Init(_taskStorage.GetAll());

        private void Init(IEnumerable<TaskItem> items)
        {
            Tasks = new(item => item.Id);
            blockedById = new();
            foreach (var taskItem in items)
            {
                var vm = new TaskItemViewModel(taskItem, this);
                Tasks.AddOrUpdate(vm);

                if (taskItem.BlocksTasks.Any())
                {
                    foreach (var blocksTask in taskItem.BlocksTasks)
                    {
                        AddBlockedBy(blocksTask, taskItem);
                    }
                }
            }

            rootFilter = Tasks.Connect()
                .AutoRefreshOnObservable(t => t.Contains.ToObservableChangeSet())
                .TransformMany(item =>
                {
                    var many = item.Contains.Where(s => !string.IsNullOrEmpty(s)).Select(id => id);
                    return many;
                }, s => s)

                .Distinct(k => k)
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Count == 0 || items.All(t => t != task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });
            if (_dbWatcher != null)
            {
                _dbWatcher.OnDatabaseUpdated += async (object? sender, DbUpdatedEventArgs e) => await _dbWatcher_OnDatabaseUpdated(sender, e);
                _dbWatcher.Start();
            }
        }

        private async Task _dbWatcher_OnDatabaseUpdated(object? sender, DbUpdatedEventArgs e)
        {
            foreach (var task in e.UpdatedTasks)
            {
                TaskItem? taskItem;
                switch (task.Type)
                {
                    case UpdateType.TaskCreated:
                        if (!Tasks.Keys.Contains(new FileInfo(task.Id).Name))
                        {
                            taskItem = LoadTaskItemFromFile(task.Id);
                            if (taskItem != null)
                            {
                                var vm = new TaskItemViewModel(taskItem, this);
                                Tasks.AddOrUpdate(vm);
                            }
                        }
                        break;
                    case UpdateType.TaskDeleted:
                        var fi = new FileInfo(task.Id);
                        Remove(fi.Name);
                        break;
                    case UpdateType.TaskChanged:
                        taskItem = LoadTaskItemFromFile(task.Id);
                        if (taskItem != null)
                        {
                            var vm = new TaskItemViewModel(taskItem, this);
                            Tasks.AddOrUpdate(vm);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private TaskItem? LoadTaskItemFromFile(string filePath)
        {
            if (_taskStorage is IFileTaskStorage fts)
            {
                return fts.LoadFromFile(filePath);
            }

            return null;
        }

        public void AddBlockedBy(string blocksTask, TaskItem taskItem)
        {
            if (!blockedById.TryGetValue(blocksTask, out var hashSet))
            {
                hashSet = new HashSet<string>();
                blockedById[blocksTask] = hashSet;
            }

            hashSet.Add(taskItem.Id);
        }

        public void RemoveBlockedBy(string subTask, string taskItemId)
        {
            if (blockedById.TryGetValue(subTask, out var hashSet))
            {
                hashSet.Remove(taskItemId);
                if (hashSet.Count == 0)
                {
                    blockedById.Remove(subTask);
                }
            }
        }

        public TaskItemViewModel? GetById(string id)
        {
            var item = Tasks.Lookup(id);
            return item.HasValue ? item.Value : null;
        }

        public IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots()
        {
            IObservable<IChangeSet<TaskItemViewModel, string>> roots;
            roots = Tasks.Connect()
                .Filter(rootFilter);
            return roots;
        }

        public TaskItemViewModel Clone(TaskItem clone, params TaskItemViewModel[] destinations)
        {
            var task = new TaskItemViewModel(clone, this);
            task.SaveItemCommand.Execute();
            foreach (var destination in destinations)
            {
                destination.Contains.Add(task.Id);
            }
            this.Tasks.AddOrUpdate(task);
            return task;
        }
    }
}
