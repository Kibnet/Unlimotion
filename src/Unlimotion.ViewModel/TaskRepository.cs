using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace Unlimotion.ViewModel
{
    public class TaskRepository
    {
        private readonly ITaskStorage _taskStorage;
        private ConcurrentBag<TaskItem> _saveBag;
        private Timer _saveTimer;

        public TaskRepository(ITaskStorage taskStorage)
        {
            _saveBag = new ConcurrentBag<TaskItem>();
            _saveTimer = new Timer();
            _saveTimer.Interval = TimeSpan.FromSeconds(1).TotalMilliseconds;
            _saveTimer.Elapsed += SaveTimerOnElapsed;
            _taskStorage = taskStorage;
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
                _taskStorage.SaveTask(task);
            }
        }

        public void Save(TaskItem item)
        {
            _saveBag.Add(item);
        }

        public void Init() => Init(_taskStorage.GetAllTasks());

        ~TaskRepository()
        {
            _saveTimer.Stop();
        }

        public void Init(IEnumerable<TaskItem> items)
        {
            taskById = new();
            parentsById = new();
            blockedById = new();
            foreach (var taskItem in items)
            {
                taskById[taskItem.Id] = taskItem;
                if (taskItem.ContainsTasks.Any())
                {
                    foreach (var subTask in taskItem.ContainsTasks)
                    {
                        AddParent(subTask, taskItem.Id);
                    }
                }
                if (taskItem.BlocksTasks.Any())
                {
                    foreach (var blocksTask in taskItem.BlocksTasks)
                    {
                        AddBlockedBy(blocksTask, taskItem);
                    }
                }
            }
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

        public void AddParent(string subTask, string taskItemId)
        {
            if (!parentsById.TryGetValue(subTask, out var hashSet))
            {
                hashSet = new HashSet<string>();
                parentsById[subTask] = hashSet;
            }

            hashSet.Add(taskItemId);
        }

        public void RemoveParent(string subTask, string taskItemId)
        {
            if (parentsById.TryGetValue(subTask, out var hashSet))
            {
                hashSet.Remove(taskItemId);
                if (hashSet.Count==0)
                {
                    parentsById.Remove(subTask);
                }
            }
        }

        public TaskItem GetById(string id)
        {
            if (taskById.TryGetValue(id, out var item))
            {
                return item;
            }

            return null;
        }

        public IEnumerable<TaskItem> GetById(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                yield break;
            }

            foreach (var id in ids)
            {
                yield return GetById(id);
            }
        }

        public IEnumerable<TaskItem> GetRoots()
        {
            return GetById(taskById.Where(pair => !parentsById.ContainsKey(pair.Key)).Select(pair => pair.Key));
        }

        public IEnumerable<TaskItem> GetUnblocks()
        {
            return GetById(taskById.Where(pair => !blockedById.ContainsKey(pair.Key)).Select(pair => pair.Key));
        }

        private Dictionary<string, TaskItem> taskById { get; set; }
        private Dictionary<string, HashSet<string>> parentsById { get; set; }
        private Dictionary<string, HashSet<string>> blockedById { get; set; }

        public IEnumerable<TaskItem> GetParentsById(string id)
        {
            if (parentsById.TryGetValue(id, out var itemsSet))
            {
                foreach (var item in GetById(itemsSet))
                {
                    yield return item;
                }
            }
        }

        public IEnumerable<TaskItem> GetBlockedById(string id)
        {
            if (blockedById.TryGetValue(id, out var itemsSet))
            {
                foreach (var item in GetById(itemsSet))
                {
                    yield return item;
                }
            }
        }
    }
}
