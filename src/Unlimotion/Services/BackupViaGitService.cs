using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string DefaultSshKeyFileName = "id_ed25519_unlimotion";
    private const string TasksFolderName = "Tasks";
    private static readonly object LockObject = new();
    public static Func<string, string> GetAbsolutePath;

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

    public List<string> GetSshPublicKeys()
    {
        var sshDirectory = GetSshDirectory();
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
        var sshDirectory = GetSshDirectory();
        Directory.CreateDirectory(sshDirectory);

        var keyPaths = GetSshKeyPaths(sshDirectory, keyName);
        if (File.Exists(keyPaths.PrivateKeyPath) || File.Exists(keyPaths.PublicKeyPath))
        {
            throw new InvalidOperationException($"SSH key already exists: {keyPaths.PublicKeyPath}");
        }

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
            throw new InvalidOperationException($"ssh-keygen failed: {GetProcessError(processResult)}");
        }

        if (!File.Exists(keyPaths.PrivateKeyPath) || !File.Exists(keyPaths.PublicKeyPath))
        {
            throw new InvalidOperationException($"ssh-keygen did not create expected key files in {sshDirectory}.");
        }

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

    public CredentialsHandler GetCredentials(GitSettings gitSettings)
    {
        return (url, _user, _cred) =>
        {
            if (IsSshUrl(url))
            {
                return new DefaultCredentials();
            }

            return new UsernamePasswordCredentials
            {
                Username = gitSettings.UserName,
                Password = gitSettings.Password
            };
        };
    }

    public BackupRepositoryConnectPreview PreviewConnectRepository()
    {
        var settings = GetSettings();
        var repositoryPath = GetRepositoryPath(settings.repositoryPath);
        var remoteUrl = RequireRemoteUrl(settings.git);
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
                            "Подключение отменено: локальная папка задач и удаленный репозиторий не пустые.");
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
            ShowUiError("Ошибка при клонировании или обновлении репозитория:\n" + ex.Message, ex);
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
                ShowUiError($"Remote not found: {settings.git.RemoteName}");
                return;
            }

            var dbwatcher = _storageFactory?.CurrentWatcher;

            ShowUiMessage("Start Git Push");
            try
            {
                dbwatcher?.SetEnable(false);
                if (repo.RetrieveStatus().IsDirty)
                {
                    Commands.Checkout(repo, settings.git.PushRefSpec);
                    Commands.Stage(repo, "*");

                    var committer = new Signature(settings.git.CommitterName, settings.git.CommitterEmail, DateTime.Now);
                    repo.Commit(msg, committer, committer);

                    ShowUiMessage("Commit Created");
                }
            }
            finally
            {
                dbwatcher?.SetEnable(true);
            }

            var localBranch = repo.Branches[settings.git.PushRefSpec];
            if (localBranch == null)
            {
                ShowUiError($"Local branch not found: {settings.git.PushRefSpec}");
                return;
            }

            var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];
            if (remoteBranch == null || localBranch.Tip.Sha != remoteBranch.Tip.Sha)
            {
                try
                {
                    dbwatcher?.SetEnable(false);

                    if (ShouldUseConfiguredSshKey(remote.Url, settings.git))
                    {
                        PushWithConfiguredSshKey(path, settings.git.RemoteName, settings.git.PushRefSpec, settings.git);
                    }
                    else
                    {
                        var options = new PushOptions
                        {
                            CredentialsProvider = GetCredentials(settings.git)
                        };

                        repo.Network.Push(remote, settings.git.PushRefSpec, options);
                    }

                    ShowUiMessage("Push Successful");
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Can't push the remote repository, because {ex.Message}";
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
                ShowUiError($"Remote not found: {settings.git.RemoteName}");
                return;
            }

            ShowUiMessage("Start Git Pull");

            var dbwatcher = _storageFactory?.CurrentWatcher;
            try
            {
                dbwatcher?.SetEnable(false);

                if (ShouldUseConfiguredSshKey(remote.Url, settings.git))
                {
                    FetchWithConfiguredSshKey(path, settings.git.RemoteName, settings.git);
                }
                else
                {
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                    Commands.Fetch(repo, settings.git.RemoteName, refSpecs, new FetchOptions
                    {
                        CredentialsProvider = GetCredentials(settings.git)
                    }, string.Empty);
                }

                var localBranch = repo.Branches[settings.git.PushRefSpec];
                if (localBranch == null)
                {
                    ShowUiError($"Local branch not found: {settings.git.PushRefSpec}");
                    return;
                }

                var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];
                if (remoteBranch == null)
                {
                    ShowUiError($"Remote branch not found after fetch: {settings.git.RemoteName}/{localBranch.FriendlyName}");
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

                        ShowUiMessage("Merge Successful");
                    }
                    catch (Exception ex)
                    {
                        var errorMessage = $"Can't merge remote branch to local branch, because {ex.Message}";
                        Debug.WriteLine(errorMessage);
                        ShowUiError(errorMessage, ex);
                    }

                    if (stash != null)
                    {
                        var stashIndex = repo.Stashes.ToList().IndexOf(stash);
                        var applyStatus = repo.Stashes.Apply(stashIndex);

                        ShowUiMessage("Stash Applied");
                        if (applyStatus == StashApplyStatus.Applied)
                        {
                            repo.Stashes.Remove(stashIndex);
                        }
                    }

                    if (repo.Index.Conflicts.Any())
                    {
                        const string errorMessage = "Fix conflicts and then commit the result";
                        ShowUiError(errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowUiError($"Can't pull the remote repository, because {ex.Message}", ex);
            }
            finally
            {
                dbwatcher?.SetEnable(true);
            }
        }
    }

    private void InitializeLocalRepositoryAndPush(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        EnsureCommitOnConfiguredBranch(repo, gitSettings, "Initial task backup");
        PushConfiguredBranch(repo, gitSettings);
        ShowUiMessage("Repository initialized and pushed");
    }

    private void InitializeLocalRepositoryAndCheckoutRemote(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        FetchRemote(repo, repositoryPath, gitSettings);
        CheckoutRemoteBranch(repo, gitSettings);
        ShowUiMessage("Repository connected");
    }

    private void InitializeLocalRepositoryMergeAndPush(string repositoryPath, string remoteUrl, GitSettings gitSettings)
    {
        Directory.CreateDirectory(repositoryPath);
        Repository.Init(repositoryPath);

        using var repo = new Repository(repositoryPath);
        EnsureRemote(repo, gitSettings.RemoteName, remoteUrl);
        EnsureCommitOnConfiguredBranch(repo, gitSettings, "Local tasks before connecting backup");
        FetchRemote(repo, repositoryPath, gitSettings);
        MergeRemoteBranch(repo, repositoryPath, gitSettings);
        PushConfiguredBranch(repo, gitSettings);
        ShowUiMessage("Repository connected and merged");
    }

    private void EnsureCommitOnConfiguredBranch(Repository repo, GitSettings gitSettings, string message)
    {
        Commands.Stage(repo, "*");
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
        if (ShouldUseConfiguredSshKey(gitSettings.RemoteUrl, gitSettings))
        {
            FetchWithConfiguredSshKey(repositoryPath, gitSettings.RemoteName, gitSettings);
            return;
        }

        var remote = repo.Network.Remotes[gitSettings.RemoteName]
                     ?? throw new InvalidOperationException($"Remote not found: {gitSettings.RemoteName}");
        Commands.Fetch(repo, gitSettings.RemoteName, remote.FetchRefSpecs.Select(x => x.Specification), new FetchOptions
        {
            CredentialsProvider = GetCredentials(gitSettings)
        }, string.Empty);
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
            MergeRemoteBranchWithGitCli(repositoryPath, gitSettings);
        }

        if (repo.Index.Conflicts.Any())
        {
            throw new InvalidOperationException("Не удалось объединить локальные задачи с удаленным репозиторием: есть Git conflicts.");
        }
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

        var processResult = ShouldUseConfiguredSshKey(gitSettings.RemoteUrl, gitSettings)
            ? RunGitCommandWithConfiguredSshKey(repositoryPath, gitSettings, "git merge", arguments.ToArray())
            : RunGitCommand(repositoryPath, "git merge", arguments.ToArray());

        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"git merge failed: {GetProcessError(processResult)}");
        }
    }

    private void PushConfiguredBranch(Repository repo, GitSettings gitSettings)
    {
        var remote = repo.Network.Remotes[gitSettings.RemoteName]
                     ?? throw new InvalidOperationException($"Remote not found: {gitSettings.RemoteName}");
        if (ShouldUseConfiguredSshKey(remote.Url, gitSettings))
        {
            PushWithConfiguredSshKey(repo.Info.WorkingDirectory, gitSettings.RemoteName, gitSettings.PushRefSpec, gitSettings);
            return;
        }

        repo.Network.Push(remote, gitSettings.PushRefSpec, new PushOptions
        {
            CredentialsProvider = GetCredentials(gitSettings)
        });
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
                   $"Remote branch not found after fetch: {gitSettings.RemoteName}/{branchName}");
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
        if (ShouldUseConfiguredSshKey(remoteUrl, gitSettings))
        {
            var processResult = RunGitCommandWithConfiguredSshKey(
                Environment.CurrentDirectory,
                gitSettings,
                "git ls-remote",
                "ls-remote",
                "--heads",
                remoteUrl);
            return !string.IsNullOrWhiteSpace(processResult.StandardOutput);
        }

        return Repository
            .ListRemoteReferences(remoteUrl, GetCredentials(gitSettings))
            .Any(reference => reference.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal));
    }

    private static bool HasLocalFolderContent(string repositoryPath)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return false;
        }

        return Directory
            .EnumerateFileSystemEntries(repositoryPath)
            .Any(path => !string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireRemoteUrl(GitSettings gitSettings)
    {
        if (string.IsNullOrWhiteSpace(gitSettings.RemoteUrl))
        {
            throw new InvalidOperationException("Remote repository URL is not configured.");
        }

        return gitSettings.RemoteUrl;
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
            throw new InvalidOperationException($"SSH key path must stay inside {rootDirectory}.");
        }

        return (privateKeyPath, $"{privateKeyPath}.pub");
    }

    internal static string BuildGitSshCommand(string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new InvalidOperationException("SSH private key path is not configured.");
        }

        var normalizedPath = privateKeyPath.Replace('\\', '/').Replace("\"", "\\\"");
        return $"ssh -i \"{normalizedPath}\" -o IdentitiesOnly=yes -o BatchMode=yes";
    }

    private static string GetRepositoryPath(string? pathFromSettings)
    {
        var path = string.IsNullOrWhiteSpace(pathFromSettings) ? TasksFolderName : pathFromSettings;
        if (!IsAbsolutePath(path))
        {
            if (GetAbsolutePath == null)
            {
                throw new Exception("Can't get absolute path");
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

    private static bool ShouldUseConfiguredSshKey(string? remoteUrl, GitSettings gitSettings)
    {
        return IsSshUrl(remoteUrl) && !string.IsNullOrWhiteSpace(gitSettings.SshPrivateKeyPath);
    }

    private void CloneRepositoryWithConfiguredSshKey(string remoteUrl, string repositoryPath, GitSettings gitSettings)
    {
        var cloneRoot = Path.GetDirectoryName(repositoryPath);
        if (string.IsNullOrWhiteSpace(cloneRoot))
        {
            throw new InvalidOperationException($"Can't resolve clone directory for {repositoryPath}.");
        }

        Directory.CreateDirectory(cloneRoot);

        var arguments = new List<string> { "clone" };
        if (!string.IsNullOrWhiteSpace(gitSettings.Branch))
        {
            arguments.Add("--branch");
            arguments.Add(gitSettings.Branch);
        }

        arguments.Add(remoteUrl);
        arguments.Add(repositoryPath);

        RunGitCommandWithConfiguredSshKey(cloneRoot, gitSettings, "git clone", arguments.ToArray());
    }

    private void FetchWithConfiguredSshKey(string repositoryPath, string remoteName, GitSettings gitSettings)
    {
        RunGitCommandWithConfiguredSshKey(repositoryPath, gitSettings, "git fetch", "fetch", remoteName);
    }

    private void PushWithConfiguredSshKey(string repositoryPath, string remoteName, string pushRefSpec, GitSettings gitSettings)
    {
        RunGitCommandWithConfiguredSshKey(repositoryPath, gitSettings, "git push", "push", remoteName, pushRefSpec);
    }

    private (int ExitCode, string StandardOutput, string StandardError) RunGitCommandWithConfiguredSshKey(
        string workingDirectory,
        GitSettings gitSettings,
        string operationName,
        params string[] arguments)
    {
        var privateKeyPath = GetConfiguredSshPrivateKeyPath(gitSettings);
        var startInfo = CreateProcessStartInfo("git", workingDirectory, arguments);
        startInfo.Environment["GIT_SSH_COMMAND"] = BuildGitSshCommand(privateKeyPath);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var processResult = RunProcess(startInfo);
        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"{operationName} failed: {GetProcessError(processResult)}");
        }

        return processResult;
    }

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
            throw new InvalidOperationException($"{operationName} failed: {GetProcessError(processResult)}");
        }

        return processResult;
    }

    private static string GetConfiguredSshPrivateKeyPath(GitSettings gitSettings)
    {
        var privateKeyPath = gitSettings.SshPrivateKeyPath;
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new InvalidOperationException("SSH private key path is not configured.");
        }

        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException($"SSH private key not found: {privateKeyPath}");
        }

        return privateKeyPath;
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
                            ?? throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");

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

    private static string GetSshDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, ".ssh");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(home, ".ssh");
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
        return (_configuration.Get<GitSettings>("Git"),
            _configuration.Get<TaskStorageSettings>("TaskStorage")?.Path);
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
