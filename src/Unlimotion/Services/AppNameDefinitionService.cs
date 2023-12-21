using System;
using System.IO;
using LibGit2Sharp;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class AppNameDefinitionService : IAppNameDefinitionService
{
    private string AppName { get; set; } = "Unlimotion";
    
    public AppNameDefinitionService()
    {
        SetVersion();
    }
    
    public string GetVersion()
    {
        return AppName;
    }

    private void SetVersion()
    {
#if GITHUB_ACTIONS
        var githubRef = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        AppName = string.Join(" ", AppName, githubRef ?? "Can't get the tag");
#else
        var branchWithHash = GetCurrentBranchNameWithShortHash();
        AppName = branchWithHash == null ? AppName : string.Join(" ", AppName, GetCurrentBranchNameWithShortHash());
#endif
    }
    
    private static string? GetCurrentBranchNameWithShortHash()
    {
        var gitDirectory = FindGitRoot(Environment.CurrentDirectory);

        if (gitDirectory == null)
            return null;
        
        using var repo = new Repository(gitDirectory);
        return $"{repo.Head.FriendlyName} -> {repo.Head.Tip.Sha[..7]}";
    }
    
    private static string? FindGitRoot(string startDirectory)
    {
        var directoryInfo = new DirectoryInfo(startDirectory);

        while (directoryInfo != null)
        {
            if (Directory.Exists(Path.Combine(directoryInfo.FullName, ".git")))
                return directoryInfo.FullName;

            directoryInfo = directoryInfo.Parent;
        }

        return null; // Каталог ".git" не найден в иерархии каталогов
    }
}