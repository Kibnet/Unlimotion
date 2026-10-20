using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TUnit.Core;

namespace Unlimotion.Test;

internal static class RenderedTestProcess
{
    private const string ChildFlag = "UNLIMOTION_RENDERED_TEST_CHILD";

    public static async Task<bool> RunIfNeededAsync(TimeSpan? timeout = null)
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1" ||
            Environment.GetEnvironmentVariable("UNLIMOTION_TAB_EMOJI_RENDERED_CHILD") == "1") return false;
        var context = TestContext.Current ?? throw new InvalidOperationException("No current rendered UI test.");
        var root = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "rendered-ui");
        var directory = Path.Combine(root, "rendered-" + Guid.NewGuid().ToString("N"));
        var results = Path.Combine(directory, "results");
        Directory.CreateDirectory(directory);
        // Semantic sessions can leave a drawing/input backend behind. As in the
        // existing importance tests, start Skia first in a fresh child process.
        // Use the stable test-node ID, not the per-execution context GUID: each
        // parameterized parent must run exactly its own case, not the whole method.
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in new[]
        {
            typeof(RenderedTestProcess).Assembly.Location,
            "--filter-uid", context.Metadata.TestDetails.Identity.TestId,
            "--maximum-parallel-tests", "1", "--minimum-expected-tests", "1",
            "--output", "Detailed", "--report-trx", "--results-directory", results
        }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        start.Environment["UNLIMOTION_TEST_TRACE_DIRECTORY"] = Path.Combine(directory, "trace");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered emoji test.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.Execution.CancellationToken);
        if (timeout.HasValue) lifetime.CancelAfter(timeout.Value);
        try { await process.WaitForExitAsync(lifetime.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await SaveLogsAsync();
            context.Execution.CancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Rendered UI test timed out: {context.Metadata.DisplayName}. Logs: {directory}");
        }
        await SaveLogsAsync();
        if (process.ExitCode != 0)
        {
            Console.WriteLine(await output);
            Console.Error.WriteLine(await errors);
            throw new InvalidOperationException($"Rendered UI test failed: {context.Metadata.DisplayName}, exit={process.ExitCode}. Logs: {directory}");
        }
        var report = XDocument.Load(Directory.GetFiles(results, "*.trx").Single());
        var counters = report.Descendants().Single(element => element.Name.LocalName == "Counters");
        if ((string?)counters.Attribute("total") != "1" || (string?)counters.Attribute("passed") != "1")
            throw new InvalidOperationException($"Expected exactly one passed rendered UI case. Logs: {directory}");
        Console.WriteLine($"Rendered UI case passed: {context.Metadata.DisplayName}. Logs/TRX/PNG: {directory}");
        return true;

        async Task SaveLogsAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await output);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await errors);
        }
    }
}
