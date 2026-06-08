using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

 [ParallelLimiter<SharedUiStateParallelLimit>]
public class SettingsViewModelTests : IDisposable
{
    private readonly string _configPath;
    private readonly List<IDisposable> _configurationDisposables = [];

    public SettingsViewModelTests()
    {
        _configPath = Path.Combine(
            Environment.CurrentDirectory,
            $"ThemeSettings_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        foreach (var disposable in _configurationDisposables)
        {
            disposable.Dispose();
        }

        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    private IConfigurationRoot CreateConfiguration()
    {
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);

        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        return configuration;
    }

    [Test]
    public async System.Threading.Tasks.Task ThemeMode_PersistsChoiceAndCompatibilityShimReflectsSelection()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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
    public async System.Threading.Tasks.Task TaskOutlineClipboardSettings_PersistChoices()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        await Assert.That(settings.CopyTaskOutlineAsMarkdown).IsFalse();
        await Assert.That(settings.CopyTaskOutlineDescription).IsFalse();

        settings.CopyTaskOutlineAsMarkdown = true;
        settings.CopyTaskOutlineDescription = true;

        await Assert.That(configuration
                .GetSection("TaskOutlineClipboard")
                .GetSection("CopyAsMarkdown")
                .Get<bool>())
            .IsTrue();
        await Assert.That(configuration
                .GetSection("TaskOutlineClipboard")
                .GetSection("CopyDescription")
                .Get<bool>())
            .IsTrue();

        var reloaded = new SettingsViewModel(configuration);
        await Assert.That(reloaded.CopyTaskOutlineAsMarkdown).IsTrue();
        await Assert.That(reloaded.CopyTaskOutlineDescription).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task TaskTreeExpansionStateSettings_PersistChoice()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        await Assert.That(settings.PersistTaskTreeExpansionState).IsFalse();

        settings.PersistTaskTreeExpansionState = true;

        await Assert.That(configuration
                .GetSection("TaskTreeExpansionState")
                .GetSection("Enabled")
                .Get<bool>())
            .IsTrue();

        var reloaded = new SettingsViewModel(configuration);
        await Assert.That(reloaded.PersistTaskTreeExpansionState).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task TaskTreeExpansionStateStore_BatchesChangesIntoOneWrite()
    {
        var writes = new ConcurrentQueue<string>();
        using var store = new TaskTreeExpansionStateStore(
            "TaskTreeExpansionState.json",
            loadPersistedState: true,
            saveThrottle: TimeSpan.FromMilliseconds(50),
            fileExists: _ => false,
            readAllText: _ => string.Empty,
            writeAllText: (_, content) => writes.Enqueue(content));

        store.SetExpansionState("AllTasksTree", "root", true, persist: true);
        store.SetExpansionState("AllTasksTree", "child", true, persist: true);
        store.SetExpansionState("AllTasksTree", "grandchild", true, persist: true);

        var written = await TestHelpers.WaitUntilAsync(
            () => writes.Count == 1,
            TimeSpan.FromSeconds(2));
        await Assert.That(written).IsTrue();

        await System.Threading.Tasks.Task.Delay(150);
        await Assert.That(writes.Count).IsEqualTo(1);

        var json = writes.Single();
        using var document = JsonDocument.Parse(json);
        var expandedIds = document.RootElement
            .GetProperty("Trees")
            .GetProperty("AllTasksTree")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToList();

        await Assert.That(expandedIds).Contains("root");
        await Assert.That(expandedIds).Contains("child");
        await Assert.That(expandedIds).Contains("grandchild");
    }

    [Test]
    public async System.Threading.Tasks.Task TaskTreeExpansionStateStore_DisposeFlushesPendingWrite()
    {
        var writes = new ConcurrentQueue<string>();
        var store = new TaskTreeExpansionStateStore(
            "TaskTreeExpansionState.json",
            loadPersistedState: true,
            saveThrottle: TimeSpan.FromHours(1),
            fileExists: _ => false,
            readAllText: _ => string.Empty,
            writeAllText: (_, content) => writes.Enqueue(content));

        store.SetExpansionState("AllTasksTree", "root", true, persist: true);
        await Assert.That(writes.Count).IsEqualTo(0);

        store.Dispose();

        await Assert.That(writes.Count).IsEqualTo(1);
        var json = writes.Single();
        using var document = JsonDocument.Parse(json);
        var expandedIds = document.RootElement
            .GetProperty("Trees")
            .GetProperty("AllTasksTree")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToList();
        await Assert.That(expandedIds).Contains("root");
    }

    [Test]
    public async System.Threading.Tasks.Task TaskTreeExpansionStateStore_DisableCancelsPendingWrite()
    {
        var writes = new ConcurrentQueue<string>();
        using var store = new TaskTreeExpansionStateStore(
            "TaskTreeExpansionState.json",
            loadPersistedState: true,
            saveThrottle: TimeSpan.FromMilliseconds(50),
            fileExists: _ => false,
            readAllText: _ => string.Empty,
            writeAllText: (_, content) => writes.Enqueue(content));

        store.SetExpansionState("AllTasksTree", "root", true, persist: true);
        store.SetPersistenceEnabled(false);

        await System.Threading.Tasks.Task.Delay(150);
        await Assert.That(writes.Count).IsEqualTo(0);
    }

    [Test]
    public async System.Threading.Tasks.Task TaskTreeExpansionStateStore_FailedWriteKeepsDirtyStateForNextFlush()
    {
        var writes = new ConcurrentQueue<string>();
        var writeAttempts = 0;
        using var store = new TaskTreeExpansionStateStore(
            "TaskTreeExpansionState.json",
            loadPersistedState: true,
            saveThrottle: TimeSpan.FromHours(1),
            fileExists: _ => false,
            readAllText: _ => string.Empty,
            writeAllText: (_, content) =>
            {
                writeAttempts++;
                if (writeAttempts == 1)
                {
                    throw new IOException("Transient write failure.");
                }

                writes.Enqueue(content);
            });

        store.SetExpansionState("AllTasksTree", "root", true, persist: true);

        store.Flush();
        await Assert.That(writeAttempts).IsEqualTo(1);
        await Assert.That(writes.Count).IsEqualTo(0);

        store.Flush();
        await Assert.That(writeAttempts).IsEqualTo(2);
        await Assert.That(writes.Count).IsEqualTo(1);
    }

    [Test]
    public async System.Threading.Tasks.Task Updates_AreDisabled_WhenUpdateServiceIsUnsupported()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService { IsSupported = false };
        var settings = CreateSettingsViewModel(configuration);

        settings.ConfigureUpdateService(updateService);

        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.Unsupported);
        await Assert.That(settings.CurrentApplicationVersion).IsEqualTo("1.0.0");
        await Assert.That(settings.CanCheckForUpdates).IsFalse();
        await Assert.That(settings.CanDownloadUpdate).IsFalse();
        await Assert.That(settings.CanApplyUpdate).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_SetsNoUpdatesState_WhenNoUpdateExists()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService();
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        await settings.CheckForUpdatesAsync();

        await Assert.That(updateService.CheckCalls).IsEqualTo(1);
        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.NoUpdates);
        await Assert.That(settings.CanCheckForUpdates).IsTrue();
        await Assert.That(settings.CanDownloadUpdate).IsFalse();
        await Assert.That(settings.CanApplyUpdate).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_UsesPendingUpdateBeforeNetworkCheck()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService();
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        updateService.PendingUpdate = new ApplicationUpdateInfo("2.0.0");

        await settings.CheckForUpdatesAsync();

        await Assert.That(updateService.CheckCalls).IsEqualTo(0);
        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.ReadyToApply);
        await Assert.That(settings.AvailableUpdateVersion).IsEqualTo("2.0.0");
        await Assert.That(settings.CanCheckForUpdates).IsFalse();
        await Assert.That(settings.CanApplyUpdate).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task DownloadUpdateAsync_SetsReadyToApply_WhenUpdateWasFound()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            NextUpdate = new ApplicationUpdateInfo("2.0.0")
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        await settings.CheckForUpdatesAsync();
        await settings.DownloadUpdateAsync();

        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.ReadyToApply);
        await Assert.That(settings.AvailableUpdateVersion).IsEqualTo("2.0.0");
        await Assert.That(updateService.DownloadCalls).IsEqualTo(1);
        await Assert.That(settings.CanApplyUpdate).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task ApplyUpdateAsync_CallsUpdateServiceRestart_WhenUpdateIsReady()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            NextUpdate = new ApplicationUpdateInfo("2.0.0")
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        await settings.CheckForUpdatesAsync();
        await settings.DownloadUpdateAsync();
        await settings.ApplyUpdateAsync();

        await Assert.That(updateService.ApplyCalls).IsEqualTo(1);
        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.Applying);
        await Assert.That(settings.CanApplyUpdate).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task ApplyUpdateAsync_KeepsInstallAction_WhenUserActionIsRequired()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            NextUpdate = new ApplicationUpdateInfo("2.0.0"),
            ApplyException = new ApplicationUpdateUserActionRequiredException("Grant install permission.")
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        await settings.CheckForUpdatesAsync();
        await settings.DownloadUpdateAsync();
        await settings.ApplyUpdateAsync();

        await Assert.That(updateService.ApplyCalls).IsEqualTo(1);
        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.ReadyToApply);
        await Assert.That(settings.UpdateStatusText).IsEqualTo("Grant install permission.");
        await Assert.That(settings.CanApplyUpdate).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_IgnoresRepeatedCalls_WhileBusy()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            CheckCompletion = new System.Threading.Tasks.TaskCompletionSource<ApplicationUpdateInfo?>()
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);

        var firstCheck = settings.CheckForUpdatesAsync();
        var secondCheck = settings.CheckForUpdatesAsync();

        await Assert.That(updateService.CheckCalls).IsEqualTo(1);

        updateService.CheckCompletion.SetResult(null);
        await firstCheck;
        await secondCheck;

        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.NoUpdates);
    }

    [Test]
    public async System.Threading.Tasks.Task UpdateAutoCheckSettings_DefaultToHourlyAndPersistChoices()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = CreateSettingsViewModel(configuration);

        await Assert.That(settings.UpdateAutoCheckEnabled).IsTrue();
        await Assert.That(settings.CanEditUpdateCheckInterval).IsTrue();
        await Assert.That(settings.UpdateCheckIntervalValue)
            .IsEqualTo(ApplicationUpdateSettings.DefaultCheckIntervalValue);
        await Assert.That(settings.UpdateCheckIntervalUnit)
            .IsEqualTo(ApplicationUpdateCheckIntervalUnit.Hours);
        await Assert.That(settings.UpdateCheckIntervalUnitIndex).IsEqualTo(1);
        await Assert.That(settings.UpdateCheckInterval).IsEqualTo(TimeSpan.FromHours(1));

        settings.UpdateAutoCheckEnabled = false;
        settings.UpdateCheckIntervalValue = 2;
        settings.UpdateCheckIntervalUnit = ApplicationUpdateCheckIntervalUnit.Days;

        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.AutoCheckEnabledKey)
                .Get<bool>())
            .IsFalse();
        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
                .Get<int>())
            .IsEqualTo(2);
        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
                .Get<string>())
            .IsEqualTo(nameof(ApplicationUpdateCheckIntervalUnit.Days));

        var reloaded = CreateSettingsViewModel(configuration);
        await Assert.That(reloaded.UpdateAutoCheckEnabled).IsFalse();
        await Assert.That(reloaded.CanEditUpdateCheckInterval).IsFalse();
        await Assert.That(reloaded.UpdateCheckInterval).IsEqualTo(TimeSpan.FromDays(2));
    }

    [Test]
    public async System.Threading.Tasks.Task UpdateAutoCheckSettings_NormalizeInvalidValuesWithoutConstructorConfigChurn()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        configuration
            .GetSection(ApplicationUpdateSettings.SectionName)
            .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
            .Set(0);
        configuration
            .GetSection(ApplicationUpdateSettings.SectionName)
            .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
            .Set("Weeks");

        var settings = CreateSettingsViewModel(configuration);

        await Assert.That(settings.UpdateCheckIntervalValue).IsEqualTo(1);
        await Assert.That(settings.UpdateCheckIntervalUnit)
            .IsEqualTo(ApplicationUpdateCheckIntervalUnit.Hours);
        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
                .Get<int>())
            .IsEqualTo(0);
        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
                .Get<string>())
            .IsEqualTo("Weeks");

        settings.NormalizeUpdateCheckSettings();

        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
                .Get<int>())
            .IsEqualTo(1);
        await Assert.That(configuration
                .GetSection(ApplicationUpdateSettings.SectionName)
                .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
                .Get<string>())
            .IsEqualTo(nameof(ApplicationUpdateCheckIntervalUnit.Hours));

        configuration
            .GetSection(ApplicationUpdateSettings.SectionName)
            .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
            .Set("999");

        var numericInvalidSettings = CreateSettingsViewModel(configuration);

        await Assert.That(numericInvalidSettings.UpdateCheckIntervalUnit)
            .IsEqualTo(ApplicationUpdateCheckIntervalUnit.Hours);
    }

    [Test]
    public async System.Threading.Tasks.Task AutomaticUpdateCheckAsync_ChecksAndDownloadsAvailableUpdate()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            NextUpdate = new ApplicationUpdateInfo("2.0.0")
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);
        var app = new App();

        await app.RunAutomaticUpdateCheckAsync(settings);

        await Assert.That(updateService.CheckCalls).IsEqualTo(1);
        await Assert.That(updateService.DownloadCalls).IsEqualTo(1);
        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.ReadyToApply);
        await Assert.That(settings.AvailableUpdateVersion).IsEqualTo("2.0.0");
    }

    [Test]
    public async System.Threading.Tasks.Task AutomaticUpdateCheckAsync_SkipsDisabledAndOverlappingRuns()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var updateService = new FakeApplicationUpdateService
        {
            CheckCompletion = new System.Threading.Tasks.TaskCompletionSource<ApplicationUpdateInfo?>()
        };
        var settings = CreateSettingsViewModel(configuration);
        settings.ConfigureUpdateService(updateService);
        var app = new App();

        settings.UpdateAutoCheckEnabled = false;
        await app.RunAutomaticUpdateCheckAsync(settings);
        await Assert.That(updateService.CheckCalls).IsEqualTo(0);

        settings.UpdateAutoCheckEnabled = true;
        var firstCheck = app.RunAutomaticUpdateCheckAsync(settings);
        var secondCheck = app.RunAutomaticUpdateCheckAsync(settings);

        await Assert.That(updateService.CheckCalls).IsEqualTo(1);

        updateService.CheckCompletion.SetResult(null);
        await firstCheck;
        await secondCheck;

        await Assert.That(settings.UpdateState).IsEqualTo(ApplicationUpdateState.NoUpdates);
    }

    [Test]
    public async System.Threading.Tasks.Task BackupAuthMode_DerivesFromRemoteUrl_WhenRepositoryRemotesAreUnavailable()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.GitRemoteUrl = "git@github.com:org/unlimotion-backup.git";

        await Assert.That(settings.BackupAuthMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.IsSshAuthSelected).IsTrue();
        await Assert.That(settings.BackupAuthModeText).IsEqualTo("SSH");
    }

    [Test]
    public async System.Threading.Tasks.Task BackupConnection_BecomesReadyForClone_WhenSshKeyIsSelected()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));
        localization.SetLanguage(LocalizationService.RussianLanguage);
        var settings = new SettingsViewModel(configuration, localizationService: localization)
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
            },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/origin.git",
                ["backup"] = "git@github.com:org/backup.git"
            }
        };

        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration, backupService);

        settings.GitRemoteNameDisplay = "backup (SSH)";

        await Assert.That(settings.BackupAuthMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.IsSshAuthSelected).IsTrue();
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/backup.git");
    }

    [Test]
    public async System.Threading.Tasks.Task FontSize_PersistsAndNormalizesConfiguredValue()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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
    public async System.Threading.Tasks.Task LanguageMode_DoesNotCultureFormatPersistedNumericSettings()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));
        var settings = new SettingsViewModel(configuration, localizationService: localization);

        settings.LanguageMode = LocalizationService.RussianLanguage;
        settings.FontSize = 18.5;
        settings.GitPullIntervalSeconds = 45;
        settings.GitPushIntervalSeconds = 90;

        var json = File.ReadAllText(_configPath);

        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
        await Assert.That(json).Contains("18.5");
        await Assert.That(json).DoesNotContain("18,5");
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.FontSizeKey)
                .Get<double>())
            .IsEqualTo(18.5);
        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.PullIntervalSeconds))
                .Get<int>())
            .IsEqualTo(45);
        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.PushIntervalSeconds))
                .Get<int>())
            .IsEqualTo(90);
    }

    [Test]
    public async System.Threading.Tasks.Task LanguageMode_PersistsChoiceAndUpdatesLocalizedStatusText()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        var settings = new SettingsViewModel(configuration, localizationService: localization)
        {
            TaskStoragePath = @"C:\Data\Tasks"
        };

        await Assert.That(settings.StorageStatusText).IsEqualTo("Connected to local storage.");

        settings.LanguageMode = LocalizationService.RussianLanguage;

        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.LanguageKey)
                .Get<string>())
            .IsEqualTo(LocalizationService.RussianLanguage);
        await Assert.That(settings.StorageStatusText).IsEqualTo("Подключено к локальному хранилищу.");
        await Assert.That(settings.GitBackupOnboardingHint).Contains("Для первого подключения");
    }

    [Test]
    public async System.Threading.Tasks.Task LanguageModeIndex_ReturnsToCapturedSystemLanguage()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));
        var settings = new SettingsViewModel(configuration, localizationService: localization);

        settings.LanguageMode = LocalizationService.EnglishLanguage;
        await Assert.That(settings.BackupStatusText).IsEqualTo("Backup is disabled.");

        settings.LanguageModeIndex = 0;

        await Assert.That(settings.LanguageMode).IsEqualTo(LocalizationService.SystemLanguage);
        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
        await Assert.That(settings.BackupStatusText).IsEqualTo("Резервное копирование выключено.");
    }

    [Test]
    public async System.Threading.Tasks.Task LanguageModeIndex_IgnoresTransientInvalidSelectionIndex()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        var settings = new SettingsViewModel(configuration, localizationService: localization);

        settings.LanguageMode = LocalizationService.RussianLanguage;
        settings.LanguageModeIndex = -1;

        await Assert.That(settings.LanguageMode).IsEqualTo(LocalizationService.RussianLanguage);
        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
    }

    [Test]
    public async System.Threading.Tasks.Task LanguageOptions_KeepCollectionInstanceWhenLanguageChanges()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        var settings = new SettingsViewModel(configuration, localizationService: localization);
        var options = settings.LanguageOptions;

        await Assert.That(options[0].DisplayName).IsEqualTo("System");

        settings.LanguageModeIndex = 2;

        await Assert.That(settings.LanguageOptions).IsSameReferenceAs(options);
        await Assert.That(settings.LanguageModeIndex).IsEqualTo(2);
        await Assert.That(options[0].DisplayName).IsEqualTo("Как в системе");
        await Assert.That(options[1].DisplayName).IsEqualTo("Английский");
        await Assert.That(options[2].DisplayName).IsEqualTo("Русский");
    }

    [Test]
    public async System.Threading.Tasks.Task CanConnectStorage_FollowsSelectedModeRequirements()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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
    public async System.Threading.Tasks.Task PrepareLocalStorageConnectionAsync_PullsBetweenPrepareAndSwitch_WhenPathChanges()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var backupService = new FakeRemoteBackupService();
        var settings = new SettingsViewModel(configuration, backupService, localizationService: new FakeLocalizationService())
        {
            TaskStoragePath = Path.Combine(Environment.CurrentDirectory, "NextTasks")
        };
        var events = new List<string>();
        backupService.PullExistingRepositoryAction = () => events.Add("pull");

        var shouldContinue = await App.PrepareLocalStorageConnectionAsync(
            settings,
            backupService,
            Path.Combine(Environment.CurrentDirectory, "CurrentTasks"),
            path =>
            {
                events.Add($"prepare:{path}");
                return System.Threading.Tasks.Task.CompletedTask;
            },
            _ => events.Add("conflict"));
        if (shouldContinue)
        {
            events.Add("switch");
        }

        await Assert.That(shouldContinue).IsTrue();
        await Assert.That(backupService.PullExistingRepositoryCalls).IsEqualTo(1);
        await Assert.That(string.Join("|", events))
            .IsEqualTo($"prepare:{settings.TaskStoragePath}|pull|switch");
    }

    [Test]
    public async System.Threading.Tasks.Task PrepareLocalStorageConnectionAsync_SkipsPull_WhenReconnectingSameLocalPath()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var backupService = new FakeRemoteBackupService();
        var selectedPath = Path.Combine(Environment.CurrentDirectory, "SameTasks");
        var settings = new SettingsViewModel(configuration, backupService, localizationService: new FakeLocalizationService())
        {
            TaskStoragePath = selectedPath
        };
        var events = new List<string>();

        var shouldContinue = await App.PrepareLocalStorageConnectionAsync(
            settings,
            backupService,
            selectedPath,
            path =>
            {
                events.Add($"prepare:{path}");
                return System.Threading.Tasks.Task.CompletedTask;
            },
            _ => events.Add("conflict"));
        if (shouldContinue)
        {
            events.Add("switch");
        }

        await Assert.That(shouldContinue).IsTrue();
        await Assert.That(backupService.PullExistingRepositoryCalls).IsEqualTo(0);
        await Assert.That(string.Join("|", events))
            .IsEqualTo($"prepare:{settings.TaskStoragePath}|switch");
    }

    [Test]
    public async System.Threading.Tasks.Task PrepareLocalStorageConnectionAsync_StopsBeforeSwitch_WhenPullFindsConflicts()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var backupService = new FakeRemoteBackupService();
        var settings = new SettingsViewModel(configuration, backupService, localizationService: new FakeLocalizationService())
        {
            TaskStoragePath = Path.Combine(Environment.CurrentDirectory, "ConflictedTasks")
        };
        var events = new List<string>();
        backupService.PullExistingRepositoryAction = () =>
        {
            events.Add("pull");
            backupService.ConflictStatus = new BackupConflictStatus(true, new List<BackupConflictFile>());
        };

        var shouldContinue = await App.PrepareLocalStorageConnectionAsync(
            settings,
            backupService,
            Path.Combine(Environment.CurrentDirectory, "CurrentTasks"),
            path =>
            {
                events.Add($"prepare:{path}");
                return System.Threading.Tasks.Task.CompletedTask;
            },
            _ => events.Add("conflict"));
        if (shouldContinue)
        {
            events.Add("switch");
        }

        await Assert.That(shouldContinue).IsFalse();
        await Assert.That(settings.IsConflictResolutionMode).IsTrue();
        await Assert.That(settings.IsStorageBusy).IsFalse();
        await Assert.That(backupService.PullExistingRepositoryCalls).IsEqualTo(1);
        await Assert.That(string.Join("|", events))
            .IsEqualTo($"prepare:{settings.TaskStoragePath}|pull|conflict");
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectCommand_AutoPullsDifferentLocalPathBeforeSwitchStorage()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var currentPath = Path.Combine(Environment.CurrentDirectory, $"CurrentTasks-{Guid.NewGuid():N}");
        var selectedPath = Path.Combine(Environment.CurrentDirectory, $"SelectedTasks-{Guid.NewGuid():N}");
        var events = new ConcurrentQueue<string>();
        var backupService = new FakeRemoteBackupService
        {
            PullExistingRepositoryAction = () => events.Enqueue("pull")
        };
        using var storageFactory = new RecordingTaskStorageFactory(currentPath, events);
        var settings = new SettingsViewModel(configuration, backupService, localizationService: new FakeLocalizationService())
        {
            TaskStoragePath = selectedPath
        };
        using var appScope = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);

        settings.ConnectCommand?.Execute(null);

        await WaitForConditionAsync(
            () => settings.StorageConnectionState == SettingsConnectionState.Connected,
            "Connect command did not finish.");
        await Assert.That(backupService.PullExistingRepositoryCalls).IsEqualTo(1);
        await Assert.That(string.Join("|", events)).IsEqualTo($"pull|switch-local:{selectedPath}");
        await Assert.That(storageFactory.CurrentFileStoragePath).IsEqualTo(selectedPath);
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectCommand_DoesNotAutoPull_WhenServerModeIsSelected()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var currentPath = Path.Combine(Environment.CurrentDirectory, $"CurrentTasks-{Guid.NewGuid():N}");
        var events = new ConcurrentQueue<string>();
        var backupService = new FakeRemoteBackupService
        {
            PullExistingRepositoryAction = () => events.Enqueue("pull")
        };
        using var storageFactory = new RecordingTaskStorageFactory(currentPath, events);
        var settings = new SettingsViewModel(configuration, backupService, localizationService: new FakeLocalizationService())
        {
            IsServerMode = true,
            ServerStorageUrl = "https://server.example",
            Login = "user@example",
            Password = "secret"
        };
        using var appScope = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);

        settings.ConnectCommand?.Execute(null);

        await WaitForConditionAsync(
            () => settings.StorageConnectionState == SettingsConnectionState.Connected,
            "Server connect command did not finish.");
        await Assert.That(backupService.PullExistingRepositoryCalls).IsEqualTo(0);
        await Assert.That(string.Join("|", events)).IsEqualTo("switch-server:https://server.example");
    }

    [Test]
    public async System.Threading.Tasks.Task EnsureDefaultTaskStoragePath_PersistsDefaultPathWhenLocalPathIsEmpty()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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
        IConfigurationRoot configuration = CreateConfiguration();
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
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = Path.Combine("Data", "Tasks");

        await Assert.That(settings.TaskStoragePathTooltip)
            .IsEqualTo(Path.GetFullPath(Path.Combine("Data", "Tasks")));
    }

    [Test]
    public async System.Threading.Tasks.Task TaskStoragePathTooltip_UsesActualFallbackPathWhenPathIsEmpty()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = string.Empty;

        await Assert.That(settings.TaskStoragePathTooltip)
            .IsEqualTo(Path.GetFullPath("Tasks"));
    }

    [Test]
    public async System.Threading.Tasks.Task TaskStoragePathTooltip_DoesNotThrowForInvalidIntermediateInput()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.TaskStoragePath = new string((char)0, 1);
        var tooltip = settings.TaskStoragePathTooltip;

        await Assert.That(tooltip).IsNotNull();
    }

    [Test]
    public async System.Threading.Tasks.Task SshKeyStorageEffectivePathText_UsesDefaultDirectoryWhenPathIsEmpty()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);
        var defaultDirectory = SshKeyStoragePathResolver.GetDefaultSshDirectory();

        settings.SshKeyStoragePath = string.Empty;

        await Assert.That(settings.EffectiveSshKeyStoragePath).IsEqualTo(defaultDirectory);
        await Assert.That(settings.SshKeyStorageEffectivePathText).Contains(defaultDirectory);
    }

    [Test]
    public async System.Threading.Tasks.Task SshKeyStorageEffectivePathText_UsesConfiguredFullPath()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);
        var configuredPath = Path.Combine("Data", "SshKeys");
        var expectedPath = Path.GetFullPath(configuredPath);

        settings.SshKeyStoragePath = configuredPath;

        await Assert.That(settings.EffectiveSshKeyStoragePath).IsEqualTo(expectedPath);
        await Assert.That(settings.SshKeyStorageEffectivePathText).Contains(expectedPath);
    }

    [Test]
    public async System.Threading.Tasks.Task SshKeyStoragePath_DoesNotThrowForInvalidIntermediateInput()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var backupService = new BackupViaGitService(configuration);
        var settings = new SettingsViewModel(configuration, backupService);
        var invalidPath = new string((char)0, 1);

        settings.SshKeyStoragePath = invalidPath;

        await Assert.That(settings.EffectiveSshKeyStoragePath).IsEqualTo(invalidPath);
        await Assert.That(settings.SshPublicKeys).IsEmpty();
        await Assert.That(settings.SelectedSshPublicKeyPath).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task LanguageMode_UpdatesSshKeyStorageEffectivePathText()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        localization.SetLanguage(LocalizationService.EnglishLanguage);
        var settings = new SettingsViewModel(configuration, localizationService: localization)
        {
            SshKeyStoragePath = Path.Combine("Data", "SshKeys")
        };
        var changedProperties = new List<string>();
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        await Assert.That(settings.SshKeyStorageEffectivePathText).Contains("Used folder:");

        settings.LanguageMode = LocalizationService.RussianLanguage;

        await Assert.That(settings.SshKeyStorageEffectivePathText).Contains("Будет использована папка:");
        await Assert.That(changedProperties).Contains(nameof(SettingsViewModel.SshKeyStorageEffectivePathText));
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

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("backup");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/unlimotion-backup.git");
    }

    [Test]
    public async System.Threading.Tasks.Task RefreshGitMetadataCommand_FillsEmptyRepositoryUrlFromCurrentLocalStorage()
    {
        var localPath = Path.Combine(Environment.CurrentDirectory, $"RemoteRefreshTasks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localPath);
        Repository.Init(localPath);
        using (var repo = new Repository(localPath))
        {
            repo.Network.Remotes.Add("origin", "git@github.com:org/unlimotion-backup.git");
        }

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(string.Empty);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set(string.Empty);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set(string.Empty);
        using var storageFactory = new RecordingTaskStorageFactory(localPath, new ConcurrentQueue<string>());
        var backupService = new BackupViaGitService(configuration, storageFactory: storageFactory);
        var settings = new SettingsViewModel(configuration, backupService);
        using var appFields = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);
        settings.GitRemoteName = null;
        settings.GitRemoteUrl = string.Empty;

        settings.RefreshGitMetadataCommand!.Execute(null);

        await Assert.That(settings.GitRemoteName).IsEqualTo("origin");
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

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/custom.git");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteUrl).IsEqualTo("https://example.com/custom.git");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_UpdatesRepositoryUrlWhenItBelongsToAnotherRemote()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin", "github.com" },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/Kibnet/Unlimotion.Tasks.git",
                ["github.com"] = "git@github.com:Kibnet/Unlimotion.Tasks.git"
            }
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("github.com");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl))
            .Set("https://github.com/Kibnet/Unlimotion.Tasks.git");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:Kibnet/Unlimotion.Tasks.git");
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

        IConfigurationRoot configuration = CreateConfiguration();
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

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("missing");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitRemoteName).IsEqualTo("origin");
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/origin.git");
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionTypeCommand_UpdatesSelectedRemoteFromServiceResult()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin" },
            RemoteAuthTypes = new Dictionary<string, string>
            {
                ["origin"] = "HTTP"
            },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/repo.git"
            }
        };
        backupService.SwitchRemoteConnectionTypeHandler = (remoteName, targetMode) =>
        {
            backupService.RemoteNames.Add("origin-ssh");
            backupService.RemoteAuthTypes["origin-ssh"] = "SSH";
            backupService.RemoteUrls["origin-ssh"] = "git@github.com:org/repo.git";
            return new RemoteConnectionTypeSwitchResult(
                "origin-ssh",
                "git@github.com:org/repo.git",
                "SSH",
                CreatedRemote: true);
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://github.com/org/repo.git");
        var settings = new SettingsViewModel(configuration, backupService);
        using var storageFactory = new RecordingTaskStorageFactory(
            Path.Combine(Environment.CurrentDirectory, $"RemoteSwitchTasks-{Guid.NewGuid():N}"),
            new ConcurrentQueue<string>());
        using var appFields = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);

        settings.SwitchRemoteConnectionTypeCommand!.Execute("SSH");

        await WaitForConditionAsync(
            () => backupService.SwitchRemoteConnectionTypeCalls == 1 &&
                  settings.GitRemoteName == "origin-ssh",
            "Remote connection type switch did not complete.");

        await Assert.That(backupService.LastSwitchRemoteName).IsEqualTo("origin");
        await Assert.That(backupService.LastSwitchTargetMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("git@github.com:org/repo.git");
        await Assert.That(settings.BackupAuthMode).IsEqualTo(BackupAuthMode.Ssh);
        await Assert.That(settings.IsSshAuthSelected).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionTypeCommand_KeepsSshKeyRequirementWhenNoKeyIsSelected()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin" },
            RemoteAuthTypes = new Dictionary<string, string>
            {
                ["origin"] = "HTTP"
            },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/repo.git"
            }
        };
        backupService.SwitchRemoteConnectionTypeHandler = (_, _) =>
        {
            backupService.RemoteNames.Add("origin-ssh");
            backupService.RemoteAuthTypes["origin-ssh"] = "SSH";
            backupService.RemoteUrls["origin-ssh"] = "git@github.com:org/repo.git";
            return new RemoteConnectionTypeSwitchResult(
                "origin-ssh",
                "git@github.com:org/repo.git",
                "SSH",
                CreatedRemote: true);
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://github.com/org/repo.git");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.UserName)).Set("user");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Password)).Set("token");
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        localization.SetLanguage(LocalizationService.EnglishLanguage);
        var settings = new SettingsViewModel(configuration, backupService, localizationService: localization);
        using var storageFactory = new RecordingTaskStorageFactory(
            Path.Combine(Environment.CurrentDirectory, $"RemoteSwitchNoKeyTasks-{Guid.NewGuid():N}"),
            new ConcurrentQueue<string>());
        using var appFields = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);

        await Assert.That(settings.BackupConnectionState).IsEqualTo(BackupStatusState.Connected);

        settings.SwitchRemoteToSshCommand!.Execute(null);

        await WaitForConditionAsync(
            () => backupService.SwitchRemoteConnectionTypeCalls == 1 &&
                  settings.GitRemoteName == "origin-ssh" &&
                  settings.BackupConnectionState != BackupStatusState.Connecting,
            "Remote connection type switch did not complete.");

        await Assert.That(settings.BackupConnectionState).IsEqualTo(BackupStatusState.NotConfigured);
        await Assert.That(settings.CanConnectRepository).IsFalse();
        await Assert.That(settings.BackupStatusText).IsEqualTo("Select an SSH key.");
    }

    [Test]
    public async System.Threading.Tasks.Task ReloadGitMetadata_DoesNotSwitchRemoteConnectionType()
    {
        var backupService = new FakeRemoteBackupService
        {
            RemoteNames = new List<string> { "origin" },
            RemoteAuthTypes = new Dictionary<string, string>
            {
                ["origin"] = "HTTP"
            },
            RemoteUrls = new Dictionary<string, string>
            {
                ["origin"] = "https://github.com/org/repo.git"
            }
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        var settings = new SettingsViewModel(configuration, backupService);
        using var storageFactory = new RecordingTaskStorageFactory(
            Path.Combine(Environment.CurrentDirectory, $"RemoteReloadTasks-{Guid.NewGuid():N}"),
            new ConcurrentQueue<string>());
        using var appFields = ConfigureAppSettingsCommands(settings, configuration, backupService, storageFactory);

        settings.ReloadGitMetadata();

        await Assert.That(backupService.SwitchRemoteConnectionTypeCalls).IsEqualTo(0);
        await Assert.That(settings.GitRemoteName).IsEqualTo("origin");
        await Assert.That(settings.GitRemoteUrl).IsEqualTo("https://github.com/org/repo.git");
    }

    [Test]
    public async System.Threading.Tasks.Task GitPushRefSpec_FallsBackToCanonicalBranchWhenPushRefSpecIsEmpty()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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

        IConfigurationRoot configuration = CreateConfiguration();
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

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set("refs/heads/release");
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.GitPushRefSpec).IsEqualTo("refs/heads/release");
    }

    [Test]
    public async System.Threading.Tasks.Task CanSyncRepository_RequiresBackupRemoteAndPushRefSpecWithoutConnectedState()
    {
        IConfigurationRoot configuration = CreateConfiguration();
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
    public async System.Threading.Tasks.Task ConflictResolutionMode_DisablesSyncAndEnablesSelectedConflictActions()
    {
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(
                true,
                new List<BackupConflictFile>
                {
                    new(
                        "Tasks/task.json",
                        true,
                        true,
                        new List<BackupConflictField>
                        {
                            new(
                                "Title",
                                "Title",
                                "Base title",
                                "Current title",
                                "Incoming title",
                                "Current title\nIncoming title",
                                true,
                                BackupConflictFieldSource.UseCurrent,
                                BackupConflictFieldChangeKind.BothDifferent,
                                true)
                        })
                })
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/repo.git");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set("refs/heads/main");

        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.IsConflictResolutionMode).IsTrue();
        await Assert.That(settings.HasBackupConflictFiles).IsTrue();
        await Assert.That(settings.SelectedBackupConflict?.Path).IsEqualTo("Tasks/task.json");
        await Assert.That(settings.CanSyncRepository).IsFalse();
        await Assert.That(settings.CanResolveSelectedConflictUseCurrent).IsTrue();
        await Assert.That(settings.CanResolveSelectedConflictUseIncoming).IsTrue();
        await Assert.That(settings.CanResolveSelectedConflictByFields).IsTrue();
        await Assert.That(settings.CanCommitConflictResolution).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task ConflictResolutionMode_DeleteModifyConflict_AllowsSelectingDeletedSide()
    {
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(
                true,
                new List<BackupConflictFile>
                {
                    new("Tasks/deleted-task.json", true, false)
                })
        };

        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration, backupService);

        await Assert.That(settings.IsConflictResolutionMode).IsTrue();
        await Assert.That(settings.SelectedBackupConflict?.Path).IsEqualTo("Tasks/deleted-task.json");
        await Assert.That(settings.CanResolveSelectedConflictUseCurrent).IsTrue();
        await Assert.That(settings.CanResolveSelectedConflictUseIncoming).IsTrue();
        await Assert.That(settings.CanResolveSelectedConflictByFields).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task GetSelectedBackupConflictFieldSelections_ReturnsCurrentUiDecisions()
    {
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(
                true,
                new List<BackupConflictFile>
                {
                    new(
                        "Tasks/task.json",
                        true,
                        true,
                        new List<BackupConflictField>
                        {
                            new(
                                "Title",
                                "Title",
                                "Base title",
                                "Current title",
                                "Incoming title",
                                "Current title\nIncoming title",
                                true,
                                BackupConflictFieldSource.UseCurrent,
                                BackupConflictFieldChangeKind.BothDifferent,
                                true)
                        })
                })
        };

        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration, backupService);

        settings.SelectedBackupConflictFields[0].IsMergeSelected = true;
        settings.SelectedBackupConflictFields[0].EditedMergedValue = "Edited merged title";
        var selections = settings.GetSelectedBackupConflictFieldSelections();

        await Assert.That(selections.Count).IsEqualTo(1);
        await Assert.That(selections[0].FieldPath).IsEqualTo("Title");
        await Assert.That(selections[0].Source).IsEqualTo(BackupConflictFieldSource.Merge);
        await Assert.That(selections[0].CustomValue).IsEqualTo("Edited merged title");
        await Assert.That(settings.SelectedBackupConflictFields[0].SelectedValue)
            .IsEqualTo("Edited merged title");
    }

    [Test]
    public async System.Threading.Tasks.Task ConflictResolutionMode_AllConflictsResolved_EnablesFinishAction()
    {
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(true, new List<BackupConflictFile>())
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/repo.git");

        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        localization.SetLanguage(LocalizationService.EnglishLanguage);
        var settings = new SettingsViewModel(configuration, backupService, localizationService: localization);

        await Assert.That(settings.IsConflictResolutionMode).IsTrue();
        await Assert.That(settings.HasBackupConflictFiles).IsFalse();
        await Assert.That(settings.CanCommitConflictResolution).IsTrue();
        await Assert.That(settings.BackupStatusText).IsEqualTo("All sync conflicts are resolved. Finish conflict resolution to continue.");
    }

    [Test]
    public async System.Threading.Tasks.Task CompleteConflictResolution_ClearsConflictModeAndRestoresSyncAvailability()
    {
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(true, new List<BackupConflictFile>())
        };

        IConfigurationRoot configuration = CreateConfiguration();
        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/repo.git");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set("origin");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set("refs/heads/main");

        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        localization.SetLanguage(LocalizationService.EnglishLanguage);
        var settings = new SettingsViewModel(configuration, backupService, localizationService: localization);

        settings.CompleteConflictResolution();

        await Assert.That(settings.IsConflictResolutionMode).IsFalse();
        await Assert.That(settings.HasBackupConflictFiles).IsFalse();
        await Assert.That(settings.CanCommitConflictResolution).IsFalse();
        await Assert.That(settings.CanSyncRepository).IsTrue();
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

        IConfigurationRoot configuration = CreateConfiguration();
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

        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration, backupService);

        settings.ReloadSshPublicKeys(@"C:\Users\Test\.ssh\id_second.pub");

        await Assert.That(settings.SelectedSshPublicKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_second.pub");
        await Assert.That(settings.GitSshPrivateKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_second");
    }

    [Test]
    public async System.Threading.Tasks.Task SshKeyStoragePath_PersistsChoiceAndClearsSelectionOutsideReloadedKeys()
    {
        var backupService = new FakeRemoteBackupService
        {
            PublicKeys = new List<string>
            {
                @"C:\Users\Test\.ssh\id_first.pub"
            }
        };

        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration, backupService);
        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_first.pub";

        backupService.PublicKeys = new List<string>
        {
            @"D:\Keys\id_second.pub"
        };
        settings.SshKeyStoragePath = @"D:\Keys";

        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.SshKeyStoragePath))
                .Get<string>())
            .IsEqualTo(@"D:\Keys");
        await Assert.That(settings.SshPublicKeys).IsEquivalentTo(new[] { @"D:\Keys\id_second.pub" });
        await Assert.That(settings.SelectedSshPublicKeyPath).IsNull();
        await Assert.That(settings.GitSshPrivateKeyPath).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task SelectedSshPublicKeyPath_UpdatesPrivateKeyPath()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_ed25519.pub";

        await Assert.That(settings.GitSshPublicKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_ed25519.pub");
        await Assert.That(settings.GitSshPrivateKeyPath).IsEqualTo(@"C:\Users\Test\.ssh\id_ed25519");
    }

    [Test]
    public async System.Threading.Tasks.Task SelectedSshPublicKeyPath_ClearsPrivateKeyPathWhenSelectionIsCleared()
    {
        IConfigurationRoot configuration = CreateConfiguration();
        var settings = new SettingsViewModel(configuration);

        settings.SelectedSshPublicKeyPath = @"C:\Users\Test\.ssh\id_ed25519.pub";
        settings.SelectedSshPublicKeyPath = null;

        await Assert.That(settings.GitSshPublicKeyPath).IsNull();
        await Assert.That(settings.GitSshPrivateKeyPath).IsNull();
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
        await Assert.That(command).Contains("StrictHostKeyChecking=accept-new");
    }

    private static IDisposable ConfigureAppSettingsCommands(
        SettingsViewModel settings,
        IConfiguration configuration,
        IRemoteBackupService backupService,
        ITaskStorageFactory storageFactory)
    {
        const BindingFlags fieldFlags = BindingFlags.Static | BindingFlags.NonPublic;
        var appType = typeof(App);
        var fieldValues = new Dictionary<string, object?>
        {
            ["_configuration"] = configuration,
            ["_backupService"] = backupService,
            ["_storageFactory"] = storageFactory,
            ["_mainWindowViewModel"] = null,
            ["_notificationManager"] = null
        };
        var previousValues = fieldValues.ToDictionary(
            pair => pair.Key,
            pair => GetRequiredAppField(appType, pair.Key, fieldFlags).GetValue(null));

        foreach (var pair in fieldValues)
        {
            GetRequiredAppField(appType, pair.Key, fieldFlags).SetValue(null, pair.Value);
        }

        var setupSettingsCommands = appType.GetMethod(
                                        "SetupSettingsCommands",
                                        BindingFlags.Instance | BindingFlags.NonPublic)
                                    ?? throw new MissingMethodException(nameof(App), "SetupSettingsCommands");
        setupSettingsCommands.Invoke(new App(), [settings]);

        return new DelegateDisposable(() =>
        {
            foreach (var pair in previousValues)
            {
                GetRequiredAppField(appType, pair.Key, fieldFlags).SetValue(null, pair.Value);
            }
        });
    }

    private static FieldInfo GetRequiredAppField(Type appType, string fieldName, BindingFlags flags) =>
        appType.GetField(fieldName, flags)
        ?? throw new MissingFieldException(nameof(App), fieldName);

    private static async System.Threading.Tasks.Task WaitForConditionAsync(
        Func<bool> condition,
        string failureMessage,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await System.Threading.Tasks.Task.Delay(10);
        }

        throw new TimeoutException(failureMessage);
    }

    private sealed class RecordingTaskStorageFactory : ITaskStorageFactory, IDisposable
    {
        private readonly ConcurrentQueue<string> _events;
        private readonly List<IDisposable> _storages = new();
        private readonly List<string> _paths = new();

        public RecordingTaskStorageFactory(string currentPath, ConcurrentQueue<string> events)
        {
            _events = events;
            CurrentStorage = CreateFileStorageWithoutRecording(currentPath);
        }

        public ITaskStorage? CurrentStorage { get; private set; }
        public IDatabaseWatcher? CurrentWatcher => null;
        public string? CurrentFileStoragePath => (CurrentStorage?.TaskTreeManager.Storage as FileStorage)?.Path;

        public ITaskStorage CreateFileStorage(string? path)
        {
            _events.Enqueue($"switch-local:{path}");
            CurrentStorage = CreateFileStorageWithoutRecording(path);
            return CurrentStorage;
        }

        public ITaskStorage CreateServerStorage(string? url)
        {
            _events.Enqueue($"switch-server:{url}");
            CurrentStorage = CreateFileStorageWithoutRecording(
                Path.Combine(Environment.CurrentDirectory, $"ServerStoragePlaceholder-{Guid.NewGuid():N}"));
            return CurrentStorage;
        }

        public void SwitchStorage(bool isServerMode, IConfiguration configuration)
        {
            var settings = configuration.Get<TaskStorageSettings>("TaskStorage");
            if (isServerMode)
            {
                CreateServerStorage(settings?.URL);
                return;
            }

            CreateFileStorage(settings?.Path);
        }

        public void Dispose()
        {
            foreach (var storage in _storages)
            {
                storage.Dispose();
            }

            foreach (var path in _paths)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        private ITaskStorage CreateFileStorageWithoutRecording(string? path)
        {
            var storagePath = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Environment.CurrentDirectory, $"Tasks-{Guid.NewGuid():N}")
                : path;
            var fileStorage = new FileStorage(storagePath, watcher: false);
            var storage = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));
            _paths.Add(fileStorage.Path);
            _storages.Add(storage);
            return storage;
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class FakeRemoteBackupService : IRemoteBackupService
    {
        public List<string> PublicKeys { get; set; } = new();
        public List<string> RemoteNames { get; set; } = new();
        public List<string> ReferenceNames { get; set; } = new();
        public Dictionary<string, string> RemoteAuthTypes { get; set; } = new();
        public Dictionary<string, string> RemoteUrls { get; set; } = new();
        public BackupConflictStatus ConflictStatus { get; set; } = BackupConflictStatus.None;
        public int PullExistingRepositoryCalls { get; private set; }
        public int SwitchRemoteConnectionTypeCalls { get; private set; }
        public string? LastSwitchRemoteName { get; private set; }
        public BackupAuthMode? LastSwitchTargetMode { get; private set; }
        public Action? PullExistingRepositoryAction { get; set; }
        public Func<string, BackupAuthMode, RemoteConnectionTypeSwitchResult>? SwitchRemoteConnectionTypeHandler { get; set; }

        public List<string> Remotes() => new(RemoteNames);
        public string? GetRemoteAuthType(string remoteName) =>
            RemoteAuthTypes.TryGetValue(remoteName, out var authType) ? authType : null;
        public string? GetRemoteUrl(string remoteName) =>
            RemoteUrls.TryGetValue(remoteName, out var remoteUrl) ? remoteUrl : null;
        public RemoteConnectionTypeSwitchResult SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode)
        {
            SwitchRemoteConnectionTypeCalls++;
            LastSwitchRemoteName = remoteName;
            LastSwitchTargetMode = targetMode;
            return SwitchRemoteConnectionTypeHandler?.Invoke(remoteName, targetMode)
                   ?? throw new NotSupportedException();
        }
        public List<string> Refs() => new(ReferenceNames);
        public List<string> GetSshPublicKeys() => new(PublicKeys);
        public string GenerateSshKey(string keyName) => throw new NotSupportedException();
        public string? ReadPublicKey(string publicKeyPath) => throw new NotSupportedException();
        public BackupConflictStatus GetConflictStatus() => ConflictStatus;
        public void ResolveConflict(string path, BackupConflictResolution resolution) => throw new NotSupportedException();
        public void ResolveConflictFields(string path, IReadOnlyList<BackupConflictFieldSelection> fieldSelections) => throw new NotSupportedException();
        public void CommitResolvedConflicts(string message) => throw new NotSupportedException();
        public void Push(string msg) => throw new NotSupportedException();
        public void Pull() => throw new NotSupportedException();
        public void PullExistingRepository()
        {
            PullExistingRepositoryCalls++;
            PullExistingRepositoryAction?.Invoke();
        }
        public BackupRepositoryConnectPreview PreviewConnectRepository() => throw new NotSupportedException();
        public void ConnectRepository(bool allowMergeWithNonEmptyRemote) => throw new NotSupportedException();
        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }

    private static SettingsViewModel CreateSettingsViewModel(IConfiguration configuration)
    {
        return new SettingsViewModel(configuration, localizationService: new FakeLocalizationService());
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public event EventHandler? CultureChanged
        {
            add { }
            remove { }
        }

        public System.Globalization.CultureInfo CurrentCulture { get; } =
            System.Globalization.CultureInfo.GetCultureInfo("en");

        public string LanguageMode { get; private set; } = LocalizationService.EnglishLanguage;

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new LanguageOption(LocalizationService.EnglishLanguage, "English")
        ];

        public void SetLanguage(string? languageMode)
        {
            LanguageMode = string.IsNullOrWhiteSpace(languageMode)
                ? LocalizationService.EnglishLanguage
                : languageMode;
        }

        public string Get(string key) => key;

        public string Format(string key, params object?[] args) => $"{key}: {string.Join(", ", args)}";

        public IReadOnlyCollection<string> GetResourceKeys(System.Globalization.CultureInfo culture) =>
            Array.Empty<string>();
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        public bool IsSupported { get; set; } = true;
        public string CurrentVersion { get; set; } = "1.0.0";
        public ApplicationUpdateInfo? PendingUpdate { get; set; }
        public ApplicationUpdateInfo? NextUpdate { get; set; }
        public System.Threading.Tasks.TaskCompletionSource<ApplicationUpdateInfo?>? CheckCompletion { get; set; }
        public Exception? ApplyException { get; set; }
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public System.Threading.Tasks.Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return CheckCompletion?.Task ?? System.Threading.Tasks.Task.FromResult(NextUpdate);
        }

        public System.Threading.Tasks.Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void ApplyUpdateAndRestart()
        {
            ApplyCalls++;

            if (ApplyException != null)
            {
                throw ApplyException;
            }
        }
    }

    private sealed class FakeSystemCultureProvider : ILocalizationSystemCultureProvider
    {
        public FakeSystemCultureProvider(string cultureName)
        {
            SystemUICulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        }

        public System.Globalization.CultureInfo SystemUICulture { get; }
    }
}
