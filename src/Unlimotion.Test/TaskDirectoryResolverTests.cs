using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Unlimotion.Test;

public sealed class TaskDirectoryResolverTests
{
    [Test]
    public async Task Resolve_UsesEnvironmentBeforeDesktopAndPreservesExplicitOverride()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var missingSettings = Path.Combine(temp.Path, "MissingSettings.json");
        var environmentTasksPath = Path.Combine(temp.Path, "EnvironmentTasks");
        var explicitTasksPath = Path.Combine(temp.Path, "ExplicitTasks");

        await Assert.That(global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(
            null, environmentTasksPath, missingSettings)).IsEqualTo(environmentTasksPath);
        await Assert.That(global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(
            explicitTasksPath, environmentTasksPath, missingSettings)).IsEqualTo(explicitTasksPath);
        var fromEnvironment = global::Unlimotion.Cli.TaskDirectoryResolver.ResolveWithSource(
            null, environmentTasksPath, missingSettings);
        var fromExplicit = global::Unlimotion.Cli.TaskDirectoryResolver.ResolveWithSource(
            explicitTasksPath, environmentTasksPath, missingSettings);
        await Assert.That(fromEnvironment.TasksPath).IsEqualTo(environmentTasksPath);
        await Assert.That(fromEnvironment.SourceKind).IsEqualTo("environmentTasks");
        await Assert.That(fromExplicit.TasksPath).IsEqualTo(explicitTasksPath);
        await Assert.That(fromExplicit.SourceKind).IsEqualTo("explicitTasks");
        await Assert.That(() => global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(
            null, "  ", missingSettings)).Throws<global::Unlimotion.Cli.CliException>();
    }

    [Test]
    public async Task Resolve_EmptyEnvironmentPreservesDesktopSettingsPath()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var settingsPath = Path.Combine(temp.Path, "Settings.json");
        var configuredTasksPath = Path.Combine(temp.Path, "DesktopTasks");
        await File.WriteAllTextAsync(settingsPath,
            JsonSerializer.Serialize(new { TaskStorage = new { Path = configuredTasksPath } }));

        foreach (var environmentPath in new string?[] { null, string.Empty, "  " })
        {
            var result = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(
                null, environmentPath, settingsPath);
            await Assert.That(result).IsEqualTo(configuredTasksPath);
        }
    }

    [Test]
    public async Task Resolve_UsesConfiguredLocalPathAndExplicitOverride()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var settingsPath = Path.Combine(temp.Path, "Settings.json");
        var configuredTasksPath = Path.Combine(temp.Path, "ConfiguredTasks");
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(new
            {
                TaskStorage = new
                {
                    Path = configuredTasksPath,
                    IsServerMode = "False"
                }
            }));

        var configured = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(null, settingsPath);
        var explicitOverride = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(
            Path.Combine(temp.Path, "ExplicitTasks"),
            Path.Combine(temp.Path, "MissingSettings.json"));

        await Assert.That(configured).IsEqualTo(configuredTasksPath);
        await Assert.That(explicitOverride).IsEqualTo(Path.Combine(temp.Path, "ExplicitTasks"));
        var configuredWithSource = global::Unlimotion.Cli.TaskDirectoryResolver.ResolveWithSource(null, settingsPath);
        var explicitWithSource = global::Unlimotion.Cli.TaskDirectoryResolver.ResolveWithSource(explicitOverride, settingsPath);
        await Assert.That(configuredWithSource.TasksPath).IsEqualTo(configuredTasksPath);
        await Assert.That(configuredWithSource.SourceKind).IsEqualTo("desktopSettings");
        await Assert.That(explicitWithSource.TasksPath).IsEqualTo(explicitOverride);
        await Assert.That(explicitWithSource.SourceKind).IsEqualTo("explicitTasks");
    }

    [Test]
    public async Task Resolve_UsesSettingsDirectoryForRelativeConfiguredPath()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var settingsDirectory = Path.Combine(temp.Path, "DesktopSettings");
        Directory.CreateDirectory(settingsDirectory);
        var settingsPath = Path.Combine(settingsDirectory, "Settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(new
            {
                TaskStorage = new
                {
                    Path = "Tasks",
                    IsServerMode = false
                }
            }));

        var configured = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(null, settingsPath);

        await Assert.That(configured).IsEqualTo(Path.Combine(settingsDirectory, "Tasks"));
    }

    [Test]
    public async Task Resolve_ExplicitPathPinsTaskSpaceAcrossDesktopSettingsChange()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var settingsPath = Path.Combine(temp.Path, "Settings.json");
        var firstPath = Path.Combine(temp.Path, "FirstTasks");
        var secondPath = Path.Combine(temp.Path, "SecondTasks");
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new { TaskStorage = new { Path = firstPath, IsServerMode = false } }));
        var pinned = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(null, settingsPath);
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new { TaskStorage = new { Path = secondPath, IsServerMode = false } }));
        await Assert.That(global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(null, settingsPath)).IsEqualTo(secondPath);
        await Assert.That(global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(pinned, settingsPath)).IsEqualTo(firstPath);
    }

    [Test]
    public async Task Resolve_RejectsMissingMalformedPathlessAndServerSettings()
    {
        using var temp = TemporarySettingsDirectory.Create();
        var cases = new[]
        {
            (Path.Combine(temp.Path, "Missing.json"), "settingsNotFound", (string?)null),
            (Path.Combine(temp.Path, "Malformed.json"), "settingsInvalid", "{"),
            (Path.Combine(temp.Path, "Pathless.json"), "settingsPathMissing", "{\"TaskStorage\":{}}"),
            (Path.Combine(temp.Path, "Server.json"), "settingsUnsupported", "{\"TaskStorage\":{\"Path\":\"https://example.invalid\",\"IsServerMode\":true}}")
        };

        foreach (var testCase in cases)
        {
            if (testCase.Item3 != null)
            {
                await File.WriteAllTextAsync(testCase.Item1, testCase.Item3);
            }

            try
            {
                _ = global::Unlimotion.Cli.TaskDirectoryResolver.Resolve(null, testCase.Item1);
                throw new InvalidOperationException("Expected default task directory resolution to fail.");
            }
            catch (global::Unlimotion.Cli.CliException exception)
            {
                await Assert.That(exception.ExitCode).IsEqualTo(1);
                await Assert.That(exception.Kind).IsEqualTo(testCase.Item2);
            }
        }
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        private TemporarySettingsDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporarySettingsDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-cli-settings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporarySettingsDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
