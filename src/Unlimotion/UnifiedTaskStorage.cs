using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class UnifiedTaskStorage : ITaskStorage
{
    private const int AvailabilityMigrationVersion = 2;
    private const int InitialLoadBatchSize = 64;
    private readonly bool isFileStorage;

    public UnifiedTaskStorage(TaskTreeManager taskTreeManager)
    {
        TaskTreeManager = taskTreeManager;
        isFileStorage = taskTreeManager.Storage is FileStorage;
        Tasks = new SourceCache<TaskItemViewModel, string>(item => item.Id);
        Relations = new TaskRelationsIndex();
    }

    public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }
    public ITaskRelationsIndex Relations { get; }

    public TaskTreeManager TaskTreeManager { get; }

    public event EventHandler<EventArgs> Initiated;

    public async Task Init()
    {
        Tasks.Edit(operations => operations.Clear());
        TaskTreeManager.Storage.Updating -= TaskStorageOnUpdating;

        var initialTaskViews = await BuildInitialTaskViewsAsync();
        var shouldYieldBetweenBatches = SynchronizationContext.Current != null;
        await AddInitialTasksToCacheAsync(initialTaskViews, shouldYieldBetweenBatches);

        if (TaskTreeManager.Storage is FileStorage initFileStorage)
        {
            initFileStorage.Watcher?.SetEnable(true);
        }

        TaskTreeManager.Storage.Updating += TaskStorageOnUpdating;

        OnInited();
    }

    private async Task<List<TaskItemViewModel>> BuildInitialTaskViewsAsync()
    {
        return await Task.Run(async () =>
        {
            if (isFileStorage && TaskTreeManager.Storage is FileStorage fileStorage)
            {
                var forceReverseLinksRecheck = ShouldForceReverseLinkRecheck(fileStorage);
                var reverseLinksResult = await MigrateReverseLinks(TaskTreeManager, forceReverseLinksRecheck);
                await MigrateIsCanBeCompleted(TaskTreeManager, forceRecheck: reverseLinksResult.AnyChanges);
            }

            var initialTasks = new List<TaskItem>();
            await foreach (var task in TaskTreeManager.Storage.GetAll())
            {
                initialTasks.Add(task);
            }

            // Initial view models subscribe immediately; keep startup hydration from saving tasks.
            var initialTaskViews = initialTasks
                .Select(task => new TaskItemViewModel(task, this, () => false))
                .ToList();

            new TaskRelationsIndex().Rebuild(initialTaskViews);
            return initialTaskViews;
        });
    }

    private async Task AddInitialTasksToCacheAsync(
        IEnumerable<TaskItemViewModel> initialTaskViews,
        bool shouldYieldBetweenBatches)
    {
        var batch = new List<TaskItemViewModel>(InitialLoadBatchSize);

        foreach (var taskView in initialTaskViews)
        {
            batch.Add(taskView);

            if (batch.Count < InitialLoadBatchSize)
            {
                continue;
            }

            Tasks.Edit(operations => operations.AddOrUpdate(batch));
            batch = new List<TaskItemViewModel>(InitialLoadBatchSize);

            // Yield only when a UI synchronization context is present, so the app stays responsive
            // without making plain test runs depend on thread-pool rescheduling between batches.
            if (shouldYieldBetweenBatches)
            {
                await Task.Yield();
            }
        }

        if (batch.Count > 0)
        {
            Tasks.Edit(operations => operations.AddOrUpdate(batch));
        }
    }

    public async Task<TaskItemViewModel> Add(TaskItemViewModel currentTask = null, bool isBlocked = false)
    {
        var createdTask = new TaskItem();
        var taskItemList = (await TaskTreeManager.AddTask(
            createdTask,
            currentTask?.Model,
            isBlocked)).ToList();

        var newTask = taskItemList.First(t => t.Id == createdTask.Id);
        var vm = new TaskItemViewModel(newTask, this);
        Tasks.AddOrUpdate(vm);

        foreach (var task in taskItemList.Where(t => t.Id != createdTask.Id)) UpdateCache(task);
        RefreshRelations();
        
        return vm;
    }

    public async Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask)
    {
        var createdTask = new TaskItem();
        var taskItemList = (await TaskTreeManager.AddChildTask(
                createdTask,
                currentTask.Model))
            .ToList();

        var newTask = taskItemList.First(t => t.Id == createdTask.Id);
        var vm = new TaskItemViewModel(newTask, this);
        Tasks.AddOrUpdate(vm);

        foreach (var task in taskItemList.Where(t => t.Id != createdTask.Id)) UpdateCache(task);
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

        if (!forceRecheck && ShouldSkipCanBeCompletedRecheck(fileStorage, migrationReportPath))
            return;

        var tasksToMigrate = new List<TaskItem>();
        await foreach (var task in taskTreeManager.Storage.GetAll()) tasksToMigrate.Add(task);

        var taskById = new Dictionary<string, TaskItem>(tasksToMigrate.Count, StringComparer.Ordinal);
        foreach (var task in tasksToMigrate)
        {
            if (task?.Id is not null)
                taskById[task.Id] = task;
        }

        var now = DateTimeOffset.UtcNow;
        var changedTasks = new List<TaskItem>();
        foreach (var task in tasksToMigrate)
        {
            if (task?.Id is null)
                continue;

            var newIsCanBeCompleted = IsCanBeCompletedForTask(task, taskById);
            var newUnlockedDateTime = task.UnlockedDateTime;

            if (newIsCanBeCompleted && task.UnlockedDateTime == null)
                newUnlockedDateTime = now;
            else if (!newIsCanBeCompleted)
                newUnlockedDateTime = null;

            if (task.IsCanBeCompleted == newIsCanBeCompleted && task.UnlockedDateTime == newUnlockedDateTime)
                continue;

            task.IsCanBeCompleted = newIsCanBeCompleted;
            task.UnlockedDateTime = newUnlockedDateTime;
            changedTasks.Add(task);
        }

        foreach (var changedTask in changedTasks)
            await taskTreeManager.Storage.Save(changedTask);

        // Create migration report
        var report = new
        {
            Version = AvailabilityMigrationVersion,
            Timestamp = DateTimeOffset.UtcNow,
            ForceRecheck = forceRecheck,
            TasksProcessed = tasksToMigrate.Count,
            ChangedTasks = changedTasks.Count,
            Message = "IsCanBeCompleted field calculated for all tasks"
        };

        await File.WriteAllTextAsync(migrationReportPath,
            JsonConvert.SerializeObject(report, Formatting.Indented));
    }

    private static bool IsCanBeCompletedForTask(TaskItem task, IReadOnlyDictionary<string, TaskItem> taskById)
    {
        return AreContainedTasksCompleted(task, taskById) &&
               !HasIncompleteBlockerInTaskOrAncestors(task, taskById, new HashSet<string>(StringComparer.Ordinal));
    }

    private static bool ShouldSkipCanBeCompletedRecheck(FileStorage fileStorage, string migrationReportPath)
    {
        if (!File.Exists(migrationReportPath))
            return false;

        var (tasksProcessed, latestTaskWriteUtc) = GetTaskFilesMetadata(fileStorage.Path);
        if (latestTaskWriteUtc is null)
            return false;

        try
        {
            var reportJson = JObject.Parse(File.ReadAllText(migrationReportPath));
            var reportVersion = reportJson["Version"]?.Value<int>() ?? 0;
            if (reportVersion < AvailabilityMigrationVersion)
                return false;

            var reportTaskCount = reportJson["TasksProcessed"]?.Value<int>() ?? -1;
            if (reportTaskCount != tasksProcessed)
                return false;

            var reportTimestamp = reportJson["Timestamp"]?.Value<DateTimeOffset?>();
            if (reportTimestamp is null)
                return false;

            return reportTimestamp.Value.UtcDateTime >= latestTaskWriteUtc.Value;
        }
        catch
        {
            return false;
        }
    }

    private static (int Count, DateTime? LatestWriteUtc) GetTaskFilesMetadata(string tasksPath)
    {
        var count = 0;
        DateTime? latestWriteUtc = null;

        foreach (var filePath in Directory.EnumerateFiles(tasksPath))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName == "migration.report" || fileName == "availability.migration.report" ||
                fileName.EndsWith(".migration.report", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(filePath);
            if (!string.IsNullOrEmpty(extension) && !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            var writeTimeUtc = File.GetLastWriteTimeUtc(filePath);
            if (latestWriteUtc is null || writeTimeUtc > latestWriteUtc.Value)
                latestWriteUtc = writeTimeUtc;
        }

        return (count, latestWriteUtc);
    }

    private static bool AreContainedTasksCompleted(
        TaskItem task,
        IReadOnlyDictionary<string, TaskItem> taskById)
    {
        if (task.ContainsTasks?.Any() != true)
            return true;

        foreach (var childId in task.ContainsTasks)
        {
            if (taskById.TryGetValue(childId, out var childTask) && childTask?.IsCompleted == false)
                return false;
        }

        return true;
    }

    private static bool HasIncompleteBlockerInTaskOrAncestors(
        TaskItem task,
        IReadOnlyDictionary<string, TaskItem> taskById,
        ISet<string> visitedTaskIds)
    {
        if (task?.Id is null || !visitedTaskIds.Add(task.Id))
            return false;

        if (HasIncompleteDirectBlocker(task, taskById))
            return true;

        if (task.ParentTasks?.Any() != true)
            return false;

        foreach (var parentId in task.ParentTasks)
        {
            if (taskById.TryGetValue(parentId, out var parentTask) &&
                parentTask != null &&
                HasIncompleteBlockerInTaskOrAncestors(parentTask, taskById, visitedTaskIds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasIncompleteDirectBlocker(
        TaskItem task,
        IReadOnlyDictionary<string, TaskItem> taskById)
    {
        if (task.BlockedByTasks?.Any() != true)
            return false;

        foreach (var blockerId in task.BlockedByTasks)
        {
            if (taskById.TryGetValue(blockerId, out var blockerTask) && blockerTask?.IsCompleted == false)
                return true;
        }

        return false;
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

    private static bool ShouldForceReverseLinkRecheck(FileStorage fileStorage)
    {
        var migrationReportPath = Path.Combine(fileStorage.Path, "migration.report");
        if (!File.Exists(migrationReportPath))
            return true;

        try
        {
            var reportJson = JObject.Parse(File.ReadAllText(migrationReportPath));
            var reportVersion = reportJson["Version"]?.Value<int>() ?? 0;
            if (reportVersion < FileTaskMigrator.Version)
                return true;

            var forceRecheck = reportJson["ForceRecheck"]?.Value<bool>() ?? false;
            if (forceRecheck)
                return true;

            var issuesToken = reportJson["Issues"];
            if (issuesToken == null)
                return true;

            if (issuesToken.Type == JTokenType.Array)
            {
                var issues = issuesToken.ToObject<string[]>() ?? Array.Empty<string>();
                return issues.Length > 0;
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}
