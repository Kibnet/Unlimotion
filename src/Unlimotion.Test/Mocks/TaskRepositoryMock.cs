using DynamicData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Test.Mocks
{
    public class TaskRepositoryMock : ITaskRepository
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }

        public event EventHandler<EventArgs> Initiated;

        private readonly ITaskStorage taskStorage;
        private Dictionary<string, HashSet<string>> blockedById { get; set; }

        public TaskRepositoryMock(ITaskStorage taskStorage)
        {
            this.taskStorage = taskStorage;
        }

        public TaskItemViewModel Clone(TaskItem clone, params TaskItemViewModel[] destinations)
        {
            throw new NotImplementedException();
        }

        public IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots()
        {
            throw new NotImplementedException();
        }

        public void Init()
        {
            Tasks = new(item => item.Id);
            blockedById = new();

            foreach (var taskItem in taskStorage.GetAll())
            {
                var vm = new TaskItemViewModel(taskItem, this);
                Tasks.AddOrUpdate(vm);

                //if (taskItem.BlocksTasks.Any())
                //{
                //    foreach (var blocksTask in taskItem.BlocksTasks.Where(s => s != null))
                //    {
                //        AddBlockedBy(blocksTask, taskItem);
                //    }
                //}
            }
        }

        public Task<TaskItem> Load(string itemId)
        {
            return taskStorage.Load(itemId);
        }

        public Task Remove(string itemId, bool deleteFile)
        {
            throw new NotImplementedException();
        }

        public async Task Save(TaskItem item)
        {
           await taskStorage.Save(item);
        }

        public void SetPause(bool pause)
        {
            throw new NotImplementedException();
        }
    }
}
