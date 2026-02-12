using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class UnifiedTaskStorage : ITaskStorage
{
    private readonly bool isFileStorage;

    public UnifiedTaskStorage(TaskTreeManager taskTreeManager)
    {
        TaskTreeManager = taskTreeManager;
        isFileStorage = taskTreeManager.Storage is FileStorage;
        Relations = new TaskRelationsIndex();
    }

    public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
    public ITaskRelationsIndex Relations { get; }

    public TaskTreeManager TaskTreeManager { get; }

    public event EventHandler<EventArgs> Initiated;

    public async Task Init()
    {
        Tasks = new SourceCache<TaskItemViewModel, string>(item => item.Id);
        
        // Perform migrations only for file storage
        if (isFileStorage)
        {
            var reverseLinksResult = await MigrateReverseLinks(TaskTreeManager, forceRecheck: true);
            await MigrateIsCanBeCompleted(TaskTreeManager, forceRecheck: reverseLinksResult.AnyChanges);
        }

        await foreach (var task in TaskTreeManager.Storage.GetAll())
        {
            var vm = new TaskItemViewModel(task, this);
            Tasks.AddOrUpdate(vm);
        }

        RefreshRelations();

        TaskTreeManager.Storage.Updating += TaskStorageOnUpdating;

        OnInited();
    }

    public async Task<TaskItemViewModel> Add(TaskItemViewModel currentTask = null, bool isBlocked = false)
    {
        var taskItemList = (await TaskTreeManager.AddTask(
            new TaskItem(),
            currentTask?.Model,
            isBlocked)).OrderBy(t => t.CreatedDateTime).ToList();

        var newTask = taskItemList.Last();
        var vm = new TaskItemViewModel(newTask, this);
        Tasks.AddOrUpdate(vm);

        foreach (var task in taskItemList.SkipLast(1)) UpdateCache(task);
        RefreshRelations();
        
        return vm;
    }

    public async Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask)
    {
        var taskItemList = (await TaskTreeManager.AddChildTask(
                new TaskItem(),
                currentTask.Model))
            .OrderBy(t => t.CreatedDateTime).ToList();

        var newTask = taskItemList.Last();
        var vm = new TaskItemViewModel(newTask, this);
        Tasks.AddOrUpdate(vm);

        foreach (var task in taskItemList.SkipLast(1)) UpdateCache(task);
        RefreshRelations();
        
        return vm;
    }

    public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
    {
        var connectedItemList = await TaskTreeManager.DeleteTask(change.Model, deleteInStorage);

        foreach (var task in connectedItemList) UpdateCache(task);
        Tasks.Remove(change);
        RefreshRelations();

        return true;
    }

    public async Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent)
    {
        var connItemList = await TaskTreeManager.DeleteParentChildRelation(parent.Model, change.Model);

        foreach (var task in connItemList) UpdateCache(task);
        RefreshRelations();
        return true;
    }

    public async Task<TaskItemViewModel> Update(TaskItemViewModel change)
    { 
        return await Update(change.Model);
    }

    public async Task<TaskItemViewModel> Update(TaskItem change)
    {
        var connItemList = (await TaskTreeManager.UpdateTask(change)).OrderBy(t => t.CreatedDateTime).ToList();
        
        var last = connItemList.Last();
        if (connItemList.Count>1)
        {
            var vm = new TaskItemViewModel(last, this);
            Tasks.AddOrUpdate(vm);

            foreach (var task in connItemList.SkipLast(1)) UpdateCache(task);
            RefreshRelations();
            return vm;
        }
        else
        {
            UpdateCache(last);
            RefreshRelations();
            return null;
        }
    }

    public async Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[] additionalParents)
    {
        var additionalItemParents = new List<TaskItem>();
        foreach (var newParent in additionalParents) additionalItemParents.Add(newParent.Model);

        var taskItemList = (await TaskTreeManager.CloneTask(change.Model, additionalItemParents)).OrderBy(t => t.CreatedDateTime).ToList();

        var clone = taskItemList.Last();
        var vm = new TaskItemViewModel(clone, this);
        Tasks.AddOrUpdate(vm);

        foreach (var task in taskItemList.SkipLast(1)) UpdateCache(task);
        RefreshRelations();

        return vm;
    }

    public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents)
    {
        var taskItemList = await TaskTreeManager.AddNewParentToTask(
            change.Model,
            additionalParents?.FirstOrDefault()?.Model);

        taskItemList.ForEach(UpdateCache);
        RefreshRelations();

        return true;
    }

    public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents,
        TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.MoveTaskToNewParent(
            change.Model,
            additionalParents?.FirstOrDefault()?.Model,
            currentTask?.Model);

        taskItemList.ForEach(UpdateCache);
        RefreshRelations();

        return true;
    }

    public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
    {
        var taskItemList = await TaskTreeManager.UnblockTask(
            taskToUnblock.Model,
            blockingTask.Model);

        taskItemList.ForEach(UpdateCache);
        RefreshRelations();

        return true;
    }

    public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
    {
        var taskItemList = await TaskTreeManager.BlockTask(
            change.Model,
            currentTask.Model);

        taskItemList.ForEach(UpdateCache);
        RefreshRelations();

        return true;
    }

    public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
    {
        var taskItemList = await TaskTreeManager.DeleteParentChildRelation(
            parent.Model,
            child.Model);

        taskItemList.ForEach(UpdateCache);
        RefreshRelations();
    }

    private static async Task<FileTaskMigrator.MigrationResult> MigrateReverseLinks(TaskTreeManager taskTreeManager,
        bool forceRecheck = false)
    {
        if (taskTreeManager.Storage is FileStorage fileStorage)
            return await FileTaskMigrator.Migrate(taskTreeManager.Storage.GetAll(),
                new Dictionary<string, (string getChild, string getParent)>
                {
                    { "Contain", (nameof(TaskItem.ContainsTasks), nameof(TaskItem.ParentTasks)) },
                    { "Block", (nameof(TaskItem.BlocksTasks), nameof(TaskItem.BlockedByTasks)) }
                }, taskTreeManager.Storage.Save, fileStorage.Path, forceRecheck: forceRecheck);

        return new FileTaskMigrator.MigrationResult(SkippedByReport: false, AnyChanges: false, UpdatedItems: 0);
    }

    private static async Task MigrateIsCanBeCompleted(TaskTreeManager taskTreeManager, bool forceRecheck = false)
    {
        if (taskTreeManager.Storage is not FileStorage fileStorage) return;
        var migrationReportPath = Path.Combine(fileStorage.Path, "availability.migration.report");

        // Check if migration has already been run
        if (!forceRecheck && File.Exists(migrationReportPath)) return;

        var tasksToMigrate = new List<TaskItem>();
        await foreach (var task in taskTreeManager.Storage.GetAll()) tasksToMigrate.Add(task);

        // Calculate availability for all tasks
        foreach (var task in tasksToMigrate) await taskTreeManager.CalculateAndUpdateAvailability(task);

        // Create migration report
        var report = new
        {
            Version = 1,
            Timestamp = DateTimeOffset.UtcNow,
            ForceRecheck = forceRecheck,
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
                    UpdateCache(taskItem, true);
                    RefreshRelations();
                }
                break;
            case UpdateType.Removed:
                // Handle file storage ID mapping
                var taskId = isFileStorage ? new FileInfo(e.Id).Name : e.Id;
                var deletedItem = Tasks.Lookup(taskId);
                if (deletedItem.HasValue)
                    await Delete(deletedItem.Value, false);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    protected virtual void OnInited()
    {
        Initiated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCache(TaskItem task)
    {
        UpdateCache(task, false);
    }

    private void RefreshRelations()
    {
        Relations.Rebuild(Tasks.Items);
    }

    private void UpdateCache(TaskItem task, bool create)
    {
        var vm = Tasks.Lookup(task.Id);

        if (vm.HasValue)
        {
            vm.Value.Update(task);
        }
        else if(create) 
        {
            vm = new TaskItemViewModel(task, this);
            Tasks.AddOrUpdate(vm.Value);
        }
    }
}
