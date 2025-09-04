using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Unlimotion
{
    public class FileTaskStorage : ITaskStorage
    {
        public string Path { get; private set; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating;

        public FileTaskStorage(string path)
        {
            Path = path;
        }

        public IEnumerable<TaskItem> GetAll()
        {
            var directoryInfo = new DirectoryInfo(Path);
            if (!directoryInfo.Exists)
            {
                Init();
            }
            foreach (var fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly).OrderBy(info => info.CreationTime))
            {
                var task = Load(fileInfo.FullName).Result;
                if (task != null)
                {
                    yield return task;
                }
            }
        }

        private void Init()
        {
            var directoryInfo = new DirectoryInfo(Path);
            directoryInfo.Create();
        }

        public async Task<bool> Save(TaskItem item)
        {
            item.Id ??= Guid.NewGuid().ToString();

            var directoryInfo = new DirectoryInfo(Path);
            var fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName, item.Id));
            var converter = new IsoDateTimeConverter()
            {
                DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
                Culture = CultureInfo.InvariantCulture,
                DateTimeStyles = DateTimeStyles.None
            };
            try
            {
                var updateEventArgs = new TaskStorageUpdateEventArgs
                {
                    Id = fileInfo.FullName,
                    Type = UpdateType.Saved,
                };
                Updating?.Invoke(this, updateEventArgs);
                using var writer = fileInfo.CreateText();
                var json = JsonConvert.SerializeObject(item, Formatting.Indented, converter);
                await writer.WriteAsync(json);

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
                Updating?.Invoke(this, new TaskStorageUpdateEventArgs
                {
                    Id = fileInfo.FullName,
                    Type = UpdateType.Removed,
                });
                fileInfo.Delete();
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public async Task<TaskItem> Load(string itemId)
        {
            var jsonSerializer = new JsonSerializer();
            var fullPath = System.IO.Path.Combine(Path, itemId);
            try
            {
                return JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, jsonSerializer, saveRepairedSidecar: false);
            }
            catch (Exception e)
            {
                File.Delete(fullPath);
                return null;
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