using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion
{
    public class FileStorage : IStorage
    {
        public string Path { get; private set; }

        private IDatabaseWatcher? dbWatcher;
        
        public event EventHandler<TaskStorageUpdateEventArgs> Updating;

        public FileStorage(string path, bool watcher = false)
        {
            Path = path;
            if (watcher)
            {
                dbWatcher = new FileDbWatcher(path);
                dbWatcher.OnUpdated += (sender, args) => OnUpdating(new TaskStorageUpdateEventArgs()
                {
                    Id = args.Id,
                    Type = args.Type,
                });
            }
        }

        public async Task<bool> Save(TaskItem taskItem)
        {
            // while (isPause)
            // {
            //     Thread.SpinWait(1);
            // }

            var item = taskItem;

            item.Id ??= Guid.NewGuid().ToString();

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
                dbWatcher?.AddIgnoredTask(item.Id);
                using var writer = fileInfo.CreateText();
                var json = JsonConvert.SerializeObject(item, Formatting.Indented, converter);
                await writer.WriteAsync(json);
                taskItem.Id = item.Id;

                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public async Task<bool> Remove(string itemId)
        {
            var directoryInfo = new DirectoryInfo(Path);
            var fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName, itemId));
            try
            {
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
            var jsonSerializer = new JsonSerializer();
            try
            {
                var fullPath = System.IO.Path.Combine(Path, itemId);
                return JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, jsonSerializer, saveRepairedSidecar: false);
            }
            catch (Exception e)
            {
                return null;
            }
        }

        protected virtual void OnUpdating(TaskStorageUpdateEventArgs e)
        {
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
    }
}