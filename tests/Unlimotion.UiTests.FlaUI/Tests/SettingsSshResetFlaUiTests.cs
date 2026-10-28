using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using LibGit2Sharp;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class SettingsSshResetFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Reset_uses_portable_default_and_survives_ordinary_desktop_restart()
    {
        var output = Environment.GetEnvironmentVariable("UNLIMOTION_SSH_RESET_NATIVE_EVIDENCE") ??
            Path.Combine(AppContext.BaseDirectory, "artifacts", "ssh-reset-native", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var baseline = Environment.GetEnvironmentVariable("UNLIMOTION_SSH_RESET_BASELINE") == "1";
        var original = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(language: "ru", theme: "light",
            buildBeforeLaunch: false, mainWindowTimeout: TimeSpan.FromSeconds(60),
            windowPlacement: DesktopWindowPlacement.Centered(1380, 650));
        // Preserve this private fixture across TWO ordinary processes. Cleanup runs only at the end.
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = baseline
                ? Environment.GetEnvironmentVariable("UNLIMOTION_SSH_RESET_BASELINE_EXECUTABLE")
                    ?? throw new InvalidOperationException("Exact baseline executable required.")
                : original.ExecutablePath,
            WorkingDirectory = original.WorkingDirectory,
            Arguments = original.Arguments, EnvironmentVariables = original.EnvironmentVariables,
            MainWindowTimeout = original.MainWindowTimeout, PollInterval = original.PollInterval,
            WindowPlacement = original.WindowPlacement
        };
        var configPath = options.Arguments.Single(a => a.StartsWith("--config="))[9..];
        var root = Path.GetDirectoryName(configPath)!;
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["Appearance"] ??= new JsonObject();
        config["Appearance"]!["Language"] = "ru";
        var tasks = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var keys = Path.Combine(root, "synthetic-keys");
        Directory.CreateDirectory(keys);
        var publicKey = Path.Combine(keys, "active.pub");
        var privateKey = Path.Combine(keys, "active");
        File.WriteAllText(publicKey, "SYNTHETIC PUBLIC FIXTURE, NOT AN AUTHENTICATION KEY");
        File.WriteAllText(privateKey, "SYNTHETIC PRIVATE FIXTURE, NOT A SECRET");
        var keyBytes = new[] { File.ReadAllBytes(publicKey), File.ReadAllBytes(privateKey) };
        // No repository: source Pull/Push both return RepositoryMissing before network/ref writes.
        if (Repository.IsValid(tasks)) throw new InvalidOperationException("Native fixture unexpectedly has a Git repository.");
        config["Git"] = new JsonObject
        {
            ["BackupEnabled"] = true, ["ShowStatusToasts"] = false,
            ["RemoteUrl"] = "git@example.test:synthetic-not-connected.git",
            ["PullIntervalSeconds"] = 86400, ["PushIntervalSeconds"] = 86400,
            ["SshKeyStoragePath"] = keys, ["SshPublicKeyPath"] = publicKey, ["SshPrivateKeyPath"] = privateKey
        };
        File.WriteAllText(configPath, config.ToJsonString());
        try
        {
            using (var session = DesktopAppSession.Launch(options))
            {
                await OpenSettings(session);
                var reset = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("ResetSshKeyStoragePathButton"));
                if (baseline) await Assert.That(reset).IsNull();
                else await Assert.That(reset!.IsEnabled).IsTrue();
                Screenshot(session, Path.Combine(output, "before-action.png"));
                var captureTitle = "SSH reset isolated test " + Guid.NewGuid().ToString("N");
                SetWindowText(new IntPtr(session.MainWindow.Properties.NativeWindowHandle.Value), captureTitle);
                using var recorder = StartRecording(captureTitle, output, baseline);
                try
                {
                    if (recorder != null)
                    {
                        var video = Path.Combine(output, baseline ? "manual-clear-before.mp4" : "reset-after.mp4");
                        await WaitUntil(() => File.Exists(video) && new FileInfo(video).Length > 0);
                        await Task.Delay(1500);
                    }
                    if (baseline) Find(session, "SshKeyStoragePathTextBox").AsTextBox().Text = "";
                    else reset!.Patterns.Invoke.Pattern.Invoke();
                    await WaitUntil(() => Find(session, "SshKeyStoragePathTextBox").AsTextBox().Text == "");
                    if (!baseline) await Assert.That(Find(session, "ResetSshKeyStoragePathButton").IsEnabled).IsFalse();
                    if (!baseline) await Assert.That(Find(session, "SelectedSshPublicKeyComboBox").AsComboBox().SelectedItem).IsNull();
                    Screenshot(session, Path.Combine(output, "after-action.png"));
                    await WaitUntil(() => JsonNode.Parse(File.ReadAllText(configPath))?["Git"]?["SshKeyStoragePath"]?.GetValue<string>() == "");
                    if (recorder != null) { await recorder.WaitForExitAsync(); await Assert.That(recorder.ExitCode).IsEqualTo(0); }
                }
                finally
                {
                    // Never leave a region recorder alive after its private app closes.
                    if (recorder is { HasExited: false }) { recorder.Kill(entireProcessTree: true); await recorder.WaitForExitAsync(); }
                }
            }
            using (var restarted = DesktopAppSession.Launch(options))
            {
                await OpenSettings(restarted);
                await Assert.That(Find(restarted, "SshKeyStoragePathTextBox").AsTextBox().Text).IsEqualTo("");
                if (!baseline) await Assert.That(Find(restarted, "ResetSshKeyStoragePathButton").IsEnabled).IsFalse();
                await Assert.That(Find(restarted, "SelectedSshPublicKeyComboBox").AsComboBox().SelectedItem).IsNull();
                Screenshot(restarted, Path.Combine(output, "after-restart.png"));
            }
            await Assert.That(File.ReadAllBytes(publicKey)).IsEquivalentTo(keyBytes[0]);
            await Assert.That(File.ReadAllBytes(privateKey)).IsEquivalentTo(keyBytes[1]);
            await Assert.That(Repository.IsValid(tasks)).IsFalse();
        }
        finally { original.DisposeCallback?.Invoke(); }
    }

    private static AutomationElement Find(DesktopAppSession session, string id) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id)) ??
        throw new InvalidOperationException("Missing control: " + id);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr handle, string title);

    private static void Screenshot(DesktopAppSession session, string path)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            using var capture = Capture.Element(session.MainWindow);
            capture.ToFile(path);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static async Task OpenSettings(DesktopAppSession session)
    {
        session.MainWindow.Focus();
        session.MainWindow.Patterns.Transform.Pattern.Resize(1380, 650);
        session.MainWindow.Patterns.Transform.Pattern.Move(100, 100);
        var details = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByName("↔️"));
        if (details?.Patterns.Toggle.PatternOrDefault is { } toggle &&
            toggle.ToggleState.Value == global::FlaUI.Core.Definitions.ToggleState.Off) toggle.Toggle();
        Find(session, "GlobalSettingsButton").Patterns.Invoke.Pattern.Invoke();
        var scroll = Find(session, "SettingsRoot").FindAllDescendants().FirstOrDefault(e => e.Patterns.Scroll.IsSupported &&
            e.Patterns.Scroll.Pattern.VerticallyScrollable.Value);
        scroll?.Patterns.Scroll.Pattern.SetScrollPercent(-1, 75);
        // Physical wheel input scrolls the actual viewport, not an offscreen accessibility action.
        var bounds = session.MainWindow.BoundingRectangle;
        global::FlaUI.Core.Input.Mouse.MoveTo((int)bounds.Right - 150, (int)bounds.Top + 350);
        global::FlaUI.Core.Input.Mouse.Scroll(-15);
        Find(session, "SshKeyStoragePathTextBox").Focus();
        await Task.Delay(250);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(150);
        }
        throw new TimeoutException("SSH reset native state did not settle.");
    }

    private static Process? StartRecording(string title, string output, bool baseline)
    {
        var script = Environment.GetEnvironmentVariable("UNLIMOTION_SSH_RESET_RECORDER_PATH");
        if (string.IsNullOrEmpty(script)) return null;
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", script, "-WindowTitle", title,
            "-ProcessName", "Unlimotion.Desktop.exe", "-Output", Path.Combine(output, baseline ? "manual-clear-before.mp4" : "reset-after.mp4"),
            "-DurationSeconds", "15", "-Fps", "15", "-NoCursor" }) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Recorder did not start.");
        _ = process.StandardOutput.ReadToEndAsync().ContinueWith(t => File.WriteAllText(Path.Combine(output, "recorder.log"), t.Result));
        _ = process.StandardError.ReadToEndAsync().ContinueWith(t => File.WriteAllText(Path.Combine(output, "recorder-error.log"), t.Result));
        return process;
    }
}
