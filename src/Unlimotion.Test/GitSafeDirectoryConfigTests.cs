using System;
using System.IO;
using System.Linq;
using Unlimotion.Services;

namespace Unlimotion.Test;

public sealed class GitSafeDirectoryConfigTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"UnlimotionGitSafeDirectoryConfigTests_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task EnsureSafeDirectory_AddsExplicitExternalPathWhenWildcardAlreadyExists()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configPath = Path.Combine(_tempDirectory, ".gitconfig");
        File.WriteAllText(configPath, "[safe]\n\tdirectory = *\n");

        GitSafeDirectoryConfig.EnsureSafeDirectory(
            configPath,
            "/storage/emulated/0/Projects/Unlimotion.Tasks/");

        var lines = File.ReadAllLines(configPath);

        await Assert.That(lines).Contains("\tdirectory = *");
        await Assert.That(lines).Contains("\tdirectory = /storage/emulated/0/Projects/Unlimotion.Tasks");
    }

    [Test]
    public async System.Threading.Tasks.Task EnsureSafeDirectory_DoesNotDuplicateExistingPath()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configPath = Path.Combine(_tempDirectory, ".gitconfig");
        File.WriteAllText(
            configPath,
            "[safe]\n\tdirectory = /storage/emulated/0/Projects/Unlimotion.Tasks\n");

        GitSafeDirectoryConfig.EnsureSafeDirectory(
            configPath,
            "/storage/emulated/0/Projects/Unlimotion.Tasks/");

        var matches = File
            .ReadAllLines(configPath)
            .Count(line => line.Trim() == "directory = /storage/emulated/0/Projects/Unlimotion.Tasks");

        await Assert.That(matches).IsEqualTo(1);
    }
}
