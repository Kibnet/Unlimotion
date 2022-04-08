using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Timers;
using DynamicData;
using DynamicData.Binding;

namespace Unlimotion.ViewModel
{
    public interface ITaskRepository
    {
        SourceCache<TaskItemViewModel, string> Tasks { get; }
        void Remove(string itemId);
        void Save(TaskItem item);
        IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots();
        TaskItemViewModel Clone(TaskItem clone, TaskItemViewModel destination);
    }

    public class TaskRepository : ITaskRepository
    {
        private readonly ITaskStorage _taskStorage;
        private ConcurrentBag<TaskItem> _saveBag;
        private Timer _saveTimer;
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
        IObservable<Func<TaskItemViewModel, bool>> rootFilter;
        private Dictionary<string, HashSet<string>> blockedById { get; set; }

        public TaskRepository(ITaskStorage taskStorage)
        {
            _saveBag = new ConcurrentBag<TaskItem>();
            _saveTimer = new Timer();
            _saveTimer.Interval = TimeSpan.FromSeconds(1).TotalMilliseconds;
            _saveTimer.Elapsed += SaveTimerOnElapsed;
            _taskStorage = taskStorage;
            Init();
        }

        private void SaveTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            var hashSet = new HashSet<TaskItem>();
            while (_saveBag.TryTake(out var task))
            {
                hashSet.Add(task);
            }

            foreach (var task in hashSet)
            {
                _taskStorage.Save(task);
            }
        }

        public void Remove(string itemId)
        {
            Tasks.Remove(itemId);
            _taskStorage.Remove(itemId);
            if (blockedById.TryGetValue(itemId, out var hashSet))
            {
                blockedById.Remove(itemId);
                foreach (var blockedId in hashSet)
                {
                    var task = GetById(blockedId);
                    if (task != null)
                    {
                        task.Blocks.Remove(itemId);
                        Save(task.Model);
                    }
                }
            }
        }

        public void Save(TaskItem item)
        {
            _saveBag.Add(item);
        }

        private void Init() => Init(_taskStorage.GetAll());

        ~TaskRepository()
        {
            _saveTimer.Stop();
        }

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
                    var many = item.Contains.Select(id => (item, id));
                    return many;
                }, s => s)
                
                .Group(tuple => tuple.id)
                .Transform(g => (g.Key, g.Cache.Items.Select(t => t.item).ToHashSet()))
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.All(t => t.Key != task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });
		

            _saveTimer.Start();
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
        
        public TaskItemViewModel GetById(string id)
        {
            var item = Tasks.Lookup(id);
            return item.Value;
        }
        
        public IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots()
        {
            IObservable<IChangeSet<TaskItemViewModel, string>> roots;
            roots = Tasks.Connect()
                .Filter(rootFilter);
            return roots;
        }

        public TaskItemViewModel Clone(TaskItem clone, TaskItemViewModel destination)
        {
            var task = new TaskItemViewModel(clone, this);
            task.SaveItemCommand.Execute(null);
            destination.Contains.Add(clone.Id);
            destination.SaveItemCommand.Execute(null);
            this.Tasks.AddOrUpdate(task);
            return task;
        }
    }
}
