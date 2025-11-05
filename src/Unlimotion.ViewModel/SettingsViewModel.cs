using System.Collections.Generic;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using PropertyChanged;
using Splat;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SettingsViewModel
{
    private readonly IConfiguration _configuration;
    private readonly IConfiguration _taskStorageSettings;
    private readonly IConfiguration _gitSettings;

    public SettingsViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        _taskStorageSettings = configuration.GetSection("TaskStorage");
        _gitSettings = configuration.GetSection("Git");
    }

    public string TaskStoragePath
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Path)).Set(value);
    }

    public string ServerStorageUrl
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.URL)).Set(value);
    }

    public string Login
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Login)).Set(value);
    }

    //TODO стоит подумать над шифрованным хранением
    public string Password
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.Password)).Set(value);
    }

    public bool IsServerMode
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Get<bool>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.IsServerMode)).Set(value);
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

    public string GitRemoteUrl
    {
        get => _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.RemoteUrl)).Set(value);
    }

    public string GitBranch
    {
        get => _gitSettings.GetSection(nameof(GitSettings.Branch)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.Branch)).Set(value);
    }

    public string GitUserName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.UserName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.UserName)).Set(value);
    }

    public string GitPassword
    {
        get => _gitSettings.GetSection(nameof(GitSettings.Password)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.Password)).Set(value);
    }

    public int GitPushIntervalSeconds
    {
        get => _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Get<int>();
        set => _gitSettings.GetSection(nameof(GitSettings.PushIntervalSeconds)).Set(value);
    }

    public int GitPullIntervalSeconds
    {
        get => _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Get<int>();
        set => _gitSettings.GetSection(nameof(GitSettings.PullIntervalSeconds)).Set(value);
    }

    public string GitRemoteName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.RemoteName)).Set(value);
    }

    public List<string> Remotes
    {
        get
        {
            var service = Locator.Current.GetService<IRemoteBackupService>();
            return service?.Remotes();
        }
    }

    public string GitPushRefSpec
    {
        get => _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.PushRefSpec)).Set(value);
    }

    public List<string> Refs
    {
        get
        {
            var service = Locator.Current.GetService<IRemoteBackupService>();
            return service?.Refs();
        }
    }


    public string GitCommitterName
    {
        get => _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.CommitterName)).Set(value);
    }

    public string GitCommitterEmail
    {
        get => _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Get<string>();
        set => _gitSettings.GetSection(nameof(GitSettings.CommitterEmail)).Set(value);
    }

    public ICommand ConnectCommand { get; set; }
    public ICommand MigrateCommand { get; set; }
    public ICommand BackupCommand { get; set; }
    public ICommand ResaveCommand { get; set; }
    public ICommand BrowseTaskStoragePathCommand { get; set; }
    public ICommand CloneCommand { get; set; }
    public ICommand PullCommand { get; set; }
    public ICommand PushCommand { get; set; }
}