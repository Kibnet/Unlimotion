using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public async System.Threading.Tasks.Task GetCredentials_ReturnsSshPrivateKeyCredentialsForConfiguredSshUrl()
    {
        var privateKeyPath = Path.Combine(_rootPath, "id_unlimotion");
        var publicKeyPath = $"{privateKeyPath}.pub";
        File.WriteAllText(privateKeyPath, "private key");
        File.WriteAllText(publicKeyPath, "public key");
        var configuration = WritableJsonConfigurationFabric.Create(_configPath);
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

        var configuration = WritableJsonConfigurationFabric.Create(_configPath);
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
        var configuration = WritableJsonConfigurationFabric.Create(_configPath);
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

        return new BackupViaGitService(configuration);
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
