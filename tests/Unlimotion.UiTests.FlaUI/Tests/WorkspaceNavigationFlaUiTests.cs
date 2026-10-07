using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class WorkspaceNavigationFlaUiTests
{
    private const string EvidenceDirectoryVariable = "UNLIMOTION_WORKSPACE_NAVIGATION_EVIDENCE_DIR";

    [Test, NotInParallel("DesktopUi")]
    public void IndependentTaskViews_RailContextMenuAndTaskCardHaveNoNestedModes()
    {
        EnsurePhysicalPixelDpiAwareness();
        var evidenceDirectory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.Feed, language: "en", buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(90),
            feedVaultPrepared: path => UnlimotionAutomationScenarioData.SeedWorkspaceStoryTasks(
                Path.Combine(Path.GetDirectoryName(path)!, "Tasks"))));
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        // Keep the wide two-pane layout while leaving the desktop notification corner
        // outside the captured application rectangle at the interactive desktop's DPI.
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 1700, 650);
        session.MainWindow.Focus();
        RecordingEvidence? recorder = null;
        Exception? flowError = null;
        var recordingScript = Environment.GetEnvironmentVariable("UNLIMOTION_WINDOW_RECORDER");
        try
        {
            recorder = StartRecording(session, recordingScript, evidenceDirectory,
                "Unlimotion independent documents automation", "workspace-independent-documents-after.mp4");
            WaitUntil(() => FindInMainWindow(session, "WorkspaceRailInProgressButton"),
                element => element is not null && !element.IsOffscreen, "In Progress rail entry is absent.")!.Click();
            var unlocked = WaitUntil(() => FindInMainWindow(session, "WorkspaceRailUnlockedButton"),
                element => element is not null && !element.IsOffscreen, "Unlocked rail entry is absent.")!;
            unlocked.RightClick();
            WaitUntil(() => FindInProcess(session, "Open in adjacent pane"),
                element => element is not null && !element.IsOffscreen, "Rail placement context menu is absent.")!.Click();
            var primary = FindInMainWindow(session, "WorkspacePrimaryPane")!;
            var secondary = WaitUntil(() => FindInMainWindow(session, "WorkspaceSecondaryPane"),
                element => element is not null && !element.IsOffscreen, "Adjacent task list pane is absent.")!;
            var leftSearch = WaitUntil(() => primary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskListSearchBox")),
                element => element is not null && !element.IsOffscreen, "In Progress has no document-local search.")!.AsTextBox();
            var rightSearch = WaitUntil(() => secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskListSearchBox")),
                element => element is not null && !element.IsOffscreen, "Unlocked has no document-local search.")!.AsTextBox();
            leftSearch.Enter("Already finished");
            rightSearch.Enter("Five-minute");
            WaitUntil(() => TaskRowIds(primary),
                ids => ids.SequenceEqual(new[] { "TaskTitle_ux08-finished" }),
                "In Progress did not apply its document-local filter.");
            WaitUntil(() => TaskRowIds(secondary),
                ids => ids.SequenceEqual(new[] { "TaskTitle_ux08-short" }),
                "Unlocked did not apply its independent document-local filter.");
            WaitUntil(() => primary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskTitle_ux08-finished")),
                element => element is not null && !element.IsOffscreen, "In Progress search did not reveal its row.");
            var shortTask = WaitUntil(() => secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskTitle_ux08-short")),
                element => element is not null && !element.IsOffscreen, "Unlocked search did not reveal its independent row.")!;
            if (leftSearch.Text != "Already finished" || rightSearch.Text != "Five-minute")
                throw new InvalidOperationException("Task-list search changed another document's filter.");
            foreach (var pane in new[] { primary, secondary })
                if (pane.FindAllDescendants().Any(element => !element.IsOffscreen &&
                        element.Properties.AutomationId.ValueOrDefault is "MainTabs" or "TaskCardDocument"))
                    throw new InvalidOperationException("Task list still includes nested modes or an embedded task card.");
            Capture(session, evidenceDirectory, "native-two-independent-task-lists.png");
            shortTask.RightClick();
            WaitUntil(() => FindInProcess(session, "Open in new tab"),
                element => element is not null && !element.IsOffscreen, "Task row has no New Tab command.")!.Click();
            WaitUntil(() => secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskCardDocument")),
                element => element is not null && !element.IsOffscreen, "Task did not open a standalone card.");
            var title = secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("CurrentTaskTitleTextBox"))?.AsTextBox();
            if (title?.Text != "UX08 Five-minute action")
                throw new InvalidOperationException("The context-menu task target is not the right-clicked row.");
            var cardControls = secondary.FindAllDescendants();
            if (cardControls.Any(element =>
                    element.Properties.AutomationId.ValueOrDefault?.Contains("Goal", StringComparison.Ordinal) == true))
                throw new InvalidOperationException("Standalone task card still exposes retired goal controls.");
            if (!cardControls.Any(element =>
                    element.Properties.AutomationId.ValueOrDefault == "CurrentTaskClassificationAreaPickerToggle"))
                throw new InvalidOperationException("Removing the goal control also removed task areas.");
            if (secondary.FindAllDescendants().Any(element => !element.IsOffscreen &&
                    element.Properties.AutomationId.ValueOrDefault is "MainTabs" or "TaskListDocument"))
                throw new InvalidOperationException("Standalone task card still contains a nested task list.");
            Capture(session, evidenceDirectory, "native-list-and-standalone-card.png");
        }
        catch (Exception error)
        {
            flowError = error;
            CaptureFailure(session, evidenceDirectory, "native-independent-documents-failure.png", error);
            throw;
        }
        finally
        {
            FinishRecording(session, recorder, flowError);
        }
    }

    private static string[] TaskRowIds(AutomationElement pane) => pane.FindAllDescendants()
        .Select(element => element.Properties.AutomationId.ValueOrDefault)
        .OfType<string>()
        .Where(id => id.StartsWith("TaskTitle_", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    [Test]
    [NotInParallel("DesktopUi")]
    public void WorkspaceTabs_OpenBesideReusesTask_AndHistoryRemainsLocal()
    {
        EnsurePhysicalPixelDpiAwareness();
        var evidenceDirectory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);

        using var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                UnlimotionAutomationScenario.Feed,
                language: "en",
                buildBeforeLaunch: false,
                mainWindowTimeout: TimeSpan.FromSeconds(90)));

        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 1000, 700);
        session.MainWindow.Focus();
        WaitUntil(
            () => FindInMainWindow(session, "WorkspaceRailTasksButton"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "The desktop workspace navigation actions are not accessible.");

        var feedRootButton = FindInMainWindow(session, "WorkspaceRailFeedButton")
            ?? throw new InvalidOperationException("The global Feed route is absent.");
        feedRootButton.Click();
        var link = WaitUntil(
            () => session.MainWindow.FindAllDescendants().FirstOrDefault(element =>
                element.Properties.HelpText.ValueOrDefault ==
                $"unlimotion://task/{UnlimotionAutomationScenarioData.FeedCurrentTaskId}"
                && !element.Properties.IsOffscreen.ValueOrDefault),
            element => element is not null,
            "The Feed task link is unavailable.")!;
        Capture(session, evidenceDirectory, "workspace-before-context-menu.png");
        link.RightClick();
        Capture(session, evidenceDirectory, "workspace-after-right-click.png");
        var openBeside = WaitUntil(
            () => FindInProcess(session, "Open in adjacent pane"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "The Feed link context menu did not expose 'Open in adjacent pane'.")!;
        Capture(session, evidenceDirectory, "workspace-context-menu.png");
        openBeside.Click();
        Thread.Sleep(500);
        Capture(session, evidenceDirectory, "workspace-after-open-beside.png");

        WaitUntil(
            () => FindInMainWindow(session, "WorkspaceSecondaryPane"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "Opening a tab beside did not create the second pane.");
        var primaryPane = FindInMainWindow(session, "WorkspacePrimaryPane")
            ?? throw new InvalidOperationException("The primary workspace pane is absent.");
        var secondary = FindInMainWindow(session, "WorkspaceSecondaryPane")
            ?? throw new InvalidOperationException("The secondary workspace pane is absent.");
        var taskCard = secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskCardDocument"));
        if (taskCard is null || taskCard.Properties.IsOffscreen.ValueOrDefault)
            throw new InvalidOperationException("Adjacent task route did not mount a standalone task card.");
        var nestedModes = secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("MainTabs"));
        var taskList = secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskListDocument"));
        if (nestedModes is not null && !nestedModes.Properties.IsOffscreen.ValueOrDefault ||
            taskList is not null && !taskList.Properties.IsOffscreen.ValueOrDefault)
            throw new InvalidOperationException("Task route still contains legacy task modes or a task list.");
        var back = secondary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("WorkspacePaneBackButton"))
            ?? throw new InvalidOperationException("Pane-local Back is absent.");
        if (back.IsEnabled)
            throw new InvalidOperationException("A new adjacent task tab incorrectly inherits the Feed history.");
        // Feed never navigated away. Reopening its task link must focus the existing
        // card rather than create a second document or mutate the Feed history.
        WaitUntil(
            () => primaryPane.FindFirstDescendant(session.ConditionFactory.ByAutomationId("FeedRoot")),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "Opening the adjacent task unexpectedly replaced the primary Feed.");
        link = WaitUntil(
            () => session.MainWindow.FindAllDescendants().FirstOrDefault(element =>
                element.Properties.HelpText.ValueOrDefault ==
                $"unlimotion://task/{UnlimotionAutomationScenarioData.FeedCurrentTaskId}"
                && !element.Properties.IsOffscreen.ValueOrDefault),
            element => element is not null,
            "The Feed task link disappeared after returning.")!;
        link.Click();
        WaitUntil(
            () => FindInMainWindow(session, "CurrentTaskTitleTextBox"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "Following an already-open task link did not focus its existing document.");
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 1200, 700);
        Capture(session, evidenceDirectory, "workspace-feed-task.png");

        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 520, 400);
        var narrowRail = FindInMainWindow(session, "WorkspaceRailTasksButton");
        if (narrowRail is not null && !narrowRail.Properties.IsOffscreen.ValueOrDefault)
            throw new InvalidOperationException("The compact layout still shows the desktop navigation rail.");
        var paneSelector = WaitUntil(
            () => FindInMainWindow(session, "WorkspaceSecondaryPaneSelector"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "The narrow layout did not expose the second-pane selector.")!;
        paneSelector.Click();
        WaitUntil(
            () => FindInMainWindow(session, "WorkspaceSecondaryPane"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "The narrow pane selector did not reveal the adjacent pane.");
        Capture(session, evidenceDirectory, "workspace-narrow.png");
    }

    [Test, NotInParallel("DesktopUi")]
    public void WorkspaceReview_ShowsSourceBesideDecision_AndNarrowSourceSwitch()
    {
        EnsurePhysicalPixelDpiAwareness();
        var evidenceDirectory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.Feed, language: "en", buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(90)));
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 1200, 800);
        session.MainWindow.Focus();
        var reviewButton = WaitUntil(() => FindInMainWindow(session, "GlobalReviewButton"),
            button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Global review is absent.")!;
        RecordingEvidence? recorder = null;
        Exception? flowError = null;
        var script = Environment.GetEnvironmentVariable("UNLIMOTION_WINDOW_RECORDER");
        try
        {
            recorder = StartRecording(session, script, evidenceDirectory,
                "Unlimotion workspace parity automation", "workspace-review-after.mp4");
            reviewButton.Click();
            WaitUntil(() => FindInMainWindow(session, "WorkspaceReviewSourceButton"),
                button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Review document did not open.");
            var primary = FindInMainWindow(session, "WorkspacePrimaryPane")!;
            WaitUntil(() => primary.FindFirstDescendant(session.ConditionFactory.ByAutomationId("FeedRoot")),
                element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault, "Review source is hidden.");
            WaitUntil(() => FindInMainWindow(session, "FeedReviewConfirmButton"),
                button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Decision is hidden.");
            Capture(session, evidenceDirectory, "workspace-review-wide.png");
        }
        catch (Exception error)
        {
            flowError = error;
            CaptureFailure(session, evidenceDirectory, "native-workspace-review-failure.png", error);
            throw;
        }
        finally
        {
            FinishRecording(session, recorder, flowError);
        }
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 520, 700);
        var sourceButton = WaitUntil(() => FindInMainWindow(session, "WorkspaceReviewSourceButton"),
            button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Source action is inaccessible on narrow screen.")!;
        sourceButton.Click();
        WaitUntil(() => FindInMainWindow(session, "FeedRoot"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault, "Narrow source switch failed.");
        var secondarySelector = FindInMainWindow(session, "WorkspaceSecondaryPaneSelector")!;
        if (secondarySelector.Properties.Name.ValueOrDefault != "Review")
            throw new InvalidOperationException("The narrow selector has no contextual document name.");
        secondarySelector.Click();
        WaitUntil(() => FindInMainWindow(session, "WorkspaceReviewSourceButton"),
            button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Review pane did not become visible.");
        Capture(session, evidenceDirectory, "workspace-review-narrow.png");
        WaitUntil(() => FindInMainWindow(session, "FeedFinishReviewButton"),
            button => button is not null && !button.Properties.IsOffscreen.ValueOrDefault, "Finish review is inaccessible.")!.Click();
        WaitUntil(() => FindInMainWindow(session, "WorkspaceReviewComplete"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault, "Completed review state is absent.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowText(IntPtr window, string title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    private sealed record RecordingEvidence(Process Process, Task<string> StandardOutput, Task<string> StandardError,
        string LogPath, string Metadata);

    private static string WindowTitle(DesktopAppSession session)
    {
        var text = new StringBuilder(1024);
        GetWindowText(new IntPtr(session.MainWindow.Properties.NativeWindowHandle.ValueOrDefault), text, text.Capacity);
        return text.ToString();
    }

    private static RecordingEvidence? StartRecording(DesktopAppSession session, string? script, string? directory,
        string title, string fileName)
    {
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(directory)) return null;
        // The fixture publishes its title after async initialization. A Win32 rename before
        // that publication is overwritten and makes the recorder miss this exact window.
        WaitUntil(() => WindowTitle(session),
            current => current == UnlimotionAutomationScenarioData.FeedWindowTitle,
            "The isolated Feed fixture did not publish its initialized window title.");
        var logPath = Path.Combine(directory, Path.ChangeExtension(fileName, ".recorder.log"));
        var before = WindowTitle(session);
        var handle = new IntPtr(session.MainWindow.Properties.NativeWindowHandle.ValueOrDefault);
        var renamed = SetWindowText(handle, title);
        var metadata = $"startedUtc={DateTime.UtcNow:O}; processId={session.MainWindow.Properties.ProcessId.ValueOrDefault}; " +
            $"handle={handle}; beforeTitle={before}; requestedTitle={title}; renameSucceeded={renamed}; " +
            $"afterRenameTitle={WindowTitle(session)}; script={script}{Environment.NewLine}";
        File.WriteAllText(logPath, metadata);
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-WindowTitle", title, "-Output",
            Path.Combine(directory, fileName), "-DurationSeconds", "20", "-Fps", "15" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the window recorder.");
        // Drain both streams immediately, not after WaitForExit: ffmpeg output can fill a pipe.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Thread.Sleep(1800);
        metadata += $"afterRecorderStartupTitle={WindowTitle(session)}{Environment.NewLine}";
        File.WriteAllText(logPath, metadata);
        return new(process, stdout, stderr, logPath, metadata);
    }

    private static void FinishRecording(DesktopAppSession session, RecordingEvidence? evidence, Exception? flowError)
    {
        if (evidence is null) return;
        try
        {
            var completed = evidence.Process.WaitForExit(40000);
            if (!completed)
            {
                evidence.Process.Kill(entireProcessTree: true);
                evidence.Process.WaitForExit(5000);
            }
            var drains = Task.WhenAll(evidence.StandardOutput, evidence.StandardError);
            var drained = drains.Wait(TimeSpan.FromSeconds(5));
            var exitCode = evidence.Process.HasExited ? evidence.Process.ExitCode.ToString() : "not exited";
            File.WriteAllText(evidence.LogPath, evidence.Metadata +
                $"completed={completed}; exitCode={exitCode}; streamsDrained={drained}; finalWindowTitle={WindowTitle(session)}\n" +
                $"flowError={flowError}\nSTDOUT:\n" +
                (evidence.StandardOutput.IsCompletedSuccessfully ? evidence.StandardOutput.Result : "<stream incomplete>") +
                "\nSTDERR:\n" + (evidence.StandardError.IsCompletedSuccessfully ? evidence.StandardError.Result : "<stream incomplete>"));
            Console.WriteLine("Window recorder diagnostics: " + evidence.LogPath);
            if (!completed || evidence.Process.ExitCode != 0 || !drained)
                throw new InvalidOperationException($"Window recording failed; diagnostics: {evidence.LogPath}.");
        }
        catch (Exception recorderError)
        {
            try
            {
                File.AppendAllText(evidence.LogPath, "\nRECORDER ERROR:\n" + recorderError +
                    "\nSTDOUT AVAILABLE:\n" + (evidence.StandardOutput.IsCompletedSuccessfully ? evidence.StandardOutput.Result : "<stream incomplete>") +
                    "\nSTDERR AVAILABLE:\n" + (evidence.StandardError.IsCompletedSuccessfully ? evidence.StandardError.Result : "<stream incomplete>"));
            }
            catch (Exception logError) { recorderError.Data["RecorderLogError"] = logError.ToString(); }
            CaptureFailure(session, Path.GetDirectoryName(evidence.LogPath), "native-recorder-failure.png", recorderError);
            if (flowError is null) throw;
            // Preserve the original assertion/timeout. Evidence failure must not mask it.
            flowError.Data["WindowRecorderError"] = recorderError.ToString();
            Console.Error.WriteLine("Additional recording error (original UI failure preserved): " + recorderError);
        }
        finally { evidence.Process.Dispose(); }
    }

    private static void CaptureFailure(DesktopAppSession session, string? directory, string name, Exception originalError)
    {
        try { Capture(session, directory, name); }
        catch (Exception captureError)
        {
            originalError.Data["FailureScreenshotError"] = captureError.ToString();
            Console.Error.WriteLine("Failure screenshot error: " + captureError);
        }
    }

    private static AutomationElement? FindInMainWindow(DesktopAppSession session, string automationId) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId));

    private static AutomationElement? FindInProcess(DesktopAppSession session, string name) =>
        session.MainWindow.Automation.GetDesktop().FindFirstDescendant(
            session.ConditionFactory.ByName(name)
                .And(session.ConditionFactory.ByProcessId(session.MainWindow.Properties.ProcessId.ValueOrDefault)));

    private static T WaitUntil<T>(Func<T> observation, Func<T, bool> isReady, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            try
            {
                var value = observation();
                if (isReady(value)) return value;
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or COMException)
            {
                // The app is still publishing or relayouting its accessibility tree.
            }
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException(timeoutMessage);
    }

    private static void Capture(DesktopAppSession session, string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        using var screenshot = session.MainWindow.Capture();
        screenshot.Save(Path.Combine(directory, name));
    }

    private static void EnsurePhysicalPixelDpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4)) &&
            GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 2)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Per-monitor DPI awareness is required for physical-pixel UI evidence.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
}
