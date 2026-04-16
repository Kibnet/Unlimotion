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
    public async System.Threading.Tasks.Task IsDarkTheme_PersistsThemeChoice()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, defaultIsDarkTheme: true);

        await Assert.That(settings.IsDarkTheme).IsTrue();

        settings.IsDarkTheme = false;

        await Assert.That(settings.IsDarkTheme).IsFalse();
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.LightTheme);

        settings.IsDarkTheme = true;

        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.DarkTheme);
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

        public List<string> Remotes() => new();
        public string? GetRemoteAuthType(string remoteName) => "SSH";
        public List<string> Refs() => new();
        public List<string> GetSshPublicKeys() => new(PublicKeys);
        public string GenerateSshKey(string keyName) => throw new NotSupportedException();
        public string? ReadPublicKey(string publicKeyPath) => throw new NotSupportedException();
        public void Push(string msg) => throw new NotSupportedException();
        public void Pull() => throw new NotSupportedException();
        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }
}
