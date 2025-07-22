using AutoMapper;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.Test.Mocks
{
    public class TestStorageMock : ITaskStorage, IDatabaseWatcher
    {
        private List<TaskItem> storageTaskItems = new List<TaskItem>();
        private static readonly object LockObject = new();
        IMapper? mapper; 
        //#region ITaskStorage
        public event EventHandler<TaskStorageUpdateEventArgs> Updating;

        public async Task<bool> Connect()
        {
            return await Task.FromResult(true);
        }

        public async Task Disconnect()
        {
        }

        public IEnumerable<TaskItem> GetAll()
        {
            return storageTaskItems;
        }

        public async Task<TaskItem> Load(string itemId)
        {
            return storageTaskItems.FirstOrDefault(i => i.Id == itemId);
        }

        public Task<bool> Remove(string itemId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Save(TaskItem item)
        {
            lock (LockObject)
            {
                item.Id ??= Guid.NewGuid().ToString();
                var storageItem = storageTaskItems.FirstOrDefault(i => i.Id == item.Id);
                if (storageItem == null)
                {
                    storageTaskItems.Add(item);
                }
                else
                {
                    storageItem.Title = item.Title;
                    storageItem.IsCompleted = item.IsCompleted;
                    storageItem.Description = item.Description;
                    storageItem.ArchiveDateTime = item.ArchiveDateTime;
                    storageItem.UnlockedDateTime = item.UnlockedDateTime;
                    storageItem.PlannedBeginDateTime = item.PlannedBeginDateTime;
                    storageItem.PlannedEndDateTime = item.PlannedEndDateTime;
                    storageItem.PlannedDuration = item.PlannedDuration;
                    storageItem.Repeater = item.Repeater;
                    storageItem.Importance = item.Importance;
                    storageItem.Wanted = item.Wanted;
                }
            }
            return Task.FromResult(true);
        }
        //#endregion
        #region IDatabaseWatcher
        public event EventHandler<DbUpdatedEventArgs> OnUpdated;

        public void AddIgnoredTask(string taskId)
        {
            throw new NotImplementedException();
        }

        public void ForceUpdateFile(string filename, UpdateType type)
        {
            throw new NotImplementedException();
        }

        public void SetEnable(bool enable)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
