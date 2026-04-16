using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SettingsViewModel
{
    private readonly IConfiguration _configuration;
    private readonly IConfiguration _taskStorageSettings;
    private readonly IConfiguration _gitSettings;
    private readonly IConfiguration _appearanceSettings;
    private readonly IRemoteBackupService? _backupService;
    private readonly bool _defaultIsDarkTheme;

    public SettingsViewModel(
        IConfiguration configuration,
        IRemoteBackupService? backupService = null,
        bool defaultIsDarkTheme = false)
    {
        _configuration = configuration;
        _taskStorageSettings = configuration.GetSection("TaskStorage");
        _gitSettings = configuration.GetSection("Git");
        _appearanceSettings = configuration.GetSection(AppearanceSettings.SectionName);
        _backupService = backupService;
        _defaultIsDarkTheme = defaultIsDarkTheme;
        ReloadSshPublicKeys();
    }

    // Commands - set externally from App.axaml.cs
    public ICommand? ConnectCommand { get; set; }
    public ICommand? MigrateCommand { get; set; }
    public ICommand? BackupCommand { get; set; }
    public ICommand? ResaveCommand { get; set; }
    public ICommand? BrowseTaskStoragePathCommand { get; set; }
    public ICommand? CloneCommand { get; set; }
    public ICommand? PullCommand { get; set; }
    public ICommand? PushCommand { get; set; }
    public ICommand? GenerateSshKeyCommand { get; set; }
    public ICommand? RefreshSshKeysCommand { get; set; }
    public ICommand? CopySelectedSshKeyCommand { get; set; }

    public string? TaskStoragePath
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Set(value);
    }

    public string? TaskStorageURL
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Set(value);
    }

    // Alias for XAML binding compatibility
    public string? ServerStorageUrl
    {
        get => TaskStorageURL;
        set => TaskStorageURL = value;
    }

    public string? Login
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Set(value);
    }

    public string? Password
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Set(value);
    }

    public bool IsServerMode
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Get<bool>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(value);
    }

    public bool IsFuzzySearch
    {
        get => _configuration.GetSection(nameof(IsFuzzySearch)).Get<bool>();
        set => _configuration.GetSection(nameof(IsFuzzySearch)).Set(value);
    }

    public bool IsDarkTheme
    {
        get
        {
            var configuredTheme = _appearanceSettings.GetSection(AppearanceSettings.ThemeKey).Get<string>();
            return AppearanceSettings.ParseIsDarkTheme(configuredTheme) ?? _defaultIsDarkTheme;
        }
        set => _appearanceSettings.GetSection(AppearanceSettings.ThemeKey)
            .Set(AppearanceSettings.ToStoredTheme(value));
    }

    public double FontSize
    {
        get => AppearanceSettings.NormalizeFontSize(
            _appearanceSettings.GetSection(AppearanceSettings.FontSizeKey).Get<double?>());
        set => _appearanceSettings.GetSection(AppearanceSettings.FontSizeKey)
            .Set(AppearanceSettings.NormalizeFontSize(value));
    }

    public bool GitBackupEnabled
    {
        get => _gitSettings.GetSection(nameof(GitSettings.BackupEnabled)).Get<bool>();
        set => _gitSettings.GetSection(nameof(GitSettings.BackupEnabled)).Set(value);
    }

    public bool GitShowStatusToasts
    {
        get => _gitSettings.GetSection(nameof(GitSettings.ShowStatusToasts)).Get<bool>();
        set => _gitSettings.GetSection(nameof(GitSettings.ShowStatusToasts)).Set(value);
    }

    public string? GitRemoteUrl
    {
        get => _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Set(value);
    }

    public string? GitBranch
    {
        get => _gitSettings.GetSection(nameof(GitSettings.Branch)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.Branch)).Set(value);
    }

    public string? GitUserName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.UserName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.UserName)).Set(value);
    }

    public string? GitPassword
    {
        get => _gitSettings.GetSection(nameof(GitSettings.Password)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.Password)).Set(value);
    }

    public int GitPullIntervalSeconds
    {
        get => _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Get<int>();
        set => _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Set(value);
    }

    public int GitPushIntervalSeconds
    {
        get => _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Get<int>();
        set => _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Set(value);
    }

    public string? GitRemoteName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Set(value);
    }

    public List<string> Remotes => _backupService?.Remotes() ?? new List<string>();

    public List<string> RemotesWithAuthType =>
        Remotes.Select(remote => $"{remote} ({_backupService?.GetRemoteAuthType(remote) ?? "Unknown"})").ToList();

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
        get => _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Set(value);
    }

    public List<string> Refs => _backupService?.Refs() ?? new List<string>();

    public string? GitCommitterName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Set(value);
    }

    public string? GitCommitterEmail
    {
        get => _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Set(value);
    }

    public string? GitSshPrivateKeyPath
    {
        get => _gitSettings.GetSection(nameof(GitSettings.SshPrivateKeyPath)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.SshPrivateKeyPath)).Set(value);
    }

    public string? GitSshPublicKeyPath
    {
        get => _gitSettings.GetSection(nameof(GitSettings.SshPublicKeyPath)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.SshPublicKeyPath)).Set(value);
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
        }
    }

    public void ReloadSshPublicKeys(string? preferredSelection = null)
    {
        SshPublicKeys = _backupService?.GetSshPublicKeys() ?? new List<string>();

        var matchedSelection = MatchSshPublicKeyPath(preferredSelection)
                               ?? MatchSshPublicKeyPath(GitSshPublicKeyPath);
        if (!string.IsNullOrWhiteSpace(matchedSelection))
        {
            SelectedSshPublicKeyPath = matchedSelection;
        }
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
}
