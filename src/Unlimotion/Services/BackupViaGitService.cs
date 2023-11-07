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

    private readonly string _repositoryPath;
    private readonly string _userName;
    private readonly string _password;

    public BackupViaGitService(string userName, string password, string repositoryPath)
    {
        _userName = userName;
        _password = password;
        _repositoryPath = repositoryPath;
    }

    public void Push(string msg)
    {
        if (string.IsNullOrWhiteSpace(_userName) || string.IsNullOrWhiteSpace(_password))
            Debug.WriteLine("Can't push to the remote repository, because username or password is empty");
        
        using var repo = new Repository(_repositoryPath);
        
        Commands.Checkout(repo, PushRefSpec);
        
        Commands.Stage(repo, "*");
        
        var committer = new Signature(CommitterName, CommitterEmail, DateTime.Now);
        
        repo.Commit(msg, committer, committer);

        var options = new PushOptions
        {
            CredentialsProvider = (_, _, _) =>
                new UsernamePasswordCredentials
                {
                    Username = _userName,
                    Password = _password
                }
        };

        repo.Network.Push(repo.Network.Remotes[RemoteName], PushRefSpec, options);
    }
}