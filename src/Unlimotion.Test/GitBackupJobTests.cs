using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class GitBackupJobTests : IDisposable
{
    private readonly string _configPath;
    private readonly List<IDisposable> _configurationDisposables = [];

    public GitBackupJobTests()
    {
        _configPath = Path.Combine(
            Environment.CurrentDirectory,
            $"GitBackupJob_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        foreach (var disposable in _configurationDisposables)
        {
            disposable.Dispose();
        }

        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task PullJob_SkipsPullWhenConflictResolutionIsInProgress()
    {
        var configuration = CreateConfiguration(backupEnabled: true);
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(true, new List<BackupConflictFile>())
        };
        var job = new GitPullJob(configuration, backupService);

        await job.Execute(null!);

        await Assert.That(backupService.PullCalls).IsEqualTo(0);
        await Assert.That(backupService.GetConflictStatusCalls).IsEqualTo(1);
    }

    [Test]
    public async System.Threading.Tasks.Task PushJob_SkipsPushWhenConflictResolutionIsInProgress()
    {
        var configuration = CreateConfiguration(backupEnabled: true);
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = new BackupConflictStatus(true, new List<BackupConflictFile>())
        };
        var job = new GitPushJob(configuration, backupService);

        await job.Execute(null!);

        await Assert.That(backupService.PushCalls).IsEqualTo(0);
        await Assert.That(backupService.GetConflictStatusCalls).IsEqualTo(1);
    }

    [Test]
    public async System.Threading.Tasks.Task Jobs_RunWhenBackupIsEnabledAndNoConflictResolutionIsInProgress()
    {
        var configuration = CreateConfiguration(backupEnabled: true);
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = BackupConflictStatus.None
        };

        await new GitPullJob(configuration, backupService).Execute(null!);
        await new GitPushJob(configuration, backupService).Execute(null!);

        await Assert.That(backupService.PullCalls).IsEqualTo(1);
        await Assert.That(backupService.PushCalls).IsEqualTo(1);
        await Assert.That(backupService.LastPushMessage).IsEqualTo("Backup created");
    }

    private IConfigurationRoot CreateConfiguration(bool backupEnabled)
    {
        var configuration = WritableJsonConfigurationFabric.Create(_configPath);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(backupEnabled);
        return configuration;
    }

    private sealed class FakeRemoteBackupService : IRemoteBackupService
    {
        public BackupConflictStatus ConflictStatus { get; set; } = BackupConflictStatus.None;

        public int GetConflictStatusCalls { get; private set; }

        public int PullCalls { get; private set; }

        public int PushCalls { get; private set; }

        public string? LastPushMessage { get; private set; }

        public List<string> Remotes() => new();

        public string? GetRemoteAuthType(string remoteName) => null;

        public string? GetRemoteUrl(string remoteName) => null;

        public List<string> Refs() => new();

        public List<string> GetSshPublicKeys() => new();

        public string GenerateSshKey(string keyName) => throw new NotSupportedException();

        public string? ReadPublicKey(string publicKeyPath) => null;

        public BackupConflictStatus GetConflictStatus()
        {
            GetConflictStatusCalls++;
            return ConflictStatus;
        }

        public void ResolveConflict(string path, BackupConflictResolution resolution) => throw new NotSupportedException();

        public void ResolveConflictFields(
            string path,
            IReadOnlyList<BackupConflictFieldSelection> fieldSelections) =>
            throw new NotSupportedException();

        public void CommitResolvedConflicts(string message) => throw new NotSupportedException();

        public void Push(string msg)
        {
            PushCalls++;
            LastPushMessage = msg;
        }

        public void Pull()
        {
            PullCalls++;
        }

        public BackupRepositoryConnectPreview PreviewConnectRepository() => throw new NotSupportedException();

        public void ConnectRepository(bool allowMergeWithNonEmptyRemote) => throw new NotSupportedException();

        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }
}
