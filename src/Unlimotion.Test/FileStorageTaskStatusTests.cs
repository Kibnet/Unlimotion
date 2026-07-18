using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class FileStorageTaskStatusTests
{
    [Test]
    public async Task UnarchiveCommand_PreservesNullAndFutureHistoryAndAppendsOneNormalizedEntry()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var validInProgressAt = now.AddHours(-2);
            var farFutureAt = now.AddDays(30);
            var storage = new FileStorage(tempDir, watcher: false);
            var task = new TaskItem
            {
                Id = "legacy-unarchive-history-task",
                UserId = "owner",
                Title = "Legacy unarchive history",
                Status = DomainTaskStatus.Archived,
                IsCanBeCompleted = true,
                StatusHistory =
                [
                    null!,
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.InProgress,
                        ChangedAt = validInProgressAt,
                        Author = "legacy"
                    },
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.Completed,
                        ChangedAt = farFutureAt,
                        Author = "future-corrupt"
                    },
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.Archived,
                        ChangedAt = now.AddHours(-1),
                        Author = "legacy"
                    }
                ]
            };
            await storage.Save(task);
            using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await repository.Init();

            var result = await repository.TryUnarchiveAsync(task.Id, "tester");
            var persisted = await storage.Load(task.Id, forced: true);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.AuthoritativeTask?.Status)
                    .IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted).IsNotNull();
                await Assert.That(persisted!.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted.StatusHistory.Count)
                    .IsEqualTo(task.StatusHistory.Count + 1);
                await Assert.That(persisted.StatusHistory[0]).IsNull();
                await Assert.That(persisted.StatusHistory[1].Status)
                    .IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(persisted.StatusHistory[1].ChangedAt.ToUnixTimeSeconds())
                    .IsEqualTo(validInProgressAt.ToUnixTimeSeconds());
                await Assert.That(persisted.StatusHistory[2].ChangedAt.ToUnixTimeSeconds())
                    .IsEqualTo(farFutureAt.ToUnixTimeSeconds());
                await Assert.That(persisted.StatusHistory[^1].Status)
                    .IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted.StatusHistory[^1].Author).IsEqualTo("tester");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_WritesExplicitStatusHistoryAndCompletionCriteriaWithoutLegacyFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var startedAt = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);
            var storage = new FileStorage(tempDir, watcher: false);
            var task = new TaskItem
            {
                Id = "status-storage-task",
                UserId = "owner",
                Title = "Status storage task",
                Description = "Storage contract",
                Status = DomainTaskStatus.InProgress,
                StatusHistory =
                [
                    new()
                    {
                        Status = DomainTaskStatus.NotReady,
                        ChangedAt = startedAt.AddHours(-1),
                        Author = "owner"
                    },
                    new()
                    {
                        Status = DomainTaskStatus.InProgress,
                        ChangedAt = startedAt,
                        Author = "owner"
                    }
                ],
                CompletionCriteria =
                [
                    new()
                    {
                        Id = "criterion-1",
                        Text = "Проверить результат",
                        IsSatisfied = true
                    }
                ]
            };

            await storage.Save(task);

            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, task.Id));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var loaded = await storage.Load(task.Id, forced: true);

            await Assert.That(root.GetProperty("Status").GetString()).IsEqualTo(nameof(DomainTaskStatus.InProgress));
            await Assert.That(root.GetProperty("StatusHistory").EnumerateArray().Count()).IsEqualTo(2);
            await Assert.That(root.GetProperty("CompletionCriteria").EnumerateArray().Count()).IsEqualTo(1);
            await Assert.That(root.TryGetProperty("IsCompleted", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("CompletedDateTime", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("ArchiveDateTime", out _)).IsFalse();
            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(loaded.StartedDateTime).IsEqualTo(startedAt);
            await Assert.That(loaded.CompletionCriteria.Single().Text).IsEqualTo("Проверить результат");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_MalformedTaskFileDoesNotThrowAndRaisesUpdating()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "malformed-task");
            await File.WriteAllTextAsync(filePath, "{ malformed json");
            var storage = new TestFileStorage(tempDir);
            var raised = false;
            string? observedId = null;

            storage.Updating += (_, args) =>
            {
                raised = true;
                observedId = args.Id;
            };

            await storage.TriggerUpdatingAsync("malformed-task");

            await Assert.That(raised).IsTrue();
            await Assert.That(observedId).IsEqualTo("malformed-task");
            await Assert.That(await storage.Load("malformed-task", forced: true)).IsNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_RefreshesCacheBeforeRaisingUpdating()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new TestFileStorage(tempDir);
            var task = new TaskItem { Id = "updated-task", Title = "Old title" };
            await storage.Save(task);
            _ = await storage.Load(task.Id, forced: true);
            task.Title = "New title";
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, task.Id),
                JsonConvert.SerializeObject(task));
            string? observedTitle = null;

            storage.Updating += (_, args) =>
                observedTitle = storage.Load(args.Id).GetAwaiter().GetResult()?.Title;

            await storage.TriggerUpdatingAsync(task.Id);

            await Assert.That(observedTitle).IsEqualTo("New title");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_TellsWatcherToIgnoreActualSourceFileName()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempDir, "alias.json");
            await File.WriteAllTextAsync(sourcePath, JsonConvert.SerializeObject(new TaskItem
            {
                Id = "alias",
                Title = "Original"
            }));
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            var task = (await storage.ReadDirectoryAsync()).Tasks.Single();

            await storage.Save(task);

            await Assert.That(watcher.IgnoredTaskIds).Contains("alias.json");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_SourceFileNameRaisesDomainTaskId()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "alias.json"),
                JsonConvert.SerializeObject(new TaskItem { Id = "alias", Title = "Aliased" }));
            var storage = new TestFileStorage(tempDir);
            _ = await storage.ReadDirectoryAsync();
            string? observedId = null;
            storage.Updating += (_, args) => observedId = args.Id;

            await storage.TriggerUpdatingAsync("alias.json");

            await Assert.That(observedId).IsEqualTo("alias");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "file-storage-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp artifacts.
        }
    }

    private sealed class TestFileStorage : FileStorage
    {
        public TestFileStorage(string path) : base(path, watcher: false)
        {
        }

        public TestFileStorage(string path, IDatabaseWatcher watcher) : base(path, watcher)
        {
        }

        public Task TriggerUpdatingAsync(string id) => OnUpdatingAsync(new TaskStorageUpdateEventArgs
        {
            Id = id,
            Type = UpdateType.Saved
        });
    }

    private sealed class RecordingDatabaseWatcher : IDatabaseWatcher
    {
        public List<string> IgnoredTaskIds { get; } = [];

        public event EventHandler<DbUpdatedEventArgs>? OnUpdated;

        public void AddIgnoredTask(string taskId) => IgnoredTaskIds.Add(taskId);

        public void SetEnable(bool enable)
        {
        }

        public void ForceUpdateFile(string filename, UpdateType type) => OnUpdated?.Invoke(this, new DbUpdatedEventArgs
        {
            Id = filename,
            Type = type
        });
    }
}
