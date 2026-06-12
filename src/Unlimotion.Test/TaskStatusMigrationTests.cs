using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class TaskStatusMigrationTests
{
    [Test]
    public async Task Init_OldActiveTask_MigratesToNotReadyAndRemovesLegacyFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            await WriteLegacyTask(tempDir, "active", "false", createdAt);

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var task = await fileStorage.Load("active", forced: true);
            var migratedJson = await ReadTaskJson(tempDir, "active");

            await Assert.That(task).IsNotNull();
            await Assert.That(task!.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(task.StatusHistory).HasSingleItem();
            await Assert.That(task.StatusHistory.Single().Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(task.StatusHistory.Single().ChangedAt).IsEqualTo(createdAt);
            await Assert.That(migratedJson.RootElement.TryGetProperty("IsCompleted", out _)).IsFalse();
            await Assert.That(migratedJson.RootElement.TryGetProperty("CompletedDateTime", out _)).IsFalse();
            await Assert.That(migratedJson.RootElement.TryGetProperty("ArchiveDateTime", out _)).IsFalse();
            await Assert.That(migratedJson.RootElement.TryGetProperty("CompletionCriteria", out _)).IsTrue();
            await Assert.That(unified.StatusModelMigrationWasApplied).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Init_OldCompletedTask_MigratesCompletedDateToStatusHistory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var completedAt = new DateTimeOffset(2026, 1, 3, 12, 30, 0, TimeSpan.Zero);
            await WriteLegacyTask(
                tempDir,
                "completed",
                "true",
                createdAt,
                completedDateTime: completedAt);

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var task = await fileStorage.Load("completed", forced: true);

            await Assert.That(task).IsNotNull();
            await Assert.That(task!.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(task.StatusHistory.Select(entry => entry.Status))
                .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Completed]);
            await Assert.That(task.CompletedDateTime).IsEqualTo(completedAt);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Init_OldArchivedTask_MigratesArchiveDateToStatusHistory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var archivedAt = new DateTimeOffset(2026, 1, 5, 18, 45, 0, TimeSpan.Zero);
            await WriteLegacyTask(
                tempDir,
                "archived",
                "null",
                createdAt,
                archiveDateTime: archivedAt);

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var task = await fileStorage.Load("archived", forced: true);

            await Assert.That(task).IsNotNull();
            await Assert.That(task!.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(task.StatusHistory.Select(entry => entry.Status))
                .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Archived]);
            await Assert.That(task.ArchiveDateTime).IsEqualTo(archivedAt);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Init_StatusTaskWithoutHistory_BackfillsStatusHistory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var updatedAt = new DateTimeOffset(2026, 1, 2, 11, 0, 0, TimeSpan.Zero);
            await WriteStatusTaskWithoutHistory(
                tempDir,
                "prepared",
                DomainTaskStatus.Prepared,
                createdAt,
                updatedAt);

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var task = await fileStorage.Load("prepared", forced: true);

            await Assert.That(task).IsNotNull();
            await Assert.That(task!.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(task.StatusHistory.Select(entry => entry.Status))
                .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Prepared]);
            await Assert.That(task.StatusHistory.Last().ChangedAt).IsEqualTo(updatedAt);
            await Assert.That(unified.StatusModelMigrationWasApplied).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Init_OldStatusFileOutsideGitWorktree_MigratesAndWritesLocalBackup()
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            await WriteLegacyTask(
                tempDir,
                "active",
                "false",
                new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            var migratedJson = await ReadTaskJson(tempDir, "active");
            var backupPath = Path.Combine(tempDir, "status-model.migration.backup");
            var backupJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(backupPath, "active")));
            var reportJson = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(tempDir, "status-model.migration.report")));

            await Assert.That(unified.StatusModelMigrationWasApplied).IsTrue();
            await Assert.That(migratedJson.RootElement.TryGetProperty("IsCompleted", out _)).IsFalse();
            await Assert.That(migratedJson.RootElement.TryGetProperty("Status", out _)).IsTrue();
            await Assert.That(backupJson.RootElement.TryGetProperty("IsCompleted", out _)).IsTrue();
            await Assert.That(reportJson.RootElement.GetProperty("GitWorkTreePath").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await Assert.That(reportJson.RootElement.GetProperty("BackupPath").GetString()).IsEqualTo(backupPath);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static async Task WriteLegacyTask(
        string tempDir,
        string id,
        string isCompletedJson,
        DateTimeOffset createdDateTime,
        DateTimeOffset? completedDateTime = null,
        DateTimeOffset? archiveDateTime = null)
    {
        var completedJson = completedDateTime.HasValue
            ? $"\"{completedDateTime:O}\""
            : "null";
        var archiveJson = archiveDateTime.HasValue
            ? $"\"{archiveDateTime:O}\""
            : "null";

        var json = $$"""
        {
          "Id": "{{id}}",
          "UserId": "migration-test",
          "Title": "{{id}}",
          "Description": "",
          "IsCompleted": {{isCompletedJson}},
          "IsCanBeCompleted": true,
          "CreatedDateTime": "{{createdDateTime:O}}",
          "UpdatedDateTime": null,
          "UnlockedDateTime": null,
          "CompletedDateTime": {{completedJson}},
          "ArchiveDateTime": {{archiveJson}},
          "PlannedBeginDateTime": null,
          "PlannedEndDateTime": null,
          "PlannedDuration": null,
          "ContainsTasks": [],
          "ParentTasks": [],
          "BlocksTasks": [],
          "BlockedByTasks": [],
          "Repeater": null,
          "Importance": 0,
          "Wanted": false,
          "Version": 1
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(tempDir, id), json);
    }

    private static async Task WriteStatusTaskWithoutHistory(
        string tempDir,
        string id,
        DomainTaskStatus status,
        DateTimeOffset createdDateTime,
        DateTimeOffset updatedDateTime)
    {
        var json = $$"""
        {
          "Id": "{{id}}",
          "UserId": "migration-test",
          "Title": "{{id}}",
          "Description": "",
          "Status": "{{status}}",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "{{createdDateTime:O}}",
          "UpdatedDateTime": "{{updatedDateTime:O}}",
          "UnlockedDateTime": null,
          "PlannedBeginDateTime": null,
          "PlannedEndDateTime": null,
          "PlannedDuration": null,
          "ContainsTasks": [],
          "ParentTasks": [],
          "BlocksTasks": [],
          "BlockedByTasks": [],
          "Repeater": null,
          "Importance": 0,
          "Wanted": false,
          "Version": 1
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(tempDir, id), json);
    }

    private static async Task<JsonDocument> ReadTaskJson(string tempDir, string id)
    {
        var rawJson = await File.ReadAllTextAsync(Path.Combine(tempDir, id));
        return JsonDocument.Parse(rawJson);
    }

    private static string CreateTempDirectory(bool createGitWorkTree = true)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "task-status-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        if (createGitWorkTree)
        {
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
        }

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
}
