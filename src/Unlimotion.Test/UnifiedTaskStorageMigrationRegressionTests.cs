using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

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
}
