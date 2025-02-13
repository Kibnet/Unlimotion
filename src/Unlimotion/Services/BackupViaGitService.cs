﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using Splat;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string TasksFolderName = "Tasks";
    private static readonly object LockObject = new();

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
            ShowUiError(ex.Message);
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
            ShowUiError(ex.Message);
        }

        return result;
    }
   
    public void CloneOrUpdateRepo()
    {
        try
        {
            var settings = GetSettings();

            var path = GetRepositoryPath(settings.repositoryPath);

            if (!Repository.IsValid(path ?? ""))
            {
                var notify = Locator.Current.GetService<INotificationManagerWrapper>();
                notify?.SuccessToast($"Начато клонирование репозитория из {settings.git.RemoteUrl} в {path}");

                var cloneOptions = new CloneOptions
                {
                    BranchName = settings.git.Branch,
                    FetchOptions =
                    {
                        CredentialsProvider = GetCredentials(settings.git)
                    }
                };

                var dbwatcher = Locator.Current.GetService<IDatabaseWatcher>();
                var taskRepository = Locator.Current.GetService<ITaskRepository>();

                dbwatcher?.SetEnable(false);
                taskRepository?.SetPause(true);
                try
                {
                    Repository.Clone(settings.git.RemoteUrl, path, cloneOptions);
                    notify?.SuccessToast($"Клонирование репозитория из {settings.git.RemoteUrl} в {path} успешно завершено");
                }
                finally
                {
                    dbwatcher?.SetEnable(true);
                    taskRepository?.SetPause(false);
                }
            }
            else
            {
                Pull();
            }
        }
        catch (Exception ex)
        {
            ShowUiError("Ошибка при клонировании или обновлении репозитория:\n" + ex.Message);
        }
    }
    
    public CredentialsHandler GetCredentials(GitSettings gitSettings)
    {
        return (_url, _user, _cred) =>
            new UsernamePasswordCredentials
            {
                Username = gitSettings.UserName,
                Password = gitSettings.Password
            };
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

            var dbwatcher = Locator.Current.GetService<IDatabaseWatcher>();

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
                CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials
                    {
                        Username = settings.git.UserName,
                        Password = settings.git.Password,
                    }
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

            var dbwatcher = Locator.Current.GetService<IDatabaseWatcher>();
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            try
            {
                dbwatcher?.SetEnable(false);
                taskRepository?.SetPause(true);
                Commands.Fetch(repo, settings.git.RemoteName, refSpecs, new FetchOptions
                {
                    CredentialsProvider = (_, _, _) =>
                        new UsernamePasswordCredentials
                        {
                            Username = settings.git.UserName,
                            Password = settings.git.Password
                        }
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

                        var configuration = Locator.Current.GetService<IConfiguration>();
                        var mainSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
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
                            dbwatcher.ForceUpdateFile(fullPath, mode);
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

                taskRepository?.SetPause(false);
            }
        }
    }

    public static Func<string, string> GetAbsolutePath;

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
            Debug.WriteLine("Can't push to the remote repository, because username or password is empty");
    }

    private static (GitSettings git, string? repositoryPath) GetSettings()
    {
        var configuration = Locator.Current.GetService<IConfiguration>();
        return (configuration.Get<GitSettings>("Git"),
            configuration.Get<TaskStorageSettings>("TaskStorage")?.Path);
    }

    private static void ShowUiError(string message)
    {
        Debug.WriteLine($"Git error: {message} at {DateTime.Now}");
        var notify = Locator.Current.GetService<INotificationManagerWrapper>();
        notify?.ErrorToast(message);
    }

    private static void ShowUiMessage(string message)
    {
        var settings = GetSettings();
        if (settings.git.ShowStatusToasts)
        {
            var notify = Locator.Current.GetService<INotificationManagerWrapper>();
            notify?.SuccessToast(message);
        }
    }
}