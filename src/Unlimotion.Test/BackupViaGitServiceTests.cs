using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class BackupViaGitServiceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _configPath;

    public BackupViaGitServiceTests()
    {
        _rootPath = Path.Combine(Environment.CurrentDirectory, $"GitBootstrap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
        _configPath = Path.Combine(_rootPath, "settings.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
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

    private string CreateLocalTaskFolder()
    {
        var localPath = Path.Combine(_rootPath, "Tasks");
        Directory.CreateDirectory(localPath);
        File.WriteAllText(Path.Combine(localPath, "local-task"), "local content");
        return localPath;
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

    private BackupViaGitService CreateService(
        string localPath,
        string remotePath,
        string pushRefSpec = "refs/heads/main",
        string branch = "main",
        string remoteName = "origin")
    {
        return CreateService(localPath, remotePath, out _, pushRefSpec, branch, remoteName);
    }

    private BackupViaGitService CreateService(
        string localPath,
        string remotePath,
        out IConfigurationRoot configuration,
        string pushRefSpec = "refs/heads/main",
        string branch = "main",
        string remoteName = "origin")
    {
        configuration = WritableJsonConfigurationFabric.Create(_configPath);
        configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(localPath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set(remotePath);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set(remoteName);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.PushRefSpec)).Set(pushRefSpec);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Branch)).Set(branch);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.CommitterName)).Set("Backuper");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.CommitterEmail)).Set("backuper@example.com");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.ShowStatusToasts)).Set(false);

        return new BackupViaGitService(configuration);
    }

    private static Signature CreateSignature() =>
        new("Backuper", "backuper@example.com", DateTimeOffset.Now);

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
