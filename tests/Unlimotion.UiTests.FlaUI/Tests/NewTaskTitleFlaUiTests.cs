using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class NewTaskTitleFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NewTaskTitle_SurvivesTheDelayedStorageRefresh()
    {
        const string title = "Title retained after storage refresh";
        var evidenceDirectory = Environment.GetEnvironmentVariable("UNLIMOTION_NEW_TASK_TITLE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
        }

        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.Smoke,
            buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(90));
        using var session = DesktopAppSession.Launch(options);
        session.MainWindow.Focus();

        (FindInMainWindow(session, "GlobalTaskCreateMenuButton")
            ?? throw new InvalidOperationException("The global task-create menu button was not exposed."))
            .Click();
        var createTask = await WaitUntil(
            () => FindInProcess(session, "GlobalTaskCreateTaskMenuItem"),
            element => element is not null,
            "The global task-create menu did not expose the new-task action.");
        createTask!.Click();

        var titleEditor = await WaitUntil(
            () => FindInMainWindow(session, "CurrentTaskTitleTextBox"),
            element => element is not null,
            "The new task title editor did not become available.");
        titleEditor!.AsTextBox().Enter(title);

        // The file watcher is debounced by one second; this stays deliberately below the ten-second autosave.
        await Task.Delay(TimeSpan.FromSeconds(3));
        var refreshedTitleEditor = FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox();
        Capture(session, evidenceDirectory, "new-task-title-after-refresh.png");

        await Assert.That(refreshedTitleEditor).IsNotNull();
        await Assert.That(refreshedTitleEditor!.Text).IsEqualTo(title);
    }

    private static AutomationElement? FindInMainWindow(DesktopAppSession session, string automationId) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId));

    private static AutomationElement? FindInProcess(DesktopAppSession session, string automationId)
    {
        var processId = session.MainWindow.Properties.ProcessId.ValueOrDefault;
        return session.MainWindow.Automation.GetDesktop().FindFirstDescendant(
            session.ConditionFactory.ByAutomationId(automationId)
                .And(session.ConditionFactory.ByProcessId(processId)));
    }

    private static async Task<T> WaitUntil<T>(
        Func<T> observation,
        Func<T, bool> isReady,
        string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
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

    private static void Capture(DesktopAppSession session, string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        using var screenshot = session.MainWindow.Capture();
        screenshot.Save(Path.Combine(directory, name));
    }
}
