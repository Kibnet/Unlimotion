using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _configPath;

    public SettingsViewModelTests()
    {
        _configPath = Path.Combine(
            Environment.CurrentDirectory,
            $"ThemeSettings_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task ThemeMode_PersistsChoiceAndCompatibilityShimReflectsSelection()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, defaultIsDarkTheme: true);

        await Assert.That(settings.ThemeMode).IsEqualTo(ThemeMode.System);
        await Assert.That(settings.IsDarkTheme).IsTrue();

        settings.ThemeMode = ThemeMode.Light;

        await Assert.That(settings.ThemeMode).IsEqualTo(ThemeMode.Light);
        await Assert.That(settings.IsDarkTheme).IsFalse();
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.LightTheme);

        settings.ThemeMode = ThemeMode.System;

        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.SystemTheme);
    }

    [Test]
    public async System.Threading.Tasks.Task BackupAuthMode_DerivesFromRemoteUrl_WhenRepositoryRemotesAreUnavailable()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        settings.GitRemoteUrl = "git@github.com:org/unlimotion-backup.git";

        await Assert.That(settings.BackupAuthMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.IsSshAuthSelected).IsTrue();
        await Assert.That(settings.BackupAuthModeText).IsEqualTo("SSH");
    }

    [Test]
    public async System.Threading.Tasks.Task BackupConnection_BecomesReadyForClone_WhenSshKeyIsSelected()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration)
        {
            GitBackupEnabled = true,
            GitRemoteUrl = "git@github.com:org/unlimotion-backup.git"
        };

        await Assert.That(settings.CanConnectRepository).IsFalse();
        await Assert.That(settings.BackupStatusText).IsEqualTo("Выберите SSH-ключ.");

        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_ed25519.pub";

        await Assert.That(settings.CanConnectRepository).IsTrue();
        await Assert.That(settings.BackupStatusText)
            .IsEqualTo("Параметры сохранены. Нажмите \"Подключить репозиторий\".");
    }

    [Test]
    public async System.Threading.Tasks.Task BackupAuthMode_UsesSelectedRemoteAuthType_WhenRemoteExists()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin", "backup" },
            RemoteAuthTypes = new Dictionary<string, string>
            {
                ["origin"] = "HTTP",
                ["backup"] = "SSH"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, backupService);

        settings.GitRemoteNameDisplay = "backup (SSH)";

        await Assert.That(settings.BackupAuthMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.IsSshAuthSelected).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task FontSize_PersistsAndNormalizesConfiguredValue()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        await Assert.That(settings.FontSize).IsEqualTo(AppearanceSettings.DefaultFontSize);

        settings.FontSize = 18;

        await Assert.That(settings.FontSize).IsEqualTo(18);
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.FontSizeKey)
                .Get<double>())
            .IsEqualTo(18);

        settings.FontSize = 1;

        await Assert.That(settings.FontSize).IsEqualTo(AppearanceSettings.MinFontSize);
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.FontSizeKey)
                .Get<double>())
            .IsEqualTo(AppearanceSettings.MinFontSize);
    }

    [Test]
    public async System.Threading.Tasks.Task CanConnectStorage_FollowsSelectedModeRequirements()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        await Assert.That(settings.CanConnectStorage).IsFalse();

        settings.TaskStoragePath = @"C:\Data\Tasks";
        await Assert.That(settings.CanConnectStorage).IsTrue();

        settings.IsServerMode = true;
        await Assert.That(settings.CanConnectStorage).IsFalse();

        settings.ServerStorageUrl = "https://server.example";
        settings.Login = "user@example";
        settings.Password = "secret";

        await Assert.That(settings.CanConnectStorage).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task EnsureDefaultTaskStoragePath_PersistsDefaultPathWhenLocalPathIsEmpty()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var defaultPath = Path.Combine(Environment.CurrentDirectory, "DefaultTasks");

        App.EnsureDefaultTaskStoragePath(configuration, defaultPath);

        await Assert.That(configuration
                .GetSection("TaskStorage")
                .GetSection(nameof(TaskStorageSettings.Path))
                .Get<string>())
            .IsEqualTo(defaultPath);
    }

    [Test]
    public async System.Threading.Tasks.Task EnsureDefaultTaskStoragePath_PreservesExistingPath()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var existingPath = Path.Combine(Environment.CurrentDirectory, "ExistingTasks");
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(existingPath);

        App.EnsureDefaultTaskStoragePath(configuration, Path.Combine(Environment.CurrentDirectory, "DefaultTasks"));

        await Assert.That(configuration
                .GetSection("TaskStorage")
                .GetSection(nameof(TaskStorageSettings.Path))
                .Get<string>())
            .IsEqualTo(existingPath);
    }

    [Test]
    public async System.Threading.Tasks.Task TaskStoragePathTooltip_ResolvesRelativePathToFullPath()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = Path.Combine("Data", "Tasks");

        await Assert.That(settings.TaskStoragePathTooltip)
            .IsEqualTo(Path.GetFullPath(Path.Combine("Data", "Tasks")));
    }

    [Test]
    public async System.Threading.Tasks.Task TaskStoragePathTooltip_UsesActualFallbackPathWhenPathIsEmpty()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = string.Empty;

        await Assert.That(settings.TaskStoragePathTooltip)
            .IsEqualTo(Path.GetFullPath("Tasks"));
    }

    [Test]
    public async System.Threading.Tasks.Task TaskStoragePathTooltip_DoesNotThrowForInvalidIntermediateInput()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = new string((char)0, 1);
        var tooltip = settings.TaskStoragePathTooltip;

        await Assert.That(tooltip).IsNotNull();
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_FillsEmptyRepositoryUrlFromSelectedRemote()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin", "backup" },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/origin.git",
                ["backup"] = "git@github.com:org/unlimotion-backup.git"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("backup");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/unlimotion-backup.git");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_DoesNotOverwriteExistingRepositoryUrl()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin" },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/origin.git"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/custom.git");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteUrl).IsEqualTo("https://example.com/custom.git");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_SelectsSingleRemoteWhenStoredRemoteIsMissing()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "backup" },
            RemoteUrls = new Dictionary<string, string>
            {
                ["backup"] = "git@github.com:org/unlimotion-backup.git"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteName).IsEqualTo("backup");
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/unlimotion-backup.git");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_SelectsOriginWhenStoredRemoteIsMissingAndMultipleRemotesExist()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "backup", "origin" },
            RemoteUrls = new Dictionary<string, string>
            {
                ["backup"] = "git@github.com:org/backup.git",
                ["origin"] = "git@github.com:org/origin.git"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("missing");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteName).IsEqualTo("origin");
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/origin.git");
    }

    [Test]
    public async System.Threading.Tasks.Task GitPushRefSpec_FallsBackToCanonicalBranchWhenPushRefSpecIsEmpty()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Branch)).Set("master");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set(string.Empty);

        var settings = new SettingsViewModel(configuration);

        await Assert.That(settings.GitPushRefSpec).IsEqualTo("refs/heads/master");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_SelectsAvailableBranchWhenStoredPushRefSpecIsMissing()
    {
        var backupService = new FakeRemoteBackupService
        {
            ReferenceNames = new List<string> { "refs/heads/main" }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set("refs/heads/master");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitPushRefSpec).IsEqualTo("refs/heads/main");
        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.PushRefSpec))
                .Get<string>())
            .IsEqualTo("refs/heads/main");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_PreservesStoredPushRefSpecWhenItExists()
    {
        var backupService = new FakeRemoteBackupService
        {
            ReferenceNames = new List<string> { "refs/heads/main", "refs/heads/release" }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set("refs/heads/release");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitPushRefSpec).IsEqualTo("refs/heads/release");
    }

    [Test]
    public async System.Threading.Tasks.Task CanSyncRepository_RequiresBackupRemoteAndPushRefSpecWithoutConnectedState()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration)
        {
            GitBackupEnabled = true,
            GitRemoteName = "origin",
            GitPushRefSpec = "refs/heads/main"
        };

        await Assert.That(settings.BackupConnectionState).IsNotEqualTo(BackupStatusState.Connected);
        await Assert.That(settings.CanSyncRepository).IsTrue();

        settings.GitPushRefSpec = string.Empty;

        await Assert.That(settings.CanSyncRepository).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadSshPublicKeys_PreservesExistingSelectionWhenKeyStillExists()
    {
        var backupService = new FakeRemoteBackupService
        {
            PublicKeys = new List<string>
            {
                @"C:\Users\Test\.ssh\id_first.pub"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, backupService);
        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_first.pub";

        backupService.PublicKeys = new List<string>
        {
            @"C:\Users\Test\.ssh\id_first.pub",
            @"C:\Users\Test\.ssh\id_second.pub"
        };

        settings.ReloadSshPublicKeys();

        await Assert.That(settings.SshPublicKeys.Count).IsEqualTo(2);
        await Assert.That(settings.SelectedSshPublicKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_first.pub");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadSshPublicKeys_SelectsPreferredKeyWhenAvailable()
    {
        var backupService = new FakeRemoteBackupService
        {
            PublicKeys = new List<string>
            {
                @"C:\Users\Test\.ssh\id_first.pub",
                @"C:\Users\Test\.ssh\id_second.pub"
            }
        };

        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, backupService);

        settings.ReloadSshPublicKeys(@"C:\Users\Test\.ssh\id_second.pub");

        await Assert.That(settings.SelectedSshPublicKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_second.pub");
        await Assert.That(settings.GitSshPrivateKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_second");
    }

    [Test]
    public async System.Threading.Tasks.Task SelectedSshPublicKeyPath_UpdatesPrivateKeyPath()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration);

        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_ed25519.pub";

        await Assert.That(settings.GitSshPublicKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_ed25519.pub");
        await Assert.That(settings.GitSshPrivateKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_ed25519");
    }

    [Test]
    public async System.Threading.Tasks.Task NormalizeSshKeyFileName_StripsPathTraversalAndKeepsSafeFileName()
    {
        var normalized = BackupViaGitService.NormalizeSshKeyFileName(@"..\keys\id_ed25519 custom");

        await Assert.That(normalized).IsEqualTo("id_ed25519_custom");
    }

    [Test]
    public async System.Threading.Tasks.Task GetSshKeyPaths_AlwaysReturnsPathsInsideSshDirectory()
    {
        var sshDirectory = Path.Combine(Environment.CurrentDirectory, ".ssh");
        var keyPaths = BackupViaGitService.GetSshKeyPaths(sshDirectory, "..");

        await Assert.That(Path.GetFileName(keyPaths.PrivateKeyPath)).IsEqualTo("id_ed25519_unlimotion");
        await Assert.That(Path.GetDirectoryName(keyPaths.PrivateKeyPath)).IsEqualTo(Path.GetFullPath(sshDirectory));
    }

    [Test]
    public async System.Threading.Tasks.Task BuildGitSshCommand_UsesExplicitKeyAndIdentitiesOnly()
    {
        var command = BackupViaGitService.BuildGitSshCommand(@"C:\Users\Test\.ssh\id_ed25519");

        await Assert.That(command).Contains("ssh -i");
        await Assert.That(command).Contains("id_ed25519");
        await Assert.That(command).Contains("IdentitiesOnly=yes");
    }

    private sealed class FakeRemoteBackupService : IRemoteBackupService
    {
        public List<string> PublicKeys { get; set; } = new();
        public List<string> RemoteNames { get; set; } = new();
        public List<string> ReferenceNames { get; set; } = new();
        public Dictionary<string, string> RemoteAuthTypes { get; set; } = new();
        public Dictionary<string, string> RemoteUrls { get; set; } = new();

        public List<string> Remotes() => new(RemoteNames);
        public string? GetRemoteAuthType(string remoteName) =>
            RemoteAuthTypes.TryGetValue(remoteName, out var authType) ? authType : null;
        public string? GetRemoteUrl(string remoteName) =>
            RemoteUrls.TryGetValue(remoteName, out var remoteUrl) ? remoteUrl : null;
        public List<string> Refs() => new(ReferenceNames);
        public List<string> GetSshPublicKeys() => new(PublicKeys);
        public string GenerateSshKey(string keyName) => throw new NotSupportedException();
        public string? ReadPublicKey(string publicKeyPath) => throw new NotSupportedException();
        public void Push(string msg) => throw new NotSupportedException();
        public void Pull() => throw new NotSupportedException();
        public BackupRepositoryConnectPreview PreviewConnectRepository() => throw new NotSupportedException();
        public void ConnectRepository(bool allowMergeWithNonEmptyRemote) => throw new NotSupportedException();
        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }
}
