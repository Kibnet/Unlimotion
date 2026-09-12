using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Unlimotion.Test;

public sealed class TaskDirectoryResolverTests
{
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
