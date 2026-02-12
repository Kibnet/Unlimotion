using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Xunit;

namespace Unlimotion.Test;

public class UnifiedTaskStorageMigrationRegressionTests
{
    [Fact]
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

            Assert.NotNull(storedParent);
            Assert.NotNull(storedChild);
            Assert.Contains("c", storedParent.ContainsTasks);
            Assert.Contains("p", storedChild.ParentTasks);
            Assert.True(storedParent.Version >= 1);
            Assert.True(storedChild.Version >= 1);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
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

            Assert.NotNull(storedBlocked);
            Assert.Contains("blocker", storedBlocked.BlockedByTasks);
            Assert.False(storedBlocked.IsCanBeCompleted);
            Assert.Null(storedBlocked.UnlockedDateTime);
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

    private static async Task SeedMigrationReports(string tempDir)
    {
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "migration.report"),
            "{\"Version\":1,\"Timestamp\":\"2026-01-01T00:00:00Z\"}");
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "availability.migration.report"),
            "{\"Version\":1,\"Timestamp\":\"2026-01-01T00:00:00Z\"}");
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
