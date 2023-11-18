using System;
using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Splat;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string TasksFolderName = "Tasks";
    private static readonly object LockObject = new();

    public void Push(string msg)
    {
        lock (LockObject)
        {
            var settings = GetSettings();
            CheckGitSettings(settings.git.UserName, settings.git.Password);
            
            using var repo = new Repository(GetRepositoryPath(settings.repositoryPath));
        
            if (!repo.RetrieveStatus().IsDirty)
                return;

            Commands.Checkout(repo, settings.git.PushRefSpec);
        
            Commands.Stage(repo, "*");
        
            var committer = new Signature(settings.git.CommitterName, settings.git.CommitterEmail, DateTime.Now);

            repo.Commit(msg, committer, committer);

            var options = new PushOptions
            {
                CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials
                    {
                        Username = settings.git.UserName,
                        Password = settings.git.Password,
                    }
            };
      
            try
            {
                repo.Network.Push(repo.Network.Remotes[settings.git.RemoteName], settings.git.PushRefSpec, options);
            }
            catch (Exception e)
            {
                var errorMessage = $"Can't push the remote repository, because {e.Message}";
                Debug.WriteLine(errorMessage);
                new Thread(() => ShowUiError(errorMessage)).Start();
            }
        }
    }

    public void Pull()
    {
        lock (LockObject)
        {
            var settings = GetSettings();
            CheckGitSettings(settings.git.UserName, settings.git.Password);
        
            using var repo = new Repository(GetRepositoryPath(settings.repositoryPath));
            var options = new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    CredentialsProvider = (_, _, _) =>
                        new UsernamePasswordCredentials
                        {
                            Username = settings.git.UserName,
                            Password = settings.git.Password
                        }
                }
            };

            var signature = new Signature(new Identity(settings.git.CommitterName, settings.git.CommitterEmail), DateTimeOffset.Now);

            try
            {
                Commands.Pull(repo, signature, options);
            }
            catch (Exception e)
            {
                var errorMessage = $"Can't pull the remote repository, because {e.Message}";
                Debug.WriteLine(errorMessage);
                new Thread(() => ShowUiError(errorMessage)).Start();
            }
        }
    }

    private static string GetRepositoryPath(string? pathFromSettings)
    {
        return string.IsNullOrWhiteSpace(pathFromSettings) ? TasksFolderName : pathFromSettings;
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
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var notificationManager = Locator.Current.GetService<INotificationManagerWrapper>();
            notificationManager?.Ask("Git Error", message, 
                () => Debug.WriteLine($"User read the git error {message} at {DateTime.Now}"));
        });
    }
}