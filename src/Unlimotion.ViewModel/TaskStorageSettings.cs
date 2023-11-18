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
    public bool BackupEnabled { get; set; }
    
    public string UserName { get; set; }
    public string Password { get; set; }
    
    public int PullIntervalSeconds { get; set; }
    public int PushIntervalSeconds { get; set; }

    public string RemoteName { get; set; }
    public string PushRefSpec { get; set; }
    
    public string CommitterName { get; set; }
    public string CommitterEmail { get; set; }
}