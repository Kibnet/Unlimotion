using System.Diagnostics;
using System.Text.Json.Nodes;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Input;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using LibGit2Sharp;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class GitSyncSettingsFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Git_settings_toggle_shows_shared_information_and_hides_header_when_disabled()
    {
        var baseline = Environment.GetEnvironmentVariable("UNLIMOTION_GIT_SYNC_BASELINE") == "1";
        var output = Environment.GetEnvironmentVariable("UNLIMOTION_GIT_SYNC_NATIVE_EVIDENCE") ??
            Path.Combine(AppContext.BaseDirectory, "artifacts", "git-sync-native", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(language: "ru", theme: "light",
            buildBeforeLaunch: false, mainWindowTimeout: TimeSpan.FromSeconds(60),
            windowPlacement: DesktopWindowPlacement.Centered(800, 600));
        var configPath = options.Arguments.Single(argument => argument.StartsWith("--config="))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["Appearance"]!["Language"] = "ru";
        var tasksPath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        // Native startup keeps the real scheduler. Its initial jobs may run, so every remote is local.
        var remotePath = Path.Combine(Path.GetDirectoryName(tasksPath)!, "native-git-remote.git");
        Repository.Init(remotePath, isBare: true);
        config["Git"]!["RemoteUrl"] = remotePath;
        config["Git"]!["UserName"] = "";
        config["Git"]!["Password"] = "";
        config["Git"]!["PullIntervalSeconds"] = "86400";
        config["Git"]!["PushIntervalSeconds"] = "86400";
        File.WriteAllText(configPath, config.ToJsonString());
        Repository.Init(tasksPath);
        using (var repository = new Repository(tasksPath))
        {
            Commands.Stage(repository, "*");
            var author = new Signature("Fixture", "fixture@example.com", DateTimeOffset.Now);
            repository.Commit("fixture", author, author);
            repository.Network.Remotes.Add("origin", remotePath);
        }
        using var session = DesktopAppSession.Launch(options);
        session.MainWindow.Patterns.Transform.Pattern.Resize(800, 600);
        session.MainWindow.Focus();
        var detailsToggle = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByName("↔️")) ??
            throw new InvalidOperationException("Task details toggle missing");
        detailsToggle.Patterns.Toggle.Pattern.Toggle();
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        if (Find("SettingsTabItem") is { } settingsTab) settingsTab.AsTabItem().Select();
        else
        {
            (Find("MainTabsOverflowButton") ?? throw new InvalidOperationException("Tabs overflow missing"))
                .Patterns.Invoke.Pattern.Invoke();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            AutomationElement? settingsMenu = null;
            while (settingsMenu == null && DateTime.UtcNow < deadline)
            {
                settingsMenu = session.MainWindow.Automation.GetDesktop()
                    .FindAllChildren(session.ConditionFactory.ByProcessId(session.MainWindow.Properties.ProcessId.Value))
                    .Select(root => root.FindFirstDescendant(session.ConditionFactory.ByAutomationId("MainTabsOverflowSettingsTabItem")))
                    .FirstOrDefault(element => element != null);
                if (settingsMenu == null) await Task.Delay(200);
            }
            var menu = settingsMenu ?? throw new InvalidOperationException("Settings overflow menu missing");
            if (menu.Patterns.Invoke.PatternOrDefault is { } invoke) invoke.Invoke();
            else DesktopPointer.Click(menu);
        }
        page.SetChecked(static p => p.BackupAutoCheckBox, false);
        var checkbox = Find("BackupAutoCheckBox") ?? throw new InvalidOperationException("Git checkbox missing");
        checkbox.Focus();
        var scroll = session.MainWindow.FindAllDescendants().FirstOrDefault(element => element.Patterns.Scroll.IsSupported);
        if (scroll != null && scroll.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            scroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 40);
        Screenshot("setup.png");
        using var recording = StartRecording();
        try
        {
            await Task.Delay(2500);
            await Verify(false);
            Screenshot("disabled-start.png");
            page.SetChecked(static p => p.BackupAutoCheckBox, true);
            await Verify(true);
            await Task.Delay(2500);
            Screenshot("enabled.png");
            page.SetChecked(static p => p.BackupAutoCheckBox, false);
            await Verify(false);
            await Task.Delay(2500);
            Screenshot("disabled-end.png");
            if (recording != null)
            {
                await recording.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
                await Assert.That(recording.ExitCode).IsEqualTo(0);
                await Assert.That(new FileInfo(Path.Combine(output, "flow.mp4")).Length > 0).IsTrue();
            }
            await File.WriteAllTextAsync(Path.Combine(output, "flow-trace.txt"),
                $"Native FlaUI, RU/light, 800x600, isolated temporary config/repository and local bare remote; actual Settings tab + checkbox off→on→off; baseline={baseline}; real scheduler initial jobs may run against that local remote, subsequent interval=86400s; recording={(recording != null)}\n");
        }
        finally { if (recording is { HasExited: false }) recording.Kill(entireProcessTree: true); }

        AutomationElement? Find(string id) => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id));
        bool Visible(string id) => Find(id) is { Properties.IsOffscreen.ValueOrDefault: false };
        async Task Verify(bool enabled)
        {
            if (baseline)
            {
                await Assert.That(Find("GitSyncInformationText")).IsNull();
                return;
            }
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                var text = Find("GitSyncInformationText")?.Properties.Name.ValueOrDefault ?? "";
                if (Visible("GitStatusIndicator") == enabled &&
                    (enabled ? text.Contains("Файлов:") : text == "Git-синхронизация отключена")) return;
                await Task.Delay(200);
            }
            Screenshot("failure.png");
            throw new TimeoutException($"Git UI mismatch: enabled={enabled}, header={Visible("GitStatusIndicator")}, info={Find("GitSyncInformationText")?.Properties.Name.ValueOrDefault}");
        }
        void Screenshot(string name)
        {
            using var capture = Capture.Element(session.MainWindow);
            capture.ToFile(Path.Combine(output, name));
        }
        Process? StartRecording()
        {
            var script = Environment.GetEnvironmentVariable("UNLIMOTION_GIT_SYNC_RECORDER_PATH");
            if (string.IsNullOrEmpty(script)) return null;
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-File", script, "-WindowTitle", session.MainWindow.Title,
                         "-ProcessName", "Unlimotion.Desktop.exe", "-Output", Path.Combine(output, "flow.mp4"),
                         "-DurationSeconds", "18", "-Fps", "15" }) start.ArgumentList.Add(arg);
            var process = Process.Start(start) ?? throw new InvalidOperationException("Recorder did not start");
            _ = process.StandardOutput.ReadToEndAsync().ContinueWith(task => File.WriteAllText(Path.Combine(output, "recorder.log"), task.Result));
            _ = process.StandardError.ReadToEndAsync().ContinueWith(task => File.WriteAllText(Path.Combine(output, "recorder-error.log"), task.Result));
            return process;
        }
    }
}
