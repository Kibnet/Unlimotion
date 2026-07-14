using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using FileTaskStorage = global::Unlimotion.Storage.FileTaskStorage;
using FileTaskStorageOptions = global::Unlimotion.Storage.FileTaskStorageOptions;

namespace Unlimotion.Test;

public sealed class FileTaskStorageTests
{
    [Test]
    public async Task Load_MalformedTaskFileReturnsNullAndDiagnosticReadReportsLoadError()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "malformed-task");
            await File.WriteAllTextAsync(filePath, "{ malformed json");
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });

            var loaded = await storage.Load("malformed-task", forced: true);
            var directoryRead = await storage.ReadDirectoryAsync();

            await Assert.That(loaded).IsNull();
            await Assert.That(directoryRead.Tasks).IsEmpty();
            await Assert.That(directoryRead.LoadErrors.Single().File).IsEqualTo(filePath);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task ReadDirectory_UnsafeTaskIdIsRejectedAndSaveCannotEscapeDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var escapedId = $"..{Path.DirectorySeparatorChar}escaped-{Guid.NewGuid():N}";
            var sourcePath = Path.Combine(tempDir, "unsafe-task");
            await File.WriteAllTextAsync(sourcePath, JsonConvert.SerializeObject(new TaskItem
            {
                Id = escapedId,
                Title = "Unsafe task"
            }));
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });

            var directoryRead = await storage.ReadDirectoryAsync();

            await Assert.That(directoryRead.Tasks).IsEmpty();
            await Assert.That(directoryRead.LoadErrors.Single().File).IsEqualTo(sourcePath);
            Func<Task<TaskItem?>> saveUnsafeTask = async () => await storage.Save(new TaskItem
                {
                    Id = escapedId,
                    Title = "Unsafe task"
                });
            await Assert.That(saveUnsafeTask).Throws<InvalidDataException>();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_TaskLoadedFromJsonFilePreservesItsSourceFileName()
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
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });
            var directoryRead = await storage.ReadDirectoryAsync();
            var task = directoryRead.Tasks.Single();
            task.Title = "Updated";

            await storage.Save(task);

            var persisted = JObject.Parse(await File.ReadAllTextAsync(sourcePath));
            await Assert.That(persisted.Value<string>(nameof(TaskItem.Title))).IsEqualTo("Updated");
            await Assert.That(File.Exists(Path.Combine(tempDir, task.Id))).IsFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task ReadDirectory_IgnoresFilesOutsideTaskFileConvention()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "notes.txt"),
                JsonConvert.SerializeObject(new TaskItem { Id = "notes", Title = "Not a task file" }));
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });

            var directoryRead = await storage.ReadDirectoryAsync();

            await Assert.That(directoryRead.Tasks).IsEmpty();
            await Assert.That(directoryRead.LoadErrors).IsEmpty();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_PreservesUnknownRepeaterJsonFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempDir, "repeating-task");
            var json = JObject.FromObject(new TaskItem
            {
                Id = "repeating-task",
                Title = "Repeating task",
                Repeater = new RepeaterPattern
                {
                    Type = RepeaterType.Daily,
                    Period = 1,
                    Pattern = []
                }
            });
            json[nameof(TaskItem.Repeater)]!["FutureRule"] = "preserve-me";
            await File.WriteAllTextAsync(sourcePath, json.ToString());
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });
            var task = (await storage.ReadDirectoryAsync()).Tasks.Single();
            task.Title = "Updated";

            await storage.Save(task);

            var persisted = JObject.Parse(await File.ReadAllTextAsync(sourcePath));
            await Assert.That(persisted[nameof(TaskItem.Repeater)]?.Value<string>("FutureRule"))
                .IsEqualTo("preserve-me");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_WaitsForContendedDirectoryLock()
    {
        var tempDir = CreateTempDirectory();
        FileStream? externalLock = null;
        try
        {
            externalLock = new FileStream(
                Path.Combine(tempDir, ".unlimotion.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir });

            var saveTask = storage.Save(new TaskItem { Id = "lock-test", Title = "Lock test" });
            await Task.Delay(100);

            await Assert.That(saveTask.IsCompleted).IsFalse();
            externalLock.Dispose();
            externalLock = null;

            var saved = await saveTask.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(saved.Id).IsEqualTo("lock-test");
        }
        finally
        {
            externalLock?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_ConcurrentStorageInstances_DoNotRaceLockCleanup()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storages = Enumerable.Range(0, 4)
                .Select(_ => new FileTaskStorage(new FileTaskStorageOptions { Path = tempDir }))
                .ToArray();
            var saves = Enumerable.Range(0, 80)
                .Select(index => storages[index % storages.Length].Save(new TaskItem
                {
                    Id = $"concurrent-{index}",
                    Title = $"Concurrent {index}"
                }));

            var saved = await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(20));

            await Assert.That(saved).Count().IsEqualTo(80);
            await Assert.That(Directory.EnumerateFiles(tempDir)
                    .Count(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)))
                .IsEqualTo(80);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "file-task-storage-" + Guid.NewGuid().ToString("N"));
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
