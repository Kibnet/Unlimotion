using System.Text;

namespace Unlimotion.BeforeBuild.Helpers;

public static class FileHelper
{
    public static string? FindDirectoryWithFile(string rootDirectory, string fileName)
    {
        var directories = Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories);

        return directories.FirstOrDefault(dir => File.Exists(Path.Combine(dir, fileName)));
    }
    
    public static void CreateFileInDirectory(string directory, string newFileName, string content)
    {
        var newFilePath = Path.Combine(directory, newFileName);

        using (var fs = File.Create(newFilePath))
        {
            var info = new UTF8Encoding(true).GetBytes(content);
            fs.Write(info, 0, info.Length);
        }

        Console.WriteLine($"File with partial AppNameDefinitionService is created or overwritten: {newFilePath}");
    }
}