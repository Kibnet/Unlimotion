using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Controls;
using DynamicData;
using DynamicData.Binding;
using ExCSS;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Splat;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;
using static System.Reflection.Metadata.BlobBuilder;
using Unlimotion.Views.Graph;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Unlimotion.Server.Domain;
using TaskItem = Unlimotion.ViewModel.TaskItem;
using LibGit2Sharp;

namespace Unlimotion
{
    public class FileTaskStorage : ITaskStorage, IStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
        public ITaskTreeManager TaskTreeManager { get; set; }

        public string Path { get; private set; }
        private IDatabaseWatcher? dbWatcher;
        public bool isPause;
        private IObservable<Func<TaskItemViewModel, bool>> rootFilter;

        public event EventHandler<TaskStorageUpdateEventArgs> Updating;
        public event EventHandler<EventArgs> Initiated;
        private IMapper mapper;

        public FileTaskStorage(string path)
        {
            Path = path;
            mapper = Locator.Current.GetService<IMapper>();
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            var directoryInfo = new DirectoryInfo(Path);

            foreach (var fileInfo in directoryInfo.EnumerateFiles())
            {
                var task = await TaskTreeManager.LoadTask(fileInfo.FullName);
                if (task != null)
                {
                    yield return mapper.Map<TaskItem>(task);
                }
                else throw new FileLoadException($"�� ������� ��������� ���� � ������� {fileInfo.FullName}");
            }
        }
        public async Task Init() 
        {
            Tasks = new(item => item.Id);

            await foreach (var task in GetAll())
            {
                var vm = new TaskItemViewModel(task, this);
                Tasks.AddOrUpdate(vm);
            }

            rootFilter = Tasks.Connect()
               .AutoRefreshOnObservable(t => t.Contains.ToObservableChangeSet())
               .TransformMany(item =>
               {
                   var many = item.Contains.Where(s => !string.IsNullOrEmpty(s)).Select(id => id);
                   return many;
               }, s => s)
               .Distinct(k => k)
               .ToCollection()
               .Select(items =>
               {
                   bool Predicate(TaskItemViewModel task) => items.Count == 0 || items.All(t => t != task.Id);
                   return (Func<TaskItemViewModel, bool>)Predicate;
               });

            dbWatcher = Locator.Current.GetService<IDatabaseWatcher>();
            Updating += TaskStorageOnUpdating;
            dbWatcher.OnUpdated += DbWatcherOnUpdated;

            OnInited();
        }        
        
        private void TaskStorageOnUpdating(object sender, TaskStorageUpdateEventArgs e)
        {
            dbWatcher?.AddIgnoredTask(e.Id);
        }

        private async void DbWatcherOnUpdated(object sender, DbUpdatedEventArgs e)
        {
            switch (e.Type)
            {
                case UpdateType.Saved:
                    var taskItem = mapper.Map<TaskItem>(await Load(e.Id));
                    if (taskItem != null)
                    {
                        var vml = Tasks.Lookup(taskItem.Id);
                        if (vml.HasValue)
                        {
                            var vm = vml.Value;
                            vm.Update(taskItem);
                        }
                        else
                        {
                            var vm = new TaskItemViewModel(taskItem, this);
                            Tasks.AddOrUpdate(vm);
                        }
                    }
                    break;
                case UpdateType.Removed:
                    var fileInfo = new FileInfo(e.Id);
                    var deletedItem = Tasks.Lookup(fileInfo.Name);
                    await Delete(deletedItem.Value, false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots()
        {
            IObservable<IChangeSet<TaskItemViewModel, string>> roots;
            roots = Tasks.Connect()
                .Filter(rootFilter);
            return roots;
        }
        protected virtual void OnInited()
        {
            Initiated?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> Save(Server.Domain.TaskItem taskItem)
        {
            while (isPause)
            {
                Thread.SpinWait(1);
            }
            var item = mapper.Map<TaskItem>(taskItem);
            
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
 
        public async Task<Server.Domain.TaskItem> Load(string itemId)
        {
            var jsonSerializer = new JsonSerializer();
            try
            {
                using var reader = File.OpenText(System.IO.Path.Combine(Path, itemId));
                using var jsonReader = new JsonTextReader(reader);
                return jsonSerializer.Deserialize<Server.Domain.TaskItem>(jsonReader);
            }
            catch (Exception e)
            {
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

        public void SetPause(bool pause)
        {
            isPause = pause;
        }

        public async Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null, bool isBlocked = false)
        {
            var taskItemList = (await TaskTreeManager.AddTask(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                mapper.Map<Server.Domain.TaskItem>(currentTask?.Model),
                isBlocked)).OrderBy(t => t.SortOrder);

            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(mapper.Map<TaskItem>(newTask));
            Tasks.AddOrUpdate(change);

            foreach (var task in taskItemList.SkipLast(1))
            {
                UpdateCache(task);
            }
            return true;
        }

        public async Task<bool> AddChild(TaskItemViewModel change, TaskItemViewModel currentTask)
        {
            var taskItemList = (await TaskTreeManager.AddChildTask(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                mapper.Map<Server.Domain.TaskItem>(currentTask.Model)))
                .OrderBy(t => t.SortOrder);

            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(mapper.Map<TaskItem>(newTask));
            Tasks.AddOrUpdate(change); 

            foreach (var task in taskItemList.SkipLast(1))
            {
                UpdateCache(task);
            }
            
            return true;
        }

        public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
        {
            var connectedItemList = await TaskTreeManager.DeleteTask(mapper.Map<Server.Domain.TaskItem>(change.Model));
            
            foreach (var task in connectedItemList)
            {
                UpdateCache(task);
            }
            Tasks.Remove(change);
            
            return true;
        }

        public async Task<bool> Update(TaskItemViewModel change)
        {
            await TaskTreeManager.UpdateTask(mapper.Map<Server.Domain.TaskItem>(change.Model));
            return true;
        }

        public async Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents)
        {
            var additionalItemParents = new List<Server.Domain.TaskItem>();
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(mapper.Map<Server.Domain.TaskItem>(newParent.Model));
            }
            var taskItemList = (await TaskTreeManager.CloneTask(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                additionalItemParents)).OrderBy(t => t.SortOrder);

            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(mapper.Map<TaskItem>(newTask));
            Tasks.AddOrUpdate(change);
            
            foreach (var task in taskItemList.SkipLast(1))
            {
                UpdateCache(task);
            }
            
            return change;
        }

        public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents)
        {
            var additionalItemParents = new List<Server.Domain.TaskItem>();
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(mapper.Map<Server.Domain.TaskItem>(newParent.Model));
            }

            var taskItemList = await TaskTreeManager.AddNewParentToTask(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                mapper.Map<Server.Domain.TaskItem>(additionalParents[0].Model));

            taskItemList.ForEach(item => UpdateCache(item));

            return true;
        }

        public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask)
        {
            var taskItemList = await TaskTreeManager.MoveTaskToNewParent(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                mapper.Map<Server.Domain.TaskItem>(additionalParents[0].Model),
                mapper.Map<Server.Domain.TaskItem>(currentTask.Model));
            
            taskItemList.ForEach(item => UpdateCache(item));
            
            return true;
        }

        public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
        {
            var taskItemList = await TaskTreeManager.UnblockTask(
                mapper.Map<Server.Domain.TaskItem>(taskToUnblock.Model),
                mapper.Map<Server.Domain.TaskItem>(blockingTask.Model));          

            taskItemList.ForEach(item => UpdateCache(item));
            
            return true;
        }

        public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
        {
            var taskItemList = await TaskTreeManager.BlockTask(
                mapper.Map<Server.Domain.TaskItem>(change.Model),
                mapper.Map<Server.Domain.TaskItem>(currentTask.Model));

            taskItemList.ForEach(item => UpdateCache(item));
            
            return true;
        }

        public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
        {
            var taskItemList = await TaskTreeManager.DeleteParentChildRelation(
                mapper.Map<Server.Domain.TaskItem>(parent.Model),
                mapper.Map<Server.Domain.TaskItem>(child.Model));

            taskItemList.ForEach(item => UpdateCache(item));
        }

        private void UpdateCache(Server.Domain.TaskItem task)
        {
            var vm = Tasks.Lookup(task.Id).Value;

            if (vm is not null)
                vm.Update(mapper.Map<TaskItem>(task));
            else
                throw new NotFoundException($"No task with id = {task.Id} is found in cache");
        }
    }
}