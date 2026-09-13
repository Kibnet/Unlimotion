using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class AgentExecutionCommandServiceTests
{
    [Test]
    public async Task RepeatedReleaseAndClaim_KeepsLastTwentyAttemptsAndSignalsTruncation()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreatePreparedTask();
        await storage.Save(task);
        var service = new TaskGraphCommandService(storage);
        var claim = await service.TryClaimAsync(task.Id, "agent-0", DomainTaskStatus.Prepared);

        for (var index = 1; index <= 22; index++)
        {
            var execution = claim.AuthoritativeTask?.AgentExecution ?? throw new InvalidOperationException();
            var release = await service.TryReleaseExecutionAsync(
                task.Id, execution.AgentId, execution.LeaseId, $"release-{index}");
            await Assert.That(release.Success).IsTrue();
            claim = await service.TryClaimAsync(task.Id, $"agent-{index}", DomainTaskStatus.Prepared);
            await Assert.That(claim.Success).IsTrue();
        }

        var finalExecution = claim.AuthoritativeTask!.AgentExecution!;
        await Assert.That(finalExecution.PreviousAttempts).Count().IsEqualTo(20);
        await Assert.That(finalExecution.AuditTruncated).IsTrue();
        await Assert.That(finalExecution.PreviousAttempts[0].AgentId).IsEqualTo("agent-2");
        await Assert.That(finalExecution.PreviousAttempts[^1].AgentId).IsEqualTo("agent-21");
    }

    [Test]
    public async Task ConcurrentReleaseAndComplete_ProducesOneAuthoritativeWinner()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var task = CreatePreparedTask();
        await storage.Save(task);
        var claim = await new TaskGraphCommandService(storage)
            .TryClaimAsync(task.Id, "agent", DomainTaskStatus.Prepared);
        var lease = claim.AuthoritativeTask!.AgentExecution!.LeaseId;

        var results = await Task.WhenAll(
            new TaskGraphCommandService(CreateStorage(temp.Path))
                .TryReleaseExecutionAsync(task.Id, "agent", lease, "release"),
            new TaskGraphCommandService(CreateStorage(temp.Path))
                .TryCompleteExecutionAsync(task.Id, "agent", lease, "complete", Array.Empty<string>()));

        await Assert.That(results.Count(static result => result.Success)).IsEqualTo(1);
        var authoritative = await CreateStorage(temp.Path).Load(task.Id, forced: true);
        await Assert.That(authoritative).IsNotNull();
        await Assert.That(authoritative!.Status is DomainTaskStatus.Prepared or DomainTaskStatus.Completed).IsTrue();
        await Assert.That(authoritative.AgentExecution!.State is AgentExecutionState.Released or AgentExecutionState.Completed)
            .IsTrue();
    }

    [Test]
    public async Task AvailabilityChange_DoesNotSilentlyDiscardActiveLease()
    {
        using var temp = TempDirectory.Create();
        var storage = CreateStorage(temp.Path);
        var blocker = CreatePreparedTask();
        blocker.Status = DomainTaskStatus.Completed;
        blocker.StatusHistory[0].Status = DomainTaskStatus.Completed;
        var worker = CreatePreparedTask();
        blocker.BlocksTasks.Add(worker.Id);
        worker.BlockedByTasks.Add(blocker.Id);
        await storage.Save(blocker);
        await storage.Save(worker);
        var service = new TaskGraphCommandService(storage);
        var claim = await service.TryClaimAsync(worker.Id, "agent", DomainTaskStatus.Prepared);
        var lease = claim.AuthoritativeTask!.AgentExecution!.LeaseId;

        var blockerChange = await service.TrySetStatusAsync(blocker.Id, DomainTaskStatus.Prepared, "tester");

        await Assert.That(blockerChange.Success).IsTrue();
        var workerAfter = await storage.Load(worker.Id, forced: true);
        await Assert.That(workerAfter!.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(workerAfter.IsCanBeCompleted).IsFalse();
        await Assert.That(workerAfter.AgentExecution?.LeaseId).IsEqualTo(lease);
        var release = await service.TryReleaseExecutionAsync(worker.Id, "agent", lease, "blocked");
        await Assert.That(release.Success).IsTrue();
    }

    private static FileTaskStorage CreateStorage(string path) => new(new FileTaskStorageOptions
    {
        Path = path,
        PreserveUnknownJson = true,
        UseDirectoryLock = true
    });

    private static TaskItem CreatePreparedTask()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new TaskItem
        {
            Id = Guid.NewGuid().ToString("D"),
            UserId = "tester",
            Title = "Задача",
            Description = string.Empty,
            Status = DomainTaskStatus.Prepared,
            IsCanBeCompleted = true,
            CreatedDateTime = now,
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

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unlimotion-agent-execution-tests",
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
