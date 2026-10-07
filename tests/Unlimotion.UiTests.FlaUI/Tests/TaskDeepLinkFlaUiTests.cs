using System.Diagnostics;
using System.Text.Json.Nodes;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.Definitions;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class TaskDeepLinkFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task TaskLink_ColdAndWarmStartup_OpensExistingCardWithoutAnotherWindow()
    {
        var baseline = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            buildBeforeLaunch: false, mainWindowTimeout: TimeSpan.FromSeconds(60));
        var configPath = baseline.Arguments.Single(argument => argument.StartsWith("--config=", StringComparison.Ordinal))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        var taskPath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var tasks = Directory.GetFiles(taskPath).Select(path => JsonNode.Parse(File.ReadAllText(path))!)
            .Where(task => task["Id"] is not null).ToArray();
        var currentId = UnlimotionAppLaunchHost.CurrentTaskId;
        var coldTarget = tasks.First(task => task["Id"]!.GetValue<string>() != currentId);
        var coldId = coldTarget["Id"]!.GetValue<string>();
        var coldTitle = coldTarget["Title"]!.GetValue<string>();
        var channel = "Unlimotion.TaskDeepLinkActivation.NativeTests." + Guid.NewGuid().ToString("N");
        var environment = baseline.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value);
        environment["UNLIMOTION_AUTOMATION_TASK_DEEP_LINK_CHANNEL"] = channel;
        environment[UnlimotionAppLaunchHost.AutomationWindowTitleEnvironmentVariable] = channel;
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = baseline.ExecutablePath,
            WorkingDirectory = baseline.WorkingDirectory,
            Arguments = baseline.Arguments.Concat(new[] { "unlimotion://task/" + coldId }).ToArray(),
            EnvironmentVariables = environment,
            MainWindowTimeout = baseline.MainWindowTimeout,
            PollInterval = baseline.PollInterval,
            WindowPlacement = baseline.WindowPlacement,
            DisposeCallback = baseline.DisposeCallback
        };
        using var session = DesktopAppSession.Launch(options);
        await WaitForTitle(session, coldTitle);
        var evidence = Path.GetFullPath(Path.Combine("artifacts", "deep-links", "native", Guid.NewGuid().ToString("N")));
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
        var secondaryStart = new ProcessStartInfo(options.ExecutablePath)
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in baseline.Arguments) secondaryStart.ArgumentList.Add(argument);
        secondaryStart.ArgumentList.Add("unlimotion://task/" + currentId);
        secondaryStart.Environment["UNLIMOTION_AUTOMATION_TASK_DEEP_LINK_CHANNEL"] = channel;
        using var secondary = Process.Start(secondaryStart)!;
        try
        {
            await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(secondary.ExitCode).IsEqualTo(0);
            await WaitForTitle(session, UnlimotionAppLaunchHost.CurrentTaskTitle);
            await Assert.That(session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value)
                .IsEqualTo(WindowVisualState.Normal);
            await RecordTransitions(session, baseline, channel, evidence, coldId, coldTitle, currentId);
            Console.WriteLine("Native deep link evidence: " + evidence);
        }
        finally
        {
            if (!secondary.HasExited) secondary.Kill(entireProcessTree: true);
        }
    }

    private static async Task RecordTransitions(DesktopAppSession session, DesktopAppLaunchOptions baseline,
        string channel, string evidence, string coldId, string coldTitle, string currentId)
    {
        // Record only the foreground fixture window, never its minimized region
        // where an unrelated desktop or notifications could become visible.
        using var recording = StartRecording(channel, evidence);
        if (recording is null) return;
        try
        {
            await Task.Delay(2000);
            await Forward(coldId);
            await WaitForTitle(session, coldTitle);
            await Task.Delay(1000);
            await Forward(currentId);
            await WaitForTitle(session, UnlimotionAppLaunchHost.CurrentTaskTitle);
            await recording.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(recording.ExitCode).IsEqualTo(0);
        }
        finally { if (!recording.HasExited) recording.Kill(entireProcessTree: true); }

        async Task Forward(string id)
        {
            var start = new ProcessStartInfo(baseline.ExecutablePath)
                { WorkingDirectory = baseline.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in baseline.Arguments) start.ArgumentList.Add(argument);
            start.ArgumentList.Add("unlimotion://task/" + id);
            start.Environment["UNLIMOTION_AUTOMATION_TASK_DEEP_LINK_CHANNEL"] = channel;
            using var child = Process.Start(start)!;
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                await Assert.That(child.ExitCode).IsEqualTo(0);
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
    }

    private static Process? StartRecording(string title, string evidence)
    {
        var script = Environment.GetEnvironmentVariable("UNLIMOTION_DEEP_LINK_RECORD_SCRIPT");
        if (string.IsNullOrWhiteSpace(script)) return null;
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-WindowTitle", title,
                     "-Output", Path.Combine(evidence, "activation-after.mp4"), "-DurationSeconds", "8", "-Fps", "20", "-NoCursor" })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot start window recording.");
    }

    private static async Task WaitForTitle(DesktopAppSession session, string title)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var editor = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("CurrentTaskTitleTextBox"));
            if (editor?.Patterns.Value.PatternOrDefault?.Value.Value == title) return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Task link did not open the expected card: " + title);
    }
}
