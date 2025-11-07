using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using DynamicData;
using DynamicData.Binding;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Splat;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Unlimotion
{
    public class FileTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }

        public TaskTreeManager TaskTreeManager { get; private set; }

        private IObservable<Func<TaskItemViewModel, bool>> rootFilter;

        public event Action<Exception?>? OnConnectionError;
        public event EventHandler<EventArgs> Initiated;
        private IMapper mapper;

        public FileTaskStorage(TaskTreeManager taskTreeManager)
        {
            mapper = Locator.Current.GetService<IMapper>();
            TaskTreeManager = taskTreeManager;
        }

        public async Task Init()
        {
            Tasks = new(item => item.Id);

            if (TaskTreeManager.Storage is FileStorage fileStorage)
            {
                await FileTaskMigrator.Migrate(TaskTreeManager.Storage.GetAll(), new Dictionary<string, (string getChild, string getParent)>
                {
                    {"Contain", (nameof(TaskItem.ContainsTasks), nameof(TaskItem.ParentTasks))},
                    {"Block", (nameof(TaskItem.BlocksTasks), nameof(TaskItem.BlockedByTasks))},
                }, TaskTreeManager.Storage.Save, fileStorage.Path);
            }

            // Migrate IsCanBeCompleted for all existing tasks
            await MigrateIsCanBeCompleted();

            await foreach (var task in TaskTreeManager.Storage.GetAll())
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

            TaskTreeManager.Storage.Updating += TaskStorageOnUpdating;

            OnInited();
        }

        private async Task MigrateIsCanBeCompleted()
        {
            if (TaskTreeManager.Storage is not FileStorage fileStorage)
            {
                return;
            }
            var migrationReportPath = System.IO.Path.Combine(fileStorage.Path, "availability.migration.report");

            // Check if migration has already been run
            if (File.Exists(migrationReportPath))
            {
                return;
            }

            var tasksToMigrate = new List<TaskItem>();
            await foreach (var task in TaskTreeManager.Storage.GetAll())
            {
                tasksToMigrate.Add(task);
            }

            // Calculate availability for all tasks
            foreach (var task in tasksToMigrate)
            {
                await TaskTreeManager.CalculateAndUpdateAvailability(task);
            }

            // Create migration report
            var report = new
            {
                Version = 1,
                Timestamp = DateTimeOffset.UtcNow,
                TasksProcessed = tasksToMigrate.Count,
                Message = "IsCanBeCompleted field calculated for all tasks"
            };

            await File.WriteAllTextAsync(migrationReportPath,
                JsonConvert.SerializeObject(report, Formatting.Indented));
        }

        private async void TaskStorageOnUpdating(object sender, TaskStorageUpdateEventArgs e)
        {
            switch (e.Type)
            {
                case UpdateType.Saved:
                    var taskItem = await TaskTreeManager.Storage.Load(e.Id);
                    if (taskItem?.Id != null)
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
                    if(deletedItem.HasValue)
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

        public async Task<bool> Connect()
        {
            return await Task.FromResult(true);
        }

        public async Task Disconnect()
        {
        }

        public async Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null, bool isBlocked = false)
        {
            var taskItemList = (await TaskTreeManager.AddTask(
                change.Model,
                currentTask?.Model,
                isBlocked)).OrderBy(t => t.SortOrder);

            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(newTask);
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
                change.Model,
                currentTask.Model))
                .OrderBy(t => t.SortOrder);

            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(newTask);
            Tasks.AddOrUpdate(change);

            foreach (var task in taskItemList.SkipLast(1))
            {
                UpdateCache(task);
            }

            return true;
        }

        public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
        {
            var connectedItemList = await TaskTreeManager.DeleteTask(change.Model);

            foreach (var task in connectedItemList)
            {
                UpdateCache(task);
            }
            Tasks.Remove(change);

            return true;
        }

        public async Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent)
        {
            var connItemList = await TaskTreeManager.DeleteParentChildRelation(parent.Model, change.Model);

            foreach (var task in connItemList)
            {
                UpdateCache(task);
            }
            return true;
        }

        public async Task<bool> Update(TaskItemViewModel change)
        {
            Update(change.Model); 
            return true;
        }

        public async Task<bool> Update(TaskItem change)
        {
            var connItemList = await TaskTreeManager.UpdateTask(change);
            foreach (var task in connItemList)
            {
                UpdateCache(task);
            }
            return true;
        }

        public async Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents)
        {
            var additionalItemParents = new List<TaskItem>();
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(newParent.Model);
            }
            var taskItemList = (await TaskTreeManager.CloneTask(change.Model, additionalItemParents)).OrderBy(t => t.SortOrder).ToList();

            var clone = taskItemList.Last();
            var vm = new TaskItemViewModel(clone, this);
            Tasks.AddOrUpdate(vm);

            foreach (var task in taskItemList.SkipLast(1))
            {
                UpdateCache(task);
            }

            return vm;
        }

        public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents)
        {
            var additionalItemParents = new List<TaskItem>();
            foreach (var newParent in additionalParents)
            {
                additionalItemParents.Add(newParent.Model);
            }

            var taskItemList = await TaskTreeManager.AddNewParentToTask(
                change.Model,
                additionalParents[0].Model);

            taskItemList.ForEach(UpdateCache);

            return true;
        }

        public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask)
        {
            var taskItemList = await TaskTreeManager.MoveTaskToNewParent(
                change.Model,
                additionalParents[0].Model,
                currentTask?.Model);

            taskItemList.ForEach(UpdateCache);

            return true;
        }

        public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
        {
            var taskItemList = await TaskTreeManager.UnblockTask(
                taskToUnblock.Model,
                blockingTask.Model);

            taskItemList.ForEach(UpdateCache);

            return true;
        }

        public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
        {
            var taskItemList = await TaskTreeManager.BlockTask(
                change.Model,
                currentTask.Model);

            taskItemList.ForEach(UpdateCache);

            return true;
        }

        public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
        {
            var taskItemList = await TaskTreeManager.DeleteParentChildRelation(
                parent.Model,
                child.Model);

            taskItemList.ForEach(UpdateCache);
        }

        private void UpdateCache(TaskItem task)
        {
            var vm = Tasks.Lookup(task.Id);

            if (vm.HasValue)
                vm.Value.Update(task);
            else if (task.SortOrder != null)
            {
                vm = new TaskItemViewModel(task, this);
                Tasks.AddOrUpdate(vm.Value);
            }
            // throw new NotFoundException($"No task with id = {task.Id} is found in cache");
        }
    }
}