using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
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

    private readonly IConfiguration _configuration;
    private readonly IConfiguration _taskStorageSettings;
    private readonly IConfiguration _gitSettings;
    private readonly IConfiguration _appearanceSettings;
    private readonly IRemoteBackupService? _backupService;
    private readonly ILocalizationService _localization;
    private readonly bool _defaultIsDarkTheme;
    private readonly Func<string?>? _defaultTaskStoragePathProvider;

    private ThemeMode _themeMode;
    private string? _taskStoragePath;
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

    public SettingsViewModel(
        IConfiguration configuration,
        IRemoteBackupService? backupService = null,
        bool defaultIsDarkTheme = false,
        Func<string?>? defaultTaskStoragePathProvider = null,
        ILocalizationService? localizationService = null)
    {
        _configuration = configuration;
        _taskStorageSettings = configuration.GetSection("TaskStorage");
        _gitSettings = configuration.GetSection("Git");
        _appearanceSettings = configuration.GetSection(AppearanceSettings.SectionName);
        _backupService = backupService;
        _localization = localizationService ?? LocalizationService.Current;
        _defaultIsDarkTheme = defaultIsDarkTheme;
        _defaultTaskStoragePathProvider = defaultTaskStoragePathProvider;

        _localization.SetLanguage(_appearanceSettings.GetSection(AppearanceSettings.LanguageKey).Get<string>());
        _localization.CultureChanged += (_, __) => RefreshLocalizedText();

        _themeMode = AppearanceSettings.ParseThemeMode(
            _appearanceSettings.GetSection(AppearanceSettings.ThemeKey).Get<string>());
        _taskStoragePath = _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Get<string>();
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
    public ICommand? CloneCommand { get; set; }
    public ICommand? PullCommand { get; set; }
    public ICommand? PushCommand { get; set; }
    public ICommand? GenerateSshKeyCommand { get; set; }
    public ICommand? RefreshSshKeysCommand { get; set; }
    public ICommand? RefreshGitMetadataCommand { get; set; }
    public ICommand? CopySelectedSshKeyCommand { get; set; }

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

    [AlsoNotifyFor(nameof(LanguageModeIndex))]
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
            _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Set(value);
            RefreshStorageStatusText();
        }
    }

    public string TaskStoragePathTooltip => ResolveTaskStoragePathTooltip();

    public string? TaskStorageURL
    {
        get => _taskStorageUrl;
        set
        {
            _taskStorageUrl = value;
            _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Set(value);
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
            _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Set(value);
            RefreshStorageStatusText();
        }
    }

    public string? Password
    {
        get => _password;
        set
        {
            _password = value;
            _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Set(value);
            RefreshStorageStatusText();
        }
    }

    public bool IsServerMode
    {
        get => _isServerMode;
        set
        {
            _isServerMode = value;
            _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(value);
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
            _gitSettings.GetSection(nameof(GitSettings.BackupEnabled)).Set(value);
            RefreshBackupState();
        }
    }

    public bool GitShowStatusToasts
    {
        get => _gitShowStatusToasts;
        set
        {
            _gitShowStatusToasts = value;
            _gitSettings.GetSection(nameof(GitSettings.ShowStatusToasts)).Set(value);
        }
    }

    public string? GitRemoteUrl
    {
        get => _gitRemoteUrl;
        set
        {
            _gitRemoteUrl = value;
            _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Set(value);
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
            _gitSettings.GetSection(nameof(GitSettings.Branch)).Set(value);
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
            _gitSettings.GetSection(nameof(GitSettings.UserName)).Set(value);
            RefreshBackupState();
        }
    }

    public string? GitPassword
    {
        get => _gitPassword;
        set
        {
            _gitPassword = value;
            _gitSettings.GetSection(nameof(GitSettings.Password)).Set(value);
            RefreshBackupState();
        }
    }

    public int GitPullIntervalSeconds
    {
        get => _gitPullIntervalSeconds;
        set
        {
            _gitPullIntervalSeconds = value;
            _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Set(value);
        }
    }

    public int GitPushIntervalSeconds
    {
        get => _gitPushIntervalSeconds;
        set
        {
            _gitPushIntervalSeconds = value;
            _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Set(value);
        }
    }

    public string? GitRemoteName
    {
        get => _gitRemoteName;
        set
        {
            _gitRemoteName = value;
            _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Set(value);
            TryFillGitRemoteUrlFromSelectedRemote();
            RefreshBackupAuthMode();
            RefreshBackupState();
        }
    }

    public List<string> Remotes { get; private set; } = new();

    public List<string> RemotesWithAuthType { get; private set; } = new();

    public bool HasMultipleRemotes { get; private set; }

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
            _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Set(value);
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
            _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Set(value);
        }
    }

    public string? GitCommitterEmail
    {
        get => _gitCommitterEmail;
        set
        {
            _gitCommitterEmail = value;
            _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Set(value);
        }
    }

    public string? GitSshPrivateKeyPath
    {
        get => _gitSshPrivateKeyPath;
        set
        {
            _gitSshPrivateKeyPath = value;
            _gitSettings.GetSection(nameof(GitSettings.SshPrivateKeyPath)).Set(value);
        }
    }

    public string? GitSshPublicKeyPath
    {
        get => _gitSshPublicKeyPath;
        set
        {
            _gitSshPublicKeyPath = value;
            _gitSettings.GetSection(nameof(GitSettings.SshPublicKeyPath)).Set(value);
        }
    }

    public string? NewSshKeyName { get; set; } = "id_ed25519_unlimotion";

    public List<string> SshPublicKeys { get; private set; } = new();

    public string? SelectedSshPublicKeyPath
    {
        get => GitSshPublicKeyPath;
        set
        {
            GitSshPublicKeyPath = value;
            if (!string.IsNullOrWhiteSpace(value) && value.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
            {
                GitSshPrivateKeyPath = value[..^4];
            }

            RefreshBackupState();
        }
    }

    public BackupAuthMode BackupAuthMode { get; private set; } = BackupAuthMode.Token;

    public string BackupAuthModeText { get; private set; } = string.Empty;

    public bool IsTokenAuthSelected { get; private set; } = true;

    public bool IsSshAuthSelected { get; private set; }

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

    public bool CanRunServerMaintenance { get; private set; }

    public bool CanRunResave { get; private set; }

    public bool ShowAdvancedBackupSettings { get; set; }

    public bool ShowServiceActions { get; set; }

    public string GitBackupOnboardingHint =>
        _localization.Get("BackupOnboardingHint");

    public bool IsBackupConfigured =>
        GitBackupEnabled &&
        !string.IsNullOrWhiteSpace(GitRemoteUrl) &&
        (!IsTokenAuthSelected || !string.IsNullOrWhiteSpace(GitUserName));

    public void ReloadSshPublicKeys(string? preferredSelection = null)
    {
        SshPublicKeys = _backupService?.GetSshPublicKeys() ?? new List<string>();

        var matchedSelection = MatchSshPublicKeyPath(preferredSelection)
                               ?? MatchSshPublicKeyPath(GitSshPublicKeyPath);
        if (!string.IsNullOrWhiteSpace(matchedSelection))
        {
            SelectedSshPublicKeyPath = matchedSelection;
            return;
        }

        if (SshPublicKeys.Count == 0)
        {
            SelectedSshPublicKeyPath = null;
        }

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
        TryFillGitRemoteUrlFromSelectedRemote();
        EnsureGitPushRefSpecSelection();
        RefreshBackupAuthMode();
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

    private void TryFillGitRemoteUrlFromSelectedRemote()
    {
        if (!string.IsNullOrWhiteSpace(GitRemoteUrl) || string.IsNullOrWhiteSpace(GitRemoteName))
        {
            return;
        }

        var remoteUrl = _backupService?.GetRemoteUrl(GitRemoteName);
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            GitRemoteUrl = remoteUrl;
        }
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

        CanConnectRepository = !IsBackupBusy && hasRemoteUrl && (hasReadyTokenAuth || hasReadySshAuth);
        CanSyncRepository = !IsBackupBusy && hasReadySyncTarget;
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
}
