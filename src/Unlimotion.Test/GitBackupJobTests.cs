using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
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

    [Test]
    public async System.Threading.Tasks.Task PullJob_HoldsSharedTaskSpaceLeaseForEntireBackupOperation()
    {
        var configuration = CreateConfiguration(backupEnabled: true);
        using var runner = new TaskSpaceOperationRunner();
        var pullEntered = new ManualResetEventSlim();
        var releasePull = new ManualResetEventSlim();
        var backupService = new FakeRemoteBackupService
        {
            ConflictStatus = BackupConflictStatus.None,
            PullAction = () =>
            {
                pullEntered.Set();
                releasePull.Wait();
            }
        };
        var sourceConfiguration = new RecordingActiveTaskSpaceConfiguration(runner);
        var job = new GitPullJob(
            configuration,
            backupService,
            runner,
            () => "space-a",
            sourceConfiguration);

        var jobTask = Task.Run(() => job.Execute(null!));
        await Assert.That(pullEntered.Wait(TimeSpan.FromSeconds(5))).IsTrue();
        var switchEntered = false;
        var switchTask = runner.RunExclusiveAsync(
            "SwitchTaskSpace",
            "space-b",
            _ =>
            {
                switchEntered = true;
                return Task.CompletedTask;
            });

        await Task.Delay(50);
        await Assert.That(switchEntered).IsFalse();
        releasePull.Set();
        await Task.WhenAll(jobTask, switchTask);
        await Assert.That(switchEntered).IsTrue();
        await Assert.That(sourceConfiguration.PersistedSourceIds).IsEquivalentTo(["space-a"]);
    }

    private IConfigurationRoot CreateConfiguration(bool backupEnabled)
    {
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
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

        public Action? PullAction { get; init; }

        public List<string> Remotes() => new();

        public string? GetRemoteAuthType(string remoteName) => null;

        public string? GetRemoteUrl(string remoteName) => null;

        public RemoteConnectionTypeSwitchResult SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode) =>
            throw new NotSupportedException();

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
            PullAction?.Invoke();
        }

        public void PullExistingRepository() => throw new NotSupportedException();

        public BackupRepositoryConnectPreview PreviewConnectRepository() => throw new NotSupportedException();

        public void ConnectRepository(bool allowMergeWithNonEmptyRemote) => throw new NotSupportedException();

        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }

    private sealed class RecordingActiveTaskSpaceConfiguration(ITaskSpaceOperationRunner runner)
        : IActiveTaskSpaceConfiguration
    {
        public List<string> PersistedSourceIds { get; } = [];

        public TaskSpaceSettingsDraft Read(string sourceId) =>
            new()
            {
                SourceId = sourceId,
                Storage = new TaskStorageSettings(),
                Git = new GitSettings { BackupEnabled = true }
            };

        public TaskSpaceSettingsDraft CaptureActiveProjection(string sourceId) => Read(sourceId);

        public void PersistCore(TaskSpaceOperationContext context, TaskSpaceSettingsDraft draft)
        {
            runner.Validate(context);
            PersistedSourceIds.Add(draft.SourceId);
        }
    }
}
