using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string DefaultSshKeyFileName = "id_ed25519_unlimotion";
    private const string KnownHostsFileName = "known_hosts_unlimotion";
    private const string TasksFolderName = "Tasks";
    // SHA1 hashes of GitHub SSH host key blobs derived from GitHub-published known_hosts entries.
    private static readonly byte[][] GitHubSshHostKeySha1Hashes =
    {
        Convert.FromHexString("E9619E2ED56C2F2A71729DB80BACC2CE9CCCE8D4"),
        Convert.FromHexString("3358AB5DD3E306C461C840F7487E93B697E30600"),
        Convert.FromHexString("6F4C60375018BAE0918E37D9162BC15BA40E6365")
    };

    private static readonly string[] IgnoredMigrationReportFileNames =
    {
        "availability.migration.report",
        "migration.report"
    };

    private static readonly HashSet<string> TextMergeFieldNames = new(StringComparer.Ordinal)
    {
        "Title",
        "Description"
    };

    private static readonly HashSet<string> RelationFieldNames = new(StringComparer.Ordinal)
    {
        "ContainsTasks",
        "ParentTasks",
        "BlocksTasks",
        "BlockedByTasks"
    };

    private static readonly object LockObject = new();
    public static Func<string, string> GetAbsolutePath = null!;

    private readonly IConfiguration _configuration;
    private readonly INotificationManagerWrapper? _notificationManager;
    private readonly ITaskStorageFactory? _storageFactory;

    public BackupViaGitService(
        IConfiguration configuration,
        INotificationManagerWrapper? notificationManager = null,
        ITaskStorageFactory? storageFactory = null)
    {
        _configuration = configuration;
        _notificationManager = notificationManager;
        _storageFactory = storageFactory;
    }

    public List<string> Refs()
    {
        var result = new List<string>();
        try
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return result;
            }

            using var repo = new Repository(path);
            foreach (var reference in repo.Refs)
            {
                if (reference.CanonicalName.StartsWith("refs/heads", StringComparison.Ordinal))
                {
                    result.Add(reference.CanonicalName);
                }
            }
        }
        catch (Exception ex)
        {
            ShowUiError(ex.Message, ex);
        }

        return result;
    }

    public List<string> Remotes()
    {
        var result = new List<string>();
        try
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return result;
            }

            using var repo = new Repository(path);
            foreach (var remote in repo.Network.Remotes)
            {
                result.Add(remote.Name);
            }
        }
        catch (Exception ex)
        {
            ShowUiError(ex.Message, ex);
        }

        return result;
    }

    public string? GetRemoteAuthType(string remoteName)
    {
        try
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return null;
            }

            using var repo = new Repository(path);
            var remote = repo.Network.Remotes.FirstOrDefault(r => r.Name == remoteName);
            return remote == null ? null : DetectAuthType(remote.Url);
        }
        catch (Exception ex)
        {
            ShowUiError(ex.Message, ex);
            return null;
        }
    }

    public string? GetRemoteUrl(string remoteName)
    {
        try
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return null;
            }

            using var repo = new Repository(path);
            return repo.Network.Remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
        }
        catch (Exception ex)
        {
            ShowUiError(ex.Message, ex);
            return null;
        }
    }

    public RemoteConnectionTypeSwitchResult SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode)
    {
        lock (LockObject)
        {
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                throw new InvalidOperationException(L10n.Get("RemoteNameNotConfigured"));
            }

            var settings = GetSettings();
            var repositoryPath = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(repositoryPath))
            {
                throw new InvalidOperationException(L10n.Get("RepositoryNotInitialized"));
            }

            using var repo = new Repository(repositoryPath);
            var selectedRemote = repo.Network.Remotes[remoteName]
                                 ?? throw new InvalidOperationException(L10n.Format("RemoteNotFound", remoteName));

            if (ParseBackupAuthMode(selectedRemote.Url) == targetMode)
            {
                SetRemoteName(settings.git, selectedRemote.Name);
                SetRemoteUrl(settings.git, selectedRemote.Url);
                return new RemoteConnectionTypeSwitchResult(
                    selectedRemote.Name,
                    selectedRemote.Url,
                    DetectAuthType(selectedRemote.Url),
                    CreatedRemote: false);
            }

            var targetUrl = BuildRemoteUrlForConnectionType(selectedRemote.Url, targetMode);
            var existingRemote = repo.Network.Remotes.FirstOrDefault(remote =>
                AreEquivalentRemoteUrls(remote.Url, targetUrl));
            if (existingRemote != null)
            {
                SetRemoteName(settings.git, existingRemote.Name);
                SetRemoteUrl(settings.git, existingRemote.Url);
                return new RemoteConnectionTypeSwitchResult(
                    existingRemote.Name,
                    existingRemote.Url,
                    DetectAuthType(existingRemote.Url),
                    CreatedRemote: false);
            }

            var targetRemoteName = CreateUniqueRemoteName(
                repo,
                $"{selectedRemote.Name}-{(targetMode == BackupAuthMode.Ssh ? "ssh" : "http")}");
            var createdRemote = repo.Network.Remotes.Add(targetRemoteName, targetUrl);
            SetRemoteName(settings.git, createdRemote.Name);
            SetRemoteUrl(settings.git, createdRemote.Url);
            return new RemoteConnectionTypeSwitchResult(
                createdRemote.Name,
                createdRemote.Url,
                DetectAuthType(createdRemote.Url),
                CreatedRemote: true);
        }
    }

    public List<string> GetSshPublicKeys()
    {
        var sshDirectory = GetSshDirectory(GetSettings().git);
        if (!Directory.Exists(sshDirectory))
        {
            return new List<string>();
        }

        return Directory.GetFiles(sshDirectory, "*.pub", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string GenerateSshKey(string keyName)
    {
        var sshDirectory = GetSshDirectory(GetSettings().git);
        Directory.CreateDirectory(sshDirectory);

        var keyPaths = GetSshKeyPaths(sshDirectory, keyName);
        if (File.Exists(keyPaths.PrivateKeyPath) || File.Exists(keyPaths.PublicKeyPath))
        {
            throw new InvalidOperationException(L10n.Format("SshKeyAlreadyExists", keyPaths.PublicKeyPath));
        }

        try
        {
            var processResult = RunProcess(CreateProcessStartInfo(
                "ssh-keygen",
                sshDirectory,
                "-t",
                "ed25519",
                "-f",
                keyPaths.PrivateKeyPath,
                "-N",
                string.Empty,
                "-C",
                "unlimotion"));

            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(L10n.Format("SshKeygenFailed", GetProcessError(processResult)));
            }
        }
        catch (Exception ex) when (ShouldUseManagedSshKeyGenerationFallback(ex))
        {
            GenerateManagedRsaSshKey(keyPaths.PrivateKeyPath, keyPaths.PublicKeyPath);
        }

        if (!File.Exists(keyPaths.PrivateKeyPath) || !File.Exists(keyPaths.PublicKeyPath))
        {
            throw new InvalidOperationException(L10n.Format("SshKeygenDidNotCreateFiles", sshDirectory));
        }

        TrySetPrivateKeyPermissions(keyPaths.PrivateKeyPath);
        return keyPaths.PublicKeyPath;
    }

    public string? ReadPublicKey(string publicKeyPath)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPath) || !File.Exists(publicKeyPath))
        {
            return null;
        }

        return File.ReadAllText(publicKeyPath).Trim();
    }

    public BackupConflictStatus GetConflictStatus()
    {
        try
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return BackupConflictStatus.None;
            }

            using var repo = new Repository(path);
            var conflicts = repo.Index.Conflicts
                .Select(conflict =>
                {
                    var conflictPath = GetConflictPath(conflict);
                    return new BackupConflictFile(
                        conflictPath,
                        conflict.Ours != null,
                        conflict.Theirs != null,
                        CreateConflictFields(repo, conflict.Ancestor, conflict.Ours, conflict.Theirs));
                })
                .OrderBy(conflict => conflict.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new BackupConflictStatus(HasConflictResolutionInProgress(repo), conflicts);
        }
        catch (Exception ex)
        {
            ShowUiError(ex.Message, ex);
            return BackupConflictStatus.None;
        }
    }

    public void ResolveConflict(string path, BackupConflictResolution resolution)
    {
        lock (LockObject)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(L10n.Get("ConflictPathNotSpecified"));
            }

            var settings = GetSettings();
            var repositoryPath = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(repositoryPath))
            {
                throw new InvalidOperationException(L10n.Get("RepositoryNotInitialized"));
            }

            using var repo = new Repository(repositoryPath);
            var conflict = repo.Index.Conflicts[path]
                           ?? throw new InvalidOperationException(L10n.Format("ConflictNotFound", path));
            var conflictPath = GetConflictPath(conflict);
            var selectedEntry = resolution == BackupConflictResolution.UseCurrent
                ? conflict.Ours
                : conflict.Theirs;

            ResolveConflictEntry(repo, repositoryPath, conflictPath, selectedEntry);
        }
    }

    public void ResolveConflictFields(string path, IReadOnlyList<BackupConflictFieldSelection> fieldSelections)
    {
        lock (LockObject)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(L10n.Get("ConflictPathNotSpecified"));
            }

            if (fieldSelections.Count == 0)
            {
                throw new InvalidOperationException(L10n.Get("ConflictFieldsNotSpecified"));
            }

            var settings = GetSettings();
            var repositoryPath = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(repositoryPath))
            {
                throw new InvalidOperationException(L10n.Get("RepositoryNotInitialized"));
            }

            using var repo = new Repository(repositoryPath);
            var conflict = repo.Index.Conflicts[path]
                           ?? throw new InvalidOperationException(L10n.Format("ConflictNotFound", path));
            ResolveConflictFieldsEntry(
                repo,
                repositoryPath,
                GetConflictPath(conflict),
                conflict.Ancestor,
                conflict.Ours,
                conflict.Theirs,
                fieldSelections);
        }
    }

    public void CommitResolvedConflicts(string message)
    {
        lock (LockObject)
        {
            var settings = GetSettings();
            var repositoryPath = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(repositoryPath))
            {
                throw new InvalidOperationException(L10n.Get("RepositoryNotInitialized"));
            }

            using var repo = new Repository(repositoryPath);
            if (repo.Index.Conflicts.Any())
            {
                throw new InvalidOperationException(L10n.Get("ResolveAllSyncConflictsBeforeCommit"));
            }

            if (!repo.RetrieveStatus().IsDirty && !HasConflictResolutionInProgress(repo))
            {
                return;
            }

            var signature = CreateSignature(settings.git);
            repo.Commit(
                string.IsNullOrWhiteSpace(message) ? L10n.Get("ResolveSyncConflictsCommitMessage") : message,
                signature,
                signature);
            PushConfiguredBranch(repo, repositoryPath, settings.git);
            ShowUiMessage(L10n.Get("ConflictResolutionCommitted"));
        }
    }

    public CredentialsHandler GetCredentials(GitSettings gitSettings)
    {
        return (url, _user, _cred) =>
        {
            if (IsSshUrl(url))
            {
                var privateKeyPath = GetConfiguredSshPrivateKeyPath(gitSettings);
                return new SshPrivateKeyCredentials(
                    GetSshUsername(url, _user),
                    privateKeyPath,
                    GetConfiguredSshPublicKeyPath(gitSettings, privateKeyPath));
            }

            return new UsernamePasswordCredentials
            {
                Username = gitSettings.UserName,
                Password = gitSettings.Password
            };
        };
    }

    private FetchOptions CreateFetchOptions(GitSettings gitSettings)
    {
        return new FetchOptions
        {
            CredentialsProvider = GetCredentials(gitSettings),
            CertificateCheck = (certificate, valid, host) =>
                CheckRemoteCertificate(certificate, valid, host, gitSettings)
        };
    }

    private PushOptions CreatePushOptions(GitSettings gitSettings)
    {
        return new PushOptions
        {
            CredentialsProvider = GetCredentials(gitSettings),
            CertificateCheck = (certificate, valid, host) =>
                CheckRemoteCertificate(certificate, valid, host, gitSettings)
        };
    }

    private static ProxyOptions CreateProxyOptions(GitSettings gitSettings)
    {
        return new ProxyOptions
        {
            CertificateCheck = (certificate, valid, host) =>
                CheckRemoteCertificate(certificate, valid, host, gitSettings)
        };
    }

    internal static bool CheckRemoteCertificate(Certificate certificate, bool valid, string host)
    {
        return CheckRemoteCertificate(certificate, valid, host, gitSettings: null);
    }

    private static bool CheckRemoteCertificate(
        Certificate certificate,
        bool valid,
        string host,
        GitSettings? gitSettings)
    {
        if (valid)
        {
            return true;
        }

        if (certificate is not CertificateSsh sshCertificate ||
            !sshCertificate.HasSHA1 ||
            sshCertificate.HashSHA1 == null)
        {
            return false;
        }

        return IsKnownGitHubSshHostKey(host, sshCertificate.HashSHA1) ||
               IsKnownOrTrustFirstUseSshHostKey(GetKnownHostsPath(gitSettings), host, sshCertificate.HashSHA1);
    }

    internal static bool IsKnownGitHubSshHostKey(string host, byte[] sha1Hash)
    {
        if (!IsGitHubSshHost(host))
        {
            return false;
        }

        return GitHubSshHostKeySha1Hashes.Any(expectedHash => expectedHash.SequenceEqual(sha1Hash));
    }

    private static bool IsGitHubSshHost(string host)
    {
        var normalizedHost = NormalizeSshHost(host);
        return string.Equals(normalizedHost, "github.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedHost, "ssh.github.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsKnownOrTrustFirstUseSshHostKey(string knownHostsPath, string host, byte[] sha1Hash)
    {
        var normalizedHost = NormalizeSshHost(host);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return false;
        }

        var hashHex = Convert.ToHexString(sha1Hash);
        if (File.Exists(knownHostsPath))
        {
            foreach (var line in File.ReadLines(knownHostsPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !string.Equals(parts[0], normalizedHost, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return string.Equals(parts[1], hashHex, StringComparison.OrdinalIgnoreCase);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(knownHostsPath) ?? ".");
        File.AppendAllText(knownHostsPath, $"{normalizedHost} {hashHex}{Environment.NewLine}");
        return true;
    }

    private static string NormalizeSshHost(string host)
    {
        var normalizedHost = host.Trim();
        if (normalizedHost.StartsWith("[", StringComparison.Ordinal) &&
            normalizedHost.Contains(']', StringComparison.Ordinal))
        {
            normalizedHost = normalizedHost[1..normalizedHost.IndexOf(']', StringComparison.Ordinal)];
        }
        else
        {
            var portSeparatorIndex = normalizedHost.LastIndexOf(':');
            if (portSeparatorIndex > 0 &&
                normalizedHost[(portSeparatorIndex + 1)..].All(char.IsDigit))
            {
                normalizedHost = normalizedHost[..portSeparatorIndex];
            }
        }

        return normalizedHost;
    }

    public BackupRepositoryConnectPreview PreviewConnectRepository()
    {
        var settings = GetSettings();
        var repositoryPath = GetRepositoryPath(settings.repositoryPath);
        var remoteUrl = ResolveRemoteUrl(settings.git, repositoryPath);
        var localRepositoryExists = Repository.IsValid(repositoryPath);
        var localFolderHasContent = HasLocalFolderContent(repositoryPath);
        var remoteHasContent = RemoteHasContent(remoteUrl, settings.git);

        var action = localRepositoryExists
            ? BackupRepositoryConnectAction.PullExistingRepository
            : remoteHasContent
                ? localFolderHasContent
                    ? BackupRepositoryConnectAction.MergeNonEmptyLocalWithRemote
                    : BackupRepositoryConnectAction.FetchIntoEmptyLocalFolder
                : BackupRepositoryConnectAction.InitializeLocalAndPush;

        return new BackupRepositoryConnectPreview(
            action,
            action == BackupRepositoryConnectAction.MergeNonEmptyLocalWithRemote,
            localFolderHasContent,
            remoteHasContent,
            repositoryPath,
            remoteUrl);
    }

    public void ConnectRepository(bool allowMergeWithNonEmptyRemote)
    {
        lock (LockObject)
        {
            var preview = PreviewConnectRepository();
            var settings = GetSettings();

            switch (preview.Action)
            {
                case BackupRepositoryConnectAction.PullExistingRepository:
                    EnsureGitSelectionFromLocalRepository(preview.RepositoryPath, settings.git);
                    Pull();
                    return;
                case BackupRepositoryConnectAction.InitializeLocalAndPush:
                    InitializeLocalRepositoryAndPush(preview.RepositoryPath, preview.RemoteUrl, settings.git);
                    return;
                case BackupRepositoryConnectAction.FetchIntoEmptyLocalFolder:
                    InitializeLocalRepositoryAndCheckoutRemote(preview.RepositoryPath, preview.RemoteUrl, settings.git);
                    return;
                case BackupRepositoryConnectAction.MergeNonEmptyLocalWithRemote:
                    if (!allowMergeWithNonEmptyRemote)
                    {
                        throw new InvalidOperationException(
                            L10n.Get("BackupConnectCanceledNonEmpty"));
                    }

                    InitializeLocalRepositoryMergeAndPush(preview.RepositoryPath, preview.RemoteUrl, settings.git);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported Git connect action: {preview.Action}.");
            }
        }
    }

    public void CloneOrUpdateRepo()
    {
        try
        {
            ConnectRepository(allowMergeWithNonEmptyRemote: false);
        }
        catch (Exception ex)
        {
            ShowUiError(L10n.Format("CloneOrUpdateRepositoryFailed", ex.Message), ex);
        }
    }

    public void Push(string msg)
    {
        lock (LockObject)
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return;
            }

            CheckGitSettings(settings.git.UserName, settings.git.Password);

            using var repo = new Repository(path);
            var remote = repo.Network.Remotes[settings.git.RemoteName];
            if (remote == null)
            {
                ShowUiError(L10n.Format("RemoteNotFound", settings.git.RemoteName));
                return;
            }

            var dbwatcher = _storageFactory?.CurrentWatcher;

            ShowUiMessage(L10n.Get("StartGitPush"));
            try
            {
                dbwatcher?.SetEnable(false);
                if (repo.RetrieveStatus().IsDirty)
                {
                    Commands.Checkout(repo, settings.git.PushRefSpec);
                    Commands.Stage(repo, "*");

                    var committer = new Signature(settings.git.CommitterName, settings.git.CommitterEmail, DateTime.Now);
                    repo.Commit(msg, committer, committer);

                    ShowUiMessage(L10n.Get("CommitCreated"));
                }
            }
            finally
            {
                dbwatcher?.SetEnable(true);
            }

            var localBranch = repo.Branches[settings.git.PushRefSpec];
            if (localBranch == null)
            {
                ShowUiError(L10n.Format("LocalBranchNotFound", settings.git.PushRefSpec));
                return;
            }

            var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];
            if (remoteBranch == null || localBranch.Tip.Sha != remoteBranch.Tip.Sha)
            {
                try
                {
                    dbwatcher?.SetEnable(false);

                    PushRemote(repo, path, settings.git, settings.git.PushRefSpec);

                    ShowUiMessage(L10n.Get("PushSuccessful"));
                }
                catch (Exception ex)
                {
                    var errorMessage = L10n.Format("PushRemoteFailed", ex.Message);
                    Debug.WriteLine(errorMessage);
                    ShowUiError(errorMessage, ex);
                }
                finally
                {
                    dbwatcher?.SetEnable(true);
                }
            }
        }
    }

    public void Pull()
    {
        lock (LockObject)
        {
            PullCurrentRepository(notifyCurrentWatcher: true);
        }
    }

    public void PullExistingRepository()
    {
        lock (LockObject)
        {
            var settings = GetSettings();
            var path = GetRepositoryPath(settings.repositoryPath);
            if (!Repository.IsValid(path))
            {
                return;
            }

            using (var repo = new Repository(path))
            {
                if (!repo.Network.Remotes.Any())
                {
                    return;
                }

                EnsureRemoteNameMatchesLocalRepository(repo, settings.git);
                EnsurePushRefSpecMatchesLocalRepository(repo, settings.git);
            }

            PullCurrentRepository(notifyCurrentWatcher: false);
        }
    }

    private void PullCurrentRepository(bool notifyCurrentWatcher)
    {
        var settings = GetSettings();
        var path = GetRepositoryPath(settings.repositoryPath);
        if (!Repository.IsValid(path))
        {
            return;
        }

        CheckGitSettings(settings.git.UserName, settings.git.Password);

        using var repo = new Repository(path);
        var remote = repo.Network.Remotes[settings.git.RemoteName];
        if (remote == null)
        {
            ShowUiError(L10n.Format("RemoteNotFound", settings.git.RemoteName));
            return;
        }

        ShowUiMessage(L10n.Get("StartGitPull"));

        var dbwatcher = notifyCurrentWatcher ? _storageFactory?.CurrentWatcher : null;
        try
        {
            dbwatcher?.SetEnable(false);

            FetchRemote(repo, path, settings.git);

            var localBranch = repo.Branches[settings.git.PushRefSpec];
            if (localBranch == null)
            {
                ShowUiError(L10n.Format("LocalBranchNotFound", settings.git.PushRefSpec));
                return;
            }

            var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];
            if (remoteBranch == null)
            {
                ShowUiError(L10n.Format("RemoteBranchNotFoundAfterFetch", $"{settings.git.RemoteName}/{localBranch.FriendlyName}"));
                return;
            }

            if (localBranch.Tip.Sha != remoteBranch.Tip.Sha)
            {
                var changes = repo.Diff.Compare<TreeChanges>(localBranch.Tip.Tree, remoteBranch.Tip.Tree);
                var signature = new Signature(
                    new Identity(settings.git.CommitterName, settings.git.CommitterEmail),
                    DateTimeOffset.Now);

                var stash = repo.Stashes.Add(signature, "Stash before merge");

                Commands.Checkout(repo, settings.git.PushRefSpec);

                try
                {
                    repo.Merge(remoteBranch, signature, new MergeOptions());

                    foreach (var change in changes)
                    {
                        UpdateType mode;
                        switch (change.Status)
                        {
                            case ChangeKind.Added:
                            case ChangeKind.Modified:
                            case ChangeKind.Renamed:
                            case ChangeKind.Copied:
                                mode = UpdateType.Saved;
                                break;
                            case ChangeKind.Deleted:
                                mode = UpdateType.Removed;
                                break;
                            default:
                                continue;
                        }

                        dbwatcher?.ForceUpdateFile(change.Path, mode);
                    }

                    ShowUiMessage(L10n.Get("MergeSuccessful"));
                }
                catch (Exception ex)
                {
                    var errorMessage = L10n.Format("MergeRemoteBranchFailed", ex.Message);
                    Debug.WriteLine(errorMessage);
                    ShowUiError(errorMessage, ex);
                }

                if (stash != null)
                {
                    var stashIndex = repo.Stashes.ToList().IndexOf(stash);
                    var applyStatus = repo.Stashes.Apply(stashIndex);

                    ShowUiMessage(L10n.Get("StashApplied"));
                    if (applyStatus == StashApplyStatus.Applied)
                    {
                        repo.Stashes.Remove(stashIndex);
                    }
                }

                if (repo.Index.Conflicts.Any())
                {
                    ShowUiError(L10n.Get("FixConflictsCommitResult"));
                }
            }
        }
        catch (Exception ex)
        {
            ShowUiError(L10n.Format("PullErrorToast", ex.Message), ex);
        }
        finally
        {
            dbwatcher?.SetEnable(true);
        }
    }

    private void InitializeLocalRepositoryAndPush(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        EnsureRemoteNameConfigured(gitSettings);
        EnsurePushRefSpecConfigured(gitSettings);

        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        EnsureCommitOnConfiguredBranch(repo, gitSettings, "Initial task backup");
        PushConfiguredBranch(repo, repositoryPath, gitSettings);
        ShowUiMessage(L10n.Get("RepositoryInitializedAndPushed"));
    }

    private void InitializeLocalRepositoryAndCheckoutRemote(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        EnsureRemoteNameConfigured(gitSettings);
        EnsurePushRefSpecMatchesRemote(remoteUrl, gitSettings);

        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        FetchRemote(repo, repositoryPath, gitSettings);
        var localMigrationReports = BackupAndRemoveIgnoredMigrationReports(repo);
        var hasCleanupCommit = false;
        try
        {
            CheckoutRemoteBranch(repo, gitSettings);
            hasCleanupCommit = CommitIgnoredMigrationReportsCleanup(repo, gitSettings, "Ignore migration reports");
        }
        finally
        {
            DiscardIgnoredMigrationReports(localMigrationReports);
        }

        DeleteIgnoredMigrationReportFiles(repo);
        if (hasCleanupCommit)
        {
            PushConfiguredBranch(repo, repositoryPath, gitSettings);
        }

        ShowUiMessage(L10n.Get("RepositoryConnected"));
    }

    private void InitializeLocalRepositoryMergeAndPush(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        EnsureRemoteNameConfigured(gitSettings);
        EnsurePushRefSpecMatchesRemote(remoteUrl, gitSettings);

        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        EnsureCommitOnConfiguredBranch(repo, gitSettings, "Local tasks before connecting backup");
        FetchRemote(repo, repositoryPath, gitSettings);
        var localMigrationReports = BackupAndRemoveIgnoredMigrationReports(repo);
        try
        {
            MergeRemoteBranch(repo, repositoryPath, gitSettings);
            CommitIgnoredMigrationReportsCleanup(repo, gitSettings, "Ignore migration reports");
        }
        finally
        {
            RestoreIgnoredMigrationReports(repo, localMigrationReports);
        }

        PushConfiguredBranch(repo, repositoryPath, gitSettings);
        ShowUiMessage(L10n.Get("RepositoryConnectedAndMerged"));
    }

    private void EnsureCommitOnConfiguredBranch(Repository repo, GitSettings gitSettings, string message)
    {
        EnsureMigrationReportIgnoreRules(repo);
        Commands.Stage(repo, "*");
        EnsureIgnoredMigrationReportsUntracked(repo);

        if (!repo.RetrieveStatus().IsDirty)
        {
            return;
        }

        var signature = CreateSignature(gitSettings);
        var commit = repo.Commit(message, signature, signature);
        var branch = EnsureLocalBranch(repo, gitSettings, commit);
        Commands.Checkout(repo, branch);
    }

    private void FetchRemote(Repository repo, string repositoryPath, GitSettings gitSettings)
    {
        var remote = repo.Network.Remotes[gitSettings.RemoteName]
                     ?? throw new InvalidOperationException(L10n.Format("RemoteNotFound", gitSettings.RemoteName));
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification).ToArray();
        if (ShouldUseGitCliSshTransport(remote.Url))
        {
            RunGitCommandWithConfiguredSshKey(
                repositoryPath,
                "git fetch",
                gitSettings,
                BuildGitFetchArguments(gitSettings.RemoteName, refSpecs));
            return;
        }

        Commands.Fetch(
            repo,
            gitSettings.RemoteName,
            refSpecs,
            CreateFetchOptions(gitSettings),
            string.Empty);
    }

    private void CheckoutRemoteBranch(Repository repo, GitSettings gitSettings)
    {
        var remoteBranch = GetRemoteBranch(repo, gitSettings);
        var localBranch = EnsureLocalBranch(repo, gitSettings, remoteBranch.Tip);
        repo.Branches.Update(localBranch, updater => updater.TrackedBranch = remoteBranch.CanonicalName);
        Commands.Checkout(repo, localBranch);
    }

    private void MergeRemoteBranch(Repository repo, string repositoryPath, GitSettings gitSettings)
    {
        var remoteBranch = GetRemoteBranch(repo, gitSettings);
        var signature = CreateSignature(gitSettings);
        Commands.Checkout(repo, EnsureLocalBranch(repo, gitSettings, repo.Head.Tip));

        try
        {
            repo.Merge(remoteBranch, signature, new MergeOptions());
        }
        catch (Exception)
        {
            try
            {
                MergeUnrelatedHistoriesWithLibGit2(repo, remoteBranch, signature);
            }
            catch (Exception)
            {
                MergeRemoteBranchWithGitCli(repositoryPath, gitSettings);
            }
        }

        if (repo.Index.Conflicts.Any())
        {
            throw new InvalidOperationException(L10n.Get("GitConflicts"));
        }
    }

    private static bool HasConflictResolutionInProgress(Repository repo)
    {
        return repo.Index.Conflicts.Any() ||
               IsConflictOperationInProgress(repo.Info.CurrentOperation);
    }

    private static bool IsConflictOperationInProgress(CurrentOperation operation)
    {
        return operation is CurrentOperation.Merge
            or CurrentOperation.Rebase
            or CurrentOperation.RebaseInteractive
            or CurrentOperation.RebaseMerge
            or CurrentOperation.CherryPick
            or CurrentOperation.CherryPickSequence
            or CurrentOperation.Revert
            or CurrentOperation.RevertSequence
            or CurrentOperation.ApplyMailbox
            or CurrentOperation.ApplyMailboxOrRebase;
    }

    private static string GetConflictPath(Conflict conflict)
    {
        return conflict.Ours?.Path
               ?? conflict.Theirs?.Path
               ?? conflict.Ancestor?.Path
               ?? throw new InvalidOperationException(L10n.Get("ConflictPathNotSpecified"));
    }

    private static IReadOnlyList<BackupConflictField> CreateConflictFields(
        Repository repo,
        IndexEntry? ancestorEntry,
        IndexEntry? currentEntry,
        IndexEntry? incomingEntry)
    {
        if (currentEntry == null || incomingEntry == null)
        {
            return new List<BackupConflictField>();
        }

        try
        {
            var ancestorObject = new JObject();
            if (ancestorEntry != null)
            {
                var ancestorContent = ReadConflictEntryContent(repo, ancestorEntry);
                if (!TryParseJsonObject(ancestorContent, out ancestorObject))
                {
                    return new List<BackupConflictField>();
                }
            }

            var currentContent = ReadConflictEntryContent(repo, currentEntry);
            var incomingContent = ReadConflictEntryContent(repo, incomingEntry);
            if (!TryParseJsonObject(currentContent, out var currentObject) ||
                !TryParseJsonObject(incomingContent, out var incomingObject))
            {
                return new List<BackupConflictField>();
            }

            var titleResolver = CreateTaskTitleResolver(repo);
            var fields = new List<BackupConflictField>();
            foreach (var fieldName in GetOrderedPropertyNames(ancestorObject, currentObject, incomingObject))
            {
                var ancestorToken = ancestorObject[fieldName];
                var currentToken = currentObject[fieldName];
                var incomingToken = incomingObject[fieldName];
                var currentChanged = !JToken.DeepEquals(currentToken, ancestorToken);
                var incomingChanged = !JToken.DeepEquals(incomingToken, ancestorToken);
                var changeKind = GetConflictFieldChangeKind(currentChanged, incomingChanged, currentToken, incomingToken);
                var defaultSource = changeKind == BackupConflictFieldChangeKind.IncomingOnly
                    ? BackupConflictFieldSource.UseIncoming
                    : BackupConflictFieldSource.UseCurrent;
                var canMerge = changeKind == BackupConflictFieldChangeKind.BothDifferent &&
                               CanMergeConflictTokens(fieldName, currentToken, incomingToken);
                var canEditMergedValue = canMerge && CanEditMergedConflictToken(fieldName, currentToken, incomingToken);
                var mergedToken = canMerge
                    ? MergeConflictTokens(fieldName, currentToken, incomingToken)
                    : null;
                fields.Add(new BackupConflictField(
                    fieldName,
                    fieldName,
                    FormatConflictValue(fieldName, ancestorToken, titleResolver),
                    FormatConflictValue(fieldName, currentToken, titleResolver),
                    FormatConflictValue(fieldName, incomingToken, titleResolver),
                    FormatConflictValue(fieldName, mergedToken, titleResolver),
                    canMerge,
                    defaultSource,
                    changeKind,
                    canEditMergedValue));
            }

            return fields;
        }
        catch
        {
            return new List<BackupConflictField>();
        }
    }

    private static void ResolveConflictFieldsEntry(
        Repository repo,
        string repositoryPath,
        string conflictPath,
        IndexEntry? ancestorEntry,
        IndexEntry? currentEntry,
        IndexEntry? incomingEntry,
        IReadOnlyList<BackupConflictFieldSelection> fieldSelections)
    {
        if (currentEntry == null || incomingEntry == null)
        {
            throw new InvalidOperationException(L10n.Get("ConflictFieldResolutionRequiresBothVersions"));
        }

        var ancestorObject = new JObject();
        if (ancestorEntry != null)
        {
            var ancestorContent = ReadConflictEntryContent(repo, ancestorEntry);
            if (!TryParseJsonObject(ancestorContent, out ancestorObject))
            {
                throw new InvalidOperationException(L10n.Get("ConflictFieldResolutionRequiresJson"));
            }
        }

        var currentContent = ReadConflictEntryContent(repo, currentEntry);
        var incomingContent = ReadConflictEntryContent(repo, incomingEntry);
        if (!TryParseJsonObject(currentContent, out var currentObject) ||
            !TryParseJsonObject(incomingContent, out var incomingObject))
        {
            throw new InvalidOperationException(L10n.Get("ConflictFieldResolutionRequiresJson"));
        }

        var selectionByField = fieldSelections
            .Where(selection => !string.IsNullOrWhiteSpace(selection.FieldPath))
            .GroupBy(selection => selection.FieldPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var result = (JObject)ancestorObject.DeepClone();
        foreach (var field in CreateConflictFields(repo, ancestorEntry, currentEntry, incomingEntry))
        {
            var fieldName = field.FieldPath;
            var selection = selectionByField.TryGetValue(fieldName, out var selectedSelection)
                ? selectedSelection
                : null;
            var source = selection?.Source ?? field.DefaultSource;
            if (source == BackupConflictFieldSource.Merge && !field.CanMerge)
            {
                throw new InvalidOperationException(L10n.Format("ConflictFieldCannotMerge", fieldName));
            }

            var currentToken = currentObject[fieldName];
            var incomingToken = incomingObject[fieldName];
            var resolvedToken = source switch
            {
                BackupConflictFieldSource.UseCurrent => currentToken?.DeepClone(),
                BackupConflictFieldSource.UseIncoming => incomingToken?.DeepClone(),
                BackupConflictFieldSource.Merge => ResolveMergedConflictToken(
                    field,
                    currentToken,
                    incomingToken,
                    selection?.CustomValue),
                _ => currentToken?.DeepClone()
            };

            if (resolvedToken == null)
            {
                result.Remove(fieldName);
            }
            else
            {
                result[fieldName] = resolvedToken;
            }
        }

        var worktreePath = GetSafeWorktreePath(repositoryPath, conflictPath);
        var directoryPath = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(worktreePath, result.ToString(Formatting.Indented));
        repo.Index.Remove(conflictPath);
        Commands.Stage(repo, conflictPath);
        repo.Index.Write();
    }

    private static IEnumerable<string> GetOrderedPropertyNames(
        JObject ancestorObject,
        JObject currentObject,
        JObject incomingObject)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in ancestorObject.Properties()
                     .Concat(currentObject.Properties())
                     .Concat(incomingObject.Properties()))
        {
            if (seen.Add(property.Name))
            {
                fields.Add(property.Name);
            }
        }

        return fields;
    }

    private static bool TryParseJsonObject(string? content, out JObject result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using var stringReader = new StringReader(content);
            using var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None
            };
            result = JObject.Load(jsonReader);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static BackupConflictFieldChangeKind GetConflictFieldChangeKind(
        bool currentChanged,
        bool incomingChanged,
        JToken? currentToken,
        JToken? incomingToken)
    {
        if (!currentChanged && !incomingChanged)
        {
            return BackupConflictFieldChangeKind.Unchanged;
        }

        if (currentChanged && incomingChanged)
        {
            return JToken.DeepEquals(currentToken, incomingToken)
                ? BackupConflictFieldChangeKind.BothSame
                : BackupConflictFieldChangeKind.BothDifferent;
        }

        return currentChanged
            ? BackupConflictFieldChangeKind.CurrentOnly
            : BackupConflictFieldChangeKind.IncomingOnly;
    }

    private static string ReadConflictEntryContent(Repository repo, IndexEntry entry)
    {
        var blob = repo.Lookup<Blob>(entry.Id)
                   ?? throw new InvalidOperationException(L10n.Format("ConflictVersionNotFound", entry.Path));
        using var content = blob.GetContentStream();
        using var reader = new StreamReader(content);
        return reader.ReadToEnd();
    }

    private static bool CanMergeConflictTokens(string fieldName, JToken? currentToken, JToken? incomingToken)
    {
        if (currentToken == null || incomingToken == null)
        {
            return false;
        }

        if (currentToken is JArray currentArray &&
            incomingToken is JArray incomingArray &&
            IsMergeableArray(currentArray) &&
            IsMergeableArray(incomingArray))
        {
            return true;
        }

        return TextMergeFieldNames.Contains(fieldName) &&
               currentToken.Type == JTokenType.String &&
               incomingToken.Type == JTokenType.String;
    }

    private static bool CanEditMergedConflictToken(string fieldName, JToken? currentToken, JToken? incomingToken)
    {
        return currentToken != null &&
               incomingToken != null &&
               TextMergeFieldNames.Contains(fieldName) &&
               currentToken.Type == JTokenType.String &&
               incomingToken.Type == JTokenType.String;
    }

    private static JToken? ResolveMergedConflictToken(
        BackupConflictField field,
        JToken? currentToken,
        JToken? incomingToken,
        string? customValue)
    {
        if (field.CanEditMergedValue && customValue != null)
        {
            return new JValue(customValue);
        }

        return MergeConflictTokens(field.FieldPath, currentToken, incomingToken);
    }

    private static JToken? MergeConflictTokens(string fieldName, JToken? currentToken, JToken? incomingToken)
    {
        if (currentToken == null)
        {
            return incomingToken?.DeepClone();
        }

        if (incomingToken == null)
        {
            return currentToken.DeepClone();
        }

        if (JToken.DeepEquals(currentToken, incomingToken))
        {
            return currentToken.DeepClone();
        }

        if (currentToken is JArray currentArray &&
            incomingToken is JArray incomingArray &&
            IsMergeableArray(currentArray) &&
            IsMergeableArray(incomingArray))
        {
            return MergeArrays(currentArray, incomingArray);
        }

        if (TextMergeFieldNames.Contains(fieldName) &&
            currentToken.Type == JTokenType.String &&
            incomingToken.Type == JTokenType.String)
        {
            return MergeStrings((string?)currentToken, (string?)incomingToken);
        }

        throw new InvalidOperationException(L10n.Format("ConflictFieldCannotMerge", fieldName));
    }

    private static bool IsMergeableArray(JArray array)
    {
        return array.All(IsScalarJsonValue);
    }

    private static bool IsScalarJsonValue(JToken token)
    {
        return token is JValue;
    }

    private static JArray MergeArrays(JArray currentArray, JArray incomingArray)
    {
        var result = new JArray();
        foreach (var item in currentArray.Concat(incomingArray))
        {
            if (!result.Any(existing => JToken.DeepEquals(existing, item)))
            {
                result.Add(item.DeepClone());
            }
        }

        return result;
    }

    private static JValue MergeStrings(string? currentValue, string? incomingValue)
    {
        if (string.IsNullOrEmpty(currentValue))
        {
            return new JValue(incomingValue);
        }

        if (string.IsNullOrEmpty(incomingValue) || string.Equals(currentValue, incomingValue, StringComparison.Ordinal))
        {
            return new JValue(currentValue);
        }

        if (currentValue.Contains(incomingValue, StringComparison.Ordinal))
        {
            return new JValue(currentValue);
        }

        if (incomingValue.Contains(currentValue, StringComparison.Ordinal))
        {
            return new JValue(incomingValue);
        }

        return new JValue($"{currentValue}{Environment.NewLine}{incomingValue}");
    }

    private static Func<string, string?> CreateTaskTitleResolver(Repository repo)
    {
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var repositoryPath = repo.Info.WorkingDirectory;
        return taskId =>
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return null;
            }

            if (cache.TryGetValue(taskId, out var cachedTitle))
            {
                return cachedTitle;
            }

            string? title = null;
            try
            {
                var taskPath = GetSafeWorktreePath(repositoryPath, taskId);
                if (File.Exists(taskPath) &&
                    TryParseJsonObject(File.ReadAllText(taskPath), out var taskObject) &&
                    taskObject["Title"]?.Type == JTokenType.String)
                {
                    title = (string?)taskObject["Title"];
                }
            }
            catch
            {
                title = null;
            }

            cache[taskId] = title;
            return title;
        };
    }

    private static string FormatConflictValue(
        string fieldName,
        JToken? token,
        Func<string, string?>? titleResolver = null)
    {
        if (token == null)
        {
            return L10n.Get("ConflictMissingValue");
        }

        if (RelationFieldNames.Contains(fieldName) && token is JArray relationArray)
        {
            return FormatRelationArrayValue(relationArray, titleResolver);
        }

        var value = token.Type == JTokenType.String
            ? (string?)token
            : token.ToString(Formatting.None);
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        const int maxLength = 500;
        return value.Length <= maxLength
            ? value
            : $"{value[..maxLength]}...";
    }

    private static string FormatRelationArrayValue(JArray array, Func<string, string?>? titleResolver)
    {
        var values = array
            .OfType<JValue>()
            .Select(value => value.Value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
            {
                var title = titleResolver?.Invoke(value!);
                return string.IsNullOrWhiteSpace(title)
                    ? value!
                    : $"{title} ({value})";
            })
            .ToList();

        return values.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, values);
    }

    private static void ResolveConflictEntry(
        Repository repo,
        string repositoryPath,
        string conflictPath,
        IndexEntry? selectedEntry)
    {
        var worktreePath = GetSafeWorktreePath(repositoryPath, conflictPath);

        if (selectedEntry == null)
        {
            if (File.Exists(worktreePath))
            {
                File.Delete(worktreePath);
            }

            repo.Index.Remove(conflictPath);
            repo.Index.Write();
            return;
        }

        var blob = repo.Lookup<Blob>(selectedEntry.Id)
                   ?? throw new InvalidOperationException(L10n.Format("ConflictVersionNotFound", conflictPath));
        var directoryPath = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using (var content = blob.GetContentStream())
        using (var file = File.Open(worktreePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            content.CopyTo(file);
        }

        repo.Index.Remove(conflictPath);
        repo.Index.Add(blob, conflictPath, selectedEntry.Mode);
        repo.Index.Write();
    }

    private static string GetSafeWorktreePath(string repositoryPath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(L10n.Format("InvalidConflictPath", relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            repositoryPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinDirectory(fullPath, repositoryPath))
        {
            throw new InvalidOperationException(L10n.Format("InvalidConflictPath", relativePath));
        }

        return fullPath;
    }

    private static void MergeUnrelatedHistoriesWithLibGit2(Repository repo, Branch remoteBranch, Signature signature)
    {
        var localBranch = repo.Head;
        var localCommit = localBranch.Tip
                          ?? throw new InvalidOperationException(L10n.Get("RepositoryHeadUnavailable"));
        var remoteCommit = remoteBranch.Tip
                           ?? throw new InvalidOperationException(
                               L10n.Format("RemoteBranchNotFoundAfterFetch", remoteBranch.FriendlyName));

        var mergeResult = repo.ObjectDatabase.MergeCommits(localCommit, remoteCommit, new MergeTreeOptions());
        if (mergeResult.Status == MergeTreeStatus.Conflicts)
        {
            throw new InvalidOperationException(L10n.Get("GitConflicts"));
        }

        var mergeCommit = repo.ObjectDatabase.CreateCommit(
            signature,
            signature,
            $"Merge remote-tracking branch '{remoteBranch.FriendlyName}'",
            mergeResult.Tree,
            new[] { localCommit, remoteCommit },
            prettifyMessage: true);

        repo.Refs.UpdateTarget(localBranch.Reference, mergeCommit.Id, "merge unrelated histories");
        repo.Reset(ResetMode.Hard, mergeCommit);
    }

    private static bool CommitIgnoredMigrationReportsCleanup(
        Repository repo,
        GitSettings gitSettings,
        string message)
    {
        EnsureMigrationReportIgnoreRules(repo);
        Commands.Stage(repo, ".gitignore");
        EnsureIgnoredMigrationReportsUntracked(repo);

        if (!repo.RetrieveStatus().IsDirty)
        {
            return false;
        }

        var signature = CreateSignature(gitSettings);
        repo.Commit(message, signature, signature);
        return true;
    }

    private void MergeRemoteBranchWithGitCli(string repositoryPath, GitSettings gitSettings)
    {
        var remoteBranchName = $"{gitSettings.RemoteName}/{GetBranchShortName(gitSettings.PushRefSpec)}";
        var arguments = new List<string>
        {
            "-c",
            $"user.name={gitSettings.CommitterName}",
            "-c",
            $"user.email={gitSettings.CommitterEmail}",
            "merge",
            "--allow-unrelated-histories",
            "--no-edit",
            remoteBranchName
        };

        var processResult = RunGitCommand(repositoryPath, "git merge", arguments.ToArray());

        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException(L10n.Format("GitMergeFailed", GetProcessError(processResult)));
        }
    }

    private void PushConfiguredBranch(Repository repo, string repositoryPath, GitSettings gitSettings)
    {
        PushRemote(repo, repositoryPath, gitSettings, gitSettings.PushRefSpec);
    }

    private void PushRemote(Repository repo, string repositoryPath, GitSettings gitSettings, string pushRefSpec)
    {
        var remote = repo.Network.Remotes[gitSettings.RemoteName]
                     ?? throw new InvalidOperationException(L10n.Format("RemoteNotFound", gitSettings.RemoteName));
        if (ShouldUseGitCliSshTransport(remote.Url))
        {
            RunGitCommandWithConfiguredSshKey(
                repositoryPath,
                "git push",
                gitSettings,
                "push",
                gitSettings.RemoteName,
                pushRefSpec);
            return;
        }

        repo.Network.Push(remote, pushRefSpec, CreatePushOptions(gitSettings));
    }

    private Branch EnsureLocalBranch(Repository repo, GitSettings gitSettings, Commit targetCommit)
    {
        var branchName = GetBranchShortName(gitSettings.PushRefSpec);
        var branch = repo.Branches[branchName] ?? repo.CreateBranch(branchName, targetCommit);
        return branch;
    }

    private Branch GetRemoteBranch(Repository repo, GitSettings gitSettings)
    {
        var branchName = GetBranchShortName(gitSettings.PushRefSpec);
        return repo.Branches[$"refs/remotes/{gitSettings.RemoteName}/{branchName}"]
               ?? throw new InvalidOperationException(
                   L10n.Format("RemoteBranchNotFoundAfterFetch", $"{gitSettings.RemoteName}/{branchName}"));
    }

    private void EnsureRemote(Repository repo, string remoteName, string remoteUrl)
    {
        var existingRemote = repo.Network.Remotes[remoteName];
        if (existingRemote != null)
        {
            if (!string.Equals(existingRemote.Url, remoteUrl, StringComparison.Ordinal))
            {
                repo.Network.Remotes.Remove(remoteName);
                repo.Network.Remotes.Add(remoteName, remoteUrl);
            }

            return;
        }

        repo.Network.Remotes.Add(remoteName, remoteUrl);
    }

    private bool RemoteHasContent(string remoteUrl, GitSettings gitSettings)
    {
        return GetRemoteHeadRefs(remoteUrl, gitSettings).Count > 0;
    }

    private List<string> GetRemoteHeadRefs(string remoteUrl, GitSettings gitSettings)
    {
        if (ShouldUseGitCliSshTransport(remoteUrl))
        {
            return GetRemoteHeadRefsViaGitCli(remoteUrl, gitSettings);
        }

        if (IsSshUrl(remoteUrl))
        {
            return GetRemoteHeadRefsViaFetch(remoteUrl, gitSettings);
        }

        return Repository
            .ListRemoteReferences(remoteUrl, GetCredentials(gitSettings), CreateProxyOptions(gitSettings))
            .Select(reference => reference.CanonicalName)
            .Where(reference => reference.StartsWith("refs/heads/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private List<string> GetRemoteHeadRefsViaFetch(string remoteUrl, GitSettings gitSettings)
    {
        var remoteName = string.IsNullOrWhiteSpace(gitSettings.RemoteName) ? "origin" : gitSettings.RemoteName;
        var remoteRefPrefix = $"refs/remotes/{remoteName}/";
        var fetchRefSpec = $"+refs/heads/*:{remoteRefPrefix}*";
        var tempRepositoryPath = Path.Combine(Path.GetTempPath(), $"unlimotion-remote-refs-{Guid.NewGuid():N}");

        try
        {
            Repository.Init(tempRepositoryPath, isBare: true);
            using var repo = new Repository(tempRepositoryPath);
            repo.Network.Remotes.Add(remoteName, remoteUrl, fetchRefSpec);

            Commands.Fetch(
                repo,
                remoteName,
                new[] { fetchRefSpec },
                CreateFetchOptions(gitSettings),
                string.Empty);

            return repo.Refs
                .Select(reference => reference.CanonicalName)
                .Where(reference => reference.StartsWith(remoteRefPrefix, StringComparison.Ordinal))
                .Select(reference => reference[remoteRefPrefix.Length..])
                .Where(branchName => !string.Equals(branchName, "HEAD", StringComparison.Ordinal))
                .Select(branchName => $"refs/heads/{branchName}")
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            TryDeleteDirectory(tempRepositoryPath);
        }
    }

    private List<string> GetRemoteHeadRefsViaGitCli(string remoteUrl, GitSettings gitSettings)
    {
        var processResult = RunGitCommandWithConfiguredSshKey(
            Path.GetTempPath(),
            "git ls-remote",
            gitSettings,
            "ls-remote",
            "--heads",
            remoteUrl);

        return processResult.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[1])
            .Where(reference => reference.StartsWith("refs/heads/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete temporary git repository '{path}': {ex}");
        }
    }

    private void EnsureGitSelectionFromLocalRepository(string repositoryPath, GitSettings gitSettings)
    {
        using var repo = new Repository(repositoryPath);
        EnsureRemoteNameMatchesLocalRepository(repo, gitSettings);
        EnsurePushRefSpecMatchesLocalRepository(repo, gitSettings);
    }

    private void EnsureRemoteNameMatchesLocalRepository(Repository repo, GitSettings gitSettings)
    {
        var remotes = repo.Network.Remotes.ToList();
        if (remotes.Count == 0)
        {
            EnsureRemoteNameConfigured(gitSettings);
            return;
        }

        if (!string.IsNullOrWhiteSpace(gitSettings.RemoteName) &&
            remotes.Any(remote => string.Equals(remote.Name, gitSettings.RemoteName, StringComparison.Ordinal)))
        {
            return;
        }

        var selectedRemote = remotes.FirstOrDefault(remote =>
                                 string.Equals(remote.Name, "origin", StringComparison.OrdinalIgnoreCase))
                             ?? remotes[0];
        SetRemoteName(gitSettings, selectedRemote.Name);

        if (string.IsNullOrWhiteSpace(gitSettings.RemoteUrl) && !string.IsNullOrWhiteSpace(selectedRemote.Url))
        {
            SetRemoteUrl(gitSettings, selectedRemote.Url);
        }
    }

    private void EnsureRemoteNameConfigured(GitSettings gitSettings)
    {
        if (!string.IsNullOrWhiteSpace(gitSettings.RemoteName))
        {
            return;
        }

        SetRemoteName(gitSettings, "origin");
    }

    private void EnsurePushRefSpecMatchesLocalRepository(Repository repo, GitSettings gitSettings)
    {
        var localRefs = repo.Branches
            .Where(branch => !branch.IsRemote)
            .Select(branch => branch.CanonicalName)
            .ToList();
        var preferredRef = repo.Head is { IsRemote: false } ? repo.Head.CanonicalName : null;
        EnsurePushRefSpecMatchesAvailableRefs(gitSettings, localRefs, preferredRef);
    }

    private void EnsurePushRefSpecMatchesRemote(string remoteUrl, GitSettings gitSettings)
    {
        EnsurePushRefSpecMatchesAvailableRefs(gitSettings, GetRemoteHeadRefs(remoteUrl, gitSettings), preferredRef: null);
    }

    private void EnsurePushRefSpecMatchesAvailableRefs(
        GitSettings gitSettings,
        IReadOnlyList<string> refs,
        string? preferredRef)
    {
        if (refs.Count == 0)
        {
            EnsurePushRefSpecConfigured(gitSettings);
            return;
        }

        if (!string.IsNullOrWhiteSpace(gitSettings.PushRefSpec) &&
            refs.Any(reference => string.Equals(reference, gitSettings.PushRefSpec, StringComparison.Ordinal)))
        {
            return;
        }

        SetPushRefSpec(gitSettings, ChoosePreferredRef(refs, ToCanonicalBranchRef(gitSettings.Branch), preferredRef));
    }

    private void EnsurePushRefSpecConfigured(GitSettings gitSettings)
    {
        var canonicalRef = ToCanonicalBranchRef(gitSettings.PushRefSpec)
                           ?? ToCanonicalBranchRef(gitSettings.Branch)
                           ?? "refs/heads/master";
        if (string.Equals(gitSettings.PushRefSpec, canonicalRef, StringComparison.Ordinal))
        {
            return;
        }

        SetPushRefSpec(gitSettings, canonicalRef);
    }

    private void SetRemoteName(GitSettings gitSettings, string remoteName)
    {
        gitSettings.RemoteName = remoteName;
        _configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteName)).Set(remoteName);
    }

    private void SetRemoteUrl(GitSettings gitSettings, string remoteUrl)
    {
        gitSettings.RemoteUrl = remoteUrl;
        _configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set(remoteUrl);
    }

    private void SetPushRefSpec(GitSettings gitSettings, string pushRefSpec)
    {
        gitSettings.PushRefSpec = pushRefSpec;
        gitSettings.Branch = GetBranchShortName(pushRefSpec);
        var gitSection = _configuration.GetSection("Git");
        gitSection.GetSection(nameof(GitSettings.PushRefSpec)).Set(pushRefSpec);
        gitSection.GetSection(nameof(GitSettings.Branch)).Set(gitSettings.Branch);
    }

    private static bool HasLocalFolderContent(string repositoryPath)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return false;
        }

        return Directory
            .EnumerateFileSystemEntries(repositoryPath)
            .Any(path =>
                !string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase) &&
                !IsIgnoredMigrationReportFile(path));
    }

    private static void EnsureMigrationReportIgnoreRules(Repository repo)
    {
        var workingDirectory = repo.Info.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(L10n.Get("RepositoryWorkingDirectoryUnavailable"));
        }

        var gitIgnorePath = Path.Combine(workingDirectory, ".gitignore");
        var existingText = File.Exists(gitIgnorePath) ? File.ReadAllText(gitIgnorePath) : string.Empty;
        var existingRules = existingText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingRules = IgnoredMigrationReportFileNames
            .Where(fileName => !existingRules.Contains(fileName))
            .ToList();

        if (missingRules.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(existingText) &&
            !existingText.EndsWith("\n", StringComparison.Ordinal))
        {
            builder.AppendLine();
        }

        foreach (var rule in missingRules)
        {
            builder.AppendLine(rule);
        }

        File.AppendAllText(gitIgnorePath, builder.ToString());
    }

    private static bool EnsureIgnoredMigrationReportsUntracked(Repository repo)
    {
        var changed = false;
        foreach (var fileName in IgnoredMigrationReportFileNames)
        {
            if (repo.Index[fileName] == null)
            {
                continue;
            }

            repo.Index.Remove(fileName);
            changed = true;
        }

        if (changed)
        {
            repo.Index.Write();
        }

        return changed;
    }

    private static bool IsIgnoredMigrationReportFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return IgnoredMigrationReportFileNames.Any(ignoredFileName =>
            string.Equals(fileName, ignoredFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<IgnoredMigrationReportBackup> BackupAndRemoveIgnoredMigrationReports(Repository repo)
    {
        var workingDirectory = repo.Info.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(L10n.Get("RepositoryWorkingDirectoryUnavailable"));
        }

        var backups = new List<IgnoredMigrationReportBackup>();
        foreach (var fileName in IgnoredMigrationReportFileNames)
        {
            var path = Path.Combine(workingDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            backups.Add(new IgnoredMigrationReportBackup(fileName, File.ReadAllBytes(path)));
            File.Delete(path);
        }

        return backups;
    }

    private static void RestoreIgnoredMigrationReports(
        Repository repo,
        IReadOnlyCollection<IgnoredMigrationReportBackup> backups)
    {
        if (backups.Count == 0)
        {
            return;
        }

        var workingDirectory = repo.Info.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(L10n.Get("RepositoryWorkingDirectoryUnavailable"));
        }

        foreach (var backup in backups)
        {
            File.WriteAllBytes(Path.Combine(workingDirectory, backup.FileName), backup.Content);
        }
    }

    private static void DiscardIgnoredMigrationReports(IReadOnlyCollection<IgnoredMigrationReportBackup> backups)
    {
        foreach (var backup in backups)
        {
            Array.Clear(backup.Content);
        }
    }

    private static void DeleteIgnoredMigrationReportFiles(Repository repo)
    {
        var workingDirectory = repo.Info.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(L10n.Get("RepositoryWorkingDirectoryUnavailable"));
        }

        foreach (var fileName in IgnoredMigrationReportFileNames)
        {
            var path = Path.Combine(workingDirectory, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed record IgnoredMigrationReportBackup(string FileName, byte[] Content);

    private static string RequireRemoteUrl(GitSettings gitSettings)
    {
        if (string.IsNullOrWhiteSpace(gitSettings.RemoteUrl))
        {
            throw new InvalidOperationException(L10n.Get("RemoteRepositoryUrlNotConfigured"));
        }

        return gitSettings.RemoteUrl;
    }

    private string ResolveRemoteUrl(GitSettings gitSettings, string repositoryPath)
    {
        if (!string.IsNullOrWhiteSpace(gitSettings.RemoteName) &&
            Repository.IsValid(repositoryPath))
        {
            using var repo = new Repository(repositoryPath);
            var remoteUrl = repo.Network.Remotes[gitSettings.RemoteName]?.Url;
            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                return remoteUrl;
            }
        }

        return RequireRemoteUrl(gitSettings);
    }

    internal static string[] BuildGitFetchArguments(string remoteName, IReadOnlyCollection<string> refSpecs)
    {
        var arguments = new List<string> { "fetch", remoteName };
        arguments.AddRange(refSpecs);
        return arguments.ToArray();
    }

    private static string GetBranchShortName(string canonicalBranchName)
    {
        const string headsPrefix = "refs/heads/";
        if (canonicalBranchName.StartsWith(headsPrefix, StringComparison.Ordinal))
        {
            return canonicalBranchName[headsPrefix.Length..];
        }

        return canonicalBranchName;
    }

    private static string? ToCanonicalBranchRef(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        var trimmedBranch = branch.Trim();
        return trimmedBranch.StartsWith("refs/", StringComparison.Ordinal)
            ? trimmedBranch
            : $"refs/heads/{trimmedBranch}";
    }

    private static string ChoosePreferredRef(
        IReadOnlyList<string> refs,
        string? configuredBranchRef,
        string? preferredRef)
    {
        if (!string.IsNullOrWhiteSpace(preferredRef))
        {
            var matchedPreferredRef = refs.FirstOrDefault(reference =>
                string.Equals(reference, preferredRef, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(matchedPreferredRef))
            {
                return matchedPreferredRef;
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredBranchRef))
        {
            var matchedConfiguredRef = refs.FirstOrDefault(reference =>
                string.Equals(reference, configuredBranchRef, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(matchedConfiguredRef))
            {
                return matchedConfiguredRef;
            }
        }

        return refs.FirstOrDefault(reference =>
                   string.Equals(reference, "refs/heads/main", StringComparison.Ordinal))
               ?? refs.FirstOrDefault(reference =>
                   string.Equals(reference, "refs/heads/master", StringComparison.Ordinal))
               ?? refs[0];
    }

    private static Signature CreateSignature(GitSettings gitSettings) =>
        new(gitSettings.CommitterName, gitSettings.CommitterEmail, DateTimeOffset.Now);

    internal static string NormalizeSshKeyFileName(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return DefaultSshKeyFileName;
        }

        var candidate = keyName.Trim().Replace('\\', '/');
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            candidate = segments[^1];
        }

        var sanitized = new StringBuilder(candidate.Length);
        foreach (var character in candidate)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                sanitized.Append(character);
            }
            else
            {
                sanitized.Append('_');
            }
        }

        var normalized = sanitized.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(normalized) || normalized is "." or ".."
            ? DefaultSshKeyFileName
            : normalized;
    }

    internal static (string PrivateKeyPath, string PublicKeyPath) GetSshKeyPaths(string sshDirectory, string? keyName)
    {
        var rootDirectory = Path.GetFullPath(sshDirectory);
        var safeFileName = NormalizeSshKeyFileName(keyName);
        var privateKeyPath = Path.GetFullPath(Path.Combine(rootDirectory, safeFileName));
        if (!IsPathWithinDirectory(privateKeyPath, rootDirectory))
        {
            throw new InvalidOperationException(L10n.Format("SshKeyPathOutsideDirectory", rootDirectory));
        }

        return (privateKeyPath, $"{privateKeyPath}.pub");
    }

    internal static string BuildGitSshCommand(string privateKeyPath, string? knownHostsPath = null)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new InvalidOperationException(L10n.Get("SshPrivateKeyPathNotConfigured"));
        }

        var normalizedPath = ToGitSshCommandPath(privateKeyPath);
        var command = $"ssh -i \"{normalizedPath}\" -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=accept-new";
        if (!string.IsNullOrWhiteSpace(knownHostsPath))
        {
            command += $" -o UserKnownHostsFile=\"{ToGitSshCommandPath(knownHostsPath)}\"";
        }

        return command;
    }

    internal static string BuildGitSshCommand(GitSettings gitSettings) =>
        BuildGitSshCommand(GetConfiguredSshPrivateKeyPath(gitSettings), GetKnownHostsPath(gitSettings));

    private static string ToGitSshCommandPath(string path) =>
        path.Replace('\\', '/').Replace("\"", "\\\"");

    internal static void GenerateManagedRsaSshKey(string privateKeyPath, string publicKeyPath)
    {
        using var rsa = RSA.Create(4096);
        File.WriteAllText(privateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(publicKeyPath, BuildOpenSshRsaPublicKey(rsa, "unlimotion"));
        TrySetPrivateKeyPermissions(privateKeyPath);
    }

    internal static string BuildOpenSshRsaPublicKey(RSA rsa, string comment)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        using var stream = new MemoryStream();
        WriteOpenSshString(stream, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteOpenSshMpint(stream, parameters.Exponent);
        WriteOpenSshMpint(stream, parameters.Modulus);

        var key = Convert.ToBase64String(stream.ToArray());
        return string.IsNullOrWhiteSpace(comment)
            ? $"ssh-rsa {key}"
            : $"ssh-rsa {key} {comment}";
    }

    private static void WriteOpenSshMpint(Stream stream, byte[]? value)
    {
        value ??= Array.Empty<byte>();
        var firstNonZero = 0;
        while (firstNonZero < value.Length && value[firstNonZero] == 0)
        {
            firstNonZero++;
        }

        var length = value.Length - firstNonZero;
        var needsPositivePrefix = length > 0 && (value[firstNonZero] & 0x80) != 0;
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)(length + (needsPositivePrefix ? 1 : 0)));
        stream.Write(lengthBytes);

        if (needsPositivePrefix)
        {
            stream.WriteByte(0);
        }

        stream.Write(value, firstNonZero, length);
    }

    private static void WriteOpenSshString(Stream stream, byte[] value)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)value.Length);
        stream.Write(lengthBytes);
        stream.Write(value, 0, value.Length);
    }

    private static bool ShouldUseManagedSshKeyGenerationFallback(Exception ex)
    {
        return ex is Win32Exception or PlatformNotSupportedException ||
               ex.InnerException is Win32Exception or PlatformNotSupportedException;
    }

    private static void TrySetPrivateKeyPermissions(string privateKeyPath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TrySetWindowsPrivateKeyPermissions(privateKeyPath);
                return;
            }

            File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set SSH private key permissions for '{privateKeyPath}': {ex}");
        }
    }

    private static void TrySetWindowsPrivateKeyPermissions(string privateKeyPath)
    {
        var currentUserSid = WindowsIdentity.GetCurrent().User;
        if (currentUserSid == null)
        {
            return;
        }

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var allowedSidValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentUserSid.Value,
            systemSid.Value,
            administratorsSid.Value
        };

        var fileInfo = new FileInfo(privateKeyPath);
        var security = fileInfo.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var accessRules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        foreach (var rule in accessRules)
        {
            if (rule.IdentityReference is SecurityIdentifier identity &&
                !allowedSidValues.Contains(identity.Value))
            {
                security.RemoveAccessRuleAll(rule);
            }
        }

        try
        {
            security.SetOwner(currentUserSid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set SSH private key owner for '{privateKeyPath}': {ex}");
        }

        security.ResetAccessRule(CreatePrivateKeyAccessRule(currentUserSid));
        security.ResetAccessRule(CreatePrivateKeyAccessRule(systemSid));
        security.ResetAccessRule(CreatePrivateKeyAccessRule(administratorsSid));
        fileInfo.SetAccessControl(security);
    }

    private static FileSystemAccessRule CreatePrivateKeyAccessRule(IdentityReference identity) =>
        new(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow);

    private static string GetRepositoryPath(string? pathFromSettings)
    {
        var path = string.IsNullOrWhiteSpace(pathFromSettings) ? TasksFolderName : pathFromSettings;
        if (!IsAbsolutePath(path))
        {
            if (GetAbsolutePath == null)
            {
                throw new Exception(L10n.Get("CannotGetAbsolutePath"));
            }

            path = GetAbsolutePath(path);
        }

        return path;
    }

    private static bool IsAbsolutePath(string path)
    {
        return Path.IsPathRooted(path)
               && !string.IsNullOrWhiteSpace(Path.GetPathRoot(path)?.Trim('\\', '/'));
    }

    private static void CheckGitSettings(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            Debug.WriteLine("Username/password can be empty when SSH auth is used.");
        }
    }

    private static string DetectAuthType(string? remoteUrl)
    {
        return IsSshUrl(remoteUrl) ? "SSH" : "HTTP";
    }

    private static BackupAuthMode ParseBackupAuthMode(string? remoteUrl) =>
        IsSshUrl(remoteUrl) ? BackupAuthMode.Ssh : BackupAuthMode.Token;

    internal static string BuildRemoteUrlForConnectionType(string remoteUrl, BackupAuthMode targetMode)
    {
        var identity = ParseRemoteUrlIdentity(remoteUrl);
        return targetMode switch
        {
            BackupAuthMode.Ssh => identity.ToSshUrl(),
            BackupAuthMode.Token => identity.ToHttpUrl(),
            _ => throw new InvalidOperationException($"Unsupported backup auth mode: {targetMode}.")
        };
    }

    internal static bool AreEquivalentRemoteUrls(string? firstRemoteUrl, string? secondRemoteUrl)
    {
        if (string.IsNullOrWhiteSpace(firstRemoteUrl) || string.IsNullOrWhiteSpace(secondRemoteUrl))
        {
            return false;
        }

        if (string.Equals(firstRemoteUrl, secondRemoteUrl, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return ParseRemoteUrlIdentity(firstRemoteUrl).Equals(ParseRemoteUrlIdentity(secondRemoteUrl));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static RemoteUrlIdentity ParseRemoteUrlIdentity(string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new InvalidOperationException(L10n.Get("RemoteUrlUnsupportedForTypeSwitch"));
        }

        var trimmedUrl = remoteUrl.Trim();
        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            return ParseRemoteUriIdentity(uri);
        }

        var atSignIndex = trimmedUrl.IndexOf('@');
        var colonIndex = trimmedUrl.LastIndexOf(':');
        if (atSignIndex <= 0 ||
            colonIndex <= atSignIndex + 1 ||
            trimmedUrl.IndexOf("://", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", remoteUrl));
        }

        var user = trimmedUrl[..atSignIndex];
        if (!string.Equals(user, "git", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", remoteUrl));
        }

        var host = trimmedUrl[(atSignIndex + 1)..colonIndex];
        var path = trimmedUrl[(colonIndex + 1)..];
        if (host.Contains(':', StringComparison.Ordinal) ||
            host.Contains('/', StringComparison.Ordinal) ||
            host.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", remoteUrl));
        }

        return new RemoteUrlIdentity(BackupAuthMode.Ssh, NormalizeRemoteHost(host, null), NormalizeRemotePath(path));
    }

    private static RemoteUrlIdentity ParseRemoteUriIdentity(Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", uri.ToString()));
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", uri.ToString()));
            }

            return new RemoteUrlIdentity(
                BackupAuthMode.Token,
                NormalizeRemoteHost(uri.Host, uri.IsDefaultPort ? null : uri.Port),
                NormalizeRemotePath(uri.AbsolutePath));
        }

        if (string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(uri.UserInfo) &&
                !string.Equals(uri.UserInfo.Split(':', 2)[0], "git", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", uri.ToString()));
            }

            return new RemoteUrlIdentity(
                BackupAuthMode.Ssh,
                NormalizeRemoteHost(uri.Host, uri.IsDefaultPort || uri.Port == 22 ? null : uri.Port),
                NormalizeRemotePath(uri.AbsolutePath));
        }

        throw new InvalidOperationException(L10n.Format("RemoteUrlUnsupportedForTypeSwitchWithUrl", uri.ToString()));
    }

    private static string NormalizeRemoteHost(string host, int? port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(L10n.Get("RemoteUrlUnsupportedForTypeSwitch"));
        }

        var normalizedHost = host.Trim().Trim('[', ']').ToLowerInvariant();
        return port is > 0 ? $"{normalizedHost}:{port.Value}" : normalizedHost;
    }

    private static string NormalizeRemotePath(string path)
    {
        var normalizedPath = path.Trim().Replace('\\', '/').TrimStart('/');
        if (normalizedPath.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[..^1];
        }

        if (normalizedPath.EndsWith(".git", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[..^4];
        }

        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            normalizedPath.Contains('?') ||
            normalizedPath.Contains('#'))
        {
            throw new InvalidOperationException(L10n.Get("RemoteUrlUnsupportedForTypeSwitch"));
        }

        return normalizedPath;
    }

    private static string CreateUniqueRemoteName(Repository repo, string baseName)
    {
        var candidate = baseName;
        var suffix = 2;
        while (repo.Network.Remotes.Any(remote => string.Equals(remote.Name, candidate, StringComparison.Ordinal)))
        {
            candidate = $"{baseName}-{suffix++}";
        }

        return candidate;
    }

    private sealed record RemoteUrlIdentity(BackupAuthMode Mode, string Host, string Path)
    {
        public string ToHttpUrl() => $"https://{Host}/{Path}.git";

        public string ToSshUrl()
        {
            var portSeparatorIndex = Host.LastIndexOf(':');
            if (portSeparatorIndex > 0 && Host[(portSeparatorIndex + 1)..].All(char.IsDigit))
            {
                return $"ssh://git@{Host}/{Path}.git";
            }

            return $"git@{Host}:{Path}.git";
        }
    }

    private static bool IsSshUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        if (remoteUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase);
        }

        var atSignIndex = remoteUrl.IndexOf('@');
        var colonIndex = remoteUrl.LastIndexOf(':');
        return atSignIndex > 0
               && colonIndex > atSignIndex + 1
               && remoteUrl.IndexOf("://", StringComparison.Ordinal) < 0;
    }

    private static bool ShouldUseGitCliSshTransport(string? remoteUrl) =>
        ShouldUseGitCliSshTransport(remoteUrl, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    internal static bool ShouldUseGitCliSshTransport(string? remoteUrl, bool isWindows) =>
        isWindows && IsSshUrl(remoteUrl);

    private static (int ExitCode, string StandardOutput, string StandardError) RunGitCommand(
        string workingDirectory,
        string operationName,
        params string[] arguments)
    {
        var startInfo = CreateProcessStartInfo("git", workingDirectory, arguments);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var processResult = RunProcess(startInfo);
        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException(L10n.Format("GitOperationFailed", operationName, GetProcessError(processResult)));
        }

        return processResult;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunGitCommandWithConfiguredSshKey(
        string workingDirectory,
        string operationName,
        GitSettings gitSettings,
        params string[] arguments)
    {
        var privateKeyPath = GetConfiguredSshPrivateKeyPath(gitSettings);
        var knownHostsPath = GetKnownHostsPath(gitSettings);
        Directory.CreateDirectory(Path.GetDirectoryName(knownHostsPath) ?? ".");
        var startInfo = CreateProcessStartInfo("git", workingDirectory, arguments);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_SSH_COMMAND"] = BuildGitSshCommand(privateKeyPath, knownHostsPath);

        var processResult = RunProcess(startInfo);
        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException(L10n.Format("GitOperationFailed", operationName, GetProcessError(processResult)));
        }

        return processResult;
    }

    private static string GetConfiguredSshPrivateKeyPath(GitSettings gitSettings)
    {
        var privateKeyPath = gitSettings.SshPrivateKeyPath;
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new InvalidOperationException(L10n.Get("SshPrivateKeyPathNotConfigured"));
        }

        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException(L10n.Format("SshPrivateKeyNotFound", privateKeyPath));
        }

        TrySetPrivateKeyPermissions(privateKeyPath);
        return privateKeyPath;
    }

    private static string? GetConfiguredSshPublicKeyPath(GitSettings gitSettings, string privateKeyPath)
    {
        if (!string.IsNullOrWhiteSpace(gitSettings.SshPublicKeyPath) &&
            File.Exists(gitSettings.SshPublicKeyPath))
        {
            return gitSettings.SshPublicKeyPath;
        }

        var inferredPublicKeyPath = $"{privateKeyPath}.pub";
        return File.Exists(inferredPublicKeyPath) ? inferredPublicKeyPath : null;
    }

    private static string GetSshUsername(string remoteUrl, string usernameFromUrl)
    {
        if (!string.IsNullOrWhiteSpace(usernameFromUrl))
        {
            return usernameFromUrl;
        }

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return uri.UserInfo.Split(':', 2)[0];
        }

        var atSignIndex = remoteUrl.IndexOf('@');
        if (atSignIndex > 0 && remoteUrl.IndexOf("://", StringComparison.Ordinal) < 0)
        {
            return remoteUrl[..atSignIndex];
        }

        return "git";
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, string? workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(L10n.Format("FailedToStartProcess", startInfo.FileName));

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();
        Task.WaitAll(standardOutputTask, standardErrorTask);

        return (process.ExitCode, standardOutputTask.Result, standardErrorTask.Result);
    }

    private static string GetProcessError((int ExitCode, string StandardOutput, string StandardError) processResult)
    {
        var errorText = string.IsNullOrWhiteSpace(processResult.StandardError)
            ? processResult.StandardOutput
            : processResult.StandardError;

        return errorText.Trim();
    }

    internal static string GetSshDirectory(GitSettings? gitSettings = null)
    {
        return SshKeyStoragePathResolver.GetSshDirectory(gitSettings);
    }

    private static string GetKnownHostsPath(GitSettings? gitSettings = null)
    {
        return Path.Combine(GetSshDirectory(gitSettings), KnownHostsFileName);
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(directory));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, comparison);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private (GitSettings git, string? repositoryPath) GetSettings()
    {
        return (_configuration.Get<GitSettings>("Git"), ResolveRepositoryPathFromSettings());
    }

    private string? ResolveRepositoryPathFromSettings()
    {
        var configuredPath = _configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return (_storageFactory?.CurrentStorage?.TaskTreeManager.Storage as FileStorage)?.Path;
    }

    private void ShowUiError(string message, Exception? ex = null)
    {
        if (ex == null)
        {
            Debug.WriteLine($"Git error: {message} at {DateTime.Now}");
        }
        else
        {
            Debug.WriteLine($"Git error: {message} at {DateTime.Now}\n{ex}");
        }

        _notificationManager?.ErrorToast(message);
    }

    private void ShowUiMessage(string message)
    {
        var settings = GetSettings();
        if (settings.git.ShowStatusToasts)
        {
            _notificationManager?.SuccessToast(message);
        }
    }
}
