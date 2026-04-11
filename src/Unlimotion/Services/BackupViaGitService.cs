using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string TasksFolderName = "Tasks";
    private static readonly object LockObject = new();
    public static Func<string, string> GetAbsolutePath;

    private readonly IConfiguration _configuration;
    private readonly INotificationManagerWrapper? _notificationManager;
    private readonly ITaskStorageFactory? _storageFactory;

    public BackupViaGitService(IConfiguration configuration, INotificationManagerWrapper? notificationManager = null, ITaskStorageFactory? storageFactory = null)
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
            var refs = repo.Refs;
            foreach (var re in refs)
            {
                if (re.CanonicalName.StartsWith("refs/heads"))
                {
                    result.Add(re.CanonicalName);
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

            var remotes = repo.Network.Remotes;
            foreach (var remote in remotes)
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

    public List<string> GetSshPublicKeys()
    {
        var sshDirectory = GetSshDirectory();
        if (!Directory.Exists(sshDirectory))
        {
            return new List<string>();
        }

        return Directory.GetFiles(sshDirectory, "*.pub", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .ToList();
    }

    public string GenerateSshKey(string keyName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(keyName) ? "id_ed25519_unlimotion" : keyName.Trim();
        var sshDirectory = GetSshDirectory();
        Directory.CreateDirectory(sshDirectory);

        var privateKeyPath = Path.Combine(sshDirectory, normalizedName);
        var publicKeyPath = $"{privateKeyPath}.pub";
        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
        {
            throw new InvalidOperationException($"SSH key already exists: {publicKeyPath}");
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ssh-keygen",
            Arguments = $"-t ed25519 -f \"{privateKeyPath}\" -N \"\" -C \"unlimotion\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ssh-keygen.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"ssh-keygen failed: {error}");
        }

        return publicKeyPath;
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
                var privateKeyPath = gitSettings.SshPrivateKeyPath;
                if (!string.IsNullOrWhiteSpace(privateKeyPath) && File.Exists(privateKeyPath))
                {
                    TryAddSshKeyToAgent(privateKeyPath);
                }

                return new DefaultCredentials();
            }

            return new UsernamePasswordCredentials
            {
                Username = gitSettings.UserName,
                Password = gitSettings.Password
            };
        };
    }

    public void CloneOrUpdateRepo()
    {
        try
        {
            var settings = GetSettings();
            if (!Repository.IsValid(settings.repositoryPath ?? ""))
            {
                ShowUiError($"Клонирование репозитория из {settings.git.RemoteUrl} в {settings.repositoryPath}");

                var cloneOptions = new CloneOptions
                {
                    BranchName = settings.git.Branch,
                    FetchOptions =
                    {
                        CredentialsProvider = GetCredentials(settings.git)
                    }
                };

                Repository.Clone(settings.git.RemoteUrl, settings.repositoryPath, cloneOptions);
            }
            else
            {
                Pull();
            }
        }
        catch (Exception ex)
        {
            ShowUiError("Ошибка при клонировании или обновлении репозитория:\n" +ex.Message);
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

            var options = new PushOptions
            {
                CredentialsProvider = GetCredentials(settings.git)
            };

            var localBranch = repo.Branches[settings.git.PushRefSpec];
            var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];

            if (localBranch.Tip.Sha != remoteBranch.Tip.Sha)
            {
                try
                {
                    dbwatcher?.SetEnable(false);
                    repo.Network.Push(repo.Network.Remotes[settings.git.RemoteName], settings.git.PushRefSpec, options);
                    ShowUiMessage("Push Successful");
                }
                catch (Exception e)
                {
                    var errorMessage = $"Can't push the remote repository, because {e.Message}";
                    Debug.WriteLine(errorMessage);
                    ShowUiError(errorMessage);
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

            var refSpecs = repo.Network.Remotes[settings.git.RemoteName].FetchRefSpecs.Select(x => x.Specification);

            ShowUiMessage("Start Git Pull");

            var dbwatcher = _storageFactory?.CurrentWatcher;
            try
            {
                dbwatcher?.SetEnable(false);
                //taskRepository?.SetPause(true);
                Commands.Fetch(repo, settings.git.RemoteName, refSpecs, new FetchOptions
                {
                    CredentialsProvider = GetCredentials(settings.git)
                }, string.Empty);

                var localBranch = repo.Branches[settings.git.PushRefSpec];
                var remoteBranch = repo.Branches[$"refs/remotes/{settings.git.RemoteName}/{localBranch.FriendlyName}"];

                if (localBranch.Tip.Sha != remoteBranch.Tip.Sha)
                {
                    var changes = repo.Diff.Compare<TreeChanges>(localBranch.Tip.Tree, remoteBranch.Tip.Tree);

                    var signature = new Signature(new Identity(settings.git.CommitterName, settings.git.CommitterEmail), DateTimeOffset.Now);

                    var stash = repo.Stashes.Add(signature, "Stash before merge");

                    Commands.Checkout(repo, settings.git.PushRefSpec);

                    try
                    {
                        var results = repo.Merge(remoteBranch, signature, new MergeOptions());

                        var mainSettings = _configuration.Get<TaskStorageSettings>("TaskStorage");
                        // Выводим список измененных файлов
                        foreach (var change in changes)
                        {
                            var fullPath = Path.Combine(mainSettings.Path, change.Path);
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
                            dbwatcher.ForceUpdateFile(change.Path, mode);
                        }
                        ShowUiMessage("Merge Successful");
                    }
                    catch (Exception e)
                    {
                        var errorMessage = $"Can't merge remote branch to local branch, because {e.Message}";
                        Debug.WriteLine(errorMessage);
                        ShowUiError(errorMessage);
                    }

                    if (stash != null)
                    {
                        var stashIndex = repo.Stashes.ToList().IndexOf(stash);
                        var applyStatus = repo.Stashes.Apply(stashIndex);

                        ShowUiMessage("Stash Applied");
                        if (applyStatus == StashApplyStatus.Applied)
                            repo.Stashes.Remove(stashIndex);
                    }

                    if (repo.Index.Conflicts.Any())
                    {
                        const string errorMessage = "Fix conflicts and then commit the result";
                        ShowUiError(errorMessage);
                    }
                }
            }
            finally
            {
                dbwatcher?.SetEnable(true);
            }
        }
    }

    private static string GetRepositoryPath(string? pathFromSettings)
    {
        string path = string.IsNullOrWhiteSpace(pathFromSettings) ? TasksFolderName : pathFromSettings;

        //Проверка пути на абсолютность или относительность
        if (!IsAbsolutePath(path))
        {
            if (GetAbsolutePath != null)
            {
                path = GetAbsolutePath(path);
            }
            else
            {
                throw new Exception("Can't get absolute path");
            }
        }

        return path;
    }
    static bool IsAbsolutePath(string path)
    {
        return Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(Path.GetPathRoot(path)?.Trim('\\', '/'));
    }

    private static void CheckGitSettings(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            Debug.WriteLine("Username/password can be empty when SSH auth is used.");
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

    private static void TryAddSshKeyToAgent(string privateKeyPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ssh-add",
                Arguments = $"\"{privateKeyPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return;
            }

            process.WaitForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add SSH key to agent: {ex.Message}");
        }
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
