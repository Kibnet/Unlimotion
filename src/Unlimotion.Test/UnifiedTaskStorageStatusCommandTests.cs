using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class UnifiedTaskStorageStatusCommandTests
{
    [Test]
    public async Task InMemoryDiagnosticRead_PreservesNullHistorySlots()
    {
        var storage = new InMemoryStorage();
        var task = CreateTask("task", DomainTaskStatus.Archived);
        task.StatusHistory = [null!];
        await storage.Save(task);

        var graph = await storage.ReadGraphAsync();

        using (Assert.Multiple())
        {
            await Assert.That(graph.Tasks[0].StatusHistory.Count).IsEqualTo(1);
            await Assert.That(graph.Tasks[0].StatusHistory[0]).IsNull();
        }
    }

    [Test]
    public async Task SameStatusNoOp_HydratesStaleCacheFromAuthoritativeSnapshot()
    {
        var storage = new InMemoryStorage();
        await storage.Save(CreateTask("task", DomainTaskStatus.Prepared));
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();
        await storage.Save(CreateTask("task", DomainTaskStatus.Completed));

        var result = await unified.TrySetStatusAsync("task", DomainTaskStatus.Completed, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.ChangedTasks).IsEmpty();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(unified.Tasks.Lookup("task").Value.Status).IsEqualTo(DomainTaskStatus.Completed);
        }
    }

    [Test]
    public async Task DeniedTransition_HydratesStaleCacheFromAuthoritativeSnapshot()
    {
        var storage = new InMemoryStorage();
        await storage.Save(CreateTask("task", DomainTaskStatus.Prepared));
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();
        await storage.Save(CreateTask("task", DomainTaskStatus.Completed));

        var result = await unified.TrySetStatusAsync("task", DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.StatusTransitionDenied);
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(unified.Tasks.Lookup("task").Value.Status).IsEqualTo(DomainTaskStatus.Completed);
        }
    }

    [Test]
    public async Task Unarchive_UsesAuthoritativeHistoryAndHydratesStaleCachedHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var cached = CreateTask("stale-unarchive-history", DomainTaskStatus.Archived);
        cached.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.InProgress,
                ChangedAt = now.AddHours(-3),
                Author = "cached"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = now.AddHours(-2),
                Author = "cached"
            }
        ];
        var storage = new InMemoryStorage();
        await storage.Save(cached);
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var authoritative = CloneTask(cached);
        authoritative.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Completed,
                ChangedAt = now.AddHours(-3),
                Author = "authoritative"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = now.AddHours(-1),
                Author = "authoritative"
            }
        ];
        await storage.Save(authoritative);

        var result = await unified.TryUnarchiveAsync(cached.Id, "tester");
        var cachedAfter = unified.Tasks.Lookup(cached.Id).Value;

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cachedAfter.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cachedAfter.Model.StatusHistory.Count)
                .IsEqualTo(authoritative.StatusHistory.Count + 1);
            await Assert.That(cachedAfter.Model.StatusHistory[^1].Status)
                .IsEqualTo(DomainTaskStatus.NotReady);
        }
    }

    [Test]
    public async Task SameStatusNoOp_RebuildsRelationsAfterAuthoritativeHydration()
    {
        var storage = new InMemoryStorage();
        var source = CreateTask("source", DomainTaskStatus.Prepared);
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared);
        await storage.Save(source);
        await storage.Save(blocked);
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var persistedSource = CloneTask(source);
        persistedSource.BlocksTasks.Add(blocked.Id);
        var persistedBlocked = CloneTask(blocked);
        persistedBlocked.BlockedByTasks.Add(source.Id);
        await storage.Save(persistedSource);
        await storage.Save(persistedBlocked);

        var result = await unified.TrySetStatusAsync(
            source.Id,
            DomainTaskStatus.Prepared,
            "tester");
        var cachedSource = unified.Tasks.Lookup(source.Id).Value;

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.ChangedTasks).IsEmpty();
            await Assert.That(cachedSource.Blocks).Contains(blocked.Id);
            await Assert.That(cachedSource.BlocksTasks.Select(static task => task.Id))
                .IsEquivalentTo([blocked.Id]);
        }
    }

    [Test]
    public async Task RepeatingCompletion_AddsCreatedCloneToCache()
    {
        var storage = new InMemoryStorage();
        var source = CreateTask("source", DomainTaskStatus.Prepared);
        source.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        source.PlannedEndDateTime = DateTimeOffset.UtcNow;
        source.Repeater = new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1
        };
        await storage.Save(source);
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var result = await unified.TrySetStatusAsync(
            source.Id,
            DomainTaskStatus.Completed,
            "tester");
        var clone = result.ChangedTasks.Single(task => task.Id != source.Id);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(unified.Tasks.Count).IsEqualTo(2);
            await Assert.That(unified.Tasks.Lookup(clone.Id).HasValue).IsTrue();
            await Assert.That(unified.Tasks.Lookup(clone.Id).Value.Status)
                .IsEqualTo(DomainTaskStatus.Prepared);
        }
    }

    [Test]
    public async Task OutcomeUnknown_ReloadsAuthoritativeTaskInsteadOfUsingCommandSnapshot()
    {
        var storage = new OutcomeUnknownStorage(
            CreateTask("task", DomainTaskStatus.Prepared),
            failAuthoritativeReload: false,
            addSelfRelationOnSave: true);
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var result = await unified.TrySetStatusAsync("task", DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(result.AuthoritativeTask).IsNull();
            await Assert.That(storage.AuthoritativeReloadCalls).IsEqualTo(1);
            await Assert.That(unified.Tasks.Lookup("task").Value.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(unified.Tasks.Lookup("task").Value.BlockedByTasks.Select(static task => task.Id))
                .IsEquivalentTo(["task"]);
        }
    }

    [Test]
    public async Task OutcomeUnknown_WhenAuthoritativeReloadFails_LeavesCacheUntouched()
    {
        var storage = new OutcomeUnknownStorage(
            CreateTask("task", DomainTaskStatus.Prepared),
            failAuthoritativeReload: true);
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var result = await unified.TrySetStatusAsync("task", DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(storage.AuthoritativeReloadCalls).IsEqualTo(1);
            await Assert.That(unified.Tasks.Lookup("task").Value.Status).IsEqualTo(DomainTaskStatus.Prepared);
        }
    }

    [Test]
    public async Task ConcurrentStatusCommands_AreSerializedThroughCommandAndHydration()
    {
        var storage = new BlockingDiagnosticStorage(CreateTask("task", DomainTaskStatus.NotReady));
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var first = unified.TrySetStatusAsync("task", DomainTaskStatus.Prepared, "first");
        await storage.FirstReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = unified.TrySetStatusAsync("task", DomainTaskStatus.InProgress, "second");
        await Task.Delay(100);

        await Assert.That(storage.ReadGraphCalls).IsEqualTo(1);

        storage.ReleaseFirstRead.TrySetResult();
        var results = await Task.WhenAll(first, second);

        using (Assert.Multiple())
        {
            await Assert.That(results.All(static result => result.Success)).IsTrue();
            await Assert.That(unified.Tasks.Lookup("task").Value.Status).IsEqualTo(DomainTaskStatus.InProgress);
        }
    }

    [Test]
    public async Task ConcurrentStatusCommands_KeepGateUntilCapturedContextHydrationCompletes()
    {
        var storage = new BlockingDiagnosticStorage(CreateTask("task", DomainTaskStatus.NotReady));
        storage.ReleaseFirstRead.TrySetResult();
        var context = new PumpSynchronizationContext();
        using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        var previousContext = SynchronizationContext.Current;
        Task init;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            init = unified.Init();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        PumpUntilCompleted(init, context);
        await init;
        var postsBeforeCommand = context.PostCount;
        var first = Task.Run(() =>
            unified.TrySetStatusAsync("task", DomainTaskStatus.Prepared, "first"));
        await WaitUntilAsync(
            () => context.PostCount > postsBeforeCommand,
            TimeSpan.FromSeconds(5));
        var readsBeforeSecondCommand = storage.ReadGraphCalls;
        var second = Task.Run(() =>
            unified.TrySetStatusAsync("task", DomainTaskStatus.InProgress, "second"));
        await Task.Delay(100);

        await Assert.That(storage.ReadGraphCalls).IsEqualTo(readsBeforeSecondCommand);

        PumpUntilCompleted(first, context);
        await first;
        PumpUntilCompleted(second, context);
        var secondResult = await second;

        using (Assert.Multiple())
        {
            await Assert.That(secondResult.Success).IsTrue();
            await Assert.That(unified.Tasks.Lookup("task").Value.Status)
                .IsEqualTo(DomainTaskStatus.InProgress);
        }
    }

    [Test]
    public async Task CacheHydration_PostsToSynchronizationContextCapturedDuringInit()
    {
        var storage = new InMemoryStorage();
        await storage.Save(CreateTask("task", DomainTaskStatus.Prepared));
        var context = new PumpSynchronizationContext();
        var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        var previousContext = SynchronizationContext.Current;
        Task init;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            init = unified.Init();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        using (unified)
        {
            PumpUntilCompleted(init, context);
            await init;
            await storage.Save(CreateTask("task", DomainTaskStatus.Completed));
            var command = Task.Run(() =>
                unified.TrySetStatusAsync("task", DomainTaskStatus.Completed, "tester"));

            while (!command.IsCompleted)
            {
                context.ExecuteOne(TimeSpan.FromMilliseconds(100));
            }

            var result = await command;
            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(context.PostCount).IsGreaterThanOrEqualTo(1);
                await Assert.That(unified.Tasks.Lookup("task").Value.Status)
                    .IsEqualTo(DomainTaskStatus.Completed);
            }
        }
    }

    [Test]
    public async Task DisposeDuringCommand_SkipsCacheHydrationWithoutBreakingCommandCompletion()
    {
        var storage = new BlockingDiagnosticStorage(CreateTask("task", DomainTaskStatus.NotReady));
        var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await unified.Init();

        var command = unified.TrySetStatusAsync("task", DomainTaskStatus.Prepared, "tester");
        await storage.FirstReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        unified.Dispose();
        storage.ReleaseFirstRead.TrySetResult();

        var result = await command;
        await Assert.That(result.Success).IsTrue();
    }

    private static TaskItem CreateTask(string id, DomainTaskStatus status) => new()
    {
        Id = id,
        Title = id,
        Status = status,
        IsCanBeCompleted = true,
        StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = status,
                ChangedAt = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
                Author = "seed"
            }
        ]
    };

    private static TaskItem CloneTask(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory.Select(entry => new TaskStatusHistoryEntry
        {
            Status = entry.Status,
            ChangedAt = entry.ChangedAt,
            Author = entry.Author,
            ExtensionData = entry.ExtensionData
        }).ToList(),
        CompletionCriteria = task.CompletionCriteria.Select(criterion => new TaskCompletionCriterion
        {
            Id = criterion.Id,
            Text = criterion.Text,
            IsSatisfied = criterion.IsSatisfied,
            ExtensionData = criterion.ExtensionData
        }).ToList(),
        ContainsTasks = task.ContainsTasks.ToList(),
        ParentTasks = task.ParentTasks.ToList(),
        BlocksTasks = task.BlocksTasks.ToList(),
        BlockedByTasks = task.BlockedByTasks.ToList()
    };

    private sealed class OutcomeUnknownStorage : IStorage, ITaskGraphDiagnosticStorage
    {
        private readonly bool failAuthoritativeReload;
        private readonly bool addSelfRelationOnSave;
        private TaskItem persisted;
        private int graphReads;
        private bool postWriteReadFailed;

        public OutcomeUnknownStorage(
            TaskItem persisted,
            bool failAuthoritativeReload,
            bool addSelfRelationOnSave = false)
        {
            this.persisted = CloneTask(persisted);
            this.failAuthoritativeReload = failAuthoritativeReload;
            this.addSelfRelationOnSave = addSelfRelationOnSave;
        }

        public int AuthoritativeReloadCalls { get; private set; }

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
            persisted = CloneTask(item);
            if (addSelfRelationOnSave)
            {
                persisted.BlocksTasks = [persisted.Id];
                persisted.BlockedByTasks = [persisted.Id];
            }

            return Task.FromResult(item);
        }

        public Task<bool> Remove(string itemId) => Task.FromResult(true);

        public Task<TaskItem?> Load(string itemId)
        {
            if (postWriteReadFailed)
            {
                AuthoritativeReloadCalls++;
                if (failAuthoritativeReload)
                {
                    return Task.FromException<TaskItem?>(new IOException("authoritative reload failed"));
                }
            }

            return Task.FromResult<TaskItem?>(CloneTask(persisted));
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            yield return CloneTask(persisted);
            await Task.CompletedTask;
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;

        public Task<TaskGraphReadResult> ReadGraphAsync()
        {
            graphReads++;
            if (graphReads > 1)
            {
                postWriteReadFailed = true;
                return Task.FromException<TaskGraphReadResult>(new IOException("post-write read failed"));
            }

            return Task.FromResult(CreateGraph(persisted));
        }
    }

    private sealed class BlockingDiagnosticStorage : IStorage, ITaskGraphDiagnosticStorage
    {
        private TaskItem persisted;
        private int readGraphCalls;

        public BlockingDiagnosticStorage(TaskItem persisted)
        {
            this.persisted = CloneTask(persisted);
        }

        public TaskCompletionSource FirstReadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadGraphCalls => Volatile.Read(ref readGraphCalls);

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
            persisted = CloneTask(item);
            return Task.FromResult(item);
        }

        public Task<bool> Remove(string itemId) => Task.FromResult(true);

        public Task<TaskItem?> Load(string itemId) =>
            Task.FromResult<TaskItem?>(CloneTask(persisted));

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            yield return CloneTask(persisted);
            await Task.CompletedTask;
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;

        public async Task<TaskGraphReadResult> ReadGraphAsync()
        {
            if (Interlocked.Increment(ref readGraphCalls) == 1)
            {
                FirstReadEntered.TrySetResult();
                await ReleaseFirstRead.Task;
            }

            return CreateGraph(persisted);
        }
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
        private int postCount;

        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref postCount);
            queue.Add((d, state));
        }

        public bool ExecuteOne(TimeSpan timeout)
        {
            if (!queue.TryTake(out var work, timeout))
            {
                return false;
            }

            work.Callback(work.State);
            return true;
        }
    }

    private static TaskGraphReadResult CreateGraph(TaskItem task)
    {
        var clone = CloneTask(task);
        return new TaskGraphReadResult(
            [clone],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [clone.Id] = $"<test:{clone.Id}>"
            },
            Array.Empty<TaskGraphLoadError>(),
            Array.Empty<TaskGraphDuplicateIdIssue>());
    }

    private static void PumpUntilCompleted(Task task, PumpSynchronizationContext context)
    {
        while (!task.IsCompleted)
        {
            context.ExecuteOne(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow - startedAt >= timeout)
            {
                throw new TimeoutException("Timed out waiting for the status-command test condition.");
            }

            await Task.Delay(10);
        }
    }
}
