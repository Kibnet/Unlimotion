using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PropertyChanged;
using Unlimotion.ViewModel.Localization;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SettingsViewModel
{
    private const string ClientSettingsSectionName = "ClientSettings";
    private const string ClientLoginKey = "Login";
    private const string DefaultTaskStoragePath = "Tasks";
    private const string NoteVaultSectionName = "NoteVault";
    private const string NoteVaultRootPathKey = "RootPath";
    private const string NoteVaultDayBoundaryMinutesKey = "DayBoundaryMinutes";
    private const string NoteVaultIsFeedEnabledKey = "IsFeedEnabled";
    private const string DefaultNoteDailyFileNameFormat = "yyyy-MM-dd";
    private const string TaskOutlineClipboardSectionName = "TaskOutlineClipboard";
    private const string TaskOutlineCopyAsMarkdownKey = "CopyAsMarkdown";
    private const string TaskOutlineCopyDescriptionKey = "CopyDescription";
    private const string TaskTreeExpansionStateSectionName = "TaskTreeExpansionState";
    private const string TaskTreeExpansionStateEnabledKey = "Enabled";

    private readonly IConfiguration _configuration;
    private readonly IConfiguration _taskStorageSettings;
    private readonly IConfiguration _noteVaultSettings;
    private readonly IConfiguration _gitSettings;
    private readonly IConfiguration _appearanceSettings;
    private readonly IConfiguration _taskOutlineClipboardSettings;
    private readonly IConfiguration _taskTreeExpansionStateSettings;
    private readonly IConfiguration _updateSettings;
    private readonly IRemoteBackupService? _backupService;
    private readonly ILocalizationService _localization;
    private readonly bool _defaultIsDarkTheme;
    private readonly bool _isExternalNoteVaultSupported;
    private readonly Func<string?>? _defaultTaskStoragePathProvider;
    private static readonly Regex DailyNoteFileNameFormatPattern = new(
        "^(?:yyyy|MM|dd)(?:[-._]?(?:yyyy|MM|dd)){2}$",
        RegexOptions.CultureInvariant);
    private bool _deferTaskSpaceSettingsPersistence;
    private IApplicationUpdateService? _applicationUpdateService;
    private ApplicationUpdateInfo? _availableUpdate;
    private string? _updateStatusOverride;

    private ThemeMode _themeMode;
    private string? _taskStoragePath;
    private string? _noteVaultRootPath;
    private TimeSpan _noteDayBoundary;
    private bool _isFeedEnabled;
    private string _noteDailyFileNameFormatDraft = DefaultNoteDailyFileNameFormat;
    private string _appliedNoteDailyFileNameFormat = DefaultNoteDailyFileNameFormat;
    // Every Feed session replacement makes the original Settings operation
    // stale. Only its own confirmed local Apply result may cross a same-root
    // replacement.
    private long _noteDailyFileNameFormatOperationGeneration;
    // A local Apply may survive only a same-root Feed rebind. Changes to the
    // selected vault, feed enablement or bridge invalidate it even if a path is
    // later selected again.
    private long _noteDailyFileNameFormatApplyContextGeneration;
    private long? _noteDailyFileNameFormatFeedSessionGeneration;
    private string? _activeNoteDailyFileNameFormatFeedRootPath;
    private bool _isNoteDailyFileNameFormatFeedInitialized;
    private bool _isNoteDailyFileNameFormatFeedBusyOrRecovering;
    private Func<string, NoteDailyFileNameFormatValidation>? _noteDailyFileNameFormatValidator;
    private Func<string, Task<NoteDailyFileNameFormatApplyResult>>? _applyNoteDailyFileNameFormatAsync;
    private Func<Task<NoteDailyFileNameFormatState>>? _reloadNoteDailyFileNameFormatAsync;
    private string? _taskStorageUrl;
    private string? _login;
    private string? _password;
    private bool _isServerMode;
    private bool _gitBackupEnabled;
    private bool _gitShowStatusToasts;
    private string? _gitRemoteUrl;
    private string? _gitBranch;
    private string? _gitUserName;
    private string? _gitPassword;
    private int _gitPullIntervalSeconds;
    private int _gitPushIntervalSeconds;
    private string? _gitRemoteName;
    private string _gitPushRefSpec = string.Empty;
    private string? _gitCommitterName;
    private string? _gitCommitterEmail;
    private string? _gitSshPrivateKeyPath;
    private string? _gitSshPublicKeyPath;
    private string? _sshKeyStoragePath;
    private bool _copyTaskOutlineAsMarkdown;
    private bool _copyTaskOutlineDescription;
    private bool _persistTaskTreeExpansionState;
    private bool _updateAutoCheckEnabled;
    private int _updateCheckIntervalValue;
    private ApplicationUpdateCheckIntervalUnit _updateCheckIntervalUnit;
    private BackupConflictFile? _selectedBackupConflict;
    private TaskSpaceOptionViewModel? _selectedTaskSpace;
    private TaskSpaceOptionViewModel? _headerTaskSpace;

    public SettingsViewModel(
        IConfiguration configuration,
        IRemoteBackupService? backupService = null,
        bool defaultIsDarkTheme = false,
        Func<string?>? defaultTaskStoragePathProvider = null,
        ILocalizationService? localizationService = null,
        bool? isExternalNoteVaultSupported = null)
    {
        _configuration = configuration;
        _taskStorageSettings = configuration.GetSection("TaskStorage");
        _noteVaultSettings = configuration.GetSection(NoteVaultSectionName);
        _gitSettings = configuration.GetSection("Git");
        _appearanceSettings = configuration.GetSection(AppearanceSettings.SectionName);
        _taskOutlineClipboardSettings = configuration.GetSection(TaskOutlineClipboardSectionName);
        _taskTreeExpansionStateSettings = configuration.GetSection(TaskTreeExpansionStateSectionName);
        _updateSettings = configuration.GetSection(ApplicationUpdateSettings.SectionName);
        _backupService = backupService;
        _localization = localizationService ?? LocalizationService.Current;
        _defaultIsDarkTheme = defaultIsDarkTheme;
        _isExternalNoteVaultSupported = isExternalNoteVaultSupported ?? IsDesktopOperatingSystem();
        _defaultTaskStoragePathProvider = defaultTaskStoragePathProvider;

        _localization.SetLanguage(_appearanceSettings.GetSection(AppearanceSettings.LanguageKey).Get<string>());
        NewTaskSpaceName = _localization.Get("TaskSpacesDefaultName");
        _localization.CultureChanged += (_, __) => RefreshLocalizedText();

        _themeMode = AppearanceSettings.ParseThemeMode(
            _appearanceSettings.GetSection(AppearanceSettings.ThemeKey).Get<string>());
        _taskStoragePath = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Get<string>();
        _noteVaultRootPath = _noteVaultSettings.GetSection(NoteVaultRootPathKey).Get<string>();
        _noteDayBoundary = TimeSpan.FromMinutes(NormalizeDayBoundaryMinutes(
            _noteVaultSettings.GetSection(NoteVaultDayBoundaryMinutesKey).Get<int?>() ?? 0));
        _isFeedEnabled = _noteVaultSettings
            .GetSection(NoteVaultIsFeedEnabledKey)
            .Get<bool?>() ?? true;
        _taskStorageUrl = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Get<string>();
        _login = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Get<string>();
        _password = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Get<string>();
        _isServerMode = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Get<bool>();

        _gitBackupEnabled = _gitSettings.GetSection(nameof(GitSettings.BackupEnabled)).Get<bool>();
        _gitShowStatusToasts = _gitSettings.GetSection(nameof(GitSettings.ShowStatusToasts)).Get<bool>();
        _gitRemoteUrl = _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Get<string>();
        _gitBranch = _gitSettings.GetSection(nameof(GitSettings.Branch)).Get<string>();
        _gitUserName = _gitSettings.GetSection(nameof(GitSettings.UserName)).Get<string>();
        _gitPassword = _gitSettings.GetSection(nameof(GitSettings.Password)).Get<string>();
        _gitPullIntervalSeconds = _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Get<int>();
        _gitPushIntervalSeconds = _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Get<int>();
        _gitRemoteName = _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Get<string>();
        _gitPushRefSpec = _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Get<string>() ?? string.Empty;
        EnsureGitPushRefSpecFallback();
        _gitCommitterName = _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Get<string>();
        _gitCommitterEmail = _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Get<string>();
        _gitSshPrivateKeyPath = _gitSettings.GetSection(nameof(GitSettings.SshPrivateKeyPath)).Get<string>();
        _gitSshPublicKeyPath = _gitSettings.GetSection(nameof(GitSettings.SshPublicKeyPath)).Get<string>();
        _sshKeyStoragePath = _gitSettings.GetSection(nameof(GitSettings.SshKeyStoragePath)).Get<string>();
        _copyTaskOutlineAsMarkdown = _taskOutlineClipboardSettings.GetSection(TaskOutlineCopyAsMarkdownKey).Get<bool>();
        _copyTaskOutlineDescription = _taskOutlineClipboardSettings.GetSection(TaskOutlineCopyDescriptionKey).Get<bool>();
        _persistTaskTreeExpansionState = _taskTreeExpansionStateSettings
            .GetSection(TaskTreeExpansionStateEnabledKey)
            .Get<bool>();
        _updateAutoCheckEnabled = _updateSettings
            .GetSection(ApplicationUpdateSettings.AutoCheckEnabledKey)
            .Get<bool?>() ?? ApplicationUpdateSettings.DefaultAutoCheckEnabled;
        _updateCheckIntervalValue = ApplicationUpdateSettings.NormalizeCheckIntervalValue(
            _updateSettings
                .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
                .Get<int?>() ?? ApplicationUpdateSettings.DefaultCheckIntervalValue);
        _updateCheckIntervalUnit = ApplicationUpdateSettings.ParseCheckIntervalUnit(
            _updateSettings
                .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
                .Get<string>());

        ConnectedServerLogin = GetStoredClientLogin();
        StorageConnectionState = IsServerMode ? SettingsConnectionState.Disconnected : SettingsConnectionState.Connected;
        BackupConnectionState = BackupStatusState.NotConfigured;

        ReloadSshPublicKeys();
        ReloadGitMetadata();
        RefreshStorageSelectionState();
        RefreshLocalizedText();
    }

    // Commands - set externally from App.axaml.cs
    public ICommand? ConnectCommand { get; set; }
    public ICommand? SignOutCommand { get; set; }
    public ICommand? SyncNowCommand { get; set; }
    public ICommand? MigrateCommand { get; set; }
    public ICommand? BackupCommand { get; set; }
    public ICommand? ResaveCommand { get; set; }
    public ICommand? BrowseTaskStoragePathCommand { get; set; }
    public ICommand? BrowseNoteVaultRootPathCommand { get; set; }
    public ICommand? ApplyNoteDailyFileNameFormatCommand { get; set; }
    public ICommand? ReloadExternalNoteDailyFileNameFormatCommand { get; set; }
    public ICommand? CloneCommand { get; set; }
    public ICommand? PullCommand { get; set; }
    public ICommand? PushCommand { get; set; }
    public ICommand? ResolveConflictUseCurrentCommand { get; set; }
    public ICommand? ResolveConflictUseIncomingCommand { get; set; }
    public ICommand? ResolveConflictUseFieldSelectionCommand { get; set; }
    public ICommand? RefreshBackupConflictsCommand { get; set; }
    public ICommand? CommitConflictResolutionCommand { get; set; }
    public ICommand? OpenConflictResolutionWindowCommand { get; set; }
    public ICommand? GenerateSshKeyCommand { get; set; }
    public ICommand? RefreshSshKeysCommand { get; set; }
    public ICommand? BrowseSshKeyStoragePathCommand { get; set; }
    public ICommand? RefreshGitMetadataCommand { get; set; }
    public ICommand? SwitchRemoteConnectionTypeCommand { get; set; }
    public ICommand? SwitchRemoteToHttpCommand { get; set; }
    public ICommand? SwitchRemoteToSshCommand { get; set; }
    public ICommand? CopySelectedSshKeyCommand { get; set; }
    public ICommand? CheckForUpdatesCommand { get; set; }
    public ICommand? DownloadUpdateCommand { get; set; }
    public ICommand? ApplyUpdateCommand { get; set; }
    public ICommand? AddTaskSpaceCommand { get; set; }
    public ICommand? SwitchTaskSpaceCommand { get; set; }
    public ICommand? RenameTaskSpaceCommand { get; set; }
    public ICommand? RemoveTaskSpaceCommand { get; set; }
    public ICommand? RetryTaskSpaceSettingsPersistenceCommand { get; set; }

    public ObservableCollection<TaskSpaceOptionViewModel> TaskSpaces { get; } = new();

    public TaskSpaceOptionViewModel? SelectedTaskSpace
    {
        get => _selectedTaskSpace;
        set
        {
            if (ReferenceEquals(_selectedTaskSpace, value))
            {
                return;
            }

            _selectedTaskSpace = value;
            RefreshTaskSpaceActionAvailability();
        }
    }

    public TaskSpaceOptionViewModel? HeaderTaskSpace
    {
        get => _headerTaskSpace;
        set
        {
            if (ReferenceEquals(_headerTaskSpace, value))
            {
                return;
            }

            _headerTaskSpace = value;
            if (value != null && !value.IsActive && SwitchTaskSpaceCommand?.CanExecute(value) == true)
            {
                SwitchTaskSpaceCommand.Execute(value);
            }
        }
    }

    public string NewTaskSpaceName { get; set; } = "New space";

    public bool IsTaskSpaceSwitching { get; set; }
    public bool IsTaskSpaceRecoveryRequired { get; set; }
    public string TaskSpaceRecoveryMessage { get; set; } = string.Empty;
    public bool CanRemoveTaskSpace { get; private set; }
    public bool IsTaskSpaceSettingsPersistenceStatusVisible { get; set; }
    public bool IsTaskSpaceSettingsPersistenceError { get; set; }
    public string TaskSpaceSettingsPersistenceStatus { get; set; } = string.Empty;

    public void ReloadTaskSpaces(IEnumerable<TaskSourceDescriptor> sources, string activeSourceId)
    {
        TaskSpaces.Clear();
        foreach (var source in sources)
        {
            TaskSpaces.Add(new TaskSpaceOptionViewModel
            {
                SourceId = source.Id,
                DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? source.Id : source.DisplayName,
                Kind = source.Kind,
                SourceSummary = source.Kind == TaskSourceKind.Server ? source.Url : source.Path,
                IsActive = string.Equals(source.Id, activeSourceId, StringComparison.Ordinal)
            });
        }

        var active = TaskSpaces.FirstOrDefault(space => space.IsActive) ?? TaskSpaces.FirstOrDefault();
        SelectedTaskSpace = active;
        HeaderTaskSpace = active;
        RefreshTaskSpaceActionAvailability();
    }

    public bool IsTaskSpaceRemovalBlockedByConflict(string sourceId) =>
        IsConflictResolutionMode &&
        TaskSpaces.Any(space =>
            space.IsActive && string.Equals(space.SourceId, sourceId, StringComparison.Ordinal));

    public void ReloadActiveTaskSpaceSettings()
    {
        var storage = _configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        TaskStoragePath = storage.Path;
        TaskStorageURL = storage.URL;
        Login = storage.Login;
        Password = storage.Password;
        IsServerMode = storage.IsServerMode;

        var note = _configuration.GetSection(NoteVaultSectionName);
        NoteVaultRootPath = note.GetSection(NoteVaultRootPathKey).Get<string>();
        IsFeedEnabled = note.GetSection(NoteVaultIsFeedEnabledKey).Get<bool?>() ?? true;
        NoteDayBoundary = TimeSpan.FromMinutes(NormalizeDayBoundaryMinutes(
            note.GetSection(NoteVaultDayBoundaryMinutesKey).Get<int?>() ?? 0));

        var git = _configuration.Get<GitSettings>("Git") ?? new GitSettings();
        GitBackupEnabled = git.BackupEnabled;
        GitShowStatusToasts = git.ShowStatusToasts;
        GitRemoteUrl = git.RemoteUrl;
        GitBranch = git.Branch;
        GitUserName = git.UserName;
        GitPassword = git.Password;
        GitPullIntervalSeconds = git.PullIntervalSeconds;
        GitPushIntervalSeconds = git.PushIntervalSeconds;
        GitRemoteName = git.RemoteName;
        GitPushRefSpec = git.PushRefSpec;
        GitCommitterName = git.CommitterName;
        GitCommitterEmail = git.CommitterEmail;
        GitSshPrivateKeyPath = git.SshPrivateKeyPath;
        GitSshPublicKeyPath = git.SshPublicKeyPath;
        SshKeyStoragePath = git.SshKeyStoragePath;
        ReloadSshPublicKeys();
        ReloadGitMetadata();
        RefreshStorageSelectionState();
        RefreshStorageStatusText();
    }

    private void PersistActiveNoteProfile()
    {
        var sourceId = _configuration
            .GetSection(TaskSourcesSettings.SectionName)
            .GetSection(nameof(TaskSourcesSettings.ActiveSourceId))
            .Get<string>();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var serialized = _configuration.Get<string>(TaskSourcesSettings.NoteProfilesSectionName);
        List<TaskSourceNoteSettings> profiles;
        try
        {
            profiles = string.IsNullOrWhiteSpace(serialized)
                ? []
                : JsonSerializer.Deserialize<List<TaskSourceNoteSettings>>(serialized) ?? [];
        }
        catch (JsonException)
        {
            return;
        }

        var profile = profiles.FirstOrDefault(candidate => string.Equals(
            candidate.SourceId,
            sourceId,
            StringComparison.Ordinal));
        if (profile is null)
        {
            profile = new TaskSourceNoteSettings { SourceId = sourceId };
            profiles.Add(profile);
        }

        profile.RootPath = _noteVaultRootPath;
        profile.IsFeedEnabled = _isFeedEnabled;
        profile.DayBoundaryMinutes = (int)_noteDayBoundary.TotalMinutes;
        _configuration.Set(
            TaskSourcesSettings.NoteProfilesSectionName,
            JsonSerializer.Serialize(profiles));
    }

    public void UseDeferredTaskSpaceSettingsPersistence() =>
        _deferTaskSpaceSettingsPersistence = true;

    public TaskSpaceSettingsDraft CreateTaskSpaceSettingsDraft(string sourceId) =>
        new()
        {
            SourceId = sourceId,
            Storage = new TaskStorageSettings
            {
                Path = TaskStoragePath ?? string.Empty,
                URL = TaskStorageURL ?? string.Empty,
                Login = Login ?? string.Empty,
                Password = Password ?? string.Empty,
                IsServerMode = IsServerMode,
                IsFuzzySearch = IsFuzzySearch
            },
            Git = new GitSettings
            {
                BackupEnabled = GitBackupEnabled,
                ShowStatusToasts = GitShowStatusToasts,
                RemoteUrl = GitRemoteUrl ?? string.Empty,
                Branch = GitBranch ?? string.Empty,
                UserName = GitUserName ?? string.Empty,
                Password = GitPassword ?? string.Empty,
                SshPrivateKeyPath = GitSshPrivateKeyPath,
                SshPublicKeyPath = GitSshPublicKeyPath,
                SshKeyStoragePath = SshKeyStoragePath,
                PullIntervalSeconds = GitPullIntervalSeconds,
                PushIntervalSeconds = GitPushIntervalSeconds,
                RemoteName = GitRemoteName ?? string.Empty,
                PushRefSpec = GitPushRefSpec,
                CommitterName = GitCommitterName ?? string.Empty,
                CommitterEmail = GitCommitterEmail ?? string.Empty
            }
        };

    private void SetTaskSpaceSetting(IConfiguration section, string key, object? value)
    {
        if (!_deferTaskSpaceSettingsPersistence)
        {
            section.GetSection(key).Set(value);
        }
    }

    public ThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (_themeMode == value)
            {
                return;
            }

            _themeMode = value;
            _appearanceSettings.GetSection(AppearanceSettings.ThemeKey)
                .Set(AppearanceSettings.ToStoredTheme(value));
        }
    }

    public int ThemeModeIndex
    {
        get => (int)ThemeMode;
        set => ThemeMode = value switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System
        };
    }

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();

    [AlsoNotifyFor(nameof(LanguageModeIndex))]
    public string LanguageMode
    {
        get => _localization.LanguageMode;
        set
        {
            var normalizedValue = L10n.NormalizeLanguageMode(value);
            _appearanceSettings.GetSection(AppearanceSettings.LanguageKey).Set(normalizedValue);
            _localization.SetLanguage(normalizedValue);
            RefreshLocalizedText();
        }
    }

    [AlsoNotifyFor(nameof(LanguageModeIndex), nameof(SshKeyStorageEffectivePathText))]
    public int LanguageOptionsVersion { get; private set; }

    public int LanguageModeIndex
    {
        get
        {
            var index = FindLanguageOptionIndex(LanguageMode);
            return index >= 0 ? index : 0;
        }
        set
        {
            if (value < 0 || value >= LanguageOptions.Count)
            {
                return;
            }

            LanguageMode = LanguageOptions[value].Value;
        }
    }

    // Compatibility shim for older callers/tests.
    public bool IsDarkTheme
    {
        get => ThemeMode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => _defaultIsDarkTheme
        };
        set => ThemeMode = value ? ThemeMode.Dark : ThemeMode.Light;
    }

    [AlsoNotifyFor(nameof(TaskStoragePathTooltip))]
    public string? TaskStoragePath
    {
        get => _taskStoragePath;
        set
        {
            _taskStoragePath = value;
            SetTaskSpaceSetting(_taskStorageSettings, nameof(TaskStorageSettings.Path), value);
            RefreshStorageStatusText();
        }
    }

    public string TaskStoragePathTooltip => ResolveTaskStoragePathTooltip();

    [AlsoNotifyFor(
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat),
        nameof(IsNoteDailyFileNameFormatRootRequiredVisible))]
    public string? NoteVaultRootPath
    {
        get => _noteVaultRootPath;
        set
        {
            if (string.Equals(_noteVaultRootPath, value, StringComparison.Ordinal))
            {
                return;
            }

            _noteVaultRootPath = value;
            AdvanceNoteDailyFileNameFormatApplyContextGeneration();
            ResetNoteDailyFileNameFormatFeedSession();
            HasExternalNoteDailyFileNameFormatChange = false;
            NoteDailyFileNameFormatStatusText = null;
            _noteVaultSettings.GetSection(NoteVaultRootPathKey).Set(value);
            PersistActiveNoteProfile();
        }
    }

    [AlsoNotifyFor(
        nameof(CanEditNoteVaultSettings),
        nameof(IsNoteDailyFileNameFormatReadOnly),
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool IsExternalNoteVaultSupported => _isExternalNoteVaultSupported;

    [AlsoNotifyFor(
        nameof(HasUnappliedNoteDailyFileNameFormatDraft),
        nameof(CanApplyNoteDailyFileNameFormat))]
    public string NoteDailyFileNameFormatDraft
    {
        get => _noteDailyFileNameFormatDraft;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_noteDailyFileNameFormatDraft, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _noteDailyFileNameFormatDraft = normalized;
            RefreshNoteDailyFileNameFormatDraftPresentation();
        }
    }

    [AlsoNotifyFor(nameof(HasUnappliedNoteDailyFileNameFormatDraft))]
    public string AppliedNoteDailyFileNameFormat
    {
        get => _appliedNoteDailyFileNameFormat;
        private set => _appliedNoteDailyFileNameFormat = value;
    }

    [AlsoNotifyFor(nameof(IsNoteDailyFileNameFormatValidationVisible))]
    public string? NoteDailyFileNameFormatValidationMessage { get; private set; }

    public bool IsNoteDailyFileNameFormatValidationVisible =>
        !string.IsNullOrWhiteSpace(NoteDailyFileNameFormatValidationMessage);

    [AlsoNotifyFor(nameof(NoteDailyFileNameFormatPreviewText))]
    public string NoteDailyFileNameFormatPreview { get; private set; } =
        $"Ежедневные/{DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.md";

    public string NoteDailyFileNameFormatPreviewText => string.Concat(
        _localization.Get("NoteDailyFileNameFormatPreview"),
        NoteDailyFileNameFormatPreview);

    public bool HasUnappliedNoteDailyFileNameFormatDraft =>
        !string.Equals(
            NoteDailyFileNameFormatDraft,
            AppliedNoteDailyFileNameFormat,
            StringComparison.Ordinal);

    [AlsoNotifyFor(
        nameof(CanEditNoteVaultSettings),
        nameof(IsNoteDailyFileNameFormatReadOnly),
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool IsApplyingNoteDailyFileNameFormat { get; private set; }

    [AlsoNotifyFor(
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool IsNoteDailyFileNameFormatFeedInitialized
    {
        get => _isNoteDailyFileNameFormatFeedInitialized;
        private set => _isNoteDailyFileNameFormatFeedInitialized = value;
    }

    [AlsoNotifyFor(
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool IsNoteDailyFileNameFormatFeedBusyOrRecovering
    {
        get => _isNoteDailyFileNameFormatFeedBusyOrRecovering;
        private set => _isNoteDailyFileNameFormatFeedBusyOrRecovering = value;
    }

    [AlsoNotifyFor(
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public string? ActiveNoteDailyFileNameFormatFeedRootPath
    {
        get => _activeNoteDailyFileNameFormatFeedRootPath;
        private set => _activeNoteDailyFileNameFormatFeedRootPath = value;
    }

    [AlsoNotifyFor(nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool HasExternalNoteDailyFileNameFormatChange { get; private set; }

    [AlsoNotifyFor(nameof(IsNoteDailyFileNameFormatStatusVisible))]
    public string? NoteDailyFileNameFormatStatusText { get; private set; }

    public bool IsNoteDailyFileNameFormatStatusVisible =>
        !string.IsNullOrWhiteSpace(NoteDailyFileNameFormatStatusText);

    public bool CanEditNoteVaultSettings =>
        IsExternalNoteVaultSupported && !IsApplyingNoteDailyFileNameFormat;

    public bool IsNoteDailyFileNameFormatReadOnly =>
        !CanEditNoteVaultSettings;

    public bool IsNoteDailyFileNameFormatRootRequiredVisible =>
        IsExternalNoteVaultSupported && string.IsNullOrWhiteSpace(NoteVaultRootPath);

    public bool CanApplyNoteDailyFileNameFormat =>
        IsExternalNoteVaultSupported &&
        IsFeedEnabled &&
        !string.IsNullOrWhiteSpace(NoteVaultRootPath) &&
        IsNoteDailyFileNameFormatFeedBoundToSelectedVault &&
        IsNoteDailyFileNameFormatFeedInitialized &&
        !IsNoteDailyFileNameFormatFeedBusyOrRecovering &&
        !IsApplyingNoteDailyFileNameFormat &&
        HasUnappliedNoteDailyFileNameFormatDraft &&
        !IsNoteDailyFileNameFormatValidationVisible &&
        _applyNoteDailyFileNameFormatAsync != null;

    public bool CanReloadExternalNoteDailyFileNameFormat =>
        HasExternalNoteDailyFileNameFormatChange &&
        IsExternalNoteVaultSupported &&
        IsFeedEnabled &&
        !string.IsNullOrWhiteSpace(NoteVaultRootPath) &&
        IsNoteDailyFileNameFormatFeedBoundToSelectedVault &&
        IsNoteDailyFileNameFormatFeedInitialized &&
        !IsNoteDailyFileNameFormatFeedBusyOrRecovering &&
        !IsApplyingNoteDailyFileNameFormat &&
        _reloadNoteDailyFileNameFormatAsync != null;

    [AlsoNotifyFor(
        nameof(CanApplyNoteDailyFileNameFormat),
        nameof(CanReloadExternalNoteDailyFileNameFormat))]
    public bool IsFeedEnabled
    {
        get => _isFeedEnabled;
        set
        {
            if (_isFeedEnabled == value)
            {
                return;
            }

            _isFeedEnabled = value;
            AdvanceNoteDailyFileNameFormatApplyContextGeneration();
            ResetNoteDailyFileNameFormatFeedSession();
            _noteVaultSettings.GetSection(NoteVaultIsFeedEnabledKey).Set(value);
            PersistActiveNoteProfile();
        }
    }

    public TimeSpan NoteDayBoundary
    {
        get => _noteDayBoundary;
        set
        {
            var normalized = TimeSpan.FromMinutes(NormalizeDayBoundaryMinutes((int)value.TotalMinutes));
            if (_noteDayBoundary == normalized)
            {
                return;
            }

            _noteDayBoundary = normalized;
            _noteVaultSettings
                .GetSection(NoteVaultDayBoundaryMinutesKey)
                .Set((int)normalized.TotalMinutes);
            PersistActiveNoteProfile();
        }
    }

    /// <summary>
    /// Connects the Settings surface to the Feed-owned portable setting without
    /// making Settings responsible for vault persistence or reconfiguration.
    /// </summary>
    public void ConfigureNoteDailyFileNameFormatBridge(
        Func<string, NoteDailyFileNameFormatValidation> validator,
        Func<string, Task<NoteDailyFileNameFormatApplyResult>> applyAsync,
        Func<Task<NoteDailyFileNameFormatState>> reloadAsync)
    {
        _noteDailyFileNameFormatValidator = validator ?? throw new ArgumentNullException(nameof(validator));
        _applyNoteDailyFileNameFormatAsync = applyAsync ?? throw new ArgumentNullException(nameof(applyAsync));
        _reloadNoteDailyFileNameFormatAsync = reloadAsync ?? throw new ArgumentNullException(nameof(reloadAsync));
        AdvanceNoteDailyFileNameFormatApplyContextGeneration();
        ResetNoteDailyFileNameFormatFeedSession();
        RefreshNoteDailyFileNameFormatDraftPresentation();
    }

    /// <summary>
    /// Receives Feed lifecycle availability so Apply cannot interrupt a vault
    /// mutation, recovery or an uninitialized selected root.
    /// </summary>
    public void SetNoteDailyFileNameFormatFeedAvailability(
        bool isVaultInitialized,
        bool isBusyOrRecovering,
        string? activeFeedVaultRootPath)
    {
        if (!AreNullableNoteDailyFileNameFormatVaultRootsEqual(
                ActiveNoteDailyFileNameFormatFeedRootPath,
                activeFeedVaultRootPath))
        {
            ActiveNoteDailyFileNameFormatFeedRootPath = activeFeedVaultRootPath;
            ResetNoteDailyFileNameFormatFeedSession();
        }

        if (IsNoteDailyFileNameFormatFeedInitialized != isVaultInitialized)
        {
            // Feed resets IsVaultInitialized before it commits every vault
            // session replacement. This also happens for a same-root
            // reconfigure, so root-path comparison alone is insufficient to
            // decide whether an async Settings command may still publish.
            AdvanceNoteDailyFileNameFormatOperationGeneration();
        }

        IsNoteDailyFileNameFormatFeedInitialized = isVaultInitialized;
        IsNoteDailyFileNameFormatFeedBusyOrRecovering = isBusyOrRecovering;
    }

    /// <summary>
    /// Receives the current vault-owned setting after initialization, a local
    /// Apply, or a watcher-driven external sidecar change.
    /// </summary>
    public void ApplyNoteDailyFileNameFormatState(
        NoteDailyFileNameFormatState state,
        bool replaceDirtyDraft = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsFeedEnabled ||
            !IsNoteDailyFileNameFormatStateForCurrentVault(state) ||
            !TryAcceptNoteDailyFileNameFormatFeedSession(state.SessionGeneration))
        {
            return;
        }

        var hadDirtyDraft = HasUnappliedNoteDailyFileNameFormatDraft;
        if (state.RequiresReload)
        {
            // Feed could not accept the externally written sidecar. Keep both the last
            // known-good layout and any local draft intact, but expose the same explicit
            // Reload path used for a competing valid external change.
            AppliedNoteDailyFileNameFormat = state.FileNameFormat;
            HasExternalNoteDailyFileNameFormatChange = true;
            NoteDailyFileNameFormatStatusText = state.StatusMessage ??
                _localization.Get("NoteDailyFileNameFormatApplyFailed");
            return;
        }

        var shouldPublishAppliedStatus =
            state.IsExternalChange ||
            replaceDirtyDraft ||
            IsApplyingNoteDailyFileNameFormat ||
            !string.IsNullOrWhiteSpace(state.StatusMessage);
        var appliedStatusText = _localization.Format(
            "NoteDailyFileNameFormatApplied",
            state.FileNameFormat);
        var preserveSuccessfulLocalApplyStatus =
            !state.IsExternalChange &&
            !replaceDirtyDraft &&
            !hadDirtyDraft &&
            string.Equals(
                NoteDailyFileNameFormatStatusText,
                appliedStatusText,
                StringComparison.Ordinal);
        AppliedNoteDailyFileNameFormat = state.FileNameFormat;

        if (hadDirtyDraft && !replaceDirtyDraft)
        {
            // Feed state is delivered asynchronously and may have been queued before a
            // same-root session rebind. It may update the applied value, but a passive
            // notification must not replace a draft the user has not explicitly applied
            // or reloaded.
            if (state.IsExternalChange)
            {
                HasExternalNoteDailyFileNameFormatChange = true;
                NoteDailyFileNameFormatStatusText = state.StatusMessage ??
                    _localization.Format(
                        "NoteDailyFileNameFormatExternalChanged",
                        state.FileNameFormat);
            }

            return;
        }

        NoteDailyFileNameFormatDraft = state.FileNameFormat;
        HasExternalNoteDailyFileNameFormatChange = false;
        NoteDailyFileNameFormatStatusText = state.StatusMessage ??
            (shouldPublishAppliedStatus
                ? state.IsExternalChange
                    ? _localization.Format(
                        "NoteDailyFileNameFormatExternalChanged",
                        state.FileNameFormat)
                    : appliedStatusText
                : preserveSuccessfulLocalApplyStatus
                    ? appliedStatusText
                    : null);
    }

    public async Task ApplyNoteDailyFileNameFormatAsync()
    {
        if (!CanApplyNoteDailyFileNameFormat || _applyNoteDailyFileNameFormatAsync == null)
        {
            return;
        }

        var operationGeneration = _noteDailyFileNameFormatOperationGeneration;
        var operationFeedSessionGeneration = _noteDailyFileNameFormatFeedSessionGeneration;
        var operationApplyContextGeneration = _noteDailyFileNameFormatApplyContextGeneration;
        var operationRootPath = NoteVaultRootPath;
        var requestedFormat = NoteDailyFileNameFormatDraft;
        var applyingStatusText = _localization.Get("NoteDailyFileNameFormatApplying");
        IsApplyingNoteDailyFileNameFormat = true;
        NoteDailyFileNameFormatStatusText = applyingStatusText;
        try
        {
            var result = await _applyNoteDailyFileNameFormatAsync(requestedFormat)
                .ConfigureAwait(true);
            if (result.Succeeded && result.AppliedState is { } state)
            {
                var isCurrentOperation = IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath);
                if (!isCurrentOperation && !IsSuccessfulLocalApplyFromNewerFeedSession(
                        state,
                        requestedFormat,
                        operationFeedSessionGeneration,
                        operationRootPath,
                        operationApplyContextGeneration))
                {
                    return;
                }

                // A concurrent external writer can win after this command saved its local
                // value. Keep the user's draft in that case, exactly as for a watcher-driven
                // external update, and expose Reload instead of silently replacing it.
                ApplyNoteDailyFileNameFormatState(state, replaceDirtyDraft: !state.IsExternalChange);
                return;
            }

            if (!IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                return;
            }

            NoteDailyFileNameFormatStatusText = result.ErrorMessage ??
                (result.IsCancelled
                    ? _localization.Get("NoteDailyFileNameFormatApplyCancelled")
                    : _localization.Get("NoteDailyFileNameFormatApplyFailed"));
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                NoteDailyFileNameFormatStatusText = _localization.Get("NoteDailyFileNameFormatApplyCancelled");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                NoteDailyFileNameFormatStatusText = _localization.Format(
                    "NoteDailyFileNameFormatApplyError",
                    exception.Message);
            }
        }
        finally
        {
            ClearStaleNoteDailyFileNameFormatOperationStatus(
                operationGeneration,
                operationFeedSessionGeneration,
                operationRootPath,
                applyingStatusText);
            IsApplyingNoteDailyFileNameFormat = false;
        }
    }

    public async Task ReloadExternalNoteDailyFileNameFormatAsync()
    {
        if (!CanReloadExternalNoteDailyFileNameFormat || _reloadNoteDailyFileNameFormatAsync == null)
        {
            return;
        }

        var operationGeneration = _noteDailyFileNameFormatOperationGeneration;
        var operationFeedSessionGeneration = _noteDailyFileNameFormatFeedSessionGeneration;
        var operationRootPath = NoteVaultRootPath;
        var reloadingStatusText = _localization.Get("NoteDailyFileNameFormatReloading");
        IsApplyingNoteDailyFileNameFormat = true;
        NoteDailyFileNameFormatStatusText = reloadingStatusText;
        try
        {
            var state = await _reloadNoteDailyFileNameFormatAsync().ConfigureAwait(true);
            if (!IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath) &&
                !IsReloadResultFromNewerFeedSession(
                    state,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                return;
            }

            ApplyNoteDailyFileNameFormatState(state, replaceDirtyDraft: true);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                NoteDailyFileNameFormatStatusText = _localization.Get("NoteDailyFileNameFormatApplyCancelled");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentNoteDailyFileNameFormatOperation(
                    operationGeneration,
                    operationFeedSessionGeneration,
                    operationRootPath))
            {
                NoteDailyFileNameFormatStatusText = _localization.Format(
                    "NoteDailyFileNameFormatApplyError",
                    exception.Message);
            }
        }
        finally
        {
            ClearStaleNoteDailyFileNameFormatOperationStatus(
                operationGeneration,
                operationFeedSessionGeneration,
                operationRootPath,
                reloadingStatusText);
            IsApplyingNoteDailyFileNameFormat = false;
        }
    }

    public string? TaskStorageURL
    {
        get => _taskStorageUrl;
        set
        {
            _taskStorageUrl = value;
            SetTaskSpaceSetting(_taskStorageSettings, nameof(TaskStorageSettings.URL), value);
        }
    }

    // Alias for XAML binding compatibility
    public string? ServerStorageUrl
    {
        get => TaskStorageURL;
        set
        {
            TaskStorageURL = value;
            RefreshStorageStatusText();
        }
    }

    public string? Login
    {
        get => _login;
        set
        {
            _login = value;
            SetTaskSpaceSetting(_taskStorageSettings, nameof(TaskStorageSettings.Login), value);
            RefreshStorageStatusText();
        }
    }

    public string? Password
    {
        get => _password;
        set
        {
            _password = value;
            SetTaskSpaceSetting(_taskStorageSettings, nameof(TaskStorageSettings.Password), value);
            RefreshStorageStatusText();
        }
    }

    public bool IsServerMode
    {
        get => _isServerMode;
        set
        {
            _isServerMode = value;
            SetTaskSpaceSetting(_taskStorageSettings, nameof(TaskStorageSettings.IsServerMode), value);
            RefreshStorageSelectionState();
            RefreshStorageStatusText();
        }
    }

    public int StorageModeIndex
    {
        get => IsServerMode ? 1 : 0;
        set => IsServerMode = value == 1;
    }

    public bool IsLocalStorageSelected { get; private set; }

    public bool IsServerStorageSelected { get; private set; }

    public bool IsFuzzySearch
    {
        get => _configuration.GetSection(nameof(IsFuzzySearch)).Get<bool>();
        set => _configuration.GetSection(nameof(IsFuzzySearch)).Set(value);
    }

    public bool CopyTaskOutlineAsMarkdown
    {
        get => _copyTaskOutlineAsMarkdown;
        set
        {
            _copyTaskOutlineAsMarkdown = value;
            _taskOutlineClipboardSettings.GetSection(TaskOutlineCopyAsMarkdownKey).Set(value);
        }
    }

    public bool CopyTaskOutlineDescription
    {
        get => _copyTaskOutlineDescription;
        set
        {
            _copyTaskOutlineDescription = value;
            _taskOutlineClipboardSettings.GetSection(TaskOutlineCopyDescriptionKey).Set(value);
        }
    }

    public bool PersistTaskTreeExpansionState
    {
        get => _persistTaskTreeExpansionState;
        set
        {
            _persistTaskTreeExpansionState = value;
            _taskTreeExpansionStateSettings.GetSection(TaskTreeExpansionStateEnabledKey).Set(value);
        }
    }

    [AlsoNotifyFor(nameof(CanEditUpdateCheckInterval))]
    public bool UpdateAutoCheckEnabled
    {
        get => _updateAutoCheckEnabled;
        set
        {
            _updateAutoCheckEnabled = value;
            _updateSettings.GetSection(ApplicationUpdateSettings.AutoCheckEnabledKey).Set(value);
        }
    }

    public bool CanEditUpdateCheckInterval => UpdateAutoCheckEnabled;

    [AlsoNotifyFor(nameof(UpdateCheckInterval))]
    public int UpdateCheckIntervalValue
    {
        get => _updateCheckIntervalValue;
        set
        {
            _updateCheckIntervalValue = ApplicationUpdateSettings.NormalizeCheckIntervalValue(value);
            _updateSettings
                .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
                .Set(_updateCheckIntervalValue);
        }
    }

    [AlsoNotifyFor(nameof(UpdateCheckInterval), nameof(UpdateCheckIntervalUnitIndex))]
    public ApplicationUpdateCheckIntervalUnit UpdateCheckIntervalUnit
    {
        get => _updateCheckIntervalUnit;
        set
        {
            _updateCheckIntervalUnit = Enum.IsDefined(value)
                ? value
                : ApplicationUpdateSettings.DefaultCheckIntervalUnit;
            _updateSettings
                .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
                .Set(ApplicationUpdateSettings.ToStoredCheckIntervalUnit(_updateCheckIntervalUnit));
        }
    }

    public int UpdateCheckIntervalUnitIndex
    {
        get => (int)UpdateCheckIntervalUnit;
        set
        {
            if (!Enum.IsDefined(typeof(ApplicationUpdateCheckIntervalUnit), value))
            {
                return;
            }

            UpdateCheckIntervalUnit = (ApplicationUpdateCheckIntervalUnit)value;
        }
    }

    public TimeSpan UpdateCheckInterval =>
        ApplicationUpdateSettings.ToInterval(UpdateCheckIntervalValue, UpdateCheckIntervalUnit);

    public double FontSize
    {
        get => AppearanceSettings.NormalizeFontSize(ReadInvariantDouble(
            _appearanceSettings.GetSection(AppearanceSettings.FontSizeKey)));
        set => _appearanceSettings.GetSection(AppearanceSettings.FontSizeKey)
            .Set(AppearanceSettings.NormalizeFontSize(value).ToString(CultureInfo.InvariantCulture));
    }

    public bool GitBackupEnabled
    {
        get => _gitBackupEnabled;
        set
        {
            _gitBackupEnabled = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.BackupEnabled), value);
            RefreshBackupState();
        }
    }

    public bool GitShowStatusToasts
    {
        get => _gitShowStatusToasts;
        set
        {
            _gitShowStatusToasts = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.ShowStatusToasts), value);
        }
    }

    public string? GitRemoteUrl
    {
        get => _gitRemoteUrl;
        set
        {
            _gitRemoteUrl = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.RemoteUrl), value);
            RefreshBackupAuthMode();
            RefreshBackupState();
        }
    }

    public string? GitBranch
    {
        get => _gitBranch;
        set
        {
            _gitBranch = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.Branch), value);
            EnsureGitPushRefSpecFallback();
            RefreshBackupActionAvailability();
        }
    }

    public string? GitUserName
    {
        get => _gitUserName;
        set
        {
            _gitUserName = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.UserName), value);
            RefreshBackupState();
        }
    }

    public string? GitPassword
    {
        get => _gitPassword;
        set
        {
            _gitPassword = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.Password), value);
            RefreshBackupState();
        }
    }

    public int GitPullIntervalSeconds
    {
        get => _gitPullIntervalSeconds;
        set
        {
            _gitPullIntervalSeconds = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.PullIntervalSeconds), value);
        }
    }

    public int GitPushIntervalSeconds
    {
        get => _gitPushIntervalSeconds;
        set
        {
            _gitPushIntervalSeconds = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.PushIntervalSeconds), value);
        }
    }

    public string? GitRemoteName
    {
        get => _gitRemoteName;
        set
        {
            _gitRemoteName = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.RemoteName), value);
            SyncGitRemoteUrlFromSelectedRemote(forceKnownRemoteUrl: true);
            RefreshBackupAuthMode();
            RefreshBackupState();
        }
    }

    public List<string> Remotes { get; private set; } = new();

    public List<string> RemotesWithAuthType { get; private set; } = new();

    public bool HasMultipleRemotes { get; private set; }

    public bool CanSwitchRemoteConnectionType { get; private set; }

    public string? GitRemoteNameDisplay
    {
        get
        {
            var remoteName = GitRemoteName;
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                return null;
            }

            return $"{remoteName} ({_backupService?.GetRemoteAuthType(remoteName) ?? "Unknown"})";
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                GitRemoteName = null;
                return;
            }

            var markerIndex = value.LastIndexOf(" (", StringComparison.Ordinal);
            GitRemoteName = markerIndex > 0 ? value[..markerIndex] : value;
        }
    }

    public string GitPushRefSpec
    {
        get => _gitPushRefSpec;
        set
        {
            _gitPushRefSpec = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.PushRefSpec), value);
            RefreshBackupActionAvailability();
        }
    }

    public List<string> Refs { get; private set; } = new();

    public string? GitCommitterName
    {
        get => _gitCommitterName;
        set
        {
            _gitCommitterName = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.CommitterName), value);
        }
    }

    public string? GitCommitterEmail
    {
        get => _gitCommitterEmail;
        set
        {
            _gitCommitterEmail = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.CommitterEmail), value);
        }
    }

    public string? GitSshPrivateKeyPath
    {
        get => _gitSshPrivateKeyPath;
        set
        {
            _gitSshPrivateKeyPath = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.SshPrivateKeyPath), value);
        }
    }

    public string? GitSshPublicKeyPath
    {
        get => _gitSshPublicKeyPath;
        set
        {
            _gitSshPublicKeyPath = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.SshPublicKeyPath), value);
        }
    }

    [AlsoNotifyFor(nameof(EffectiveSshKeyStoragePath), nameof(SshKeyStorageEffectivePathText))]
    public string? SshKeyStoragePath
    {
        get => _sshKeyStoragePath;
        set
        {
            if (string.Equals(_sshKeyStoragePath, value, StringComparison.Ordinal))
            {
                return;
            }

            _sshKeyStoragePath = value;
            SetTaskSpaceSetting(_gitSettings, nameof(GitSettings.SshKeyStoragePath), value);
            ReloadSshPublicKeys();
        }
    }

    public string EffectiveSshKeyStoragePath => ResolveEffectiveSshKeyStoragePath();

    public string SshKeyStorageEffectivePathText =>
        string.Format(
            CultureInfo.CurrentCulture,
            _localization.Get("SshKeyStorageEffectivePath"),
            EffectiveSshKeyStoragePath);

    public string? NewSshKeyName { get; set; } = "id_ed25519_unlimotion";

    public List<string> SshPublicKeys { get; private set; } = new();

    public string? SelectedSshPublicKeyPath
    {
        get => GitSshPublicKeyPath;
        set
        {
            GitSshPublicKeyPath = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                GitSshPrivateKeyPath = null;
            }
            else if (value.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
            {
                GitSshPrivateKeyPath = value[..^4];
            }

            RefreshBackupState();
        }
    }

    public BackupAuthMode BackupAuthMode { get; private set; } = BackupAuthMode.Token;

    public string BackupAuthModeText { get; private set; } = string.Empty;

    [AlsoNotifyFor(nameof(IsHttpRemoteConnectionTypeSelected))]
    public bool IsTokenAuthSelected { get; private set; } = true;

    [AlsoNotifyFor(nameof(IsSshRemoteConnectionTypeSelected))]
    public bool IsSshAuthSelected { get; private set; }

    public bool IsHttpRemoteConnectionTypeSelected => IsTokenAuthSelected;

    public bool IsSshRemoteConnectionTypeSelected => IsSshAuthSelected;

    public SettingsConnectionState StorageConnectionState { get; private set; }

    public BackupStatusState BackupConnectionState { get; private set; }

    public string StorageStatusText { get; private set; } = string.Empty;

    public string BackupStatusText { get; private set; } = string.Empty;

    public string? ConnectedServerLogin { get; private set; }

    public bool CanSignOut { get; private set; }

    public bool IsStorageBusy { get; private set; }

    public bool IsBackupBusy { get; private set; }

    public bool CanConnectStorage { get; private set; }

    public bool CanConnectRepository { get; private set; }

    public bool CanSyncRepository { get; private set; }

    public bool IsConflictResolutionMode { get; private set; }

    public bool HasBackupConflictFiles { get; private set; }

    public bool CanCommitConflictResolution { get; private set; }

    public List<BackupConflictFile> BackupConflicts { get; private set; } = new();

    public List<BackupConflictFieldDecision> SelectedBackupConflictFields { get; private set; } = new();

    public BackupConflictFile? SelectedBackupConflict
    {
        get => _selectedBackupConflict;
        set
        {
            _selectedBackupConflict = value;
            SelectedBackupConflictFields = value?.Fields
                .Select(conflictField => new BackupConflictFieldDecision(conflictField))
                .ToList() ?? new List<BackupConflictFieldDecision>();
            RefreshBackupActionAvailability();
        }
    }

    public bool CanResolveSelectedConflictUseCurrent { get; private set; }

    public bool CanResolveSelectedConflictUseIncoming { get; private set; }

    public bool CanResolveSelectedConflictByFields { get; private set; }

    public IReadOnlyList<BackupConflictFieldSelection> GetSelectedBackupConflictFieldSelections()
    {
        return SelectedBackupConflictFields
            .Select(field => new BackupConflictFieldSelection(
                field.FieldPath,
                field.SelectedSource,
                field.SelectedSource == BackupConflictFieldSource.Merge && field.CanEditMergedValue
                    ? field.EditedMergedValue
                    : null))
            .ToList();
    }

    public bool CanRunServerMaintenance { get; private set; }

    public bool CanRunResave { get; private set; }

    public bool ShowAdvancedBackupSettings { get; set; }

    public bool ShowServiceActions { get; set; }

    public ApplicationUpdateState UpdateState { get; private set; } = ApplicationUpdateState.Unsupported;

    public string CurrentApplicationVersion { get; private set; } = string.Empty;

    public string? AvailableUpdateVersion { get; private set; }

    public string UpdateStatusText { get; private set; } = string.Empty;

    public bool IsUpdateBusy { get; private set; }

    public bool HasAvailableUpdate { get; private set; }

    public bool CanCheckForUpdates { get; private set; }

    public bool CanDownloadUpdate { get; private set; }

    public bool CanApplyUpdate { get; private set; }

    public string GitBackupOnboardingHint =>
        _localization.Get("BackupOnboardingHint");

    public bool IsBackupConfigured =>
        GitBackupEnabled &&
        !string.IsNullOrWhiteSpace(GitRemoteUrl) &&
        (!IsTokenAuthSelected || !string.IsNullOrWhiteSpace(GitUserName));

    public void ReloadSshPublicKeys(string? preferredSelection = null)
    {
        try
        {
            SshPublicKeys = _backupService?.GetSshPublicKeys() ?? new List<string>();
        }
        catch (Exception ex) when (IsInvalidSshKeyStoragePathException(ex))
        {
            SshPublicKeys = new List<string>();
        }

        var matchedSelection = MatchSshPublicKeyPath(preferredSelection)
                               ?? MatchSshPublicKeyPath(GitSshPublicKeyPath);
        if (!string.IsNullOrWhiteSpace(matchedSelection))
        {
            SelectedSshPublicKeyPath = matchedSelection;
            return;
        }

        SelectedSshPublicKeyPath = null;

        RefreshBackupState();
    }

    public void ReloadGitMetadata()
    {
        Remotes = _backupService?.Remotes() ?? new List<string>();
        RemotesWithAuthType = Remotes
            .Select(remote => $"{remote} ({_backupService?.GetRemoteAuthType(remote) ?? _localization.Get("Unknown")})")
            .ToList();
        Refs = _backupService?.Refs() ?? new List<string>();
        HasMultipleRemotes = RemotesWithAuthType.Count > 1;
        EnsureRemoteSelection();
        SyncGitRemoteUrlFromSelectedRemote(forceKnownRemoteUrl: true);
        EnsureGitPushRefSpecSelection();
        ReloadBackupConflictStatus();
        RefreshBackupAuthMode();
        RefreshBackupState();
    }

    public void ApplyRemoteConnectionTypeSwitch(RemoteConnectionTypeSwitchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.RemoteName) || string.IsNullOrWhiteSpace(result.RemoteUrl))
        {
            return;
        }

        GitRemoteName = result.RemoteName;
        GitRemoteUrl = result.RemoteUrl;
        ReloadGitMetadata();
    }

    public void ReloadBackupConflictStatus()
    {
        var conflictStatus = _backupService?.GetConflictStatus() ?? BackupConflictStatus.None;
        var selectedPath = SelectedBackupConflict?.Path;
        BackupConflicts = conflictStatus.Conflicts.ToList();
        SelectedBackupConflict = BackupConflicts.FirstOrDefault(conflict =>
                                     string.Equals(conflict.Path, selectedPath, StringComparison.Ordinal))
                                 ?? BackupConflicts.FirstOrDefault();
        IsConflictResolutionMode = conflictStatus.IsInProgress || BackupConflicts.Count > 0;
        HasBackupConflictFiles = BackupConflicts.Count > 0;
        RefreshBackupActionAvailability();
    }

    public void MarkConflictResolutionPendingCommit()
    {
        BackupConflicts = new List<BackupConflictFile>();
        SelectedBackupConflict = null;
        HasBackupConflictFiles = false;
        IsConflictResolutionMode = true;
        RefreshBackupState();
    }

    public void CompleteConflictResolution()
    {
        BackupConflicts = new List<BackupConflictFile>();
        SelectedBackupConflict = null;
        HasBackupConflictFiles = false;
        IsConflictResolutionMode = false;
        RefreshBackupState();
    }

    public void SetStorageConnectionState(
        SettingsConnectionState state,
        string? statusText = null,
        string? connectedLogin = null)
    {
        StorageConnectionState = state;
        IsStorageBusy = state == SettingsConnectionState.Connecting;

        if (state == SettingsConnectionState.Connected)
        {
            ConnectedServerLogin = connectedLogin ?? GetStoredClientLogin() ?? Login;
        }
        else if (!IsServerMode)
        {
            ConnectedServerLogin = null;
        }

        if (state == SettingsConnectionState.Disconnected && IsServerMode)
        {
            ConnectedServerLogin = null;
        }

        RefreshStorageStatusText(statusText);
    }

    public void SetBackupConnectionState(BackupStatusState state, string? statusText = null)
    {
        BackupConnectionState = state;
        IsBackupBusy = state is BackupStatusState.Connecting or BackupStatusState.Syncing;
        RefreshBackupStatusText(statusText);
        RefreshBackupActionAvailability();
    }

    public void ConfigureUpdateService(IApplicationUpdateService? updateService)
    {
        _applicationUpdateService = updateService;
        CurrentApplicationVersion = updateService?.CurrentVersion ?? _localization.Get("Unknown");

        if (updateService?.IsSupported != true)
        {
            _availableUpdate = null;
            AvailableUpdateVersion = null;
            SetUpdateState(ApplicationUpdateState.Unsupported);
            return;
        }

        _availableUpdate = updateService.PendingUpdate;
        AvailableUpdateVersion = _availableUpdate?.Version;
        SetUpdateState(_availableUpdate == null
            ? ApplicationUpdateState.Idle
            : ApplicationUpdateState.ReadyToApply);
    }

    public void NormalizeUpdateCheckSettings()
    {
        _updateCheckIntervalValue = ApplicationUpdateSettings.NormalizeCheckIntervalValue(_updateCheckIntervalValue);
        _updateCheckIntervalUnit = Enum.IsDefined(_updateCheckIntervalUnit)
            ? _updateCheckIntervalUnit
            : ApplicationUpdateSettings.DefaultCheckIntervalUnit;
        _updateSettings
            .GetSection(ApplicationUpdateSettings.CheckIntervalValueKey)
            .Set(_updateCheckIntervalValue);
        _updateSettings
            .GetSection(ApplicationUpdateSettings.CheckIntervalUnitKey)
            .Set(ApplicationUpdateSettings.ToStoredCheckIntervalUnit(_updateCheckIntervalUnit));
    }

    public async Task CheckForUpdatesAsync(
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        if (!CanCheckForUpdates && UpdateState != ApplicationUpdateState.Error)
        {
            return;
        }

        var updateService = _applicationUpdateService;
        if (updateService?.IsSupported != true)
        {
            SetUpdateState(ApplicationUpdateState.Unsupported);
            return;
        }

        var pendingUpdate = updateService.PendingUpdate;
        if (pendingUpdate != null)
        {
            _availableUpdate = pendingUpdate;
            AvailableUpdateVersion = pendingUpdate.Version;
            SetUpdateState(ApplicationUpdateState.ReadyToApply);
            return;
        }

        SetUpdateState(ApplicationUpdateState.Checking);

        try
        {
            _availableUpdate = await updateService.CheckForUpdatesAsync(cancellationToken);
            AvailableUpdateVersion = _availableUpdate?.Version;
            SetUpdateState(_availableUpdate == null
                ? ApplicationUpdateState.NoUpdates
                : ApplicationUpdateState.UpdateAvailable);
        }
        catch (OperationCanceledException)
        {
            SetUpdateState(ApplicationUpdateState.Idle);
        }
        catch (Exception ex)
        {
            var status = silent
                ? _localization.Get("UpdateCheckFailed")
                : _localization.Format("UpdateCheckFailedWithError", ex.Message);
            SetUpdateState(ApplicationUpdateState.Error, status);
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownloadUpdate)
        {
            return;
        }

        var updateService = _applicationUpdateService;
        if (updateService?.IsSupported != true)
        {
            SetUpdateState(ApplicationUpdateState.Unsupported);
            return;
        }

        SetUpdateState(ApplicationUpdateState.Downloading);

        try
        {
            await updateService.DownloadUpdateAsync(cancellationToken);
            SetUpdateState(ApplicationUpdateState.ReadyToApply);
        }
        catch (OperationCanceledException)
        {
            SetUpdateState(_availableUpdate == null
                ? ApplicationUpdateState.Idle
                : ApplicationUpdateState.UpdateAvailable);
        }
        catch (Exception ex)
        {
            SetUpdateState(
                ApplicationUpdateState.Error,
                _localization.Format("UpdateDownloadFailedWithError", ex.Message));
        }
    }

    public Task ApplyUpdateAsync()
    {
        if (!CanApplyUpdate)
        {
            return Task.CompletedTask;
        }

        var updateService = _applicationUpdateService;
        if (updateService?.IsSupported != true)
        {
            SetUpdateState(ApplicationUpdateState.Unsupported);
            return Task.CompletedTask;
        }

        SetUpdateState(ApplicationUpdateState.Applying);

        try
        {
            updateService.ApplyUpdateAndRestart();
        }
        catch (ApplicationUpdateUserActionRequiredException ex)
        {
            SetUpdateState(ApplicationUpdateState.ReadyToApply, ex.Message);
        }
        catch (Exception ex)
        {
            SetUpdateState(
                ApplicationUpdateState.Error,
                _localization.Format("UpdateApplyFailedWithError", ex.Message));
        }

        return Task.CompletedTask;
    }

    public void MarkSignedOut()
    {
        ConnectedServerLogin = null;
        SetStorageConnectionState(SettingsConnectionState.Disconnected, _localization.Get("SignedOut"));
    }

    private void RefreshLocalizedText()
    {
        RefreshLanguageOptions();
        RefreshBackupAuthMode();
        RefreshStorageStatusText();
        RefreshBackupState();
        RefreshUpdateStatusText();
        RefreshNoteDailyFileNameFormatDraftPresentation();
    }

    private void RefreshLanguageOptions()
    {
        var supportedLanguages = _localization.SupportedLanguages.ToList();

        for (var index = 0; index < supportedLanguages.Count; index++)
        {
            var option = supportedLanguages[index];
            var existingIndex = FindLanguageOptionIndex(option.Value);

            if (existingIndex < 0)
            {
                LanguageOptions.Insert(index, option);
                continue;
            }

            if (existingIndex != index)
            {
                LanguageOptions.Move(existingIndex, index);
            }

            LanguageOptions[index].DisplayName = option.DisplayName;
        }

        for (var index = LanguageOptions.Count - 1; index >= 0; index--)
        {
            var option = LanguageOptions[index];
            if (supportedLanguages.All(supported =>
                    !string.Equals(supported.Value, option.Value, StringComparison.OrdinalIgnoreCase)))
            {
                LanguageOptions.RemoveAt(index);
            }
        }

        LanguageOptionsVersion++;
    }

    private int FindLanguageOptionIndex(string? languageMode)
    {
        for (var index = 0; index < LanguageOptions.Count; index++)
        {
            if (string.Equals(LanguageOptions[index].Value, languageMode, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private string ResolveTaskStoragePathTooltip()
    {
        var effectivePath = string.IsNullOrWhiteSpace(TaskStoragePath)
            ? _defaultTaskStoragePathProvider?.Invoke()
            : TaskStoragePath;

        if (string.IsNullOrWhiteSpace(effectivePath))
        {
            effectivePath = DefaultTaskStoragePath;
        }

        try
        {
            return Path.GetFullPath(effectivePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return effectivePath;
        }
    }

    private string ResolveEffectiveSshKeyStoragePath()
    {
        try
        {
            return SshKeyStoragePathResolver.ResolveSshDirectory(SshKeyStoragePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SshKeyStoragePath ?? string.Empty;
        }
    }

    private static bool IsInvalidSshKeyStoragePathException(Exception ex) =>
        ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException;

    private void EnsureRemoteSelection()
    {
        if (Remotes.Count == 0)
        {
            return;
        }

        var selectedRemote = GitRemoteName;
        if (!string.IsNullOrWhiteSpace(selectedRemote) &&
            Remotes.Any(remote => string.Equals(remote, selectedRemote, StringComparison.Ordinal)))
        {
            return;
        }

        GitRemoteName = Remotes.FirstOrDefault(remote =>
                            string.Equals(remote, "origin", StringComparison.OrdinalIgnoreCase))
                        ?? Remotes[0];
    }

    private void SyncGitRemoteUrlFromSelectedRemote(bool forceKnownRemoteUrl)
    {
        if (string.IsNullOrWhiteSpace(GitRemoteName))
        {
            return;
        }

        var remoteUrl = _backupService?.GetRemoteUrl(GitRemoteName);
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            if (!string.IsNullOrWhiteSpace(GitRemoteUrl) &&
                !string.Equals(GitRemoteUrl, remoteUrl, StringComparison.Ordinal) &&
                (!forceKnownRemoteUrl || !IsKnownRemoteUrl(GitRemoteUrl)))
            {
                return;
            }

            GitRemoteUrl = remoteUrl;
        }
    }

    private bool IsKnownRemoteUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        return Remotes.Any(remote =>
        {
            var existingRemoteUrl = _backupService?.GetRemoteUrl(remote);
            return !string.IsNullOrWhiteSpace(existingRemoteUrl) &&
                   string.Equals(existingRemoteUrl, remoteUrl, StringComparison.Ordinal);
        });
    }

    private void EnsureGitPushRefSpecFallback()
    {
        if (!string.IsNullOrWhiteSpace(GitPushRefSpec))
        {
            return;
        }

        var fallback = ToCanonicalBranchRef(GitBranch);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            GitPushRefSpec = fallback;
        }
    }

    private void EnsureGitPushRefSpecSelection()
    {
        if (Refs.Count == 0)
        {
            EnsureGitPushRefSpecFallback();
            return;
        }

        if (!string.IsNullOrWhiteSpace(GitPushRefSpec) &&
            Refs.Any(reference => string.Equals(reference, GitPushRefSpec, StringComparison.Ordinal)))
        {
            return;
        }

        var branchFallback = ToCanonicalBranchRef(GitBranch);
        GitPushRefSpec = ChoosePreferredRef(Refs, branchFallback);
    }

    private static string ChoosePreferredRef(IReadOnlyList<string> refs, string? configuredBranchRef)
    {
        if (!string.IsNullOrWhiteSpace(configuredBranchRef))
        {
            var configuredRef = refs.FirstOrDefault(reference =>
                string.Equals(reference, configuredBranchRef, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(configuredRef))
            {
                return configuredRef;
            }
        }

        return refs.FirstOrDefault(reference =>
                   string.Equals(reference, "refs/heads/main", StringComparison.Ordinal))
               ?? refs.FirstOrDefault(reference =>
                   string.Equals(reference, "refs/heads/master", StringComparison.Ordinal))
               ?? refs[0];
    }

    private static string? ToCanonicalBranchRef(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        var trimmedBranch = branch.Trim();
        return trimmedBranch.StartsWith("refs/", StringComparison.Ordinal)
            ? trimmedBranch
            : $"refs/heads/{trimmedBranch}";
    }

    private static double? ReadInvariantDouble(IConfigurationSection section)
    {
        var rawValue = section.Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return section.Get<double?>();
        }

        var normalizedValue = rawValue.Contains(',') && !rawValue.Contains('.')
            ? rawValue.Replace(',', '.')
            : rawValue;

        if (double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return section.Get<double?>();
    }

    private void RefreshStorageSelectionState()
    {
        IsLocalStorageSelected = !IsServerMode;
        IsServerStorageSelected = IsServerMode;
        CanSignOut = IsServerMode && StorageConnectionState == SettingsConnectionState.Connected;
        CanRunServerMaintenance = CanSignOut;
        CanRunResave = !IsStorageBusy && !string.IsNullOrWhiteSpace(TaskStoragePath);
        CanConnectStorage = !IsStorageBusy && (IsServerMode
            ? !string.IsNullOrWhiteSpace(ServerStorageUrl) &&
              !string.IsNullOrWhiteSpace(Login) &&
              !string.IsNullOrWhiteSpace(Password)
            : !string.IsNullOrWhiteSpace(TaskStoragePath));
    }

    private void RefreshStorageStatusText(string? explicitStatus = null)
    {
        RefreshStorageSelectionState();

        if (!string.IsNullOrWhiteSpace(explicitStatus))
        {
            StorageStatusText = explicitStatus;
            return;
        }

        if (!IsServerMode)
        {
            StorageStatusText = StorageConnectionState switch
            {
                SettingsConnectionState.Connecting => _localization.Get("LocalStorageConnecting"),
                SettingsConnectionState.Error => _localization.Get("LocalStorageError"),
                SettingsConnectionState.Connected => _localization.Get("LocalStorageConnected"),
                _ => string.IsNullOrWhiteSpace(TaskStoragePath)
                    ? _localization.Get("SelectDataFolder")
                    : _localization.Get("LocalReady")
            };
            return;
        }

        StorageStatusText = StorageConnectionState switch
        {
            SettingsConnectionState.Connecting => _localization.Get("ServerConnecting"),
            SettingsConnectionState.Connected when !string.IsNullOrWhiteSpace(ConnectedServerLogin) =>
                _localization.Format("ServerConnectedAs", ConnectedServerLogin),
            SettingsConnectionState.Connected => _localization.Get("ServerConnected"),
            SettingsConnectionState.Error => _localization.Get("ServerConnectionError"),
            _ => _localization.Get("ConnectHint")
        };
    }

    private void RefreshBackupState()
    {
        if (!GitBackupEnabled)
        {
            SetBackupConnectionState(BackupStatusState.NotConfigured, _localization.Get("BackupDisabled"));
            return;
        }

        if (IsConflictResolutionMode)
        {
            SetBackupConnectionState(BackupStatusState.ConflictResolution);
            return;
        }

        if (string.IsNullOrWhiteSpace(GitRemoteUrl))
        {
            SetBackupConnectionState(BackupStatusState.NotConfigured, _localization.Get("SpecifyRepositoryUrl"));
            return;
        }

        var hasReadyTokenAuth = IsTokenAuthSelected &&
                                !string.IsNullOrWhiteSpace(GitUserName) &&
                                !string.IsNullOrWhiteSpace(GitPassword);
        var hasReadySshAuth = IsSshAuthSelected &&
                              !string.IsNullOrWhiteSpace(SelectedSshPublicKeyPath);

        if (hasReadyTokenAuth || hasReadySshAuth)
        {
            if (RemotesWithAuthType.Count > 0)
            {
                SetBackupConnectionState(BackupStatusState.Connected, _localization.Get("RepositoryConnected"));
            }
            else
            {
                SetBackupConnectionState(BackupStatusState.NotConfigured, _localization.Get("ParamsSavedConnectRepository"));
            }

            return;
        }

        SetBackupConnectionState(BackupStatusState.NotConfigured, IsTokenAuthSelected
            ? _localization.Get("EnterLoginAndToken")
            : _localization.Get("SelectSshKey"));
    }

    private void RefreshBackupStatusText(string? explicitStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitStatus))
        {
            BackupStatusText = explicitStatus;
            return;
        }

        BackupStatusText = BackupConnectionState switch
        {
            BackupStatusState.Connecting => _localization.Get("BackupConnecting"),
            BackupStatusState.Syncing => _localization.Get("BackupSyncing"),
            BackupStatusState.ConflictResolution => HasBackupConflictFiles
                ? _localization.Format("SyncConflictsStatus", BackupConflicts.Count)
                : _localization.Get("SyncConflictsReadyToCommit"),
            BackupStatusState.Connected => _localization.Get("BackupStatusConnected"),
            BackupStatusState.Error => _localization.Get("BackupError"),
            _ => _localization.Get("BackupNotConfigured")
        };
    }

    private void RefreshBackupAuthMode()
    {
        BackupAuthMode = ResolveBackupAuthMode();
        IsTokenAuthSelected = BackupAuthMode == BackupAuthMode.Token;
        IsSshAuthSelected = BackupAuthMode == BackupAuthMode.Ssh;
        BackupAuthModeText = IsSshAuthSelected ? "SSH" : _localization.Get("Token");
    }

    private void RefreshBackupActionAvailability()
    {
        var hasRemoteUrl = GitBackupEnabled && !string.IsNullOrWhiteSpace(GitRemoteUrl);
        var hasReadySyncTarget = GitBackupEnabled &&
                                 !string.IsNullOrWhiteSpace(GitRemoteName) &&
                                 !string.IsNullOrWhiteSpace(GitPushRefSpec);
        var hasReadyTokenAuth = IsTokenAuthSelected &&
                                !string.IsNullOrWhiteSpace(GitUserName) &&
                                !string.IsNullOrWhiteSpace(GitPassword);
        var hasReadySshAuth = IsSshAuthSelected &&
                              !string.IsNullOrWhiteSpace(SelectedSshPublicKeyPath);

        CanConnectRepository = !IsBackupBusy &&
                               !IsConflictResolutionMode &&
                               hasRemoteUrl &&
                               (hasReadyTokenAuth || hasReadySshAuth);
        CanSyncRepository = !IsBackupBusy &&
                            !IsConflictResolutionMode &&
                            hasReadySyncTarget;
        CanSwitchRemoteConnectionType = !IsBackupBusy &&
                                        !IsConflictResolutionMode &&
                                        !string.IsNullOrWhiteSpace(GitRemoteName) &&
                                        Remotes.Any(remote => string.Equals(remote, GitRemoteName, StringComparison.Ordinal));
        CanCommitConflictResolution = !IsBackupBusy &&
                                      IsConflictResolutionMode &&
                                      !HasBackupConflictFiles;
        CanResolveSelectedConflictUseCurrent = !IsBackupBusy &&
                                               IsConflictResolutionMode &&
                                               SelectedBackupConflict != null;
        CanResolveSelectedConflictUseIncoming = !IsBackupBusy &&
                                                IsConflictResolutionMode &&
                                                SelectedBackupConflict != null;
        CanResolveSelectedConflictByFields = !IsBackupBusy &&
                                             IsConflictResolutionMode &&
                                             SelectedBackupConflict?.CanResolveByFields == true;
        RefreshTaskSpaceActionAvailability();
    }

    private void RefreshTaskSpaceActionAvailability()
    {
        CanRemoveTaskSpace = SelectedTaskSpace != null &&
                             TaskSpaces.Count > 1 &&
                             !IsTaskSpaceRemovalBlockedByConflict(SelectedTaskSpace.SourceId);
    }

    private void SetUpdateState(ApplicationUpdateState state, string? statusText = null)
    {
        UpdateState = state;
        _updateStatusOverride = statusText;
        IsUpdateBusy = state is ApplicationUpdateState.Checking
            or ApplicationUpdateState.Downloading
            or ApplicationUpdateState.Applying;
        HasAvailableUpdate = _availableUpdate != null;
        RefreshUpdateStatusText();
        RefreshUpdateActionAvailability();
    }

    private void RefreshUpdateStatusText()
    {
        if (!string.IsNullOrWhiteSpace(_updateStatusOverride))
        {
            UpdateStatusText = _updateStatusOverride;
            return;
        }

        UpdateStatusText = UpdateState switch
        {
            ApplicationUpdateState.Idle => _localization.Get("UpdateStatusIdle"),
            ApplicationUpdateState.Checking => _localization.Get("UpdateStatusChecking"),
            ApplicationUpdateState.NoUpdates => _localization.Get("UpdateStatusNoUpdates"),
            ApplicationUpdateState.UpdateAvailable => _localization.Format(
                "UpdateStatusAvailable",
                AvailableUpdateVersion ?? _localization.Get("Unknown")),
            ApplicationUpdateState.Downloading => _localization.Format(
                "UpdateStatusDownloading",
                AvailableUpdateVersion ?? _localization.Get("Unknown")),
            ApplicationUpdateState.ReadyToApply => _localization.Format(
                "UpdateStatusReadyToApply",
                AvailableUpdateVersion ?? _localization.Get("Unknown")),
            ApplicationUpdateState.Applying => _localization.Get("UpdateStatusApplying"),
            ApplicationUpdateState.Error => _localization.Get("UpdateStatusError"),
            _ => _localization.Get("UpdateStatusUnsupported")
        };
    }

    private void RefreshUpdateActionAvailability()
    {
        var isSupported = _applicationUpdateService?.IsSupported == true;
        CanCheckForUpdates = isSupported &&
                             !IsUpdateBusy &&
                             UpdateState != ApplicationUpdateState.ReadyToApply;
        CanDownloadUpdate = isSupported &&
                            !IsUpdateBusy &&
                            UpdateState == ApplicationUpdateState.UpdateAvailable &&
                            _availableUpdate != null;
        CanApplyUpdate = isSupported &&
                         !IsUpdateBusy &&
                         UpdateState == ApplicationUpdateState.ReadyToApply &&
                         _availableUpdate != null;
    }

    private BackupAuthMode ResolveBackupAuthMode()
    {
        var remoteName = GitRemoteName;
        if (!string.IsNullOrWhiteSpace(remoteName))
        {
            var authType = _backupService?.GetRemoteAuthType(remoteName);
            if (!string.IsNullOrWhiteSpace(authType))
            {
                return ParseBackupAuthMode(authType);
            }
        }

        return ParseBackupAuthMode(GitRemoteUrl);
    }

    private static BackupAuthMode ParseBackupAuthMode(string? authSource)
    {
        if (string.IsNullOrWhiteSpace(authSource))
        {
            return BackupAuthMode.Token;
        }

        return authSource.Contains("SSH", StringComparison.OrdinalIgnoreCase) ||
               authSource.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
               authSource.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            ? BackupAuthMode.Ssh
            : BackupAuthMode.Token;
    }

    private string? MatchSshPublicKeyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return SshPublicKeys.FirstOrDefault(existingPath =>
            string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private string? GetStoredClientLogin()
    {
        return _configuration
            .GetSection(ClientSettingsSectionName)
            .GetSection(ClientLoginKey)
            .Get<string>();
    }

    private void RefreshNoteDailyFileNameFormatDraftPresentation()
    {
        var validation = ValidateNoteDailyFileNameFormat(NoteDailyFileNameFormatDraft);
        NoteDailyFileNameFormatValidationMessage = validation.IsValid
            ? null
            : validation.ErrorMessage ?? _localization.Get("NoteDailyFileNameFormatInvalid");
        NoteDailyFileNameFormatPreview = validation.IsValid
            ? validation.PreviewPath ?? BuildNoteDailyFileNameFormatPreview(NoteDailyFileNameFormatDraft)
            : string.Empty;
    }

    private NoteDailyFileNameFormatValidation ValidateNoteDailyFileNameFormat(string format)
    {
        if (_noteDailyFileNameFormatValidator != null)
        {
            return _noteDailyFileNameFormatValidator(format);
        }

        if (string.IsNullOrWhiteSpace(format) ||
            !DailyNoteFileNameFormatPattern.IsMatch(format))
        {
            return new NoteDailyFileNameFormatValidation(
                false,
                null,
                _localization.Get("NoteDailyFileNameFormatInvalid"));
        }

        var tokens = Regex.Matches(format, "yyyy|MM|dd")
            .Select(static match => match.Value)
            .ToArray();
        if (tokens.Length != 3 || tokens.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            return new NoteDailyFileNameFormatValidation(
                false,
                null,
                _localization.Get("NoteDailyFileNameFormatInvalid"));
        }

        try
        {
            return new NoteDailyFileNameFormatValidation(
                true,
                BuildNoteDailyFileNameFormatPreview(format),
                null);
        }
        catch (FormatException)
        {
            return new NoteDailyFileNameFormatValidation(
                false,
                null,
                _localization.Get("NoteDailyFileNameFormatInvalid"));
        }
    }

    private static string BuildNoteDailyFileNameFormatPreview(string format)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return $"Ежедневные/{today.ToString(format, CultureInfo.InvariantCulture)}.md";
    }

    private bool IsNoteDailyFileNameFormatStateForCurrentVault(
        NoteDailyFileNameFormatState state)
    {
        return AreNoteDailyFileNameFormatVaultRootsEqual(NoteVaultRootPath, state.RootPath);
    }

    private bool IsNoteDailyFileNameFormatFeedBoundToSelectedVault =>
        AreNoteDailyFileNameFormatVaultRootsEqual(
            NoteVaultRootPath,
            ActiveNoteDailyFileNameFormatFeedRootPath);

    private bool IsCurrentNoteDailyFileNameFormatOperation(
        long operationGeneration,
        long? operationFeedSessionGeneration,
        string? operationRootPath)
    {
        return operationGeneration == _noteDailyFileNameFormatOperationGeneration &&
               operationFeedSessionGeneration == _noteDailyFileNameFormatFeedSessionGeneration &&
               AreNoteDailyFileNameFormatVaultRootsEqual(NoteVaultRootPath, operationRootPath);
    }

    private bool IsReloadResultFromNewerFeedSession(
        NoteDailyFileNameFormatState state,
        long? operationFeedSessionGeneration,
        string? operationRootPath)
    {
        return operationFeedSessionGeneration is { } sessionGeneration &&
               state.SessionGeneration > sessionGeneration &&
               AreNoteDailyFileNameFormatVaultRootsEqual(NoteVaultRootPath, operationRootPath) &&
               IsNoteDailyFileNameFormatStateForCurrentVault(state);
    }

    private bool IsSuccessfulLocalApplyFromNewerFeedSession(
        NoteDailyFileNameFormatState state,
        string requestedFormat,
        long? operationFeedSessionGeneration,
        string? operationRootPath,
        long operationApplyContextGeneration)
    {
        if (state.IsExternalChange ||
            state.RequiresReload ||
            !string.Equals(state.FileNameFormat, requestedFormat, StringComparison.Ordinal) ||
            (operationFeedSessionGeneration is { } operationSessionGeneration &&
             state.SessionGeneration <= operationSessionGeneration) ||
            operationApplyContextGeneration != _noteDailyFileNameFormatApplyContextGeneration ||
            !AreNoteDailyFileNameFormatVaultRootsEqual(NoteVaultRootPath, operationRootPath) ||
            !IsNoteDailyFileNameFormatStateForCurrentVault(state))
        {
            return false;
        }

        if (operationFeedSessionGeneration is null)
        {
            // A Feed session starts at generation 1. Its confirmed Apply response may reach
            // Settings before the queued passive state notification, but generation 0 still
            // identifies an unbound/default response from the old session.
            return state.SessionGeneration > 0 &&
                   (_noteDailyFileNameFormatFeedSessionGeneration is not { } boundSessionGeneration ||
                    state.SessionGeneration >= boundSessionGeneration);
        }

        return _noteDailyFileNameFormatFeedSessionGeneration is not { } currentSessionGeneration ||
               state.SessionGeneration >= currentSessionGeneration;
    }

    private void ClearStaleNoteDailyFileNameFormatOperationStatus(
        long operationGeneration,
        long? operationFeedSessionGeneration,
        string? operationRootPath,
        string operationStatusText)
    {
        if (!IsCurrentNoteDailyFileNameFormatOperation(
                operationGeneration,
                operationFeedSessionGeneration,
                operationRootPath) &&
            string.Equals(
                NoteDailyFileNameFormatStatusText,
                operationStatusText,
                StringComparison.Ordinal))
        {
            NoteDailyFileNameFormatStatusText = null;
        }
    }

    private bool TryAcceptNoteDailyFileNameFormatFeedSession(long sessionGeneration)
    {
        if (_noteDailyFileNameFormatFeedSessionGeneration is { } currentSessionGeneration &&
            sessionGeneration < currentSessionGeneration)
        {
            return false;
        }

        if (_noteDailyFileNameFormatFeedSessionGeneration != sessionGeneration)
        {
            _noteDailyFileNameFormatFeedSessionGeneration = sessionGeneration;
            AdvanceNoteDailyFileNameFormatOperationGeneration();
        }

        return true;
    }

    private void ResetNoteDailyFileNameFormatFeedSession()
    {
        _noteDailyFileNameFormatFeedSessionGeneration = null;
        AdvanceNoteDailyFileNameFormatOperationGeneration();
    }

    private void AdvanceNoteDailyFileNameFormatOperationGeneration()
    {
        unchecked
        {
            _noteDailyFileNameFormatOperationGeneration++;
        }
    }

    private void AdvanceNoteDailyFileNameFormatApplyContextGeneration()
    {
        unchecked
        {
            _noteDailyFileNameFormatApplyContextGeneration++;
        }
    }

    private static bool AreNoteDailyFileNameFormatVaultRootsEqual(
        string? leftPath,
        string? rightPath)
    {
        return TryNormalizeVaultRootPath(leftPath, out var normalizedLeftPath) &&
               TryNormalizeVaultRootPath(rightPath, out var normalizedRightPath) &&
               string.Equals(
                   normalizedLeftPath,
                   normalizedRightPath,
                   OperatingSystem.IsWindows()
                       ? StringComparison.OrdinalIgnoreCase
                       : StringComparison.Ordinal);
    }

    private static bool AreNullableNoteDailyFileNameFormatVaultRootsEqual(
        string? leftPath,
        string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) && string.IsNullOrWhiteSpace(rightPath))
        {
            return true;
        }

        return AreNoteDailyFileNameFormatVaultRootsEqual(leftPath, rightPath);
    }

    private static bool TryNormalizeVaultRootPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                         PathTooLongException or UnauthorizedAccessException or
                                         System.Security.SecurityException)
        {
            return false;
        }
    }

    private static int NormalizeDayBoundaryMinutes(int minutes) =>
        minutes is >= 0 and < 1440 ? minutes : 0;

    private static bool IsDesktopOperatingSystem()
    {
        return OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    }
}
