using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using System.IO;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.ViewModel
{
    public class TaskRepository : ITaskRepository
    {
        private readonly ITaskStorage taskStorage;
        private readonly IDatabaseWatcher? dbWatcher;
        private Dictionary<string, HashSet<string>> blockedById { get; set; }
        private IObservable<Func<TaskItemViewModel, bool>> rootFilter;
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }

        public TaskRepository(ITaskStorage taskStorage)
        {
            this.taskStorage = taskStorage;
        }

        public TaskRepository(ITaskStorage taskStorage, IDatabaseWatcher? dbWatcher = null)
        {
            this.taskStorage = taskStorage;
            this.dbWatcher = dbWatcher;
        }

        public async Task Remove(string itemId)
        {
            Tasks.Remove(itemId);
            await taskStorage.Remove(itemId);
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
            await taskStorage.Save(item);
        }

        public Task<TaskItem> Load(string itemId)
        {
            return taskStorage.Load(itemId);
        }

        public void Init() => Init(taskStorage.GetAll());

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
            if (dbWatcher != null)
            {
                taskStorage.Updating += TaskStorageOnUpdating;
                dbWatcher.OnUpdated += DbWatcherOnOnUpdated;
            }
        }

        private void TaskStorageOnUpdating(object sender, TaskStorageUpdateEventArgs e)
        {
            dbWatcher?.AddIgnoredTask(e.Id);
        }

        private async void DbWatcherOnOnUpdated(object sender, DbUpdatedEventArgs e)
        {
            switch (e.Type)
            {
                case UpdateType.Saved:
                    var taskItem = await Load(e.Id);
                    if (taskItem != null)
                    {
                        var vml = Tasks.Lookup(taskItem.Id);
                        if (vml.HasValue)
                        {
                            var vm = vml.Value;
                            vm.Update(taskItem);
                        }
                        else
                        {
                            var vm = new TaskItemViewModel(taskItem, this);
                            Tasks.AddOrUpdate(vm);
                        }
                    }
                    break;
                case UpdateType.Removed:
                    var fileInfo = new FileInfo(e.Id);
                    await Remove(fileInfo.Name);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
