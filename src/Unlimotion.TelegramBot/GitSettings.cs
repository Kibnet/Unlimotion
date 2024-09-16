namespace Unlimotion.TelegramBot;

public class GitSettings
{
    public string RepositoryPath { get; set; } = "GitTasks";
    public string RemoteUrl { get; set; }
    public string Branch { get; set; } = "master";
    public string UserName { get; set; } = "YourEmail";
    public string Password { get; set; } = "YourToken";

    public int PullIntervalSeconds { get; set; } = 30;
    public int PushIntervalSeconds { get; set; } = 60;

    public string RemoteName { get; set; } = "origin";
    public string PushRefSpec { get; set; } = "refs/heads/main";

    public string CommitterName { get; set; } = "Backuper";
    public string CommitterEmail { get; set; } = "Backuper@unlimotion.ru";
}