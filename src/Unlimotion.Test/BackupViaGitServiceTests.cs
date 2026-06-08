using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class BackupViaGitServiceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _configPath;
    private readonly List<IDisposable> _configurationDisposables = [];

    public BackupViaGitServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"ug-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
        _configPath = Path.Combine(_rootPath, "settings.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        foreach (var disposable in _configurationDisposables)
        {
            disposable.Dispose();
        }

        TryDeleteDirectory(_rootPath);
    }

    [Test]
    public async System.Threading.Tasks.Task PreviewConnectRepository_ChoosesInitialPushForEmptyRemoteAndNonEmptyLocalFolder()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemote();
        var service = CreateService(localPath, remotePath);

        var preview = service.PreviewConnectRepository();

        await Assert.That(preview.Action).IsEqualTo(BackupRepositoryConnectAction.InitializeLocalAndPush);
        await Assert.That(preview.RequiresConfirmation).IsFalse();
        await Assert.That(preview.LocalFolderHasContent).IsTrue();
        await Assert.That(preview.RemoteHasContent).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task PreviewConnectRepository_RequiresConfirmationForNonEmptyRemoteAndNonEmptyLocalFolder()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        var preview = service.PreviewConnectRepository();

        await Assert.That(preview.Action).IsEqualTo(BackupRepositoryConnectAction.MergeNonEmptyLocalWithRemote);
        await Assert.That(preview.RequiresConfirmation).IsTrue();
        await Assert.That(preview.LocalFolderHasContent).IsTrue();
        await Assert.That(preview.RemoteHasContent).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task PreviewConnectRepository_TreatsOnlyMigrationReportsAsEmptyLocalFolder()
    {
        var localPath = Path.Combine(_rootPath, "OnlyReportsTasks");
        Directory.CreateDirectory(localPath);
        File.WriteAllText(Path.Combine(localPath, "migration.report"), "local migration");
        File.WriteAllText(Path.Combine(localPath, "availability.migration.report"), "local availability");
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        var preview = service.PreviewConnectRepository();

        await Assert.That(preview.Action).IsEqualTo(BackupRepositoryConnectAction.FetchIntoEmptyLocalFolder);
        await Assert.That(preview.RequiresConfirmation).IsFalse();
        await Assert.That(preview.LocalFolderHasContent).IsFalse();
        await Assert.That(preview.RemoteHasContent).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_InitializesLocalRepositoryAndPushesLocalTasksToEmptyRemote()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemote();
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);

        await Assert.That(Repository.IsValid(localPath)).IsTrue();
        using var remote = new Repository(remotePath);
        await Assert.That(remote.Branches["main"]).IsNotNull();
        await Assert.That(remote.Branches["main"].Tip.Tree["local-task"]).IsNotNull();
        await Assert.That(File.Exists(Path.Combine(localPath, "local-task"))).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_ChecksOutNonEmptyRemoteIntoEmptyLocalFolder()
    {
        var localPath = Path.Combine(_rootPath, "EmptyTasks");
        Directory.CreateDirectory(localPath);
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        var preview = service.PreviewConnectRepository();
        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);

        await Assert.That(preview.Action).IsEqualTo(BackupRepositoryConnectAction.FetchIntoEmptyLocalFolder);
        await Assert.That(preview.RequiresConfirmation).IsFalse();
        await Assert.That(Repository.IsValid(localPath)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(localPath, "remote-task"))).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_ChecksOutRemoteOverLocalMigrationReportsWithoutConflict()
    {
        var localPath = Path.Combine(_rootPath, "OnlyReportsCheckoutTasks");
        Directory.CreateDirectory(localPath);
        File.WriteAllText(Path.Combine(localPath, "migration.report"), "local migration");
        File.WriteAllText(Path.Combine(localPath, "availability.migration.report"), "local availability");
        var remotePath = CreateBareRemoteWithFiles(
            ("remote-task", "remote content"),
            ("migration.report", "remote migration"),
            ("availability.migration.report", "remote availability"));
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);

        using var local = new Repository(localPath);
        await Assert.That(File.Exists(Path.Combine(localPath, "remote-task"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(localPath, "migration.report"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(localPath, "availability.migration.report"))).IsFalse();
        await Assert.That(local.Index["migration.report"]).IsNull();
        await Assert.That(local.Index["availability.migration.report"]).IsNull();
        await Assert.That(local.Head.Tip.Tree["migration.report"]).IsNull();
        await Assert.That(local.Head.Tip.Tree["availability.migration.report"]).IsNull();
        using var remote = new Repository(remotePath);
        await Assert.That(remote.Branches["main"].Tip.Tree["migration.report"]).IsNull();
        await Assert.That(remote.Branches["main"].Tip.Tree["availability.migration.report"]).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_SelectsExistingRemoteBranchWhenConfiguredBranchIsMissing()
    {
        var localPath = Path.Combine(_rootPath, "MissingBranchTasks");
        Directory.CreateDirectory(localPath);
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(
            localPath,
            remotePath,
            out var configuration,
            pushRefSpec: "refs/heads/master",
            branch: "master");

        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);

        await Assert.That(File.Exists(Path.Combine(localPath, "remote-task"))).IsTrue();
        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.PushRefSpec))
                .Get<string>())
            .IsEqualTo("refs/heads/main");
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_RefusesNonEmptyRemoteAndNonEmptyLocalFolderWithoutConfirmation()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        await Assert.That(() => service.ConnectRepository(allowMergeWithNonEmptyRemote: false))
            .Throws<InvalidOperationException>();
        await Assert.That(Repository.IsValid(localPath)).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_MergesNonEmptyRemoteWithLocalFolderAfterConfirmation()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: true);

        await Assert.That(File.Exists(Path.Combine(localPath, "local-task"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(localPath, "remote-task"))).IsTrue();
        using var local = new Repository(localPath);
        await Assert.That(local.Index.Conflicts.Any()).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task ConnectRepository_MergesNonEmptyRemoteWithLocalFolderAndIgnoresMigrationReports()
    {
        var localPath = CreateLocalTaskFolder();
        File.WriteAllText(Path.Combine(localPath, "migration.report"), "local migration");
        File.WriteAllText(Path.Combine(localPath, "availability.migration.report"), "local availability");
        var remotePath = CreateBareRemoteWithFiles(
            ("remote-task", "remote content"),
            ("migration.report", "remote migration"),
            ("availability.migration.report", "remote availability"));
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: true);

        using var local = new Repository(localPath);
        await Assert.That(local.Index.Conflicts.Any()).IsFalse();
        await Assert.That(local.Index["migration.report"]).IsNull();
        await Assert.That(local.Index["availability.migration.report"]).IsNull();
        await Assert.That(local.Head.Tip.Tree["migration.report"]).IsNull();
        await Assert.That(local.Head.Tip.Tree["availability.migration.report"]).IsNull();
        await Assert.That(File.ReadAllText(Path.Combine(localPath, ".gitignore"))).Contains("migration.report");
        await Assert.That(File.ReadAllText(Path.Combine(localPath, ".gitignore"))).Contains("availability.migration.report");
        await Assert.That(File.Exists(Path.Combine(localPath, "migration.report"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(localPath, "availability.migration.report"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(localPath, "migration.report"))).IsEqualTo("local migration");
        await Assert.That(File.ReadAllText(Path.Combine(localPath, "availability.migration.report"))).IsEqualTo("local availability");
        using var remote = new Repository(remotePath);
        await Assert.That(remote.Branches["main"].Tip.Tree["migration.report"]).IsNull();
        await Assert.That(remote.Branches["main"].Tip.Tree["availability.migration.report"]).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task PullExistingRepository_DoesNothing_WhenTaskFolderIsNotGitRepository()
    {
        var localPath = CreateLocalTaskFolder();
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        service.PullExistingRepository();

        await Assert.That(Directory.Exists(Path.Combine(localPath, ".git"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(localPath, "local-task"))).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task PullExistingRepository_DoesNothing_WhenRepositoryHasNoRemote()
    {
        var localPath = Path.Combine(_rootPath, $"NoRemoteTasks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localPath);
        Repository.Init(localPath);
        var remotePath = CreateBareRemoteWithCommit("remote-task", "remote content");
        var service = CreateService(localPath, remotePath);

        service.PullExistingRepository();

        using var repo = new Repository(localPath);
        await Assert.That(repo.Network.Remotes.Any()).IsFalse();
        await Assert.That(File.Exists(Path.Combine(localPath, "remote-task"))).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionType_CreatesSshRemoteForSingleHttpRemote()
    {
        var localPath = CreateInitializedRepositoryWithRemote("origin", "https://github.com/org/repo.git");
        var service = CreateService(localPath, "https://github.com/org/repo.git", out var configuration);

        var result = service.SwitchRemoteConnectionType("origin", BackupAuthMode.Ssh);

        using var repo = new Repository(localPath);
        await Assert.That(result.RemoteName).IsEqualTo("origin-ssh");
        await Assert.That(result.RemoteUrl).IsEqualTo("git@github.com:org/repo.git");
        await Assert.That(result.AuthType).IsEqualTo("SSH");
        await Assert.That(result.CreatedRemote).IsTrue();
        await Assert.That(repo.Network.Remotes["origin"]?.Url).IsEqualTo("https://github.com/org/repo.git");
        await Assert.That(repo.Network.Remotes["origin-ssh"]?.Url).IsEqualTo("git@github.com:org/repo.git");
        await Assert.That(configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Get<string>())
            .IsEqualTo("origin-ssh");
        await Assert.That(configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Get<string>())
            .IsEqualTo("git@github.com:org/repo.git");
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionType_SelectsExistingCanonicalTargetWithoutDuplicate()
    {
        var localPath = CreateInitializedRepositoryWithRemote("origin", "git@github.com:org/repo.git");
        using (var repo = new Repository(localPath))
        {
            repo.Network.Remotes.Add("origin-http", "http://github.com:80/org/repo/");
        }

        var service = CreateService(localPath, "git@github.com:org/repo.git", out _);

        var result = service.SwitchRemoteConnectionType("origin", BackupAuthMode.Token);

        using var verifiedRepo = new Repository(localPath);
        await Assert.That(result.RemoteName).IsEqualTo("origin-http");
        await Assert.That(result.RemoteUrl).IsEqualTo("http://github.com:80/org/repo/");
        await Assert.That(result.CreatedRemote).IsFalse();
        await Assert.That(verifiedRepo.Network.Remotes.Count()).IsEqualTo(2);
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionType_CreatesUniqueHttpRemoteName()
    {
        var localPath = CreateInitializedRepositoryWithRemote("backup", "git@github.com:org/repo.git");
        using (var repo = new Repository(localPath))
        {
            repo.Network.Remotes.Add("backup-http", "https://github.com/org/other.git");
        }

        var service = CreateService(localPath, "git@github.com:org/repo.git", out _);

        var result = service.SwitchRemoteConnectionType("backup", BackupAuthMode.Token);

        using var verifiedRepo = new Repository(localPath);
        await Assert.That(result.RemoteName).IsEqualTo("backup-http-2");
        await Assert.That(result.RemoteUrl).IsEqualTo("https://github.com/org/repo.git");
        await Assert.That(verifiedRepo.Network.Remotes["backup-http-2"]?.Url)
            .IsEqualTo("https://github.com/org/repo.git");
    }

    [Test]
    public async System.Threading.Tasks.Task SwitchRemoteConnectionType_RejectsUnsupportedRemoteUrlWithoutChangingRemotes()
    {
        var localPath = CreateInitializedRepositoryWithRemote("origin", "file:///tmp/repo.git");
        var service = CreateService(localPath, "file:///tmp/repo.git", out _);

        await Assert.That(() => service.SwitchRemoteConnectionType("origin", BackupAuthMode.Ssh))
            .Throws<InvalidOperationException>();

        using var repo = new Repository(localPath);
        await Assert.That(repo.Network.Remotes.Count()).IsEqualTo(1);
        await Assert.That(repo.Network.Remotes["origin"]?.Url).IsEqualTo("file:///tmp/repo.git");
    }

    [Test]
    public async System.Threading.Tasks.Task AreEquivalentRemoteUrls_NormalizesSupportedHttpAndSshForms()
    {
        await Assert.That(BackupViaGitService.AreEquivalentRemoteUrls(
                "ssh://git@github.com/org/repo.git",
                "git@github.com:org/repo"))
            .IsTrue();
        await Assert.That(BackupViaGitService.AreEquivalentRemoteUrls(
                "http://github.com:80/org/repo/",
                "https://GITHUB.com/org/repo.git"))
            .IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task Remotes_UsesCurrentLocalStorageWhenConfiguredPathIsEmpty()
    {
        var localPath = CreateInitializedRepositoryWithRemote(
            "origin",
            "git@github.com:org/unlimotion-backup.git");
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(string.Empty);
        using var storageFactory = new CurrentFileStorageFactory(localPath);
        var service = new BackupViaGitService(configuration, storageFactory: storageFactory);

        var remotes = service.Remotes();

        await Assert.That(remotes).Contains("origin");
        await Assert.That(service.GetRemoteUrl("origin")).IsEqualTo("git@github.com:org/unlimotion-backup.git");
    }

    [Test]
    public async System.Threading.Tasks.Task PullExistingRepository_PullsRemoteChanges_WhenTaskFolderIsExistingRepository()
    {
        var remotePath = CreateBareRemoteWithCommit("task", "base content");
        var localPath = CloneRemoteToLocalMain(remotePath);
        var service = CreateService(localPath, remotePath);
        PushRemoteChange(remotePath, "task", "remote content");

        service.PullExistingRepository();

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("remote content");
    }

    [Test]
    public async System.Threading.Tasks.Task PullExistingRepository_SelectsRepositoryRemoteAndBranch_WhenSettingsAreStale()
    {
        var remotePath = CreateBareRemoteWithCommit("task", "base content");
        var localPath = CloneRemoteToLocalMain(remotePath);
        using (var repo = new Repository(localPath))
        {
            repo.Network.Remotes.Remove("origin");
            repo.Network.Remotes.Add("backup", remotePath);
        }

        var service = CreateService(
            localPath,
            remotePath,
            out var configuration,
            pushRefSpec: "refs/heads/master",
            branch: "master",
            remoteName: "missing");
        PushRemoteChange(remotePath, "task", "remote content");

        service.PullExistingRepository();

        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.RemoteName))
                .Get<string>())
            .IsEqualTo("backup");
        await Assert.That(configuration
                .GetSection("Git")
                .GetSection(nameof(GitSettings.PushRefSpec))
                .Get<string>())
            .IsEqualTo("refs/heads/main");
        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("remote content");
    }

    [Test]
    public async System.Threading.Tasks.Task PullExistingRepository_DoesNotNotifyCurrentStorageWatcher_WhenRepositoryChanges()
    {
        var remotePath = CreateBareRemoteWithCommit("task", "base content");
        var localPath = CloneRemoteToLocalMain(remotePath);
        var watcher = new FakeDatabaseWatcher();
        var service = CreateService(localPath, remotePath, storageFactory: new FakeTaskStorageFactory(watcher));
        PushRemoteChange(remotePath, "task", "remote content");

        service.PullExistingRepository();

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("remote content");
        await Assert.That(watcher.SetEnableCalls).IsEqualTo(0);
        await Assert.That(watcher.ForceUpdateFileCalls).IsEqualTo(0);
    }

    [Test]
    public async System.Threading.Tasks.Task Pull_NotifiesCurrentStorageWatcher_WhenRepositoryChanges()
    {
        var remotePath = CreateBareRemoteWithCommit("task", "base content");
        var localPath = CloneRemoteToLocalMain(remotePath);
        var watcher = new FakeDatabaseWatcher();
        var service = CreateService(localPath, remotePath, storageFactory: new FakeTaskStorageFactory(watcher));
        PushRemoteChange(remotePath, "task", "remote content");

        service.Pull();

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("remote content");
        await Assert.That(watcher.SetEnableCalls).IsEqualTo(2);
        await Assert.That(watcher.ForceUpdateFileCalls).IsEqualTo(1);
        await Assert.That(watcher.LastForcedFileName).IsEqualTo("task");
        await Assert.That(watcher.LastForcedUpdateType).IsEqualTo(UpdateType.Saved);
    }

    [Test]
    public async System.Threading.Tasks.Task Pull_WithDivergedFile_ExposesConflictResolutionStatus()
    {
        var (service, _, _) = CreateServiceWithMergeConflict();

        var status = service.GetConflictStatus();

        await Assert.That(status.IsInProgress).IsTrue();
        await Assert.That(status.Conflicts.Count).IsEqualTo(1);
        await Assert.That(status.Conflicts[0].Path).IsEqualTo("task");
        await Assert.That(status.Conflicts[0].HasCurrentVersion).IsTrue();
        await Assert.That(status.Conflicts[0].HasIncomingVersion).IsTrue();
        await Assert.That(status.Conflicts[0].CanResolveByFields).IsFalse();
        await Assert.That(status.Conflicts[0].Fields.Count).IsEqualTo(0);
    }

    [Test]
    public async System.Threading.Tasks.Task Pull_WithDivergedJsonTask_ExposesFieldResolutionOptions()
    {
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "Description": "base description",
                          "ContainsTasks": ["local-child"],
                          "Importance": 1
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "Description": "remote description",
                           "ContainsTasks": ["remote-child"],
                           "Importance": 5
                         }
                         """;
        var (service, _, _) = CreateServiceWithMergeConflict(localJson, remoteJson);

        var conflict = service.GetConflictStatus().Conflicts.Single();

        await Assert.That(conflict.CanResolveByFields).IsTrue();
        await Assert.That(conflict.Fields.Select(field => field.FieldPath))
            .IsEquivalentTo(new[] { "Id", "Title", "Description", "ContainsTasks", "Importance" });
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "Id").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.Unchanged);
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "Title").CanMerge).IsTrue();
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "Title").CanEditMergedValue).IsTrue();
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "ContainsTasks").CanMerge).IsTrue();
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "ContainsTasks").CanEditMergedValue).IsFalse();
        await Assert.That(conflict.Fields.Single(field => field.FieldPath == "Importance").CanMerge).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task Pull_WithDivergedJsonTask_ShowsAllChangedFieldsAndMarksRealConflicts()
    {
        var baseJson = """
                       {
                         "Id": "task",
                         "Title": "base title",
                         "Description": "base description",
                         "Wanted": false,
                         "Importance": 0
                       }
                       """;
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "Description": "local description",
                          "Wanted": true,
                          "Importance": 0
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "Description": "base description",
                           "Wanted": true,
                           "Importance": 5
                         }
                         """;
        var (service, _, _) = CreateServiceWithMergeConflict(localJson, remoteJson, baseJson);

        var fields = service.GetConflictStatus().Conflicts.Single().Fields;

        await Assert.That(fields.Select(field => field.FieldPath))
            .IsEquivalentTo(new[] { "Id", "Title", "Description", "Wanted", "Importance" });
        await Assert.That(fields.Single(field => field.FieldPath == "Id").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.Unchanged);
        await Assert.That(fields.Single(field => field.FieldPath == "Title").AncestorValue)
            .IsEqualTo("base title");
        await Assert.That(fields.Single(field => field.FieldPath == "Title").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.BothDifferent);
        await Assert.That(fields.Single(field => field.FieldPath == "Title").IsRealConflict).IsTrue();
        await Assert.That(fields.Single(field => field.FieldPath == "Description").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.CurrentOnly);
        await Assert.That(fields.Single(field => field.FieldPath == "Wanted").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.BothSame);
        await Assert.That(fields.Single(field => field.FieldPath == "Importance").ChangeKind)
            .IsEqualTo(BackupConflictFieldChangeKind.IncomingOnly);
        await Assert.That(fields.Where(field => field.IsRealConflict).Select(field => field.FieldPath))
            .IsEquivalentTo(new[] { "Title" });
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflictFields_UsesSelectedVersionsAndMergedFields()
    {
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "Description": "local description",
                          "ContainsTasks": ["shared-child", "local-child"],
                          "Importance": 1
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "Description": "remote description",
                           "ContainsTasks": ["shared-child", "remote-child"],
                           "Importance": 5
                         }
                         """;
        var (service, localPath, remotePath) = CreateServiceWithMergeConflict(localJson, remoteJson);

        service.ResolveConflictFields(
            "task",
            new List<BackupConflictFieldSelection>
            {
                new("Title", BackupConflictFieldSource.UseCurrent),
                new("Description", BackupConflictFieldSource.UseIncoming),
                new("ContainsTasks", BackupConflictFieldSource.Merge),
                new("Importance", BackupConflictFieldSource.UseIncoming)
            });

        var resolved = JObject.Parse(File.ReadAllText(Path.Combine(localPath, "task")));
        await Assert.That((string?)resolved["Title"]).IsEqualTo("local title");
        await Assert.That((string?)resolved["Description"]).IsEqualTo("remote description");
        await Assert.That((int?)resolved["Importance"]).IsEqualTo(5);
        await Assert.That(resolved["ContainsTasks"]!.Values<string>())
            .IsEquivalentTo(new[] { "shared-child", "local-child", "remote-child" });
        await Assert.That(service.GetConflictStatus().Conflicts.Count).IsEqualTo(0);

        service.CommitResolvedConflicts("Resolve sync conflict");

        var pushed = JObject.Parse(ReadFileFromBranch(remotePath, "main", "task"));
        await Assert.That((string?)pushed["Title"]).IsEqualTo("local title");
        await Assert.That((string?)pushed["Description"]).IsEqualTo("remote description");
        await Assert.That(pushed["ContainsTasks"]!.Values<string>())
            .IsEquivalentTo(new[] { "shared-child", "local-child", "remote-child" });
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflictFields_UsesCustomMergedTextValue()
    {
        var baseJson = """
                       {
                         "Id": "task",
                         "Title": "base title",
                         "Description": "base description"
                       }
                       """;
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "Description": "local description"
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "Description": "remote description"
                         }
                         """;
        var (service, localPath, _) = CreateServiceWithMergeConflict(localJson, remoteJson, baseJson);

        service.ResolveConflictFields(
            "task",
            new List<BackupConflictFieldSelection>
            {
                new("Title", BackupConflictFieldSource.Merge, "edited merged title"),
                new("Description", BackupConflictFieldSource.Merge, "edited merged description")
            });

        var resolved = JObject.Parse(File.ReadAllText(Path.Combine(localPath, "task")));
        await Assert.That((string?)resolved["Title"]).IsEqualTo("edited merged title");
        await Assert.That((string?)resolved["Description"]).IsEqualTo("edited merged description");
    }

    [Test]
    public async System.Threading.Tasks.Task Pull_WithDivergedJsonTask_DisplaysRelationTaskTitles()
    {
        var baseJson = """
                       {
                         "Id": "task",
                         "Title": "base title",
                         "ContainsTasks": []
                       }
                       """;
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "ContainsTasks": ["shared-child", "local-child"]
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "ContainsTasks": ["shared-child", "remote-child"]
                         }
                         """;
        var (service, localPath, _) = CreateServiceWithMergeConflict(localJson, remoteJson, baseJson);
        File.WriteAllText(
            Path.Combine(localPath, "shared-child"),
            """
            {
              "Id": "shared-child",
              "Title": "Shared child"
            }
            """);
        File.WriteAllText(
            Path.Combine(localPath, "local-child"),
            """
            {
              "Id": "local-child",
              "Title": "Local child"
            }
            """);
        File.WriteAllText(
            Path.Combine(localPath, "remote-child"),
            """
            {
              "Id": "remote-child",
              "Title": "Remote child"
            }
            """);

        var relationField = service.GetConflictStatus().Conflicts.Single().Fields
            .Single(field => field.FieldPath == "ContainsTasks");

        await Assert.That(relationField.CurrentValue).Contains("Shared child (shared-child)");
        await Assert.That(relationField.CurrentValue).Contains("Local child (local-child)");
        await Assert.That(relationField.IncomingValue).Contains("Shared child (shared-child)");
        await Assert.That(relationField.IncomingValue).Contains("Remote child (remote-child)");
        await Assert.That(relationField.MergedValue).Contains("Local child (local-child)");
        await Assert.That(relationField.MergedValue).Contains("Remote child (remote-child)");
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflictFields_PreservesUnknownFieldsAndIncomingOnlyDefaults()
    {
        var baseJson = """
                       {
                         "Id": "task",
                         "Title": "base title",
                         "Description": "base description",
                         "Unknown": "base"
                       }
                       """;
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "Description": "base description",
                          "Unknown": "base"
                        }
                        """;
        var remoteJson = """
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "Description": "remote description",
                           "Unknown": "incoming unknown"
                         }
                         """;
        var (service, localPath, _) = CreateServiceWithMergeConflict(localJson, remoteJson, baseJson);

        service.ResolveConflictFields(
            "task",
            service.GetConflictStatus().Conflicts.Single().Fields
                .Select(field => new BackupConflictFieldSelection(field.FieldPath, field.DefaultSource))
                .ToList());

        var resolved = JObject.Parse(File.ReadAllText(Path.Combine(localPath, "task")));
        await Assert.That((string?)resolved["Title"]).IsEqualTo("local title");
        await Assert.That((string?)resolved["Description"]).IsEqualTo("remote description");
        await Assert.That((string?)resolved["Unknown"]).IsEqualTo("incoming unknown");
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflictFields_CopiesDateTokensWithoutReformattingUsingRawJsonTokens()
    {
        var incomingDate = "2025-01-02T03:04:05.678+03:30";
        var baseJson = """
                       {
                         "Id": "task",
                         "Title": "base title",
                         "PlannedBeginDateTime": "2025-01-02T03:04:05.678+00:00"
                       }
                       """;
        var localJson = """
                        {
                          "Id": "task",
                          "Title": "local title",
                          "PlannedBeginDateTime": "2025-01-02T03:04:05.678+00:00"
                        }
                        """;
        var remoteJson = $$"""
                         {
                           "Id": "task",
                           "Title": "remote title",
                           "PlannedBeginDateTime": "{{incomingDate}}"
                         }
                         """;
        var (service, localPath, _) = CreateServiceWithMergeConflict(localJson, remoteJson, baseJson);

        service.ResolveConflictFields(
            "task",
            new List<BackupConflictFieldSelection>
            {
                new("Title", BackupConflictFieldSource.UseCurrent),
                new("PlannedBeginDateTime", BackupConflictFieldSource.UseIncoming)
            });

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).Contains(incomingDate);
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflictFields_RejectsMergeForNonMergeableScalar()
    {
        var (service, _, _) = CreateServiceWithMergeConflict(
            """
            {
              "Id": "task",
              "Title": "local title",
              "Importance": 1
            }
            """,
            """
            {
              "Id": "task",
              "Title": "remote title",
              "Importance": 5
            }
            """,
            """
            {
              "Id": "task",
              "Title": "base title",
              "Importance": 0
            }
            """);

        await Assert.That(() => service.ResolveConflictFields(
                "task",
                new List<BackupConflictFieldSelection>
                {
                    new("Title", BackupConflictFieldSource.UseCurrent),
                    new("Importance", BackupConflictFieldSource.Merge)
                }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflict_UseDeletedSide_StagesDeletion()
    {
        var (service, localPath, remotePath) = CreateServiceWithDeleteModifyConflict();

        service.ResolveConflict("task", BackupConflictResolution.UseIncoming);

        await Assert.That(File.Exists(Path.Combine(localPath, "task"))).IsFalse();
        await Assert.That(service.GetConflictStatus().Conflicts.Count).IsEqualTo(0);

        service.CommitResolvedConflicts("Resolve sync conflict");

        using var remote = new Repository(remotePath);
        await Assert.That(remote.Branches["main"].Tip.Tree["task"]).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task CommitResolvedConflicts_DoesNotStageUnrelatedFiles()
    {
        var (service, localPath, remotePath) = CreateServiceWithMergeConflict();

        service.ResolveConflict("task", BackupConflictResolution.UseCurrent);
        File.WriteAllText(Path.Combine(localPath, "unrelated-task"), "unrelated content");
        service.CommitResolvedConflicts("Resolve sync conflict");

        using var remote = new Repository(remotePath);
        await Assert.That(remote.Branches["main"].Tip.Tree["task"]).IsNotNull();
        await Assert.That(remote.Branches["main"].Tip.Tree["unrelated-task"]).IsNull();
    }

    [Test]
    public async System.Threading.Tasks.Task CommitResolvedConflicts_RejectsWhenAnyConflictRemains()
    {
        var (service, _, _) = CreateServiceWithMergeConflict();

        await Assert.That(() => service.CommitResolvedConflicts("Resolve sync conflict"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflict_UseCurrentVersion_CommitsAndPushesResolution()
    {
        var (service, localPath, remotePath) = CreateServiceWithMergeConflict();

        service.ResolveConflict("task", BackupConflictResolution.UseCurrent);

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("local content");
        var pendingStatus = service.GetConflictStatus();
        await Assert.That(pendingStatus.IsInProgress).IsTrue();
        await Assert.That(pendingStatus.Conflicts.Count).IsEqualTo(0);

        service.CommitResolvedConflicts("Resolve sync conflict");

        using var local = new Repository(localPath);
        await Assert.That(local.Index.Conflicts.Any()).IsFalse();
        await Assert.That(local.Info.CurrentOperation).IsEqualTo(CurrentOperation.None);
        var completedStatus = service.GetConflictStatus();
        await Assert.That(completedStatus.IsInProgress).IsFalse();
        await Assert.That(completedStatus.Conflicts.Count).IsEqualTo(0);
        await Assert.That(ReadFileFromBranch(remotePath, "main", "task")).IsEqualTo("local content");
    }

    [Test]
    public async System.Threading.Tasks.Task ResolveConflict_UseIncomingVersion_CommitsAndPushesResolution()
    {
        var (service, localPath, remotePath) = CreateServiceWithMergeConflict();

        service.ResolveConflict("task", BackupConflictResolution.UseIncoming);

        await Assert.That(File.ReadAllText(Path.Combine(localPath, "task"))).IsEqualTo("remote content");
        await Assert.That(service.GetConflictStatus().Conflicts.Count).IsEqualTo(0);

        service.CommitResolvedConflicts("Resolve sync conflict");

        await Assert.That(ReadFileFromBranch(remotePath, "main", "task")).IsEqualTo("remote content");
    }

    [Test]
    public async System.Threading.Tasks.Task GetCredentials_ReturnsSshPrivateKeyCredentialsForConfiguredSshUrl()
    {
        var privateKeyPath = Path.Combine(_rootPath, "id_unlimotion");
        var publicKeyPath = $"{privateKeyPath}.pub";
        File.WriteAllText(privateKeyPath, "private key");
        File.WriteAllText(publicKeyPath, "public key");
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        var service = new BackupViaGitService(configuration);

        var credentials = service.GetCredentials(new GitSettings
        {
            SshPrivateKeyPath = privateKeyPath,
            SshPublicKeyPath = publicKeyPath
        })("git@github.com:owner/repo.git", "git", SupportedCredentialTypes.Default);

        await Assert.That(credentials.GetType()).IsEqualTo(typeof(SshPrivateKeyCredentials));
        var sshCredentials = (SshPrivateKeyCredentials)credentials;
        await Assert.That(sshCredentials.Username).IsEqualTo("git");
        await Assert.That(sshCredentials.PrivateKeyPath).IsEqualTo(privateKeyPath);
        await Assert.That(sshCredentials.PublicKeyPath).IsEqualTo(publicKeyPath);
    }

    [Test]
    public async System.Threading.Tasks.Task ShouldUseGitCliSshTransport_UsesWindowsCliOnlyForSshRemotes()
    {
        await Assert.That(BackupViaGitService.ShouldUseGitCliSshTransport(
                "git@github.com:owner/repo.git",
                isWindows: true))
            .IsTrue();
        await Assert.That(BackupViaGitService.ShouldUseGitCliSshTransport(
                "ssh://git@github.com/owner/repo.git",
                isWindows: true))
            .IsTrue();
        await Assert.That(BackupViaGitService.ShouldUseGitCliSshTransport(
                "https://github.com/owner/repo.git",
                isWindows: true))
            .IsFalse();
        await Assert.That(BackupViaGitService.ShouldUseGitCliSshTransport(
                "git@github.com:owner/repo.git",
                isWindows: false))
            .IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task BuildGitSshCommand_UsesExplicitKeyAndConfiguredKnownHostsFile()
    {
        var privateKeyPath = Path.Combine(_rootPath, "id_ed25519");
        var sshKeyStoragePath = Path.Combine(_rootPath, "portable-ssh");
        File.WriteAllText(privateKeyPath, "private key");

        var command = BackupViaGitService.BuildGitSshCommand(new GitSettings
        {
            SshPrivateKeyPath = privateKeyPath,
            SshKeyStoragePath = sshKeyStoragePath
        });
        var expectedKnownHostsPath = Path.Combine(sshKeyStoragePath, "known_hosts_unlimotion")
            .Replace('\\', '/');

        await Assert.That(command).Contains("ssh -i");
        await Assert.That(command).Contains(privateKeyPath.Replace('\\', '/'));
        await Assert.That(command).Contains("IdentitiesOnly=yes");
        await Assert.That(command).Contains("BatchMode=yes");
        await Assert.That(command).Contains("StrictHostKeyChecking=accept-new");
        await Assert.That(command).Contains($"UserKnownHostsFile=\"{expectedKnownHostsPath}\"");
    }

    [Test]
    public async System.Threading.Tasks.Task BuildGitFetchArguments_IncludesRemoteAndConfiguredRefSpecs()
    {
        var arguments = BackupViaGitService.BuildGitFetchArguments(
            "github.com",
            new[] { "+refs/heads/*:refs/remotes/github.com/*" });

        await Assert.That(arguments).IsEquivalentTo(new[]
        {
            "fetch",
            "github.com",
            "+refs/heads/*:refs/remotes/github.com/*"
        });
    }

    [Test]
    public async System.Threading.Tasks.Task GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows()
    {
        var privateKeyPath = Path.Combine(_rootPath, "id_unlimotion_acl");
        var publicKeyPath = $"{privateKeyPath}.pub";
        File.WriteAllText(privateKeyPath, "private key");
        File.WriteAllText(publicKeyPath, "public key");

        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.Exists(privateKeyPath)).IsTrue();
            return;
        }

        var extraIdentity = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        AddExplicitReadRule(privateKeyPath, extraIdentity);
        await Assert.That(HasAccessRule(privateKeyPath, extraIdentity, includeInherited: false)).IsTrue();

        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        var service = new BackupViaGitService(configuration);

        _ = service.GetCredentials(new GitSettings
        {
            SshPrivateKeyPath = privateKeyPath,
            SshPublicKeyPath = publicKeyPath
        })("git@github.com:owner/repo.git", "git", SupportedCredentialTypes.Default);

        await Assert.That(HasAccessRule(privateKeyPath, extraIdentity, includeInherited: true)).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task GetCredentials_ThrowsForSshUrlWhenPrivateKeyIsMissing()
    {
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        var service = new BackupViaGitService(configuration);
        var credentials = service.GetCredentials(new GitSettings
        {
            SshPrivateKeyPath = Path.Combine(_rootPath, "missing-key")
        });

        await Assert.That(() => credentials("git@github.com:owner/repo.git", "git", SupportedCredentialTypes.Default))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async System.Threading.Tasks.Task GetSshPublicKeys_ReadsConfiguredSshKeyStoragePath()
    {
        var sshDirectory = Path.Combine(_rootPath, "custom-ssh");
        Directory.CreateDirectory(sshDirectory);
        var firstPublicKey = Path.Combine(sshDirectory, "id_b.pub");
        var secondPublicKey = Path.Combine(sshDirectory, "id_a.pub");
        File.WriteAllText(firstPublicKey, "ssh-ed25519 b");
        File.WriteAllText(secondPublicKey, "ssh-ed25519 a");
        File.WriteAllText(Path.Combine(sshDirectory, "id_private"), "private");
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        configuration.GetSection("Git")
            .GetSection(nameof(GitSettings.SshKeyStoragePath))
            .Set(sshDirectory);
        var service = new BackupViaGitService(configuration);

        var keys = service.GetSshPublicKeys();

        await Assert.That(keys.Count).IsEqualTo(2);
        await Assert.That(keys[0]).IsEqualTo(secondPublicKey);
        await Assert.That(keys[1]).IsEqualTo(firstPublicKey);
    }

    [Test]
    public async System.Threading.Tasks.Task GenerateSshKey_CreatesKeyPairInConfiguredSshKeyStoragePath()
    {
        var sshDirectory = Path.Combine(_rootPath, "generated-ssh");
        var configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        configuration.GetSection("Git")
            .GetSection(nameof(GitSettings.SshKeyStoragePath))
            .Set(sshDirectory);
        var service = new BackupViaGitService(configuration);

        var publicKeyPath = service.GenerateSshKey("id_generated");

        var privateKeyPath = Path.Combine(sshDirectory, "id_generated");
        await Assert.That(publicKeyPath).IsEqualTo($"{privateKeyPath}.pub");
        await Assert.That(File.Exists(privateKeyPath)).IsTrue();
        await Assert.That(File.Exists(publicKeyPath)).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task GetSshDirectory_UsesDefaultWhenConfiguredPathIsBlank()
    {
        var defaultDirectory = BackupViaGitService.GetSshDirectory(new GitSettings());
        var blankDirectory = BackupViaGitService.GetSshDirectory(new GitSettings
        {
            SshKeyStoragePath = " "
        });

        await Assert.That(blankDirectory).IsEqualTo(defaultDirectory);
    }

    [Test]
    public async System.Threading.Tasks.Task GetSshDirectory_NormalizesConfiguredPath()
    {
        var configuredPath = Path.Combine(_rootPath, "relative", "..", "keys");

        var sshDirectory = BackupViaGitService.GetSshDirectory(new GitSettings
        {
            SshKeyStoragePath = configuredPath
        });

        await Assert.That(sshDirectory).IsEqualTo(Path.GetFullPath(configuredPath));
    }

    [Test]
    public async System.Threading.Tasks.Task GenerateManagedRsaSshKey_CreatesPemPrivateKeyAndOpenSshPublicKey()
    {
        var privateKeyPath = Path.Combine(_rootPath, "managed_rsa");
        var publicKeyPath = $"{privateKeyPath}.pub";

        BackupViaGitService.GenerateManagedRsaSshKey(privateKeyPath, publicKeyPath);

        await Assert.That(File.ReadAllText(privateKeyPath)).Contains("BEGIN RSA PRIVATE KEY");
        await Assert.That(File.ReadAllText(publicKeyPath)).Contains("ssh-rsa ");
        await Assert.That(File.ReadAllText(publicKeyPath)).Contains(" unlimotion");
    }

    [Test]
    public async System.Threading.Tasks.Task GenerateManagedRsaSshKey_HardensPrivateKeyPermissionsOnWindows()
    {
        var privateKeyPath = Path.Combine(_rootPath, "managed_rsa_acl");
        var publicKeyPath = $"{privateKeyPath}.pub";

        BackupViaGitService.GenerateManagedRsaSshKey(privateKeyPath, publicKeyPath);

        await Assert.That(File.Exists(privateKeyPath)).IsTrue();
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var extraIdentity = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        await Assert.That(HasAccessRule(privateKeyPath, extraIdentity, includeInherited: true)).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task IsKnownGitHubSshHostKey_AcceptsKnownGitHubFingerprint()
    {
        var ed25519Sha1 = Convert.FromHexString("E9619E2ED56C2F2A71729DB80BACC2CE9CCCE8D4");

        await Assert.That(BackupViaGitService.IsKnownGitHubSshHostKey("github.com", ed25519Sha1)).IsTrue();
        await Assert.That(BackupViaGitService.IsKnownGitHubSshHostKey("ssh.github.com:443", ed25519Sha1)).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task IsKnownGitHubSshHostKey_RejectsUnknownHostAndUnknownFingerprint()
    {
        var ed25519Sha1 = Convert.FromHexString("E9619E2ED56C2F2A71729DB80BACC2CE9CCCE8D4");
        var unknownSha1 = Convert.FromHexString("0000000000000000000000000000000000000000");

        await Assert.That(BackupViaGitService.IsKnownGitHubSshHostKey("gitlab.com", ed25519Sha1)).IsFalse();
        await Assert.That(BackupViaGitService.IsKnownGitHubSshHostKey("github.com", unknownSha1)).IsFalse();
    }

    [Test]
    public async System.Threading.Tasks.Task IsKnownOrTrustFirstUseSshHostKey_StoresFirstSeenFingerprint()
    {
        var knownHostsPath = Path.Combine(_rootPath, ".ssh", "known_hosts_unlimotion");
        var sha1 = Convert.FromHexString("1111111111111111111111111111111111111111");

        await Assert.That(BackupViaGitService.IsKnownOrTrustFirstUseSshHostKey(knownHostsPath, "example.com:22", sha1)).IsTrue();
        await Assert.That(File.ReadAllText(knownHostsPath)).Contains("example.com 1111111111111111111111111111111111111111");
        await Assert.That(BackupViaGitService.IsKnownOrTrustFirstUseSshHostKey(knownHostsPath, "example.com", sha1)).IsTrue();
    }

    [Test]
    public async System.Threading.Tasks.Task IsKnownOrTrustFirstUseSshHostKey_RejectsChangedFingerprint()
    {
        var knownHostsPath = Path.Combine(_rootPath, ".ssh", "known_hosts_unlimotion");
        var originalSha1 = Convert.FromHexString("1111111111111111111111111111111111111111");
        var changedSha1 = Convert.FromHexString("2222222222222222222222222222222222222222");

        await Assert.That(BackupViaGitService.IsKnownOrTrustFirstUseSshHostKey(knownHostsPath, "example.com", originalSha1)).IsTrue();
        await Assert.That(BackupViaGitService.IsKnownOrTrustFirstUseSshHostKey(knownHostsPath, "example.com", changedSha1)).IsFalse();
    }

    private string CreateLocalTaskFolder()
    {
        var localPath = Path.Combine(_rootPath, "Tasks");
        Directory.CreateDirectory(localPath);
        File.WriteAllText(Path.Combine(localPath, "local-task"), "local content");
        return localPath;
    }

    private (BackupViaGitService Service, string LocalPath, string RemotePath) CreateServiceWithMergeConflict()
    {
        return CreateServiceWithMergeConflict("local content", "remote content", "base content");
    }

    private (BackupViaGitService Service, string LocalPath, string RemotePath) CreateServiceWithMergeConflict(
        string localContent,
        string remoteContent,
        string baseContent = """
                             {
                               "Id": "task",
                               "Title": "base title",
                               "Description": "base description",
                               "ContainsTasks": [],
                               "Importance": 0
                             }
                             """)
    {
        var localPath = Path.Combine(_rootPath, $"ConflictedTasks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localPath);
        var remotePath = CreateBareRemoteWithCommit("task", baseContent);
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);
        CommitRepositoryFile(localPath, "task", localContent, "local change");
        PushRemoteChange(remotePath, "task", remoteContent);
        service.Pull();

        return (service, localPath, remotePath);
    }

    private (BackupViaGitService Service, string LocalPath, string RemotePath) CreateServiceWithDeleteModifyConflict()
    {
        var localPath = Path.Combine(_rootPath, $"DeleteModifyTasks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localPath);
        var remotePath = CreateBareRemoteWithCommit("task", "base content");
        var service = CreateService(localPath, remotePath);

        service.ConnectRepository(allowMergeWithNonEmptyRemote: false);
        CommitRepositoryFile(localPath, "task", "local content", "local change");
        DeleteRemoteFile(remotePath, "task");
        service.Pull();

        return (service, localPath, remotePath);
    }

    private string CreateBareRemote()
    {
        var remotePath = Path.Combine(_rootPath, $"remote-{Guid.NewGuid():N}.git");
        Repository.Init(remotePath, isBare: true);
        return remotePath;
    }

    private string CreateBareRemoteWithCommit(string fileName, string content)
    {
        return CreateBareRemoteWithFiles((fileName, content));
    }

    private string CreateBareRemoteWithFiles(params (string FileName, string Content)[] files)
    {
        var remotePath = CreateBareRemote();
        var seedPath = Path.Combine(_rootPath, $"seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(seedPath);
        Repository.Init(seedPath);
        foreach (var (fileName, content) in files)
        {
            File.WriteAllText(Path.Combine(seedPath, fileName), content);
        }

        using (var seed = new Repository(seedPath))
        {
            Commands.Stage(seed, "*");
            var signature = CreateSignature();
            var commit = seed.Commit("seed remote", signature, signature);
            Commands.Checkout(seed, seed.CreateBranch("main", commit));
            seed.Network.Remotes.Add("origin", remotePath);
            seed.Network.Push(seed.Network.Remotes["origin"], "refs/heads/main", new PushOptions());
        }

        TryDeleteDirectory(seedPath);
        return remotePath;
    }

    private void PushRemoteChange(string remotePath, string fileName, string content)
    {
        var seedPath = Path.Combine(_rootPath, $"remote-change-{Guid.NewGuid():N}");
        Repository.Clone(remotePath, seedPath);
        try
        {
            using (var seed = new Repository(seedPath))
            {
                var remoteBranch = seed.Branches["origin/main"]
                                   ?? throw new InvalidOperationException("Remote main branch was not cloned.");
                var localBranch = seed.Branches["main"] ?? seed.CreateBranch("main", remoteBranch.Tip);
                seed.Branches.Update(localBranch, updater => updater.TrackedBranch = remoteBranch.CanonicalName);
                Commands.Checkout(seed, localBranch);
            }

            CommitRepositoryFile(seedPath, fileName, content, "remote change");
            using var pushRepo = new Repository(seedPath);
            pushRepo.Network.Push(pushRepo.Network.Remotes["origin"], "refs/heads/main", new PushOptions());
        }
        finally
        {
            TryDeleteDirectory(seedPath);
        }
    }

    private string CloneRemoteToLocalMain(string remotePath)
    {
        var localPath = Path.Combine(_rootPath, $"local-clone-{Guid.NewGuid():N}");
        Repository.Clone(remotePath, localPath);
        using var repo = new Repository(localPath);
        var remoteBranch = repo.Branches["origin/main"]
                           ?? throw new InvalidOperationException("Remote main branch was not cloned.");
        var localBranch = repo.Branches["main"] ?? repo.CreateBranch("main", remoteBranch.Tip);
        repo.Branches.Update(localBranch, updater => updater.TrackedBranch = remoteBranch.CanonicalName);
        Commands.Checkout(repo, localBranch);
        return localPath;
    }

    private string CreateInitializedRepositoryWithRemote(string remoteName, string remoteUrl)
    {
        var localPath = Path.Combine(_rootPath, $"remote-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localPath);
        Repository.Init(localPath);
        using var repo = new Repository(localPath);
        repo.Network.Remotes.Add(remoteName, remoteUrl);
        return localPath;
    }

    private void DeleteRemoteFile(string remotePath, string fileName)
    {
        var seedPath = Path.Combine(_rootPath, $"remote-delete-{Guid.NewGuid():N}");
        Repository.Clone(remotePath, seedPath);
        try
        {
            using (var seed = new Repository(seedPath))
            {
                var remoteBranch = seed.Branches["origin/main"]
                                   ?? throw new InvalidOperationException("Remote main branch was not cloned.");
                var localBranch = seed.Branches["main"] ?? seed.CreateBranch("main", remoteBranch.Tip);
                seed.Branches.Update(localBranch, updater => updater.TrackedBranch = remoteBranch.CanonicalName);
                Commands.Checkout(seed, localBranch);
            }

            File.Delete(Path.Combine(seedPath, fileName));
            using var deleteRepo = new Repository(seedPath);
            Commands.Stage(deleteRepo, fileName);
            var signature = CreateSignature();
            deleteRepo.Commit("remote delete", signature, signature);
            deleteRepo.Network.Push(deleteRepo.Network.Remotes["origin"], "refs/heads/main", new PushOptions());
        }
        finally
        {
            TryDeleteDirectory(seedPath);
        }
    }

    private static void CommitRepositoryFile(string repositoryPath, string fileName, string content, string message)
    {
        File.WriteAllText(Path.Combine(repositoryPath, fileName), content);
        using var repo = new Repository(repositoryPath);
        Commands.Stage(repo, fileName);
        var signature = CreateSignature();
        repo.Commit(message, signature, signature);
    }

    private static string ReadFileFromBranch(string repositoryPath, string branchName, string fileName)
    {
        using var repo = new Repository(repositoryPath);
        var branch = repo.Branches[branchName] ?? throw new InvalidOperationException($"Branch not found: {branchName}");
        var treeEntry = branch.Tip.Tree[fileName] ?? throw new InvalidOperationException($"File not found: {fileName}");
        var blob = (Blob)treeEntry.Target;
        using var reader = new StreamReader(blob.GetContentStream());
        return reader.ReadToEnd();
    }

    private BackupViaGitService CreateService(
        string localPath,
        string remotePath,
        string pushRefSpec = "refs/heads/main",
        string branch = "main",
        string remoteName = "origin",
        ITaskStorageFactory? storageFactory = null)
    {
        return CreateService(localPath, remotePath, out _, pushRefSpec, branch, remoteName, storageFactory);
    }

    private BackupViaGitService CreateService(
        string localPath,
        string remotePath,
        out IConfigurationRoot configuration,
        string pushRefSpec = "refs/heads/main",
        string branch = "main",
        string remoteName = "origin",
        ITaskStorageFactory? storageFactory = null)
    {
        configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);

        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(localPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set(remotePath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set(remoteName);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set(pushRefSpec);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Branch)).Set(branch);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.CommitterName)).Set("Backuper");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.CommitterEmail)).Set("backuper@example.com");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.ShowStatusToasts)).Set(false);

        return new BackupViaGitService(configuration, storageFactory: storageFactory);
    }

    private sealed class FakeTaskStorageFactory(IDatabaseWatcher? currentWatcher) : ITaskStorageFactory
    {
        public ITaskStorage? CurrentStorage => null;
        public IDatabaseWatcher? CurrentWatcher { get; } = currentWatcher;
        public ITaskStorage CreateFileStorage(string? path) => throw new NotSupportedException();
        public ITaskStorage CreateServerStorage(string? url) => throw new NotSupportedException();
        public void SwitchStorage(bool isServerMode, IConfiguration configuration) => throw new NotSupportedException();
    }

    private sealed class CurrentFileStorageFactory : ITaskStorageFactory, IDisposable
    {
        private readonly ITaskStorage _storage;

        public CurrentFileStorageFactory(string currentPath)
        {
            var fileStorage = new FileStorage(currentPath, watcher: false);
            _storage = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));
        }

        public ITaskStorage? CurrentStorage => _storage;
        public IDatabaseWatcher? CurrentWatcher => null;
        public ITaskStorage CreateFileStorage(string? path) => throw new NotSupportedException();
        public ITaskStorage CreateServerStorage(string? url) => throw new NotSupportedException();
        public void SwitchStorage(bool isServerMode, IConfiguration configuration) => throw new NotSupportedException();
        public void Dispose() => (_storage as IDisposable)?.Dispose();
    }

    private sealed class FakeDatabaseWatcher : IDatabaseWatcher
    {
        public event EventHandler<DbUpdatedEventArgs>? OnUpdated;
        public int SetEnableCalls { get; private set; }
        public int ForceUpdateFileCalls { get; private set; }
        public string? LastForcedFileName { get; private set; }
        public UpdateType? LastForcedUpdateType { get; private set; }

        public void AddIgnoredTask(string taskId)
        {
        }

        public void SetEnable(bool enable)
        {
            SetEnableCalls++;
        }

        public void ForceUpdateFile(string filename, UpdateType type)
        {
            ForceUpdateFileCalls++;
            LastForcedFileName = filename;
            LastForcedUpdateType = type;
            OnUpdated?.Invoke(this, new DbUpdatedEventArgs { Id = filename, Type = type });
        }
    }

    private static Signature CreateSignature() =>
        new("Backuper", "backuper@example.com", DateTimeOffset.Now);

    [SupportedOSPlatform("windows")]
    private static void AddExplicitReadRule(string filePath, SecurityIdentifier identity)
    {
        var fileInfo = new FileInfo(filePath);
        var security = fileInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.Read,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        fileInfo.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool HasAccessRule(string filePath, SecurityIdentifier identity, bool includeInherited)
    {
        var security = new FileInfo(filePath).GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited, typeof(SecurityIdentifier));
        return rules
            .Cast<FileSystemAccessRule>()
            .Any(rule => identity.Equals(rule.IdentityReference));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
