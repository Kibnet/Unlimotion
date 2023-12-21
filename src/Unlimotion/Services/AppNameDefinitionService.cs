using System;
using System.IO;
using System.Text;
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
        AppName = "Unlimotion ReleaseTag"; // DON'T Change. It's used for workflows.
#else
        AppName = $"{string.Join(" ", AppName, 
            GetCurrentBranchNameWithShortHash() == null 
            ? "Can't get the branch name with hash from git"
            : GetCurrentBranchNameWithShortHash())}";
#endif
    }
    
    private static string? GetCurrentBranchNameWithShortHash()
    {
        var gitDirectory = FindGitRoot(Environment.CurrentDirectory);

        if (gitDirectory == null)
            return null;

        using var repo = new Repository(gitDirectory);
        
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(repo.Head.FriendlyName);
        sb.Append(" -> ");
        sb.Append(repo.Head.Tip.Sha[..7]);
        if (repo.RetrieveStatus().IsDirty)
            sb.Append('*');
        sb.Append(']');
        
        return sb.ToString();
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
