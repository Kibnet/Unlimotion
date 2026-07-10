using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test.StormBdd;

internal sealed record TaskStatusMigrationCaseResult(
    DomainTaskStatus Status,
    IReadOnlyList<DomainTaskStatus> StatusHistory,
    DateTimeOffset? CompletedDateTime,
    DateTimeOffset? ArchiveDateTime,
    DateTimeOffset? LastHistoryChangedAt,
    bool LegacyFieldsRemoved,
    bool StatusPersisted,
    bool StatusHistoryPersisted,
    bool CompletionCriteriaPersisted);

internal sealed record TaskStatusMigrationScenarioResult(
    bool StatusModelMigrationWasApplied,
    DateTimeOffset ActiveCreatedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset ArchivedAt,
    DateTimeOffset PreparedUpdatedAt,
    TaskStatusMigrationCaseResult ActiveLegacyTask,
    TaskStatusMigrationCaseResult CompletedLegacyTask,
    TaskStatusMigrationCaseResult ArchivedLegacyTask,
    TaskStatusMigrationCaseResult StatusWithoutHistoryTask);

internal static class TaskStatusMigrationStepDefinitions
{
    private const string ScenarioId = "SC-0002-003";

    public static IReadOnlyList<StormStepDefinition> Create()
    {
        var supportsScenarios = new HashSet<string>(StringComparer.Ordinal) { ScenarioId };

        return
        [
            new StormStepDefinition(
                "SD-0059",
                "Дано",
                "у пользователя открыт актуальный набор задач Unlimotion",
                supportsScenarios,
                context =>
                {
                    context.TaskStatusMigrationTaskSetAvailable = true;
                    return Task.CompletedTask;
                }),
            new StormStepDefinition(
                "SD-0060",
                "И",
                "поведение относится к истории ST-0002",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusMigrationTaskSetAvailable).IsTrue();
                    context.TaskStatusMigrationStoryBehaviorConfirmed = true;
                }),
            new StormStepDefinition(
                "SD-0061",
                "Когда",
                "пользователь меняет статус задачи или проверяет доступные переходы",
                supportsScenarios,
                async context =>
                {
                    await Assert.That(context.TaskStatusMigrationTaskSetAvailable).IsTrue();
                    await Assert.That(context.TaskStatusMigrationStoryBehaviorConfirmed).IsTrue();

                    context.TaskStatusMigrationResult = await VerifyStatusMigrationAsync();
                }),
            new StormStepDefinition(
                "SD-0062",
                "Тогда",
                "История статусов и legacy-поля мигрируются без потери смысла.",
                supportsScenarios,
                async context =>
                {
                    var result = context.TaskStatusMigrationResult;

                    await Assert.That(result).IsNotNull();
                    await Assert.That(result!.StatusModelMigrationWasApplied).IsTrue();

                    await AssertCase(
                        result.ActiveLegacyTask,
                        DomainTaskStatus.NotReady,
                        [DomainTaskStatus.NotReady],
                        completedDateTime: null,
                        archiveDateTime: null,
                        expectedLastHistoryChangedAt: result.ActiveCreatedAt);
                    await AssertCase(
                        result.CompletedLegacyTask,
                        DomainTaskStatus.Completed,
                        [DomainTaskStatus.NotReady, DomainTaskStatus.Completed],
                        result.CompletedAt,
                        archiveDateTime: null,
                        expectedLastHistoryChangedAt: result.CompletedAt);
                    await AssertCase(
                        result.ArchivedLegacyTask,
                        DomainTaskStatus.Archived,
                        [DomainTaskStatus.NotReady, DomainTaskStatus.Archived],
                        completedDateTime: null,
                        result.ArchivedAt,
                        expectedLastHistoryChangedAt: result.ArchivedAt);
                    await AssertCase(
                        result.StatusWithoutHistoryTask,
                        DomainTaskStatus.Prepared,
                        [DomainTaskStatus.NotReady, DomainTaskStatus.Prepared],
                        completedDateTime: null,
                        archiveDateTime: null,
                        expectedLastHistoryChangedAt: result.PreparedUpdatedAt);
                })
        ];
    }

    private static async Task<TaskStatusMigrationScenarioResult> VerifyStatusMigrationAsync()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var activeCreatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var completedCreatedAt = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
            var completedAt = new DateTimeOffset(2026, 1, 3, 12, 30, 0, TimeSpan.Zero);
            var archivedCreatedAt = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero);
            var archivedAt = new DateTimeOffset(2026, 1, 5, 18, 45, 0, TimeSpan.Zero);
            var preparedCreatedAt = new DateTimeOffset(2026, 1, 6, 10, 0, 0, TimeSpan.Zero);
            var preparedUpdatedAt = new DateTimeOffset(2026, 1, 7, 11, 0, 0, TimeSpan.Zero);

            await WriteLegacyTask(tempDir, "active", "false", activeCreatedAt);
            await WriteLegacyTask(
                tempDir,
                "completed",
                "true",
                completedCreatedAt,
                completedDateTime: completedAt);
            await WriteLegacyTask(
                tempDir,
                "archived",
                "null",
                archivedCreatedAt,
                archiveDateTime: archivedAt);
            await WriteStatusTaskWithoutHistory(
                tempDir,
                "prepared",
                DomainTaskStatus.Prepared,
                preparedCreatedAt,
                preparedUpdatedAt);

            var fileStorage = new FileStorage(tempDir, watcher: false);
            var unified = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));

            await unified.Init();

            return new TaskStatusMigrationScenarioResult(
                unified.StatusModelMigrationWasApplied,
                activeCreatedAt,
                completedAt,
                archivedAt,
                preparedUpdatedAt,
                await ReadCase(tempDir, fileStorage, "active"),
                await ReadCase(tempDir, fileStorage, "completed"),
                await ReadCase(tempDir, fileStorage, "archived"),
                await ReadCase(tempDir, fileStorage, "prepared"));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static async Task AssertCase(
        TaskStatusMigrationCaseResult actual,
        DomainTaskStatus expectedStatus,
        IReadOnlyList<DomainTaskStatus> expectedHistory,
        DateTimeOffset? completedDateTime,
        DateTimeOffset? archiveDateTime,
        DateTimeOffset? expectedLastHistoryChangedAt)
    {
        await Assert.That(actual.Status).IsEqualTo(expectedStatus);
        await Assert.That(CreateStatusKey(actual.StatusHistory)).IsEqualTo(CreateStatusKey(expectedHistory));
        await Assert.That(actual.CompletedDateTime).IsEqualTo(completedDateTime);
        await Assert.That(actual.ArchiveDateTime).IsEqualTo(archiveDateTime);
        if (expectedLastHistoryChangedAt.HasValue)
        {
            await Assert.That(actual.LastHistoryChangedAt).IsEqualTo(expectedLastHistoryChangedAt);
        }

        await Assert.That(actual.LegacyFieldsRemoved).IsTrue();
        await Assert.That(actual.StatusPersisted).IsTrue();
        await Assert.That(actual.StatusHistoryPersisted).IsTrue();
        await Assert.That(actual.CompletionCriteriaPersisted).IsTrue();
    }

    private static string CreateStatusKey(IEnumerable<DomainTaskStatus> statuses)
    {
        return string.Join("|", statuses.Select(status => $"{status}:{(int)status}"));
    }
    private static async Task<TaskStatusMigrationCaseResult> ReadCase(
        string tempDir,
        FileStorage fileStorage,
        string id)
    {
        var task = await fileStorage.Load(id, forced: true);
        await Assert.That(task).IsNotNull();

        await using var jsonStream = File.OpenRead(Path.Combine(tempDir, id));
        using var json = await JsonDocument.ParseAsync(jsonStream);
        var root = json.RootElement;
        var legacyFieldsRemoved =
            !root.TryGetProperty(nameof(TaskItem.IsCompleted), out _) &&
            !root.TryGetProperty(nameof(TaskItem.CompletedDateTime), out _) &&
            !root.TryGetProperty(nameof(TaskItem.ArchiveDateTime), out _);

        return new TaskStatusMigrationCaseResult(
            task!.Status,
            task.StatusHistory.Select(entry => entry.Status).ToArray(),
            task.CompletedDateTime,
            task.ArchiveDateTime,
            task.StatusHistory.LastOrDefault()?.ChangedAt,
            legacyFieldsRemoved,
            root.TryGetProperty(nameof(TaskItem.Status), out _),
            root.TryGetProperty(nameof(TaskItem.StatusHistory), out _),
            root.TryGetProperty(nameof(TaskItem.CompletionCriteria), out _));
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
          "UserId": "storm-migration-test",
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
          "UserId": "storm-migration-test",
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

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "storm-task-status-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
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