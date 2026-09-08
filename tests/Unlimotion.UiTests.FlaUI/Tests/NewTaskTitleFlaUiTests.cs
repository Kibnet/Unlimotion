using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        const string storageRefreshMarker = "Storage refresh completed after title entry";
        var evidenceDirectory = Environment.GetEnvironmentVariable("UNLIMOTION_NEW_TASK_TITLE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
        }

        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.Smoke,
            buildBeforeLaunch: false,
            mainWindowTimeout: TimeSpan.FromSeconds(90));
        var configPath = options.Arguments.Single(argument => argument.StartsWith("--config=", StringComparison.Ordinal))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        var taskStoragePath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var taskFilesBeforeCreation = Directory.EnumerateFiles(taskStoragePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        var newTaskPath = await WaitUntil(
            () => FindCreatedTaskFile(taskStoragePath, taskFilesBeforeCreation),
            path => path is not null && ReadTask(path) is not null,
            "The new task file was not created.");
        titleEditor!.AsTextBox().Enter(title);

        await WaitUntil(
            () => FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text,
            text => string.Equals(text, title, StringComparison.Ordinal),
            "The new task title was not applied before the storage refresh.");

        // Simulate the stale file snapshot that arrives after editing a new task.
        // The description marker is observable proof that the watcher refresh completed after Enter(title).
        WriteStaleTaskSnapshot(newTaskPath!, storageRefreshMarker);
        await WaitUntil(
            () => FindInMainWindow(session, "CurrentTaskDescriptionTextBox")?.AsTextBox().Text,
            text => string.Equals(text, storageRefreshMarker, StringComparison.Ordinal),
            "The externally written task snapshot was not applied by the storage watcher.");

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

    private static string? FindCreatedTaskFile(string taskStoragePath, ISet<string> taskFilesBeforeCreation) =>
        Directory.EnumerateFiles(taskStoragePath)
            .FirstOrDefault(path => !taskFilesBeforeCreation.Contains(path));

    private static JsonNode? ReadTask(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteStaleTaskSnapshot(string taskPath, string storageRefreshMarker)
    {
        var task = ReadTask(taskPath) as JsonObject
            ?? throw new InvalidOperationException("The new task file could not be read before the storage refresh.");
        task["Title"] = string.Empty;
        task["Description"] = storageRefreshMarker;
        File.WriteAllText(taskPath, task.ToJsonString());
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
