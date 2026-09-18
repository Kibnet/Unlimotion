using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskTreeManagerSafetyTests
{
    [Test]
    public async Task UpdateTask_TerminalTasksCannotTransitionBackToInProgress()
    {
        foreach (var terminalStatus in new[] { DomainTaskStatus.Completed, DomainTaskStatus.Archived })
        {
            var storage = new InMemoryStorage();
            var existing = CreateTask($"terminal-{terminalStatus}", terminalStatus);
            await storage.Save(existing);
            var change = CreateTask(existing.Id, DomainTaskStatus.InProgress);

            await new TaskTreeManager(storage).UpdateTask(change);

            var saved = await storage.Load(existing.Id);
            await Assert.That(saved).IsNotNull();
            await Assert.That(saved!.Status).IsEqualTo(terminalStatus);
        }
    }

    [Test]
    public async Task UpdateTask_PartialRepeaterFailureIsNotRetried()
    {
        var storage = new FailOnceAfterRepeaterCloneStorage("source");
        var source = CreateTask("source", DomainTaskStatus.Prepared);
        source.Title = "Repeating source";
        source.Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Period = 1, Pattern = [] };
        source.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        await storage.SeedAsync(source);
        var change = CreateTask(source.Id, DomainTaskStatus.Completed);
        change.Title = source.Title;
        change.Repeater = source.Repeater;
        change.PlannedBeginDateTime = source.PlannedBeginDateTime;

        Func<Task<List<TaskItem>?>> updateTask = async () =>
            await new TaskTreeManager(storage).UpdateTask(change);
        await Assert.That(updateTask).Throws<TimeoutException>();

        var tasks = await storage.ReadAllAsync();
        await Assert.That(tasks.Count(task => task.Id != source.Id && task.Title == source.Title)).IsEqualTo(1);
    }

    [Test]
    public async Task UpdateTask_HoldsGraphWriteLockForWholeMutation()
    {
        var storage = new LockTrackingStorage();
        var existing = CreateTask("locked-update", DomainTaskStatus.Prepared);
        await storage.SeedAsync(existing);
        var change = CreateTask(existing.Id, DomainTaskStatus.Prepared);
        change.Title = "Updated";

        await new TaskTreeManager(storage).UpdateTask(change);

        await Assert.That(storage.LockCallCount).IsEqualTo(1);
        await Assert.That(storage.SaveOutsideLock).IsFalse();
    }

    [Test]
    public async Task UpdateTask_SameStatusTitleChangePreservesGenericUpdateSemantics()
    {
        var storage = new InMemoryStorage();
        var existing = CreateTask("same-status-title", DomainTaskStatus.Prepared);
        existing.Title = "Before";
        await storage.Save(existing);
        var change = CreateTask(existing.Id, DomainTaskStatus.Prepared);
        change.Title = "After";

        await new TaskTreeManager(storage).UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Title).IsEqualTo("After");
        await Assert.That(saved.Status).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task UpdateTask_DeniedStatusWithTitleChangeRestoresUndefinedPersistedSource()
    {
        var persistedStatus = (DomainTaskStatus)int.MaxValue;
        var requestedStatus = (DomainTaskStatus)(int.MaxValue - 1);
        var storage = new InMemoryStorage();
        var existing = CreateTask("undefined-source-mixed", persistedStatus);
        existing.Title = "Before";
        await storage.Save(existing);
        var change = CreateTask(existing.Id, requestedStatus);
        change.Title = "After";

        await new TaskTreeManager(storage).UpdateTask(change);

        var saved = await storage.Load(existing.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Title).IsEqualTo("After");
        await Assert.That(saved.Status).IsEqualTo(persistedStatus);
    }

    [Test]
    public async Task UpdateTask_AutomaticUnavailableRollbackIsIdempotent()
    {
        var storage = new InMemoryStorage();
        var manager = new TaskTreeManager(storage);
        var task = CreateTask("rollback-idempotency", DomainTaskStatus.InProgress);
        task.IsCanBeCompleted = false;
        task.EnsureStatusHistory();
        await storage.Save(task);

        task.Title = "first update";
        await manager.UpdateTask(task);
        var afterFirstUpdate = await storage.Load(task.Id);
        await Assert.That(afterFirstUpdate).IsNotNull();

        afterFirstUpdate!.Title = "second update";
        await manager.UpdateTask(afterFirstUpdate);
        var afterSecondUpdate = await storage.Load(task.Id);

        await Assert.That(afterSecondUpdate).IsNotNull();
        await Assert.That(afterSecondUpdate!.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(afterSecondUpdate.StatusHistory.Count(entry =>
                entry is not null &&
                entry.Status == DomainTaskStatus.Prepared &&
                string.Equals(entry.Author, "System", StringComparison.Ordinal)))
            .IsEqualTo(1);
    }

    [Test]
    public async Task CloneTask_PreservesGoalClassificationAndAreas()
    {
        var storage = new InMemoryStorage();
        var source = CreateTask("classified-source", DomainTaskStatus.Prepared);
        source.IsGoal = true;
        source.AreaIds = ["work", "personal"];
        await storage.Save(source);
        var manager = new TaskTreeManager(storage);

        var result = await manager.CloneTask(source, []);
        var clone = result.Single(task => task.Id != source.Id);
        var persisted = await storage.Load(clone.Id);

        await Assert.That(clone.IsGoal).IsTrue();
        await Assert.That(clone.AreaIds).IsEquivalentTo(source.AreaIds);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted!.IsGoal).IsTrue();
        await Assert.That(persisted.AreaIds).IsEquivalentTo(source.AreaIds);
    }

    private static TaskItem CreateTask(string id, DomainTaskStatus status) => new()
    {
        Id = id,
        UserId = "test-user",
        Title = id,
        Status = status,
        IsCanBeCompleted = true,
        ContainsTasks = [],
        ParentTasks = [],
        BlocksTasks = [],
        BlockedByTasks = [],
        CompletionCriteria = []
    };

    private sealed class FailOnceAfterRepeaterCloneStorage(string sourceId) : IStorage
    {
        private readonly InMemoryStorage _inner = new();
        private bool _hasFailed;

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

        public Task SeedAsync(TaskItem task) => _inner.Save(task);

        public async Task<IReadOnlyList<TaskItem>> ReadAllAsync()
        {
            var result = new List<TaskItem>();
            await foreach (var task in _inner.GetAll())
            {
                result.Add(task);
            }

            return result;
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            if (!_hasFailed && item.Id == sourceId && item.Status == DomainTaskStatus.Completed)
            {
                _hasFailed = true;
                throw new IOException("simulated source save failure");
            }

            return _inner.Save(item);
        }

        public Task<bool> Remove(string itemId) => _inner.Remove(itemId);

        public Task<TaskItem?> Load(string itemId) => _inner.Load(itemId);

        public IAsyncEnumerable<TaskItem> GetAll() => _inner.GetAll();

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => _inner.BulkInsert(taskItems);

        public Task<bool> Connect() => _inner.Connect();

        public Task Disconnect() => _inner.Disconnect();
    }

    private sealed class LockTrackingStorage : IStorage, ITaskGraphWriteLock
    {
        private readonly AsyncLocal<int> _lockDepth = new();
        private readonly InMemoryStorage _inner = new();

        public int LockCallCount { get; private set; }
        public bool SaveOutsideLock { get; private set; }

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

        public Task SeedAsync(TaskItem task) => _inner.Save(task);

        public async Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation)
        {
            LockCallCount++;
            _lockDepth.Value++;
            try
            {
                return await operation();
            }
            finally
            {
                _lockDepth.Value--;
            }
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            SaveOutsideLock |= _lockDepth.Value == 0;
            return _inner.Save(item);
        }

        public Task<bool> Remove(string itemId) => _inner.Remove(itemId);

        public Task<TaskItem?> Load(string itemId) => _inner.Load(itemId);

        public IAsyncEnumerable<TaskItem> GetAll() => _inner.GetAll();

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => _inner.BulkInsert(taskItems);

        public Task<bool> Connect() => _inner.Connect();

        public Task Disconnect() => _inner.Disconnect();
    }
}
