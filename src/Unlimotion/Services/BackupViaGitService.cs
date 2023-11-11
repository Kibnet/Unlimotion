using System;
using System.Diagnostics;
using LibGit2Sharp;

namespace Unlimotion.Services;

public class BackupViaGitService : IRemoteBackupService
{
    private const string RemoteName = "origin";
    private const string PushRefSpec = "refs/heads/main";
    private const string CommitterName = "Backuper";
    private const string CommitterEmail = "backuper@unlimotion.ru";

    private readonly string repositoryPath;
    private readonly string userName;
    private readonly string password;

    public BackupViaGitService(string userName, string password, string repositoryPath)
    {
        this.userName = userName;
        this.password = password;
        this.repositoryPath = repositoryPath;
    }

    public void Push(string msg)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            Debug.WriteLine("Can't push to the remote repository, because username or password is empty");
        
        using var repo = new Repository(repositoryPath);
        
        if (!repo.RetrieveStatus().IsDirty)
        {
            return;
        }

        Commands.Checkout(repo, PushRefSpec);
        
        Commands.Stage(repo, "*");
        
        var committer = new Signature(CommitterName, CommitterEmail, DateTime.Now);

        repo.Commit(msg, committer, committer);

        var options = new PushOptions
        {
            CredentialsProvider = (_, _, _) =>
                new UsernamePasswordCredentials
                {
                    Username = userName,
                    Password = password
                }
        };
      
        try
        {
            repo.Network.Push(repo.Network.Remotes[RemoteName], PushRefSpec, options);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Can't push the remote repository, because {e.Message}");
        }
    }

    public void Pull()
    {
        using var repo = new Repository(repositoryPath);
        var options = new PullOptions
        {
            FetchOptions = new FetchOptions
            {
                CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials
                    {
                        Username = userName,
                        Password = password
                    }
            }
        };

        var signature = new Signature(new Identity(CommitterName, CommitterEmail), DateTimeOffset.Now);

        try
        {
            Commands.Pull(repo, signature, options);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Can't pull the remote repository, because {e.Message}");
        }
    }
}