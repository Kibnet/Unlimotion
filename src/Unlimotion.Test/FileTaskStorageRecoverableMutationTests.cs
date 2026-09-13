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

public sealed class FileTaskStorageRecoverableMutationTests
{
    [Test]
    public async Task UncommittedJournal_IsRolledBackBeforeNextGraphRead()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Исходный заголовок");
        await storage.Save(task);
        var taskPath = Path.Combine(temp.Path, task.Id);
        var before = await File.ReadAllBytesAsync(taskPath);

        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            var changed = await storage.Load(task.Id, forced: true) ?? throw new InvalidOperationException();
            changed.Title = "Незаподтверждённый заголовок";
            await storage.Save(changed);
            return true;
        });
        scope.Dispose(); // Имитируем завершение процесса до commit.

        var recoveredStorage = CreateStorage(temp.Path);
        var graph = await recoveredStorage.ReadGraphAsync();
        await Assert.That(graph.TasksById[task.Id].Title).IsEqualTo("Исходный заголовок");
        await Assert.That(await File.ReadAllBytesAsync(taskPath)).IsEquivalentTo(before);
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task UncommittedJournal_IsRolledBackBeforeNextDirectoryRead()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Исходный заголовок");
        await storage.Save(task);

        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            var changed = await storage.Load(task.Id, forced: true) ?? throw new InvalidOperationException();
            changed.Title = "Частично записанный заголовок";
            await storage.Save(changed);
            return true;
        });
        scope.Dispose();

        var directory = await CreateStorage(temp.Path).ReadDirectoryAsync();
        await Assert.That(directory.Tasks.Single(item => item.Id == task.Id).Title)
            .IsEqualTo("Исходный заголовок");
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task CommittedJournal_IsRolledForwardBeforeNextGraphRead()
    {
        using var temp = TempDirectory.Create();
        var storage = new CommitInterruptingStorage(Options(temp.Path));
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Исходный заголовок");
        await storage.Save(task);

        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            var changed = await storage.Load(task.Id, forced: true) ?? throw new InvalidOperationException();
            changed.Title = "Подтверждённый заголовок";
            await storage.Save(changed);
            storage.InterruptCommit = true;
            try
            {
                await scope.CommitAsync();
            }
            catch (IOException)
            {
                // Имитируем аварию после durable commit record, но до удаления журнала.
            }

            return true;
        });
        scope.Dispose();

        var recoveredStorage = CreateStorage(temp.Path);
        var graph = await recoveredStorage.ReadGraphAsync();
        await Assert.That(graph.TasksById[task.Id].Title).IsEqualTo("Подтверждённый заголовок");
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task CommitConfirmationFailure_ReturnsOutcomeUnknownAndKeepsCommittedState()
    {
        using var temp = TempDirectory.Create();
        var storage = new CommitInterruptingStorage(Options(temp.Path));
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Задача");
        await storage.Save(task);
        storage.InterruptCommit = true;

        var result = await new TaskGraphCommandService(storage)
            .TryClaimAsync(task.Id, "agent", DomainTaskStatus.Prepared);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
        var persisted = await CreateStorage(temp.Path).Load(task.Id, forced: true);
        await Assert.That(persisted!.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(persisted.AgentExecution?.AgentId).IsEqualTo("agent");
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task MultiFileCreateCommitFailure_ReturnsRequestedChildAsAuthoritativeTask()
    {
        using var temp = TempDirectory.Create();
        var storage = new CommitInterruptingStorage(Options(temp.Path));
        var parent = CreateTask(Guid.NewGuid().ToString("D"), "Родитель");
        await storage.Save(parent);
        storage.InterruptCommit = true;

        var result = await new TaskGraphCommandService(storage).TryCreateTaskAsync(
            "Дочерняя",
            "Контекст",
            [parent.Id],
            "tester");

        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
        await Assert.That(result.AuthoritativeTask).IsNotNull();
        await Assert.That(result.AuthoritativeTask!.Title).IsEqualTo("Дочерняя");
        await Assert.That(result.DeniedReason?.TaskId).IsEqualTo(result.AuthoritativeTask.Id);
        var graph = await CreateStorage(temp.Path).ReadGraphAsync();
        var child = graph.TasksById[result.AuthoritativeTask.Id];
        await Assert.That(child.ParentTasks).IsEquivalentTo([parent.Id]);
        await Assert.That(graph.TasksById[parent.Id].ContainsTasks).Contains(child.Id);
        await Assert.That(TaskGraphValidationReport.From(graph).IsValid).IsTrue();
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    [Arguments(2)] // child persisted
    [Arguments(3)] // parent relation persisted
    [Arguments(4)] // parent availability persisted
    [Arguments(5)] // child availability persisted
    public async Task CreateFailureAtEveryPersistedStage_RestoresOriginalGraph(int failAfterPersistedWrite)
    {
        using var temp = TempDirectory.Create();
        var storage = new WriteInterruptingStorage(Options(temp.Path));
        var parent = CreateTask(Guid.NewGuid().ToString("D"), "Родитель");
        await storage.Save(parent);
        var parentPath = Path.Combine(temp.Path, parent.Id);
        var before = await File.ReadAllBytesAsync(parentPath);
        storage.FailAfterPersistedWrite = failAfterPersistedWrite;

        var result = await new TaskGraphCommandService(storage).TryCreateTaskAsync(
            "Дочерняя",
            "Контекст",
            [parent.Id],
            "tester");

        await Assert.That(result.Success).IsFalse();
        var recoveredStorage = CreateStorage(temp.Path);
        var graph = await recoveredStorage.ReadGraphAsync();
        await Assert.That(graph.Tasks).Count().IsEqualTo(1);
        await Assert.That(graph.TasksById[parent.Id].ContainsTasks).IsEmpty();
        await Assert.That(result.AuthoritativeTask).IsNull();
        await Assert.That(result.ChangedTasks.All(item => graph.TasksById.ContainsKey(item.Id))).IsTrue();
        await Assert.That(await File.ReadAllBytesAsync(parentPath)).IsEquivalentTo(before);
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task CreateFailureAfterJournalBeforeTaskWrite_RestoresOriginalGraph()
    {
        using var temp = TempDirectory.Create();
        var storage = new BeforeWriteInterruptingStorage(Options(temp.Path));

        var result = await new TaskGraphCommandService(storage).TryCreateTaskAsync(
            "Дочерняя",
            "Контекст",
            [],
            "tester");

        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
        await Assert.That((await CreateStorage(temp.Path).ReadGraphAsync()).Tasks).IsEmpty();
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    [Arguments(TransactionJournalFailureStage.BeforePersist)]
    [Arguments(TransactionJournalFailureStage.AfterPersist)]
    public async Task CreateFailureAroundJournalPersistence_RestoresOriginalGraph(
        TransactionJournalFailureStage failureStage)
    {
        using var temp = TempDirectory.Create();
        var storage = new JournalInterruptingStorage(Options(temp.Path))
        {
            FailureStage = failureStage
        };

        var result = await new TaskGraphCommandService(storage).TryCreateTaskAsync(
            "Дочерняя",
            "Контекст",
            [],
            "tester");

        await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
        var graph = await CreateStorage(temp.Path).ReadGraphAsync();
        await Assert.That(graph.Tasks).IsEmpty();
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    [Test]
    public async Task RecoveryDeleteFailure_LeavesJournalUntilACompleteRetry()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Временная задача");
        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            await storage.Save(task);
            return true;
        });
        scope.Dispose();

        var interrupted = new DeleteInterruptingStorage(Options(temp.Path)) { InterruptTargetDelete = true };
        await Assert.That(async () => await interrupted.ReadDirectoryAsync()).Throws<IOException>();
        await Assert.That(PendingJournals(temp.Path)).IsNotEmpty();
        await Assert.That(File.Exists(Path.Combine(temp.Path, task.Id))).IsTrue();

        interrupted.InterruptTargetDelete = false;
        var recovered = await interrupted.ReadDirectoryAsync();
        await Assert.That(recovered.Tasks).IsEmpty();
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(temp.Path, task.Id))).IsFalse();
    }

    [Test]
    public async Task JournalDeleteFailure_IsIdempotentlyRecoveredOnRetry()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreateTask(Guid.NewGuid().ToString("D"), "Исходный заголовок");
        await storage.Save(task);
        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            var changed = await storage.Load(task.Id, forced: true) ?? throw new InvalidOperationException();
            changed.Title = "Временный заголовок";
            await storage.Save(changed);
            return true;
        });
        scope.Dispose();

        var interrupted = new DeleteInterruptingStorage(Options(temp.Path)) { InterruptJournalDelete = true };
        await Assert.That(async () => await interrupted.ReadGraphAsync()).Throws<IOException>();
        await Assert.That(PendingJournals(temp.Path)).IsNotEmpty();

        interrupted.InterruptJournalDelete = false;
        var graph = await interrupted.ReadGraphAsync();
        await Assert.That(graph.TasksById[task.Id].Title).IsEqualTo("Исходный заголовок");
        await Assert.That(PendingJournals(temp.Path)).IsEmpty();
    }

    private static FileTaskStorage CreateStorage(string path) => new(Options(path));

    private static FileTaskStorageOptions Options(string path) => new()
    {
        Path = path,
        PreserveUnknownJson = true,
        UseDirectoryLock = true
    };

    private static TaskItem CreateTask(string id, string title)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new TaskItem
        {
            Id = id,
            UserId = "tester",
            Title = title,
            Description = string.Empty,
            Status = DomainTaskStatus.Prepared,
            IsCanBeCompleted = true,
            CreatedDateTime = now,
            UpdatedDateTime = now,
            UnlockedDateTime = now,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = DomainTaskStatus.Prepared,
                    ChangedAt = now,
                    Author = "tester"
                }
            ]
        };
    }

    private static IReadOnlyList<string> PendingJournals(string path)
    {
        var directory = Path.Combine(path, ".unlimotion.transactions");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    private sealed class CommitInterruptingStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        public bool InterruptCommit { get; set; }

        protected override void OnAfterTransactionCommitted(string journalPath)
        {
            if (InterruptCommit)
            {
                throw new IOException("simulated process interruption after commit record");
            }
        }
    }

    private sealed class WriteInterruptingStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        private int _persistedWrites;
        public int? FailAfterPersistedWrite { get; set; }

        protected override void OnAfterWritePersisted(string taskId, string filePath)
        {
            _persistedWrites++;
            if (FailAfterPersistedWrite.HasValue && _persistedWrites >= FailAfterPersistedWrite.Value)
            {
                throw new IOException("simulated write interruption");
            }
        }
    }

    private sealed class BeforeWriteInterruptingStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        private bool _hasInterrupted;

        protected override void OnBeforeWrite(string taskId, string filePath)
        {
            if (!_hasInterrupted)
            {
                _hasInterrupted = true;
                throw new IOException("simulated interruption after journal and before task write");
            }
        }
    }

    public enum TransactionJournalFailureStage
    {
        BeforePersist,
        AfterPersist
    }

    private sealed class JournalInterruptingStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        private bool _hasInterrupted;
        public TransactionJournalFailureStage FailureStage { get; init; }

        protected override void OnBeforeTransactionJournalPersist(string journalPath)
        {
            if (!_hasInterrupted && FailureStage == TransactionJournalFailureStage.BeforePersist)
            {
                _hasInterrupted = true;
                throw new IOException("simulated interruption before journal persistence");
            }
        }

        protected override void OnAfterTransactionJournalPersist(string journalPath)
        {
            if (!_hasInterrupted && FailureStage == TransactionJournalFailureStage.AfterPersist)
            {
                _hasInterrupted = true;
                throw new IOException("simulated interruption after journal persistence");
            }
        }
    }

    private sealed class DeleteInterruptingStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        public bool InterruptTargetDelete { get; set; }
        public bool InterruptJournalDelete { get; set; }

        protected override void OnBeforeTransactionTargetDelete(string filePath)
        {
            if (InterruptTargetDelete)
            {
                throw new IOException("simulated task deletion failure");
            }
        }

        protected override void OnBeforeTransactionJournalDelete(string journalPath)
        {
            if (InterruptJournalDelete)
            {
                throw new IOException("simulated journal deletion failure");
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unlimotion-recoverable-mutation-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test artifacts.
            }
        }
    }
}
