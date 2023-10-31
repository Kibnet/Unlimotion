using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.ViewModel;

namespace Unlimotion
{
    public class FileTaskStorage : ITaskStorage, IFileTaskStorage
    {
        public string Path { get; private set; }

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
            foreach (var fileInfo in directoryInfo.EnumerateFiles()) {
                var task = LoadFromFile(fileInfo.FullName);
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
            var list = new[]
            {
                new TaskItem { Title = "Task 1", Id = "1", ContainsTasks = new List<string> { "1.1", "1.2", "3" } },
                new TaskItem { Title = "Task 1.1", Id = "1.1", BlocksTasks = new List<string> { "1.2" } },
                new TaskItem { Title = "Task 1.2", Id = "1.2", BlocksTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 2", Id = "2", ContainsTasks = new List<string> { "2.1", "3" } },
                new TaskItem { Title = "Task 2.1", Id = "2.1" },
                new TaskItem { Title = "Task 3", Id = "3", ContainsTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 3.1", Id = "3.1" },
                new TaskItem { Title = "Task 4", Id = "4" },
            };
            foreach (var item in list)
            {
                Save(item);
            }
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
                fileInfo.Delete();
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }
        
        public async Task<bool> Connect()
        {
            return await Task.FromResult(true);
        }

        public async Task Disconnect()
        {
        }

        public TaskItem? LoadFromFile(string filePath) {
            var jsonSerializer = new JsonSerializer();
            using var reader = File.OpenText(filePath);
            try {
                return (TaskItem?)jsonSerializer.Deserialize(reader, typeof(TaskItem));    
            }
            catch (Exception e) {
                return null;
            }
        }
    }
}