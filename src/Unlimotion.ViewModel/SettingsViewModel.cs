﻿using Microsoft.Extensions.Configuration;
using PropertyChanged;
using System.Windows.Input;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SettingsViewModel
{
    private readonly IConfiguration _configuration;
    private readonly IConfiguration _taskStorageSettings;

    public SettingsViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        _taskStorageSettings = configuration.GetSection("TaskStorage");
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
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitBackupEnabled)).Get<bool>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitBackupEnabled)).Set(value);
    }
    
    public string GitUserName
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitUserName)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitUserName)).Set(value);
    }
    
    public string GitPassword
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPassword)).Get<string>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPassword)).Set(value);
    }
    
    public int GitPushIntervalSeconds
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPushIntervalSeconds)).Get<int>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPushIntervalSeconds)).Set(value);
    }
    
    public int GitPullIntervalSeconds
    {
        get => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPullIntervalSeconds)).Get<int>();
        set => _taskStorageSettings.GetSection(nameof(TaskStorageSettings.GitPullIntervalSeconds)).Set(value);
    }

    public ICommand ConnectCommand { get; set; }
    public ICommand MigrateCommand { get; set; }
    public ICommand BackupCommand { get; set; }
    public ICommand ResaveCommand { get; set; }
    public ICommand BrowseTaskStoragePathCommand { get; set; }
}