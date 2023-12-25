// See https://aka.ms/new-console-template for more information

using System.Text;
using LibGit2Sharp;

const string appNameDefinitionServiceRelativeName = "AppNameDefinitionService.cs";
const string stringForReplace = "ReleaseTag";

var gitDirectory = FindGitRoot(Environment.CurrentDirectory);

if (gitDirectory == null)
    Console.WriteLine("Can't find git directory");

var additionalAppName = GetCurrentBranchNameWithShortHash(gitDirectory);

var appNameDefinitionServicePath = string.Empty;

try
{
    // Вызов функции для рекурсивного поиска файла
    appNameDefinitionServicePath = FindFile(gitDirectory!, appNameDefinitionServiceRelativeName);
}
catch (Exception ex)
{
    Console.WriteLine("Произошла ошибка: " + ex.Message);
}

if (string.IsNullOrWhiteSpace(appNameDefinitionServicePath))
    Console.WriteLine($"File {appNameDefinitionServiceRelativeName} not found");

ReplaceFirstOccurrenceInFile(appNameDefinitionServicePath!, stringForReplace, additionalAppName!);

static void ReplaceFirstOccurrenceInFile(string filePath, string oldLine, string newLine)
{
    try
    {
        var lines = File.ReadAllLines(filePath);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains(oldLine)) continue;
            lines[i] = lines[i].Replace(oldLine, newLine);
            break;
        }

        File.WriteAllLines(filePath, lines);

        Console.WriteLine("File is updated");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error: " + ex.Message);
    }
}

static string? FindFile(string directory, string fileName)
{
    try
    {
        // Ищем файл в текущей директории
        var files = Directory.GetFiles(directory, fileName);
        
        if (files.Length > 0)
            return files[0]; // Возвращаем путь к первому найденному файлу

        // Рекурсивно ищем в поддиректориях
        foreach (var subDir in Directory.GetDirectories(directory))
        {
            var foundFile = FindFile(subDir, fileName);
            if (foundFile != null)
            {
                return foundFile;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Произошла ошибка: " + ex.Message);
    }

    return null; // Файл не найден
}

static string? GetCurrentBranchNameWithShortHash(string? gitDirectory)
{
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
    
static string? FindGitRoot(string startDirectory)
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