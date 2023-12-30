using System;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Unlimotion.AppGenerator;

[Generator]
public class AppNameGenerator : ISourceGenerator
{
    private const string DefaultAppName = "Unlimotion";

    public void Execute(GeneratorExecutionContext context)
    {
        var additionalAppName = string.Empty;
        var sb = new StringBuilder();
#if GITHUB_ACTIONS
        additionalAppName = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
#else
        var branchName = RunGitCommand("symbolic-ref --short HEAD");
        var commitHash = RunGitCommand("rev-parse HEAD");

        sb.Append('[');
        sb.Append(branchName);
        sb.Append(" -> ");
        sb.Append(commitHash);
        if (HasUncommittedChanges())
            sb.Append('*');
        sb.Append(']');
        
        additionalAppName = sb.ToString();
        sb.Clear();
#endif
        var fullName = $"{DefaultAppName} {additionalAppName}";

        sb.AppendLine("using Unlimotion.ViewModel;");
        sb.AppendLine("namespace Unlimotion.Services;");
        sb.AppendLine(string.Empty);
        
        sb.AppendLine("public partial class AppNameDefinitionService");
        sb.AppendLine("{");
        sb.AppendLine("     public AppNameDefinitionService()");
        sb.AppendLine("     {");
        sb.AppendLine($"         AppName = \"{fullName}\";");
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
            CreateNoWindow = true
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
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process!.WaitForExit();
        return process.ExitCode != 0; // Если код выхода != 0, есть незакоммиченные изменения
    }
}