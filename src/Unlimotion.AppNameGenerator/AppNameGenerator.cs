using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Unlimotion.AppGenerator;

[Generator]
public class AppNameGenerator : ISourceGenerator
{
    private const string DefaultAppName = "Unlimotion";
    private static string _callingProjectPath = null!;

    public void Execute(GeneratorExecutionContext context)
    {
        var appFullName = string.Empty;
        var sb = new StringBuilder();
#if GITHUB_ACTIONS
        var additionalAppName = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        appFullName = $"{DefaultAppName} {additionalAppName}";
#else
        var mainSyntaxTree = context.Compilation.SyntaxTrees.First(x => x.HasCompilationUnitRoot);
        var directoryPath = Path.GetDirectoryName(mainSyntaxTree.FilePath);
        
        if (directoryPath != null)
        {
            _callingProjectPath = directoryPath;

            var branchName = RunGitCommand("symbolic-ref --short HEAD");
            var shortCommitHash = RunGitCommand("rev-parse HEAD");
            if (shortCommitHash.Length >= 7)
                shortCommitHash = shortCommitHash.Substring(0, 7);

            sb.Append(DefaultAppName);
            if (HasUncommittedChanges())
                sb.Append('*');
            sb.Append(" [");
            sb.Append(branchName);
            sb.Append(" -> ");
            sb.Append(shortCommitHash);
            sb.Append(']');
        
            appFullName = sb.ToString();
            sb.Clear();
        }
        else
        {
            appFullName = "AppNameGenerator: Calling project not found";
        }
#endif
        sb.AppendLine("using Unlimotion.ViewModel;");
        sb.AppendLine("namespace Unlimotion.Services;");
        sb.AppendLine(string.Empty);
        
        sb.AppendLine("public partial class AppNameDefinitionService");
        sb.AppendLine("{");
        sb.AppendLine("     public AppNameDefinitionService()");
        sb.AppendLine("     {");
        sb.AppendLine($"         AppName = \"{appFullName}\";");
        sb.AppendLine("     }");
        sb.AppendLine("}");

        // Add the source code to the compilation
        context.AddSource("AppNameDefinitionService.g.cs", sb.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this one
    }

    private static string RunGitCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _callingProjectPath
        };

        using var process = Process.Start(startInfo);
        using var reader = process!.StandardOutput;
        return reader.ReadToEnd().Trim();
    }

    private static bool HasUncommittedChanges()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "diff --quiet HEAD --",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = _callingProjectPath
        };

        using var process = Process.Start(startInfo);
        process!.WaitForExit();
        return process.ExitCode != 0; // Если код выхода != 0, есть незакоммиченные изменения
    }
}