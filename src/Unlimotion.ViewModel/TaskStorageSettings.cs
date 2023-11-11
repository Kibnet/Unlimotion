using System;

namespace Unlimotion.ViewModel;

public class TaskStorageSettings
{
    public string Path { get; set; }

    public string URL { get; set; }

    public string Login { get; set; }

    //TODO стоит подумать над шифрованным хранением
    public string Password { get; set; }

    public bool IsServerMode { get ; set ; }
    
    public string GitUserName { get; set; }
    public string GitPassword { get; set; }
    public int GitPullIntervalSeconds { get; set; } = 60;
    public int GitPushIntervalSeconds { get; set; } = 90;
    public bool GitBackupEnabled { get; set; }
}