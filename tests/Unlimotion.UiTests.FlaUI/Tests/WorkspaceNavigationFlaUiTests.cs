using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class WorkspaceNavigationFlaUiTests
{
    private const string EvidenceDirectoryVariable = "UNLIMOTION_WORKSPACE_NAVIGATION_EVIDENCE_DIR";

    [Test]
    [NotInParallel("DesktopUi")]
    public void WorkspaceTabs_OpenBesideReusesTask_AndGlobalBackReturnsToFeed()
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
            () => FindInProcess(session, "Open beside"),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "The Feed link context menu did not expose 'Open beside'.")!;
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
        var back = FindInMainWindow(session, "WorkspaceGlobalBackButton")
            ?? throw new InvalidOperationException("Global Back is absent.");
        // Return to the Feed, then follow the same task link normally: it must focus
        // the already-open task tab instead of creating another task document.
        back.Click();
        WaitUntil(
            () => primaryPane.FindFirstDescendant(session.ConditionFactory.ByAutomationId("FeedRoot")),
            element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            "Global Back did not return to Feed.");
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
        System.Diagnostics.Process? recorder = null;
        var script = Environment.GetEnvironmentVariable("UNLIMOTION_WINDOW_RECORDER");
        if (!string.IsNullOrWhiteSpace(script) && !string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            const string title = "Unlimotion workspace parity automation";
            SetWindowText(new IntPtr(session.MainWindow.Properties.NativeWindowHandle.ValueOrDefault), title);
            var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-File", script, "-WindowTitle", title, "-Output",
                Path.Combine(evidenceDirectory, "workspace-review-after.mp4"), "-DurationSeconds", "20", "-Fps", "15" })
                start.ArgumentList.Add(argument);
            recorder = System.Diagnostics.Process.Start(start);
            Thread.Sleep(1800);
        }
        try
        {
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
        finally
        {
            if (recorder is not null)
            {
                if (!recorder.WaitForExit(40000) || recorder.ExitCode != 0)
                    throw new InvalidOperationException("Window recording failed; retain screenshots as diagnostics.");
                recorder.Dispose();
            }
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
