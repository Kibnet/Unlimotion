using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.ViewModel;

namespace Unlimotion
{
    public class FileTaskStorage : ITaskStorage
    {
        public string Path { get; private set; }
        public string ComputedTasksInfoPath { get; private set; }

        public FileTaskStorage(string path)
        {
            Path = path;
            ComputedTasksInfoPath = path + "/ComputedInfo";
        }

        public IEnumerable<TaskItem> GetAll()
        {
            var directoryInfo = new DirectoryInfo(Path);
            if (!directoryInfo.Exists)
            {
                Init();
            }

            return directoryInfo.EnumerateFiles()
                .Select(ReadItem<TaskItem>)
                .Where(item => item != null)!;
        }
        
        public IEnumerable<ComputedTaskInfo> GetTasksComputedInfo()
        {
            var directoryInfo = new DirectoryInfo(ComputedTasksInfoPath);

            if (!directoryInfo.Exists)
                directoryInfo.Create();

            return directoryInfo.EnumerateFiles()
                .Select(ReadItem<ComputedTaskInfo>)
                .Where(item => item != null)!;
        }

        private static T? ReadItem<T>(FileInfo fileInfo) where T : class
        {
            string json;
            using (var reader = fileInfo.OpenText())
            {
                json = reader.ReadToEnd();
            }

            T? task;
            try
            {
                task = JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                task = null;
            }

            return task;
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
            return await SaveItem(Path, item.Id, item);
        }

        public async Task<bool> SaveComputedTaskInfo(ComputedTaskInfo taskInfo)
        {
            return await SaveItem(ComputedTasksInfoPath, taskInfo.TaskId, taskInfo);
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

        private static async Task<bool> SaveItem(string path, string name, object objForSerialization)
        {
            var directoryInfo = new DirectoryInfo(path);
            var fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName, name));
            var converter = new IsoDateTimeConverter
            {
                DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
                Culture = CultureInfo.InvariantCulture,
                DateTimeStyles = DateTimeStyles.None
            };
            try
            {
                using var writer = fileInfo.CreateText();
                var json = JsonConvert.SerializeObject(objForSerialization, Formatting.Indented, converter);
                await writer.WriteAsync(json);

                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }
    }
}