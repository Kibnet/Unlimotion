using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public class TaskStorageSettings
{
    public string Path { get; set; } = string.Empty;

    public string URL { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    //TODO стоит подумать над шифрованным хранением
    public string Password { get; set; } = string.Empty;

    public bool IsServerMode { get ; set ; }
    public bool IsFuzzySearch { get; set; }
}

public enum TaskSourceKind
{
    File,
    Server
}

public class TaskSourceDescriptor
{
    public const string DefaultSourceId = "default";

    public string Id { get; set; } = DefaultSourceId;
    public string DisplayName { get; set; } = string.Empty;
    public TaskSourceKind Kind { get; set; } = TaskSourceKind.File;
    public string Path { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class TaskSourceServerSettings
{
    public string SourceId { get; set; } = TaskSourceDescriptor.DefaultSourceId;
    public string Login { get; set; } = string.Empty;

    //TODO стоит подумать над шифрованным хранением
    public string Password { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpireTime { get; set; }
    public string UserId { get; set; } = string.Empty;
}

public class TaskSourcesSettings
{
    public const string SectionName = "TaskSources";

    public string ActiveSourceId { get; set; } = TaskSourceDescriptor.DefaultSourceId;
    public List<TaskSourceDescriptor> Sources { get; set; } = new();
    public List<TaskSourceServerSettings> ServerSettings { get; set; } = new();
}

public class GitSettings
{
    public bool BackupEnabled { get; set; } = false;
    public bool ShowStatusToasts { get; set; } = true;

    public string RemoteUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "master";
    public string UserName { get; set; } = "YourEmail";
    public string Password { get; set; } = "YourToken";
    public string? SshPrivateKeyPath { get; set; }
    public string? SshPublicKeyPath { get; set; }
    public string? SshKeyStoragePath { get; set; }

    public int PullIntervalSeconds { get; set; } = 30;
    public int PushIntervalSeconds { get; set; } = 60;

    public string RemoteName { get; set; } = "origin";
    public string PushRefSpec { get; set; } = "refs/heads/master";

    public string CommitterName { get; set; } = "Backuper";
    public string CommitterEmail { get; set; } = "Backuper@unlimotion.ru";
}
