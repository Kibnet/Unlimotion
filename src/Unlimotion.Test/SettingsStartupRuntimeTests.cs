using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.Test;

// Both the desktop pending options and Environment.CurrentDirectory are process-wide.
[NotInParallel]
[ParallelLimiter<SharedUiStateParallelLimit>]
[SupportedOSPlatform("windows")]
public sealed class SettingsStartupRuntimeTests
{
    [Test]
    [Arguments("", null, false)]
    [Arguments("{\"TaskStorage\":", null, false)]
    [Arguments("[]", null, false)]
    [Arguments(null, "{", false)]
    [Arguments("{}", "{}", true)]
    public async Task Unreadable_settings_stops_real_runtime_before_storage_jobs_and_updates(
        string? main,
        string? backup,
        bool restrictBackup)
    {
        RequireWindows();
        using var fixture = new StartupFixture();
        if (main != null) File.WriteAllText(fixture.ConfigPath, main);
        if (backup != null) File.WriteAllText(fixture.ConfigPath + ".bak", backup);
        if (restrictBackup)
        {
            var permissions = new FileSecurity();
            permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            permissions.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(fixture.ConfigPath + ".bak").SetAccessControl(permissions);
        }
        File.WriteAllText(Path.Combine(fixture.TasksPath, "untouched-task"), "synthetic task evidence");
        var initialFiles = fixture.Fingerprint();
        using var globals = new AppGlobalsScope();
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var runtime = new RuntimeScope(fixture.ConfigPath, fixture.TasksPath);
            var result = Field<SettingsFileRecoveryResult>(runtime.App, "_startupSettingsRecovery");
            await Assert.That(result?.Status).IsEqualTo(SettingsRecoveryStatus.Blocked);
            await Assert.That(result?.Error).IsEqualTo(restrictBackup
                ? SettingsRecoveryError.AccessDenied : SettingsRecoveryError.InvalidSettings);
            foreach (var field in new[]
                     {
                         "_configuration", "_storageFactory", "_scheduler", "_mainWindowViewModel",
                         "_automaticUpdateTimer", "_taskSpaceSettingsQueue", "_backupService",
                         "_activeTaskSpaceConfiguration", "_taskSpaceCoordinator", "_startupUpdateSettings",
                         "_mapper", "_dialogs", "_taskMoveService"
                     })
            {
                if (Field<object>(runtime.App, field) != null)
                {
                    throw new InvalidOperationException($"Blocked startup unexpectedly created {field}.");
                }
            }
            await Assert.That(globals.UpdateService.Calls).IsEqualTo(0);
            await Assert.That(fixture.Fingerprint()).IsEqualTo(initialFiles);
        }, CancellationToken.None);
    }

    [Test]
    public async Task First_run_uses_explicit_synthetic_task_path_and_reopens_saved_configuration()
    {
        RequireWindows();
        using var fixture = new StartupFixture();
        using var globals = new AppGlobalsScope();
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            // Program creates the normal profile directory before App.Init. The explicit task path
            // prevents this first-run test from falling back to any installed or user dataset.
            for (var launch = 0; launch < 2; launch++)
            {
                using var runtime = new RuntimeScope(fixture.ConfigPath, fixture.TasksPath);
                var configuration = Field<IConfiguration>(runtime.App, "_configuration");
                var factory = Field<ITaskStorageFactory>(runtime.App, "_storageFactory");
                await Assert.That(configuration).IsNotNull();
                await Assert.That(configuration!["TaskStorage:Path"]).IsEqualTo(fixture.TasksPath);
                await Assert.That(factory?.SourceManager.ActiveSource?.Descriptor.Path).IsEqualTo(fixture.TasksPath);
                await Assert.That(Field<SettingsFileRecoveryResult>(runtime.App, "_startupSettingsRecovery")?.Status)
                    .IsEqualTo(SettingsRecoveryStatus.Ready);
                await Assert.That(File.Exists(fixture.ConfigPath)).IsTrue();
                await Assert.That(SettingsFileRecovery.IsReadableConfiguration(File.ReadAllBytes(fixture.ConfigPath)))
                    .IsTrue();
            }
            await Assert.That(SettingsFileRecovery.IsReadableConfiguration(File.ReadAllBytes(fixture.ConfigPath + ".bak")))
                .IsTrue();
            await Assert.That(globals.UpdateService.Calls).IsEqualTo(0);
        }, CancellationToken.None);
    }

    [Test]
    public async Task Relative_configuration_recovers_provider_path_and_leaves_cwd_homonym_untouched()
    {
        RequireWindows();
        using var fixture = new StartupFixture();
        var relativeDirectory = "settings-startup-" + Guid.NewGuid().ToString("N");
        var providerDirectory = Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        var relativePath = Path.Combine(relativeDirectory, "Settings.json");
        var providerPath = Path.Combine(providerDirectory, "Settings.json");
        var workingDirectory = Path.Combine(fixture.Root, "working");
        var homonym = Path.Combine(workingDirectory, relativePath);
        var legacySidecar = Path.Combine(Path.GetDirectoryName(homonym)!, TaskTreeExpansionStateStore.DefaultFileName);
        var providerSidecar = Path.Combine(providerDirectory, TaskTreeExpansionStateStore.DefaultFileName);
        var previousDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(providerDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(homonym)!);
        File.WriteAllBytes(providerPath, []);
        File.WriteAllText(providerPath + ".bak", JsonSerializer.Serialize(new
        {
            TaskStorage = new { Path = fixture.TasksPath, IsServerMode = false },
            Git = new { BackupEnabled = false },
            Appearance = new { Language = "en" },
            Unknown = "retained-provider-marker"
        }));
        File.WriteAllText(homonym, "{\"Sentinel\":\"cwd untouched\"}");
        var homonymBytes = File.ReadAllBytes(homonym);
        File.WriteAllText(legacySidecar,
            "{\"Version\":1,\"Trees\":{\"AllTasks\":[\"legacy-expanded-task\"]}}");
        var sidecarBytes = File.ReadAllBytes(legacySidecar);

        try
        {
            Environment.CurrentDirectory = workingDirectory;
            using var globals = new AppGlobalsScope();
            await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
            await session.DispatchAsync(async () =>
            {
                using var runtime = new RuntimeScope(relativePath, fixture.TasksPath);
                var configuration = Field<IConfiguration>(runtime.App, "_configuration");
                var recovery = Field<SettingsFileRecoveryResult>(runtime.App, "_startupSettingsRecovery");
                if (recovery?.Status == SettingsRecoveryStatus.Blocked)
                {
                    throw new InvalidOperationException($"Synthetic relative-path recovery was blocked: {recovery}");
                }
                await Assert.That(recovery?.Status).IsEqualTo(SettingsRecoveryStatus.Restored);
                await Assert.That(recovery?.ConfigPath).IsEqualTo(providerPath);
                var originalConfigArgument = Field<string>(runtime.App, "_configPath");
                await Assert.That(originalConfigArgument).IsEqualTo(relativePath);
                // Settings use the provider's physical path, but the existing expansion sidecar
                // keeps its historical cwd-based path from the original config argument.
                var sidecarPath = TaskTreeExpansionStateStore.GetDefaultPath(originalConfigArgument);
                await Assert.That(sidecarPath).IsEqualTo(legacySidecar);
                using (var expansionState = new TaskTreeExpansionStateStore(sidecarPath, loadPersistedState: true))
                {
                    await Assert.That(expansionState.GetExpansionState("AllTasks", "legacy-expanded-task"))
                        .IsTrue();
                }
                await Assert.That(File.ReadAllBytes(legacySidecar).SequenceEqual(sidecarBytes)).IsTrue();
                await Assert.That(File.Exists(providerSidecar)).IsFalse();
                await Assert.That(configuration?["Unknown"]).IsEqualTo("retained-provider-marker");
                await Assert.That(configuration?["TaskStorage:Path"]).IsEqualTo(fixture.TasksPath);
                await Assert.That(File.ReadAllBytes(recovery!.PreservedPath!).Length).IsEqualTo(0);
                await Assert.That(File.ReadAllBytes(homonym).SequenceEqual(homonymBytes)).IsTrue();
                await Assert.That(File.Exists(homonym + ".bak")).IsFalse();
                await Assert.That(globals.UpdateService.Calls).IsEqualTo(0);
            }, CancellationToken.None);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(providerDirectory, recursive: true);
        }
    }

    private static T? Field<T>(App app, string name) where T : class =>
        (T?)typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app);

    private static FieldInfo StaticField(string name) =>
        typeof(App).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(App).FullName, name);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Crash-safe desktop settings startup is enabled only on Windows.");
        }
    }

    private sealed class RuntimeScope : IDisposable
    {
        public App App { get; } = new();

        public RuntimeScope(string configPath, string tasksPath)
        {
            try
            {
                typeof(App).GetMethod("InitializeRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(App, [configPath, new UnlimotionClientOptions
                    {
                        DefaultTaskStoragePath = tasksPath,
                        GetAbsolutePath = Path.GetFullPath
                    }]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                Dispose();
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        public void Dispose()
        {
            Field<IDisposable>(App, "_mainWindowViewModel")?.Dispose();
            var factory = Field<ITaskStorageFactory>(App, "_storageFactory");
            if (factory != null)
            {
                foreach (var source in factory.SourceManager.Sources)
                {
                    (source.Storage as IDisposable)?.Dispose();
                }
            }
            Field<IDisposable>(App, "_taskSpaceOperationRunner")?.Dispose();
            Field<IDisposable>(App, "_configuration")?.Dispose();
        }
    }

    private sealed class AppGlobalsScope : IDisposable
    {
        private readonly object? _configPath = StaticField("_pendingConfigPath").GetValue(null);
        private readonly object? _clientOptions = StaticField("_pendingClientOptions").GetValue(null);
        private readonly object? _updateService = StaticField("_pendingUpdateService").GetValue(null);
        private readonly ILocalizationService _localization = LocalizationService.Current;
        private readonly string _languageMode = LocalizationService.Current.LanguageMode;
        public CountingUpdateService UpdateService { get; } = new();

        public AppGlobalsScope()
        {
            StaticField("_pendingConfigPath").SetValue(null, null);
            StaticField("_pendingClientOptions").SetValue(null, new UnlimotionClientOptions());
            StaticField("_pendingUpdateService").SetValue(null, UpdateService);
        }

        public void Dispose()
        {
            StaticField("_pendingConfigPath").SetValue(null, _configPath);
            StaticField("_pendingClientOptions").SetValue(null, _clientOptions);
            StaticField("_pendingUpdateService").SetValue(null, _updateService);
            LocalizationService.Current = _localization;
            _localization.SetLanguage(_languageMode);
        }
    }

    private sealed class CountingUpdateService : IApplicationUpdateService
    {
        public bool IsSupported => true;
        public string CurrentVersion => "1.0.0";
        public ApplicationUpdateInfo? PendingUpdate => null;
        public int Calls { get; private set; }
        public Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<ApplicationUpdateInfo?>(null);
        }
        public Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
        public void ApplyUpdateAndRestart() => Calls++;
    }

    private sealed class StartupFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Unlimotion.SettingsStartup", Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Root, "Settings.json");
        public string TasksPath => Path.Combine(Root, "Tasks");
        public StartupFixture() => Directory.CreateDirectory(TasksPath);
        public string Fingerprint() => string.Join("\n", Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(Root, path) + ":" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
