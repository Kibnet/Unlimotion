using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class UnifiedTaskStorageMigrationRegressionTests
{
    [Test]
    public async Task UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var fileStorage = new FileStorage(tempDir, watcher: false);
            var manager = new TaskTreeManager(fileStorage);
            var unified = new UnifiedTaskStorage(manager);

            var parent = new TaskItem
            {
                Id = "p",
                Version = 0,
                IsCompleted = false,
                ContainsTasks = new List<string> { "c" },
                ParentTasks = new List<string>()
            };
            var child = new TaskItem
            {
                Id = "c",
                Version = 0,
                IsCompleted = false,
                ParentTasks = new List<string>(),
                ContainsTasks = new List<string>()
            };

            await fileStorage.Save(parent);
            await fileStorage.Save(child);
            await SeedMigrationReports(tempDir);

            await unified.Init();

            var storedParent = await fileStorage.Load("p");
            var storedChild = await fileStorage.Load("c");

            await Assert.That(storedParent).IsNotNull();
            await Assert.That(storedChild).IsNotNull();
            await Assert.That(storedParent.ContainsTasks).Contains("c");
            await Assert.That(storedChild.ParentTasks).Contains("p");
            await Assert.That(storedParent.Version >= 1).IsTrue();
            await Assert.That(storedChild.Version >= 1).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnifiedTaskStorage_Init_ShouldRecalculateAvailability_WhenReverseLinksWereRepaired()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var fileStorage = new FileStorage(tempDir, watcher: false);
            var manager = new TaskTreeManager(fileStorage);
            var unified = new UnifiedTaskStorage(manager);

            var blocker = new TaskItem
            {
                Id = "blocker",
                Version = 0,
                IsCompleted = false,
                BlocksTasks = new List<string> { "blocked" },
                BlockedByTasks = new List<string>(),
                IsCanBeCompleted = true
            };
            var blocked = new TaskItem
            {
                Id = "blocked",
                Version = 0,
                IsCompleted = false,
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow
            };

            await fileStorage.Save(blocker);
            await fileStorage.Save(blocked);
            await SeedMigrationReports(tempDir);

            await unified.Init();

            var storedBlocked = await fileStorage.Load("blocked");

            await Assert.That(storedBlocked).IsNotNull();
            await Assert.That(storedBlocked.BlockedByTasks).Contains("blocker");
            await Assert.That(storedBlocked.IsCanBeCompleted).IsFalse();
            await Assert.That(storedBlocked.UnlockedDateTime).IsNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnifiedTaskStorage_Init_ShouldRefreshDuplicateGraph_BetweenDependentMigrations()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RawDatabaseWatcher();
            var fileStorage = new TestFileStorage(tempDir, watcher);
            var manager = new TaskTreeManager(fileStorage);
            var unified = new UnifiedTaskStorage(manager);

            await fileStorage.Save(new TaskItem
            {
                Id = "blocker", Version = 1, IsCompleted = false,
                BlocksTasks = new List<string> { "parent" }
            });
            await fileStorage.Save(new TaskItem
            {
                Id = "parent", Version = 1, IsCompleted = false,
                ContainsTasks = new List<string> { "child" },
                IsCanBeCompleted = true
            });
            await fileStorage.Save(new TaskItem
            {
                Id = "child", Version = 1, IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow
            });
            await fileStorage.Save(new TaskItem { Id = "duplicate", Version = 1 });
            File.Copy(
                Path.Combine(tempDir, "duplicate"),
                Path.Combine(tempDir, "duplicate-copy"));
            await SeedMigrationReports(tempDir);

            await unified.Init();

            var storedParent = await fileStorage.Load("parent", forced: true);
            var storedChild = await fileStorage.Load("child", forced: true);
            await Assert.That(storedParent).IsNotNull();
            await Assert.That(storedChild).IsNotNull();
            await Assert.That(storedParent!.BlockedByTasks).Contains("blocker");
            await Assert.That(storedChild!.ParentTasks).Contains("parent");
            await Assert.That(storedChild.IsCanBeCompleted).IsFalse();
            await Assert.That(storedChild.UnlockedDateTime).IsNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnifiedTaskStorage_Init_ShouldRecalculateDescendantAvailability_WhenAvailabilityReportVersionIsStale()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var fileStorage = new FileStorage(tempDir, watcher: false);
            var manager = new TaskTreeManager(fileStorage);
            var unified = new UnifiedTaskStorage(manager);

            var blocker = new TaskItem
            {
                Id = "blocker",
                Version = 1,
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent" },
                BlockedByTasks = new List<string>(),
                IsCanBeCompleted = true
            };
            var parent = new TaskItem
            {
                Id = "parent",
                Version = 1,
                IsCompleted = false,
                ContainsTasks = new List<string> { "child" },
                ParentTasks = new List<string>(),
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string> { "blocker" },
                IsCanBeCompleted = false,
                UnlockedDateTime = null
            };
            var child = new TaskItem
            {
                Id = "child",
                Version = 1,
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string> { "parent" },
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow
            };

            await fileStorage.Save(blocker);
            await fileStorage.Save(parent);
            await fileStorage.Save(child);
            await SeedMigrationReports(tempDir, availabilityVersion: 1);

            await unified.Init();

            var storedChild = await fileStorage.Load("child");
            var reportJson = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(tempDir, "availability.migration.report")));

            await Assert.That(storedChild).IsNotNull();
            await Assert.That(storedChild.IsCanBeCompleted).IsFalse();
            await Assert.That(storedChild.UnlockedDateTime).IsNull();
            await Assert.That(storedChild.BlockedByTasks).IsEmpty();
            await Assert.That(reportJson.RootElement.GetProperty("Version").GetInt32()).IsEqualTo(2);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnifiedTaskStorage_Init_ShouldSkipAvailabilityRecheck_WhenReportTimestampParsesAsDateTime()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var fileStorage = new FileStorage(tempDir, watcher: false);
            var manager = new TaskTreeManager(fileStorage);
            var unified = new UnifiedTaskStorage(manager);
            var reportTimestamp = DateTimeOffset.UtcNow.AddMinutes(5);

            var task = new TaskItem
            {
                Id = "task",
                Version = 1,
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>(),
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                IsCanBeCompleted = false,
                UnlockedDateTime = null
            };

            await fileStorage.Save(task);
            File.SetLastWriteTimeUtc(
                Path.Combine(tempDir, task.Id),
                reportTimestamp.UtcDateTime.AddMinutes(-1));

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "migration.report"),
                "{\"Version\":1,\"Timestamp\":\"2026-01-01T00:00:00Z\"}");
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "availability.migration.report"),
                $$"""{"Version":2,"Timestamp":"{{reportTimestamp:O}}","TasksProcessed":1}""");

            await unified.Init();

            var storedTask = await fileStorage.Load(task.Id, forced: true);

            await Assert.That(storedTask).IsNotNull();
            await Assert.That(storedTask!.IsCanBeCompleted).IsFalse();
            await Assert.That(storedTask.UnlockedDateTime).IsNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnifiedTaskStorage_Init_RetriesMigrationWhenRawEditArrivesBeforeSave()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RawDatabaseWatcher();
            var fileStorage = new RacingFileStorage(tempDir, watcher);
            await fileStorage.Save(new TaskItem
            {
                Id = "parent",
                Version = 0,
                Title = "Parent",
                ContainsTasks = ["child"]
            });
            await fileStorage.Save(new TaskItem
            {
                Id = "child",
                Version = 0,
                Title = "Child"
            });
            await SeedMigrationReports(tempDir);
            fileStorage.ArmExternalEdit((taskId, filePath) =>
            {
                var external = Newtonsoft.Json.JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(filePath))!;
                external.Title = "External edit preserved";
                File.WriteAllText(filePath, Newtonsoft.Json.JsonConvert.SerializeObject(external));
                watcher.EmitRaw(Path.GetFileName(filePath), UpdateType.Saved);
            });
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var parent = await fileStorage.Load("parent", forced: true);
            var child = await fileStorage.Load("child", forced: true);
            await Assert.That(fileStorage.ExternalEditCount).IsEqualTo(1);
            await Assert.That(new[] { parent?.Title, child?.Title }).Contains("External edit preserved");
            await Assert.That(parent?.Version).IsEqualTo(1);
            await Assert.That(child?.Version).IsEqualTo(1);
            await Assert.That(child?.ParentTasks).Contains("parent");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "unified-migration-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static async Task SeedMigrationReports(
        string tempDir,
        int migrationVersion = 1,
        int availabilityVersion = 1)
    {
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "migration.report"),
            $"{{\"Version\":{migrationVersion},\"Timestamp\":\"2026-01-01T00:00:00Z\"}}");
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "availability.migration.report"),
            $"{{\"Version\":{availabilityVersion},\"Timestamp\":\"2026-01-01T00:00:00Z\"}}");
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

    private sealed class TestFileStorage(string path, IDatabaseWatcher watcher) : FileStorage(path, watcher);

    private sealed class RacingFileStorage(string path, IDatabaseWatcher watcher) : FileStorage(path, watcher)
    {
        private Action<string, string>? _externalEdit;

        public int ExternalEditCount { get; private set; }

        public void ArmExternalEdit(Action<string, string> externalEdit) => _externalEdit = externalEdit;

        protected override void OnBeforeWrite(string taskId, string filePath)
        {
            var externalEdit = Interlocked.Exchange(ref _externalEdit, null);
            if (externalEdit != null)
            {
                ExternalEditCount++;
                externalEdit(taskId, filePath);
            }

            base.OnBeforeWrite(taskId, filePath);
        }
    }

    private sealed class RawDatabaseWatcher : IDatabaseWatcher, IRawDatabaseWatcher
    {
        public event EventHandler<DbUpdatedEventArgs>? OnUpdated;
        public event EventHandler<DbUpdatedEventArgs>? OnRawUpdated;
        public event EventHandler? OnInvalidated;

        public void AddIgnoredTask(string taskId) { }
        public void SetEnable(bool enable, Action? beforeStateChange = null) => beforeStateChange?.Invoke();
        public void ForceUpdateFile(string filename, UpdateType type) { }

        public void EmitRaw(string filename, UpdateType type) => OnRawUpdated?.Invoke(
            this,
            new DbUpdatedEventArgs { Id = filename, Type = type });
    }
}
