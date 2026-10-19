using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Win32;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class InstalledTaskProtocolFlaUiTests
{
    private const string ProtocolKey = @"Software\Classes\unlimotion";

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task InstalledProtocol_InstallUpdateColdWarmAndUninstall_UseContentExecutable()
    {
        Skip.Unless(Environment.GetEnvironmentVariable("UNLIMOTION_PROTOCOL_INSTALL_ACCEPTANCE") == "1",
            "Requires opt-in installed-package acceptance on a disposable GitHub-hosted Windows runner.");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Installed protocol acceptance must not modify a personal Windows profile.");

        var runnerTemp = Path.GetFullPath(RequiredEnvironment("RUNNER_TEMP"));
        var root = Path.Combine(runnerTemp, "Unlimotion protocol пробел", Guid.NewGuid().ToString("N"));
        var executable = Path.Combine(root, "current", "Unlimotion.Desktop.exe");
        var updater = Path.Combine(root, "Update.exe");
        var defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Unlimotion");
        using (var existing = Registry.CurrentUser.OpenSubKey(ProtocolKey))
        {
            if (existing is not null || Directory.Exists(defaultDirectory)
                || Process.GetProcessesByName("Unlimotion.Desktop").Length != 0)
                throw new InvalidOperationException("Acceptance requires an empty disposable profile, without Unlimotion or its registration.");
        }

        var baselineSetup = Directory.GetFiles(RequiredEnvironment("UNLIMOTION_PROTOCOL_BASELINE_DIR"), "*-Setup.exe").Single();
        var updatePackage = Directory.GetFiles(RequiredEnvironment("UNLIMOTION_PROTOCOL_CANDIDATE_DIR"), "*-full.nupkg").Single();
        // TUnit may run with the test output directory as its working directory.
        // Keep evidence under the workspace path uploaded by the packaging workflow.
        var evidence = Path.GetFullPath(Path.Combine(RequiredEnvironment("GITHUB_WORKSPACE"),
            "artifacts", "deep-links", "installed", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(evidence);
        var fixture = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(buildConfiguration: "Release", buildBeforeLaunch: false);
        var configPath = fixture.Arguments.Single(value => value.StartsWith("--config=", StringComparison.Ordinal))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["Updates"] = new JsonObject { ["AutoCheckEnabled"] = false };
        var tasksPath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var target = Directory.GetFiles(tasksPath).Select(path => JsonNode.Parse(File.ReadAllText(path))!)
            .First(task => task["Id"] is not null && task["Id"]!.GetValue<string>() != UnlimotionAppLaunchHost.CurrentTaskId);
        var targetId = target["Id"]!.GetValue<string>();
        var targetTitle = target["Title"]!.GetValue<string>();
        Application? application = null;
        try
        {
            Console.WriteLine("PROTOCOL_STAGE install; evidence=" + evidence);
            Directory.CreateDirectory(defaultDirectory);
            File.WriteAllText(Path.Combine(defaultDirectory, "Settings.json"), config.ToJsonString());
            await Run(baselineSetup, "--silent", "--log", Path.Combine(evidence, "install.log"), "--installto", root);
            AssertRegistration(executable, evidence, "installed");
            await Assert.That(XDocument.Load(Path.Combine(root, "current", "sq.version"))
                .Descendants().Single(element => element.Name.LocalName == "version").Value).IsEqualTo("0.0.1");

            // Cold activation goes through the actual registered URI, not a direct EXE launch.
            Console.WriteLine("PROTOCOL_STAGE cold URI activation");
            OpenUri(targetId)?.Dispose();
            using var process = await WaitForInstalledProcess(executable);
            application = Application.Attach(process);
            using var automation = new UIA3Automation();
            var window = application.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                ?? throw new InvalidOperationException("Installed URI activation did not expose a main window.");
            await WaitForTask(window, automation, targetId, targetTitle);
            Capture.Element(window).ToFile(Path.Combine(evidence, "cold.png"));

            Console.WriteLine("PROTOCOL_STAGE warm URI activation from minimized window");
            window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
            await OpenWarm(UnlimotionAppLaunchHost.CurrentTaskId);
            await WaitForTask(window, automation, UnlimotionAppLaunchHost.CurrentTaskId, UnlimotionAppLaunchHost.CurrentTaskTitle);
            await Assert.That(window.Patterns.Window.Pattern.WindowVisualState.Value).IsEqualTo(WindowVisualState.Normal);
            await Assert.That(application.GetAllTopLevelWindows(automation).Length).IsEqualTo(1);
            Capture.Element(window).ToFile(Path.Combine(evidence, "warm.png"));
            var ffmpeg = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
            if (ffmpeg is not null)
            {
                window.Focus();
                var videoPath = Path.Combine(evidence, "activation-after.mp4");
                using (var recorder = new VideoRecorder(new VideoRecorderSettings
                       { ffmpegPath = ffmpeg, TargetVideoPath = videoPath, FrameRate = 5 }, _ => Capture.Element(window)))
                {
                    await Task.Delay(1000);
                    await OpenWarm(targetId);
                    await WaitForTask(window, automation, targetId, targetTitle);
                    await Task.Delay(1000);
                    await OpenWarm(UnlimotionAppLaunchHost.CurrentTaskId);
                    await WaitForTask(window, automation, UnlimotionAppLaunchHost.CurrentTaskId, UnlimotionAppLaunchHost.CurrentTaskTitle);
                    await Task.Delay(1000);
                    recorder.Stop();
                }
                await Assert.That(new FileInfo(videoPath).Length).IsGreaterThan(0);
            }
            else
            {
                Console.WriteLine("VIDEO_FALLBACK: FFmpeg is absent from the disposable runner PATH; actual-window PNGs and UIA assertions retained.");
            }
            application.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            application.Dispose();
            application = null;

            // Reproduce the old registration before a real Velopack apply callback repairs it.
            Console.WriteLine("PROTOCOL_STAGE seed legacy registration and apply candidate");
            var legacy = Path.Combine(root, "Unlimotion.Desktop.exe");
            using (var key = Registry.CurrentUser.OpenSubKey(ProtocolKey, writable: true)!)
            {
                key.SetValue("UnlimotionOwner", legacy);
                using var command = key.OpenSubKey(@"shell\open\command", writable: true)!;
                command.SetValue(string.Empty, $"\"{legacy}\" \"%1\"");
                using var icon = key.OpenSubKey("DefaultIcon", writable: true)!;
                icon.SetValue(string.Empty, $"\"{legacy}\",0");
            }
            await Assert.That(File.Exists(legacy)).IsFalse();
            File.WriteAllText(Path.Combine(evidence, "legacy-registration.json"), JsonSerializer.Serialize(new
                { executable = legacy, exists = File.Exists(legacy), command = ReadCommand() }));
            await Run(updater, "--silent", "--log", Path.Combine(evidence, "update.log"), "apply", "--norestart", "--package", updatePackage);
            AssertRegistration(executable, evidence, "updated");
            await Assert.That(XDocument.Load(Path.Combine(root, "current", "sq.version"))
                .Descendants().Single(element => element.Name.LocalName == "version").Value).IsNotEqualTo("0.0.1");

            // This caller is the same external process that used the association before apply.
            Console.WriteLine("PROTOCOL_STAGE URI activation after real update");
            OpenUri(targetId)?.Dispose();
            using var updatedProcess = await WaitForInstalledProcess(executable);
            application = Application.Attach(updatedProcess);
            var updatedWindow = application.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                ?? throw new InvalidOperationException("URI activation after update did not expose a main window.");
            await WaitForTask(updatedWindow, automation, targetId, targetTitle);
            Capture.Element(updatedWindow).ToFile(Path.Combine(evidence, "updated.png"));
            application.Close();
            await updatedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            application.Dispose();
            application = null;

            Console.WriteLine("PROTOCOL_STAGE uninstall");
            await Run(updater, "--silent", "--log", Path.Combine(evidence, "uninstall.log"), "uninstall");
            using var removed = Registry.CurrentUser.OpenSubKey(ProtocolKey);
            await Assert.That(removed is null).IsTrue();
            Console.WriteLine("Installed protocol evidence: " + evidence);
            Console.WriteLine("BEFORE_VIDEO_FALLBACK: the original missing-launcher defect prevents an app window from opening; legacy-registration.json records the nonexistent launcher. Passing transitions are recorded when FFmpeg is available.");
        }
        finally
        {
            try
            {
                application?.Close();
                application?.Dispose();
                if (File.Exists(updater)) await Run(updater, "--silent", "uninstall");
            }
            finally
            {
                // Only the synthetic configuration created above is removed, on the guarded disposable runner.
                if (Directory.Exists(defaultDirectory)) Directory.Delete(defaultDirectory, recursive: true);
                fixture.DisposeCallback?.Invoke();
            }
        }

        async Task OpenWarm(string id)
        {
            using var child = OpenUri(id);
            if (child is not null)
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                await Assert.That(child.ExitCode).IsEqualTo(0);
            }
        }
    }

    private static Process? OpenUri(string id) => Process.Start(new ProcessStartInfo("unlimotion://task/" + id)
        { UseShellExecute = true });

    private static async Task<Process> WaitForInstalledProcess(string executable)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var candidate in Process.GetProcessesByName("Unlimotion.Desktop"))
            {
                try
                {
                    if (string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                candidate.Dispose();
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("The OS protocol did not start the installed content executable.");
    }

    private static async Task WaitForTask(Window window, UIA3Automation automation, string id, string title)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var editor = window.FindFirstDescendant(automation.ConditionFactory.ByAutomationId("CurrentTaskTitleTextBox"));
            var identity = window.FindFirstDescendant(automation.ConditionFactory.ByAutomationId("CurrentTaskIdTextBlock"));
            if (editor is not null && identity is not null
                && !editor.Properties.IsOffscreen.Value && !identity.Properties.IsOffscreen.Value
                && editor.Patterns.Value.PatternOrDefault?.Value.Value == title && identity.Name == id) return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Installed protocol did not show task '{id}' / '{title}'.");
    }

    private static void AssertRegistration(string executable, string evidence, string stage)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProtocolKey)
            ?? throw new InvalidOperationException("The real installer did not register the protocol.");
        var command = ReadCommand();
        using var iconKey = key.OpenSubKey("DefaultIcon");
        var icon = iconKey?.GetValue(string.Empty) as string;
        var owner = key.GetValue("UnlimotionOwner") as string;
        File.WriteAllText(Path.Combine(evidence, stage + "-registration.json"), JsonSerializer.Serialize(new
            { executable, exists = File.Exists(executable), command, owner, icon }));
        if (!File.Exists(executable) || command != $"\"{executable}\" \"%1\"" || owner != executable
            || icon != $"\"{executable}\",0" || key.GetValue("URL Protocol") as string != string.Empty)
            throw new InvalidOperationException("Installed callback registered an invalid protocol executable.");
    }

    private static string? ReadCommand()
    {
        using var command = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command");
        return command?.GetValue(string.Empty) as string;
    }

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing installed-acceptance input: " + name);

    private static async Task Run(string updaterPath, params string[] arguments)
    {
        var start = new ProcessStartInfo(updaterPath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + updaterPath);
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        if (child.ExitCode != 0) throw new InvalidOperationException($"{updaterPath} exited with code {child.ExitCode}.");
    }
}
