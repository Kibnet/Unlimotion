using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class FileStorageTaskStatusTests
{
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
}
