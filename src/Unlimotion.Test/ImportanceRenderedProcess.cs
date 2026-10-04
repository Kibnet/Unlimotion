using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class ImportanceRenderedProcess
{
    private const string ChildFlag = "UNLIMOTION_IMPORTANCE_RENDERED_CHILD";

    public static async Task<bool> RunIfNeededAsync(string testName)
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1") return false;

        // A semantic Headless session can leave a drawing backend that produces blank frames.
        // Start the rendered backend first in its own process; propagate every child failure.
        var directory = ImportanceImageComparison.CreateArtifactDirectory("rendered-process-" + testName);
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in new[]
                 {
                     typeof(TaskImportanceVisualUiTests).Assembly.Location,
                     "--treenode-filter", $"/*/*/TaskImportanceVisualUiTests/{testName}",
                     "--maximum-parallel-tests", "1", "--minimum-expected-tests", "1",
                     "--output", "Detailed", "--report-trx", "--results-directory", Path.Combine(directory, "results")
                 }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        if (start.Environment.ContainsKey("UNLIMOTION_TEST_TRACE_DIRECTORY"))
            start.Environment["UNLIMOTION_TEST_TRACE_DIRECTORY"] = Path.Combine(directory, "trace");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered Headless tests.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        // The 24 independent windows can take several minutes on a loaded Windows test host.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await SaveLogsAsync();
            throw new TimeoutException($"Rendered Headless test timed out: {testName}. Logs: {directory}");
        }
        await SaveLogsAsync();
        if (process.ExitCode != 0)
        {
            Console.WriteLine(await output);
            Console.Error.WriteLine(await errors);
            throw new InvalidOperationException($"Rendered Headless test failed: {testName}, exit={process.ExitCode}. Logs: {directory}");
        }
        Console.WriteLine($"Rendered Headless test passed: {testName}. Logs/TRX: {directory}");
        return true;

        async Task SaveLogsAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await output);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await errors);
        }
    }
}
