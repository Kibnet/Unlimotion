using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class TaskStatusMigrationTests
{
    [Test]
    [Arguments("{\"Status\":\"Prepared\",\"StatusHistory\":[null]}", true)]
    [Arguments("{\"status\":null,\"statusHistory\":[{}]}", true)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[/*ignored*/null,],}", true)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"nested\":{\"IsCompleted\":true,\"StatusHistory\":[]}}", true)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"statusHistory\":[]}", true)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[],\"statusHistory\":[null]}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"StatusHistory\":[]}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[],\"StatusHistory\":[null]}", true)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"Status\\u0048istory\":[]}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[/*ignored*/]}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"isCompleted\":null}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"CompletedDateTime\":null}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null],\"tail\":[1,}", false)]
    [Arguments("{\"Status\":0,\"StatusHistory\":[null]} {}", false)]
    [Arguments("[{\"Status\":0,\"StatusHistory\":[null]}]", false)]
    public async Task ModernClassifier_OnlySkipsHistoricallyModernDocuments(string json, bool expectedModern)
    {
        var content = Encoding.UTF8.GetBytes(json);
        var classified = ClassifyModern(content);
        await Assert.That(classified).IsEqualTo(expectedModern);
        if (classified)
        {
            var historicalDocument = JObject.Parse(DecodeHistorical(content));
            await Assert.That(MigrateHistorical(historicalDocument)).IsFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Init_ModernBomDocumentsRemainUnchanged(bool utf16)
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            Encoding encoding = utf16 ? new UnicodeEncoding(false, true) : new UTF8Encoding(true);
            var content = encoding.GetPreamble().Concat(encoding.GetBytes(ModernTaskJson("modern"))).ToArray();
            var path = Path.Combine(tempDir, "modern");
            await File.WriteAllBytesAsync(path, content);
            using var file = new FileStorage(tempDir, watcher: false);
            using var storage = new UnifiedTaskStorage(new TaskTreeManager(file));
            await storage.Init();
            await Assert.That(storage.StatusModelMigrationWasApplied).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(content)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(tempDir, "status-model.migration.report"))).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(tempDir, "status-model.migration.backup"))).IsFalse();
            await Assert.That(storage.Tasks.Count).IsEqualTo(1);
        }
        finally { TryDeleteDirectory(tempDir); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Init_InvalidPropertyNameStillUsesLegacyMigration(bool invalidUtf8)
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            await WriteLegacyTask(tempDir, "legacy", "false", DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
            var path = Path.Combine(tempDir, "legacy");
            var original = await File.ReadAllTextAsync(path);
            var content = invalidUtf8
                ? Encoding.UTF8.GetBytes("{\"").Concat(new byte[] { 0xff })
                    .Concat(Encoding.UTF8.GetBytes("\":1," + original[1..])).ToArray()
                : Encoding.UTF8.GetBytes("{\"\\uD800\":1," + original[1..]);
            await File.WriteAllBytesAsync(path, content);
            await Assert.That(ClassifyModern(content)).IsFalse();
            using var file = new FileStorage(tempDir, watcher: false);
            using var storage = new UnifiedTaskStorage(new TaskTreeManager(file));
            await storage.Init();
            await Assert.That(storage.StatusModelMigrationWasApplied).IsTrue();
            var migrated = JObject.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(migrated.Property("IsCompleted")).IsNull();
            await Assert.That(migrated["Status"]!.Value<string>()).IsEqualTo("NotReady");
            await Assert.That(((JArray)migrated["StatusHistory"]!).Count).IsEqualTo(1);
            var backup = await File.ReadAllTextAsync(Path.Combine(tempDir, "status-model.migration.backup", "legacy"));
            await Assert.That(backup).IsEqualTo(DecodeHistorical(content));
        }
        finally { TryDeleteDirectory(tempDir); }
    }

    [Test]
    public async Task Init_DamagedModernTailIsUnchangedAndNotCountedAsProcessed()
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            await WriteLegacyTask(tempDir, "legacy", "false", DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
            var damaged = Encoding.UTF8.GetBytes("{\"Id\":\"damaged\",\"Status\":\"Prepared\",\"StatusHistory\":[null],\"tail\":[1,}");
            var path = Path.Combine(tempDir, "damaged");
            await File.WriteAllBytesAsync(path, damaged);
            using var file = new FileStorage(tempDir, watcher: false);
            using var storage = new UnifiedTaskStorage(new TaskTreeManager(file));
            await storage.Init();
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(damaged)).IsTrue();
            var report = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(tempDir, "status-model.migration.report")));
            await Assert.That(report["TasksProcessed"]!.Value<int>()).IsEqualTo(1);
            await Assert.That(report["ChangedTasks"]!.Value<int>()).IsEqualTo(1);
            await Assert.That(File.Exists(Path.Combine(tempDir, "status-model.migration.backup", "damaged"))).IsFalse();
        }
        finally { TryDeleteDirectory(tempDir); }
    }

    [Test]
    public async Task Init_MigratesNewLegacyFileAfterExistingReport()
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            using var file = new FileStorage(tempDir, watcher: false);
            using var storage = new UnifiedTaskStorage(new TaskTreeManager(file));
            await WriteLegacyTask(tempDir, "first", "false", DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
            await storage.Init();
            await WriteLegacyTask(tempDir, "second", "true", DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
            await storage.Init();
            var second = await file.Load("second", forced: true);
            await Assert.That(second!.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(second.StatusHistory.Count).IsEqualTo(2);
            await Assert.That(storage.StatusModelMigrationWasApplied).IsTrue();
            var report = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(tempDir, "status-model.migration.report")));
            await Assert.That(report["TasksProcessed"]!.Value<int>()).IsEqualTo(2);
            await Assert.That(report["ChangedTasks"]!.Value<int>()).IsEqualTo(1);
        }
        finally { TryDeleteDirectory(tempDir); }
    }

    [Test]
    [Arguments(380, false)]
    [Arguments(381, false)]
    [Arguments(380, true)]
    [Arguments(381, true)]
    public async Task ModernClassifier_RespectsHistoricalNumericLimitsAtEveryDepth(int digits, bool negative)
    {
        var number = (negative ? "-" : "") + new string('1', digits);
        foreach (var value in new[] { number, "{\"nested\":[" + number + "]}" })
        {
            var json = "{\"Status\":0,\"StatusHistory\":[null],\"unknown\":" + value + "}";
            var content = Encoding.UTF8.GetBytes(json);
            var classified = ClassifyModern(content);
            await Assert.That(classified).IsEqualTo(number.Length <= 380);
            if (classified)
            {
                await Assert.That(MigrateHistorical(JObject.Parse(DecodeHistorical(content)))).IsFalse();
            }
        }
    }

    [Test]
    public async Task Init_OverlongNestedIntegerDoesNotChangeHistoricalProcessedCount()
    {
        var tempDir = CreateTempDirectory(createGitWorkTree: false);
        try
        {
            await WriteLegacyTask(tempDir, "legacy", "false", DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
            var content = Encoding.UTF8.GetBytes("{\"Id\":\"overlong\",\"Status\":0,\"StatusHistory\":[null],\"unknown\":[" +
                new string('1', 381) + "]}");
            var path = Path.Combine(tempDir, "overlong");
            await File.WriteAllBytesAsync(path, content);
            using var file = new FileStorage(tempDir, watcher: false);
            using var storage = new UnifiedTaskStorage(new TaskTreeManager(file));
            await storage.Init();
            var report = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(tempDir, "status-model.migration.report")));
            await Assert.That(report["TasksProcessed"]!.Value<int>()).IsEqualTo(1);
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(content)).IsTrue();
        }
        finally { TryDeleteDirectory(tempDir); }
    }

    private static bool ClassifyModern(byte[] content) => typeof(UnifiedTaskStorage)
        .GetMethod("HasModernTaskStatusJson", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<byte[], bool>>()(content);

    private static bool MigrateHistorical(JObject document) => typeof(UnifiedTaskStorage)
        .GetMethod("TryMigrateTaskStatusJson", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<JObject, DateTimeOffset, bool>>()(document, DateTimeOffset.UnixEpoch);

    private static string DecodeHistorical(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ModernTaskJson(string id) => $$"""
        {"Id":"{{id}}","Title":"Modern task","Status":"NotReady",
        "StatusHistory":[{"Status":"NotReady","ChangedAt":"2026-01-01T10:00:00Z","Author":"System"}],
        "CreatedDateTime":"2026-01-01T10:00:00Z","UnlockedDateTime":"2026-01-01T10:00:00Z",
        "IsCanBeCompleted":true,"Version":1,"ContainsTasks":[],"ParentTasks":[],"BlocksTasks":[],"BlockedByTasks":[]}
        """;

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
