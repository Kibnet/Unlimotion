using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskGraphCommandServiceTests
{
    [Test]
    public async Task TrySetStatus_DeniedTransitionDoesNotChangeFileUpdatedTimeOrHistory()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        task.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Check outcome",
            IsSatisfied = false
        });

        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var filePath = Path.Combine(temp.DirectoryPath, task.Id);
        var beforeFile = await File.ReadAllTextAsync(filePath);
        var beforeTask = await storage.Load(task.Id, forced: true);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.Completed, "tester");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(await File.ReadAllTextAsync(filePath)).IsEqualTo(beforeFile);

        var afterTask = await storage.Load(task.Id, forced: true);
        await Assert.That(afterTask).IsNotNull();
        await Assert.That(afterTask!.UpdatedDateTime).IsEqualTo(beforeTask!.UpdatedDateTime);
        await Assert.That(afterTask.StatusHistory.Count).IsEqualTo(beforeTask.StatusHistory.Count);
        await Assert.That(afterTask.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task TrySetStatus_BlockedCompleteAndInProgressReturnStructuredDenied()
    {
        using var temp = TempTaskDirectory.Create();
        var blocker = CreateTask("blocker", DomainTaskStatus.Prepared);
        blocker.BlocksTasks.Add("blocked");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        blocked.BlockedByTasks.Add("blocker");

        var storage = CreateStorage(temp.DirectoryPath);
        await SaveTasks(storage, blocker, blocked);
        var service = new TaskGraphCommandService(storage);

        var complete = await service.TrySetStatusAsync(blocked.Id, DomainTaskStatus.Completed);
        var inProgress = await service.TrySetStatusAsync(blocked.Id, DomainTaskStatus.InProgress);

        await Assert.That(complete.Success).IsFalse();
        await Assert.That(complete.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
        await Assert.That(inProgress.Success).IsFalse();
        await Assert.That(inProgress.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
    }

    [Test]
    public async Task TrySetCriterion_CompletedTaskReturnsCompletedCriteriaImmutable()
    {
        using var temp = TempTaskDirectory.Create();
        var completed = CreateTask("completed", DomainTaskStatus.Completed);
        completed.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Done",
            IsSatisfied = false
        });

        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(completed);

        var result = await new TaskGraphCommandService(storage)
            .TrySetCriterionAsync(completed.Id, "criterion", satisfied: true);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.CompletedCriteriaImmutable);
    }

    [Test]
    public async Task TrySetCriterion_MissingTaskAndCriterionReturnStructuredDenied()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        var storage = CreateStorage(temp.DirectoryPath);
        await storage.Save(task);
        var service = new TaskGraphCommandService(storage);

        var missingTask = await service.TrySetCriterionAsync("missing", "criterion", satisfied: true);
        var missingCriterion = await service.TrySetCriterionAsync(task.Id, "missing", satisfied: true);

        await Assert.That(missingTask.Success).IsFalse();
        await Assert.That(missingTask.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.TaskNotFound);
        await Assert.That(missingCriterion.Success).IsFalse();
        await Assert.That(missingCriterion.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.CriterionNotFound);
    }

    [Test]
    public async Task TrySetStatus_DuplicateIdsReturnValidationFailureWithFilePaths()
    {
        using var temp = TempTaskDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "duplicate-a"), """
        {
          "Id": "duplicate",
          "Title": "Duplicate A",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(temp.DirectoryPath, "duplicate-b"), """
        {
          "Id": "duplicate",
          "Title": "Duplicate B",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);

        var result = await new TaskGraphCommandService(CreateStorage(temp.DirectoryPath))
            .TrySetStatusAsync("duplicate", DomainTaskStatus.Completed);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate-a", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate-b", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TrySetStatus_NonDiagnosticStorageReturnsStorageFailedWithoutSaving()
    {
        var storage = new CountingStorage();

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("task", DomainTaskStatus.Completed);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StorageFailed);
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_PostMutationDiagnosticFailureReturnsStorageFailed()
    {
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        var storage = new DiagnosticStorage([task])
        {
            ThrowOnReadAfterCount = 1
        };

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(task.Id, DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.StorageFailed);
        await Assert.That(storage.SaveCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrySetStatus_DiagnosticStorageDuplicateTasksWithoutDuplicateIssuesBlocksWrite()
    {
        var first = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "first");
        var second = CreateTask("duplicate", DomainTaskStatus.Prepared, title: "second");
        var storage = new DiagnosticStorage([first, second]);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("duplicate", DomainTaskStatus.InProgress);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
        await Assert.That(result.DeniedReason?.Message.Contains("duplicate id: duplicate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(storage.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrySetStatus_RepeatingCompletionReturnsOriginalCloneAndReverseLinkTasks()
    {
        using var temp = TempTaskDirectory.Create();
        var plannedBegin = DateTimeOffset.UtcNow.AddDays(-1);
        var plannedEnd = DateTimeOffset.UtcNow;

        var child = CreateTask("child", DomainTaskStatus.Completed);
        child.ParentTasks.Add("source");
        var blocker = CreateTask("blocker", DomainTaskStatus.Completed);
        blocker.BlocksTasks.Add("source");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        blocked.BlockedByTasks.Add("source");
        var source = CreateTask("source", DomainTaskStatus.Prepared, title: "Repeating source");
        source.ContainsTasks.Add(child.Id);
        source.BlockedByTasks.Add(blocker.Id);
        source.BlocksTasks.Add(blocked.Id);
        source.Repeater = new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1
        };
        source.PlannedBeginDateTime = plannedBegin;
        source.PlannedEndDateTime = plannedEnd;

        var storage = CreateStorage(temp.DirectoryPath);
        await SaveTasks(storage, child, blocker, blocked, source);

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(source.Id, DomainTaskStatus.Completed, "tester");

        await Assert.That(result.Success).IsTrue();
        var changedIds = result.ChangedTasks.Select(static task => task.Id).ToHashSet(StringComparer.Ordinal);
        var allTasks = await LoadAllTasks(storage);
        var clone = allTasks.Single(task => task.Id != source.Id && task.Title == source.Title);
        await Assert.That(changedIds).Contains(source.Id);
        await Assert.That(changedIds).Contains(clone.Id);
        await Assert.That(changedIds).Contains(child.Id);
        await Assert.That(changedIds).Contains(blocker.Id);
        await Assert.That(changedIds).Contains(blocked.Id);

        var childAfter = await storage.Load(child.Id, forced: true);
        var blockerAfter = await storage.Load(blocker.Id, forced: true);
        var blockedAfter = await storage.Load(blocked.Id, forced: true);
        await Assert.That(childAfter!.ParentTasks).Contains(clone.Id);
        await Assert.That(blockerAfter!.BlocksTasks).Contains(clone.Id);
        await Assert.That(blockedAfter!.BlockedByTasks).Contains(clone.Id);
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted = true,
        string? title = null)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        return new TaskItem
        {
            Id = id,
            UserId = "test-user",
            Title = title ?? id,
            Description = string.Empty,
            Status = status,
            IsCanBeCompleted = isCanBeCompleted,
            CreatedDateTime = createdAt,
            UnlockedDateTime = isCanBeCompleted ? createdAt : null,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = status,
                    ChangedAt = createdAt,
                    Author = "seed"
                }
            ]
        };
    }

    private static FileTaskStorage CreateStorage(string directory) => new(new FileTaskStorageOptions
    {
        Path = directory,
        PreserveUnknownJson = true,
        UseDirectoryLock = true
    });

    private static async Task SaveTasks(FileTaskStorage storage, params TaskItem[] tasks)
    {
        foreach (var task in tasks)
        {
            await storage.Save(task);
        }
    }

    private static async Task<IReadOnlyList<TaskItem>> LoadAllTasks(FileTaskStorage storage)
    {
        var tasks = new List<TaskItem>();
        await foreach (var task in storage.GetAll())
        {
            tasks.Add(task);
        }

        return tasks;
    }

    private sealed class CountingStorage : IStorage
    {
        public int SaveCount { get; private set; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            SaveCount++;
            return Task.FromResult(item);
        }

        public Task<bool> Remove(string itemId) => Task.FromResult(true);

        public Task<TaskItem?> Load(string itemId) => Task.FromResult<TaskItem?>(null);

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;
    }

    private sealed class DiagnosticStorage : IStorage, ITaskGraphDiagnosticStorage
    {
        private readonly List<TaskItem> _tasks;
        private int _readCount;

        public DiagnosticStorage(IEnumerable<TaskItem> tasks)
        {
            _tasks = tasks.Select(CloneTask).ToList();
        }

        public int SaveCount { get; private set; }
        public int? ThrowOnReadAfterCount { get; init; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskGraphReadResult> ReadGraphAsync()
        {
            if (ThrowOnReadAfterCount.HasValue && _readCount >= ThrowOnReadAfterCount.Value)
            {
                throw new TimeoutException("diagnostic read timed out");
            }

            _readCount++;
            return Task.FromResult(new TaskGraphReadResult(
                _tasks.Select(CloneTask).ToArray(),
                _tasks
                    .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
                    .GroupBy(static task => task.Id, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => $"memory:{group.Key}", StringComparer.Ordinal),
                Array.Empty<TaskGraphLoadError>(),
                Array.Empty<TaskGraphDuplicateIdIssue>()));
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            SaveCount++;
            var clone = CloneTask(item);
            var index = _tasks.FindIndex(task => string.Equals(task.Id, item.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _tasks[index] = clone;
            }
            else
            {
                _tasks.Add(clone);
            }

            return Task.FromResult(CloneTask(clone));
        }

        public Task<bool> Remove(string itemId)
        {
            _tasks.RemoveAll(task => string.Equals(task.Id, itemId, StringComparison.Ordinal));
            return Task.FromResult(true);
        }

        public Task<TaskItem?> Load(string itemId)
        {
            var task = _tasks.LastOrDefault(task => string.Equals(task.Id, itemId, StringComparison.Ordinal));
            return Task.FromResult(task == null ? null : CloneTask(task));
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            foreach (var task in _tasks)
            {
                yield return CloneTask(task);
            }

            await Task.CompletedTask;
        }

        public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            foreach (var taskItem in taskItems)
            {
                await Save(taskItem);
            }
        }

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;
    }

    private static TaskItem CloneTask(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory?
            .Select(entry => new TaskStatusHistoryEntry
            {
                Status = entry.Status,
                ChangedAt = entry.ChangedAt,
                Author = entry.Author,
                ExtensionData = entry.ExtensionData
            })
            .ToList() ?? new List<TaskStatusHistoryEntry>(),
        CompletionCriteria = task.CompletionCriteria?
            .Select(criterion => new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied,
                ExtensionData = criterion.ExtensionData
            })
            .ToList() ?? new List<TaskCompletionCriterion>(),
        ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
        ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
        BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
        BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>()
    };

    private sealed class TempTaskDirectory : IDisposable
    {
        private TempTaskDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TempTaskDirectory Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "unlimotion-command-service-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempTaskDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
