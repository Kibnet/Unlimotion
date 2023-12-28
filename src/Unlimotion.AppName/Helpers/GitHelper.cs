using System.Text;
using LibGit2Sharp;

namespace Unlimotion.AppName.Helpers;

public static class GitHelper
{
    public static string? GetCurrentBranchNameWithShortHash()
    {
        var gitDirectory = FindGitRoot(Environment.CurrentDirectory);

        if (gitDirectory == null)
            return null;

        using var repo = new Repository(gitDirectory);
        
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(repo.Head.FriendlyName);
        sb.Append(" -> ");
        sb.Append(repo.Head.Tip.Sha.Substring(0, 8));
        if (repo.RetrieveStatus().IsDirty)
            sb.Append('*');
        sb.Append(']');
        
        return sb.ToString();
    }
    
    public static string? FindGitRoot(string startDirectory)
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