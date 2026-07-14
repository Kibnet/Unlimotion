using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
