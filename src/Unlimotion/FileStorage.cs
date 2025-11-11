using DynamicData;
using KellermanSoftware.CompareNetObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion
{
    public class FileStorage : IStorage
    {
        public string Path { get; private set; }

        private ConcurrentDictionary<string, TaskItem> tasks;

        private IDatabaseWatcher? dbWatcher;
        private CompareLogic compareLogic;

        public event EventHandler<TaskStorageUpdateEventArgs> Updating;
        public event Action<Exception?>? OnConnectionError;

        public FileStorage(string path, bool watcher = false)
        {
            Path = path;
            tasks = new();
            if (watcher)
            {
                dbWatcher = new FileDbWatcher(path);
                dbWatcher.OnUpdated += (sender, args) => OnUpdating(new TaskStorageUpdateEventArgs()
                {
                    Id = args.Id,
                    Type = args.Type,
                });
            }

            compareLogic = new CompareLogic
            {
                Config =
                {
                    MaxDifferences = 1
                }
            };
        }

        public async Task<TaskItem> Save(TaskItem taskItem)
        {
            if (taskItem.Id != null)
            {
                var exist = await Load(taskItem.Id, true);
                var result = compareLogic.Compare(exist, taskItem);
                if (result.AreEqual)
                {
                    return exist!;
                }
            }
            var item = taskItem with {};

            var id = item.Id ?? Guid.NewGuid().ToString();
            dbWatcher?.AddIgnoredTask(id);
            item.Id ??= id;

            var directoryInfo = new DirectoryInfo(Path);
            var fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName, item.Id));
            var converter = new IsoDateTimeConverter
            {
                DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
                Culture = CultureInfo.InvariantCulture,
                DateTimeStyles = DateTimeStyles.None
            };
            try
            {
                using var writer = fileInfo.CreateText();
                var json = JsonConvert.SerializeObject(item, Formatting.Indented, converter);
                await writer.WriteAsync(json);
                taskItem.Id = item.Id;

                tasks.AddOrUpdate(taskItem.Id, item, (key, oldValue) => item);
                return item;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public async Task<bool> Remove(string itemId)
        {
            var directoryInfo = new DirectoryInfo(Path);
            var fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName, itemId));
            try
            {
                tasks.TryRemove(itemId, out _);
                dbWatcher?.AddIgnoredTask(itemId);
                fileInfo.Delete();
                // Tasks.Remove(itemId);
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public async Task<TaskItem?> Load(string itemId)
        {
            return await Load(itemId, false);
        }

        public async Task<TaskItem?> Load(string itemId, bool forced)
        {
            if (!forced && tasks.TryGetValue(itemId, out var value))
            {
                return value;
            }
            var jsonSerializer = new JsonSerializer();
            try
            {
                var fullPath = System.IO.Path.Combine(Path, itemId);
                var item = JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, jsonSerializer, saveRepairedSidecar: false);
                tasks.AddOrUpdate(item.Id, item, (s, oldValue) => item); 
                return item;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        protected virtual void OnUpdating(TaskStorageUpdateEventArgs e)
        {
            Load(e.Id, true);
            Updating?.Invoke(this, e);
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            var directoryInfo = new DirectoryInfo(Path);
            foreach (var fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                         .Where(info => info.Length > 0).OrderBy(info => info.CreationTime))
            {
                var task = await Load(fileInfo.Name);
                if (task != null)
                {
                    if (task.Id == null)
                    {
                        continue;
                    }

                    yield return task;
                }
                else
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch (Exception e)
                    {
                    }
                    //throw new FileLoadException($"Не удалось загрузить файл с задачей {fileInfo.FullName}");
                }
            }
        }
        
        public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            foreach (var taskItem in taskItems)
            {
                await Save(taskItem);
            }
        }
        
        public async Task<bool> Connect()
        {
            return await Task.FromResult(true);
        }

        public async Task Disconnect()
        {
        }
    }
}