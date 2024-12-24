namespace Unlimotion.ViewModel;

public class TaskStorageSettings
{
    public string Path { get; set; }

    public string URL { get; set; }

    public string Login { get; set; }

    //TODO стоит подумать над шифрованным хранением
    public string Password { get; set; }

    public bool IsServerMode { get ; set ; }
}

public class GitSettings
{
    public bool BackupEnabled { get; set; } = false;
    public bool ShowStatusToasts { get; set; } = true;

    public string RemoteUrl { get; set; }
    public string Branch { get; set; } = "master";
    public string UserName { get; set; } = "YourEmail";
    public string Password { get; set; } = "YourToken";

    public int PullIntervalSeconds { get; set; } = 30;
    public int PushIntervalSeconds { get; set; } = 60;

    public string RemoteName { get; set; } = "origin";
    public string PushRefSpec { get; set; } = "refs/heads/master";

    public string CommitterName { get; set; } = "Backuper";
    public string CommitterEmail { get; set; } = "Backuper@unlimotion.ru";
}