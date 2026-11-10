using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LibGit2Sharp;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class TaskHistoryFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Task_history_expands_and_shows_git_commit_changes()
    {
        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.Smoke,
            buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(90));
        var configPath = options.Arguments.Single(argument =>
            argument.StartsWith("--config=", StringComparison.Ordinal))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        var taskStoragePath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var currentTaskPath = Path.Combine(
            taskStoragePath,
            UnlimotionAppLaunchHost.GetCurrentTaskId(UnlimotionAutomationScenario.Smoke));

        var referencedTask = CreateHistory(taskStoragePath, currentTaskPath);

        using var session = DesktopAppSession.Launch(options);
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        InPhysicalCoordinates(() =>
        {
            session.MainWindow.Patterns.Transform.Pattern.Resize(2700, 1800);
            session.MainWindow.Patterns.Transform.Pattern.Move(0, 0);
            return true;
        });
        session.MainWindow.Focus();
        var evidenceDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("UNLIMOTION_HISTORY_NATIVE_EVIDENCE")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-evidence", "task-history"));
        Directory.CreateDirectory(evidenceDirectory);
        using var recording = WindowRecording.Start(session.MainWindow, evidenceDirectory);

        var detailsToggle = Find(session, "DetailsPaneToggleButton")?.AsToggleButton()
            ?? throw new InvalidOperationException("The details-pane toggle was not exposed.");
        if (detailsToggle.IsToggled == true)
        {
            detailsToggle.Toggle();
            await WaitUntil(
                () => detailsToggle.IsToggled,
                toggled => toggled == false,
                "The task details pane did not open.");
        }
        var expander = await WaitUntil(
            () => Find(session, "StatusHistoryExpander"),
            element => element is not null,
            "The task-history expander was not exposed.");
        expander!.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        if (expander.Patterns.ExpandCollapse.PatternOrDefault is { } expandPattern)
        {
            expandPattern.Expand();
        }
        else
        {
            expander.Click();
        }

        var historyItems = await WaitUntil(
            () => Find(session, "TaskHistoryItems"),
            element => element is not null,
            "The Git history items control was not exposed after expanding the panel.");
        historyItems!.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        var detailsScroll = Find(session, "CurrentTaskDetailsScrollViewer")
            ?? throw new InvalidOperationException("The task-details scroll viewer was not exposed.");
        var scrollPattern = detailsScroll.Patterns.Scroll.PatternOrDefault
            ?? throw new InvalidOperationException("The task-details scroll pattern was not exposed.");
        await Assert.That(expander.IsOffscreen).IsFalse();
        await Assert.That(historyItems.IsOffscreen).IsFalse();
        var commitRow = await WaitUntil(
            () => historyItems.FindFirstDescendant(condition => condition.ByControlType(ControlType.ListItem)),
            element => element is { IsOffscreen: false }, "A visible commit row was not exposed.");
        var rowBounds = commitRow!.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point((int)rowBounds.Left + 12, (int)rowBounds.Top + 12));
        await Task.Delay(150);
        var selectedAfterPointer = SelectedCommitCount();
        var windowBounds = session.MainWindow.BoundingRectangle;
        Mouse.MoveTo((int)windowBounds.Left + 5, (int)windowBounds.Top + 5);
        Screenshot(session, Path.Combine(evidenceDirectory, "after-pointer.png"));
        Keyboard.Press(VirtualKeyShort.DOWN);
        Keyboard.Release(VirtualKeyShort.DOWN);
        await Task.Delay(150);
        var selectedAfterKeyboard = SelectedCommitCount();

        int SelectedCommitCount() => historyItems.FindAllDescendants(condition => condition.ByControlType(ControlType.ListItem))
            .Count(item => item.Patterns.SelectionItem.Pattern.IsSelected.Value);
        var historyBounds = expander.BoundingRectangle;
        if (scrollPattern.VerticalScrollPercent >= 0)
        {
            foreach (var targetPercent in new[] { 0d, 100d })
            {
                scrollPattern.SetScrollPercent(-1, targetPercent);
                await WaitUntil(
                    () => scrollPattern.VerticalScrollPercent,
                    percent => Math.Abs(percent - targetPercent) <= 1,
                    "The task-details pane did not reach the requested scroll position.");
                await Assert.That(expander.IsOffscreen).IsFalse();
                await Assert.That(historyItems.IsOffscreen).IsFalse();
                await Assert.That(expander.BoundingRectangle).IsEqualTo(historyBounds);
            }
        }

        var visibleNames = historyItems.FindAllDescendants()
            .Select(element => element.Properties.Name.ValueOrDefault)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        Console.WriteLine("Task history automation names: " + string.Join(" | ", visibleNames));
        await Assert.That(visibleNames.Contains("Update task for history", StringComparer.Ordinal)).IsTrue();
        await Assert.That(visibleNames.Any(name => name!.Contains("ui-metadata-user", StringComparison.Ordinal))).IsFalse();
        await Assert.That(visibleNames.Any(name => name!.Contains(referencedTask.Title, StringComparison.Ordinal) &&
            name.Contains(referencedTask.Id, StringComparison.Ordinal))).IsTrue();

        var gitMode = Find(session, "TaskHistoryGitModeButton");
        var optionsButton = Find(session, "TaskHistoryOptionsButton");
        await Assert.That(gitMode).IsNotNull();
        await Assert.That(optionsButton).IsNotNull();
        optionsButton!.AsButton().Invoke();
        using var popupAutomation = new UIA3Automation();
        var desktop = popupAutomation.GetDesktop();
        var metadata = await WaitUntil(
            () => desktop.FindFirstDescendant(popupAutomation.ConditionFactory.ByAutomationId("TaskHistoryMetadataCheckBox")),
            element => element is { IsOffscreen: false },
            "The history options did not expose system fields.");
        await Assert.That(metadata).IsNotNull();
        metadata!.AsCheckBox().Toggle();
        optionsButton!.AsButton().Invoke();
        await WaitUntil(
            () => historyItems.FindAllDescendants().Select(element => element.Properties.Name.ValueOrDefault).ToArray(),
            names => names.Any(name => name?.Contains("ui-metadata-user", StringComparison.Ordinal) == true),
            "Readable system-field values did not appear after enabling them.");
        var systemNames = historyItems.FindAllDescendants().Select(element => element.Properties.Name.ValueOrDefault).ToArray();
        await Assert.That(systemNames.Any(name => name is not null && (name.Contains('[') || name.Contains('{')))).IsFalse();
        optionsButton!.AsButton().Invoke();
        metadata = await WaitUntil(
            () => desktop.FindFirstDescendant(popupAutomation.ConditionFactory.ByAutomationId("TaskHistoryMetadataCheckBox")),
            element => element is { IsOffscreen: false }, "System-fields option did not reopen.");
        metadata!.AsCheckBox().Toggle();
        optionsButton!.AsButton().Invoke();
        await Assert.That(FindText(historyItems, "History title after commit")).IsNotNull();

        var showDetails = await WaitUntil(
            () => Find(session, "TaskHistoryShowDetailsButton"),
            element => element is not null,
            "The long-value details button was not exposed.");
        showDetails!.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        Screenshot(session, Path.Combine(evidenceDirectory, "history-list.png"));
        showDetails.AsButton().Invoke();
        var detailPanel = await WaitUntil(
            () => session.MainWindow.FindAllDescendants(session.ConditionFactory.ByAutomationId("TaskHistoryDetailsPanel"))
                .FirstOrDefault(element => !element.IsOffscreen),
            element => element is { IsOffscreen: false },
            "The full value did not become visible after opening details.");
        await Assert.That(detailPanel).IsNotNull();
        await Assert.That(detailPanel!.FindAllDescendants()
            .Any(element => element.Properties.Name.ValueOrDefault == new string('h', 460))).IsTrue();
        Screenshot(session, Path.Combine(evidenceDirectory, "full-value.png"));
        var collapse = detailPanel.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskHistoryCloseDetailsButton"));
        await Assert.That(collapse).IsNotNull();
        collapse!.AsButton().Invoke();
        await WaitUntil(
            () => FindText(historyItems, new string('h', 460)),
            element => element is null || element.IsOffscreen,
            "The full text remained visible after collapsing the row.");
        expander.Patterns.ExpandCollapse.Pattern.Collapse();
        await Task.Delay(250);
        var collapsedHeight = InPhysicalCoordinates(() => expander.BoundingRectangle.Height);
        var dpi = GetDpiForWindow(session.MainWindow.Properties.NativeWindowHandle.Value);
        Screenshot(session, Path.Combine(evidenceDirectory, "collapsed.png"));
        File.WriteAllText(Path.Combine(evidenceDirectory, "observations.json"),
            System.Text.Json.JsonSerializer.Serialize(new { selectedAfterPointer, selectedAfterKeyboard, collapsedHeight, dpi }));
        await Assert.That(selectedAfterPointer).IsEqualTo(0);
        await Assert.That(selectedAfterKeyboard).IsEqualTo(0);
        await Assert.That((double)collapsedHeight).IsLessThanOrEqualTo(28d * dpi / 96 + 1);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

    private static T InPhysicalCoordinates<T>(Func<T> action)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { return action(); }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static void Screenshot(DesktopAppSession session, string path) => InPhysicalCoordinates(() =>
    {
        using var capture = session.MainWindow.Capture();
        capture.Save(path);
        return true;
    });

    // Opt-in capture of this fixture window only; ordinary UI runs need no ffmpeg.
    private sealed class WindowRecording(Process process, Task<string> errors, string logPath) : IDisposable
    {
        public static WindowRecording? Start(AutomationElement window, string directory)
        {
            var ffmpeg = Environment.GetEnvironmentVariable("UNLIMOTION_HISTORY_FFMPEG");
            if (string.IsNullOrWhiteSpace(ffmpeg)) return null;
            var bounds = InPhysicalCoordinates(() => window.BoundingRectangle);
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-y", "-f", "gdigrab", "-framerate", "15", "-draw_mouse", "0",
                "-offset_x", ((int)bounds.Left).ToString(CultureInfo.InvariantCulture),
                "-offset_y", ((int)bounds.Top).ToString(CultureInfo.InvariantCulture),
                "-video_size", $"{(int)bounds.Width}x{(int)bounds.Height}", "-i", "desktop", "-an",
                "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast",
                "-pix_fmt", "yuv420p", Path.Combine(directory, "history-flow.mp4") }) start.ArgumentList.Add(argument);
            var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start window recorder.");
            return new WindowRecording(process, process.StandardError.ReadToEndAsync(), Path.Combine(directory, "video.log"));
        }

        public void Dispose()
        {
            if (!process.HasExited) { process.StandardInput.WriteLine("q"); process.StandardInput.Flush(); }
            if (!process.WaitForExit(15_000)) { process.Kill(); process.WaitForExit(); }
            File.WriteAllText(logPath, errors.GetAwaiter().GetResult());
            var exitCode = process.ExitCode;
            process.Dispose();
            if (exitCode != 0) throw new InvalidOperationException($"Window recorder failed ({exitCode}); {logPath}");
        }
    }

    private static (string Id, string Title) CreateHistory(string taskStoragePath, string currentTaskPath)
    {
        Repository.Init(taskStoragePath);
        using var repository = new Repository(taskStoragePath);
        var signature = new Signature(
            "Unlimotion UI Test",
            "ui-test@unlimotion.local",
            DateTimeOffset.UtcNow);

        var original = JsonNode.Parse(File.ReadAllText(currentTaskPath))!.AsObject();
        original.Remove("PlannedDuration");
        var task = original.DeepClone().AsObject();
        var related = Directory.EnumerateFiles(taskStoragePath)
            .Where(path => path != currentTaskPath)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))?.AsObject())
            .First(candidate => candidate?["Id"] is not null && candidate["Title"] is not null &&
                candidate["Id"]!.GetValue<string>() != original["Id"]!.GetValue<string>());
        var reference = (Id: related!["Id"]!.GetValue<string>(), Title: related["Title"]!.GetValue<string>());
        // The current task keeps its valid graph; only the old Git snapshot has this removed link.
        original["ParentTasks"] = new JsonArray(reference.Id);
        File.WriteAllText(currentTaskPath, original.ToJsonString());

        Commands.Stage(repository, "*");
        repository.Commit("Initial task snapshot", signature, signature);

        task["Title"] = "History title after commit";
        task["Description"] = new string('h', 460);
        task["PlannedDuration"] = null;
        task["UserId"] = "ui-metadata-user";
        task["StatusHistory"] = new JsonArray(new JsonObject
        {
            ["Status"] = "NotReady", ["Author"] = "ui-metadata-user", ["ChangedAt"] = DateTimeOffset.UtcNow.ToString("O")
        });
        File.WriteAllText(currentTaskPath, task.ToJsonString());

        Commands.Stage(repository, "*");
        repository.Commit("Update task for history", signature, signature);
        return reference;
    }

    private static AutomationElement? Find(DesktopAppSession session, string automationId) =>
        session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId(automationId));

    private static AutomationElement? FindText(AutomationElement root, string text) =>
        root.FindFirstDescendant(
            condition => condition.ByName(text));

    private static async Task<T> WaitUntil<T>(
        Func<T> observation,
        Func<T, bool> isReady,
        string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var value = observation();
            if (isReady(value))
            {
                return value;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(timeoutMessage);
            }

            await Task.Delay(100);
        }
    }

}
