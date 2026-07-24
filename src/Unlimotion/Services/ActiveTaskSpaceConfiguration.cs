using System;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public interface IActiveTaskSpaceConfiguration
{
    TaskSpaceSettingsDraft Read(string sourceId);
    TaskSpaceSettingsDraft CaptureActiveProjection(string sourceId);
    void PersistCore(TaskSpaceOperationContext context, TaskSpaceSettingsDraft draft);
}

public sealed class ActiveTaskSpaceConfiguration(
    IConfiguration configuration,
    ITaskSourceManager sourceManager,
    ITaskSpaceOperationRunner operationRunner)
    : IActiveTaskSpaceConfiguration
{
    public TaskSpaceSettingsDraft Read(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        try
        {
            return sourceManager.GetSourceSettings(sourceId);
        }
        catch (NotSupportedException)
        {
            return new TaskSpaceSettingsDraft
            {
                SourceId = sourceId,
                Storage = CloneStorage(configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings()),
                Git = CloneGit(configuration.Get<GitSettings>("Git") ?? new GitSettings())
            };
        }
    }

    public TaskSpaceSettingsDraft CaptureActiveProjection(string sourceId) =>
        new()
        {
            SourceId = sourceId,
            Storage = CloneStorage(configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings()),
            Git = CloneGit(configuration.Get<GitSettings>("Git") ?? new GitSettings())
        };

    public void PersistCore(TaskSpaceOperationContext context, TaskSpaceSettingsDraft draft)
    {
        operationRunner.Validate(context);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SourceId);
        sourceManager.PersistSourceSettings(draft);
    }

    internal static TaskSpaceSettingsDraft CloneDraft(TaskSpaceSettingsDraft draft) =>
        new()
        {
            SourceId = draft.SourceId,
            Storage = CloneStorage(draft.Storage),
            Git = CloneGit(draft.Git)
        };

    internal static TaskStorageSettings CloneStorage(TaskStorageSettings storage) =>
        new()
        {
            Path = storage.Path,
            URL = storage.URL,
            Login = storage.Login,
            Password = storage.Password,
            IsServerMode = storage.IsServerMode,
            IsFuzzySearch = storage.IsFuzzySearch
        };

    internal static GitSettings CloneGit(GitSettings git) =>
        new()
        {
            BackupEnabled = git.BackupEnabled,
            ShowStatusToasts = git.ShowStatusToasts,
            RemoteUrl = git.RemoteUrl,
            Branch = git.Branch,
            UserName = git.UserName,
            Password = git.Password,
            SshPrivateKeyPath = git.SshPrivateKeyPath,
            SshPublicKeyPath = git.SshPublicKeyPath,
            SshKeyStoragePath = git.SshKeyStoragePath,
            PullIntervalSeconds = git.PullIntervalSeconds,
            PushIntervalSeconds = git.PushIntervalSeconds,
            RemoteName = git.RemoteName,
            PushRefSpec = git.PushRefSpec,
            CommitterName = git.CommitterName,
            CommitterEmail = git.CommitterEmail
        };
}
