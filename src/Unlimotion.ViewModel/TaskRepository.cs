using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.ViewModel
{
    public class TaskRepository
    {
        public TaskRepository()
        {

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
                        if (!parentsById.TryGetValue(subTask, out var hashSet))
                        {
                            hashSet = new HashSet<string>();
                            parentsById[subTask] = hashSet;
                        }

                        hashSet.Add(taskItem.Id);
                    }
                }
                if (taskItem.BlocksTasks.Any())
                {
                    foreach (var blocksTask in taskItem.BlocksTasks)
                    {
                        if (!blockedById.TryGetValue(blocksTask, out var hashSet))
                        {
                            hashSet = new HashSet<string>();
                            blockedById[blocksTask] = hashSet;
                        }

                        hashSet.Add(taskItem.Id);
                    }
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
