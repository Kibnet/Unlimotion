using AppAutomation.FlaUI.Session;
using AppAutomation.FlaUI.Input;
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
        string? newTaskPath = null;
        string? newTaskId = null;
        try
        {
            var originalTitle = await WaitUntil(
                () => FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text,
                text => !string.IsNullOrEmpty(text),
                "The initial smoke card did not expose its nonempty title.");
            await Assert.That(originalTitle).IsNotEqualTo(string.Empty);
            DesktopPointer.Click(FindInMainWindow(session, "GlobalCreateMenuButton")
                ?? throw new InvalidOperationException("The global task-create menu button was not exposed."));
            var createTask = await WaitUntil(
                () => FindInProcess(session, "GlobalTaskCreateTaskMenuItem"),
                element => element is not null,
                "The global task-create menu did not expose the new-task action.");
            DesktopPointer.Click(createTask!);

            var createdTask = await WaitUntil(
                () => FindCreatedTaskFile(taskStoragePath, taskFilesBeforeCreation),
                task => task is not null,
                "The new task file was not created.");
            newTaskPath = createdTask!.Path;
            newTaskId = createdTask.Id;
            // The old smoke card already exposes this editor ID while Create awaits storage.
            // Its nonempty title distinguishes it from the freshly mounted new task editor.
            // Workspace cards intentionally hide CurrentTaskIdTextBlock, so it is not a UIA gate.
            var titleEditor = await WaitUntil(
                () => FindInMainWindow(session, "CurrentTaskTitleTextBox"),
                element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault
                    && element.BoundingRectangle.Width > 0 && element.BoundingRectangle.Height > 0
                    && string.IsNullOrEmpty(element.AsTextBox().Text),
                "The title editor for the exact newly created task did not become available.");
            var initialSnapshot = await WaitUntil(() => ReadTask(newTaskPath),
                task => task?["Id"]?.GetValue<string>() == newTaskId,
                "The canonical new task snapshot did not match the new card.");
            await Assert.That(initialSnapshot!["Title"]?.GetValue<string>() ?? string.Empty).IsEqualTo(string.Empty);
            titleEditor!.AsTextBox().Enter(title);

            await WaitUntil(
                () => FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text,
                text => string.Equals(text, title, StringComparison.Ordinal),
                "The new task title was not applied before the storage refresh.");

            // Simulate the stale file snapshot that arrives after editing a new task.
            // The description marker is observable proof that the watcher refresh completed after Enter(title).
            WriteStaleTaskSnapshot(newTaskPath!, newTaskId, storageRefreshMarker);
            await WaitUntil(
                () => FindInMainWindow(session, "CurrentTaskDescriptionTextBox")?.AsTextBox().Text,
                text => string.Equals(text, storageRefreshMarker, StringComparison.Ordinal),
                "The externally written task snapshot was not applied by the storage watcher.");

            var refreshedTitleEditor = FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox();
            Capture(session, evidenceDirectory, "new-task-title-after-refresh.png");

            await Assert.That(refreshedTitleEditor).IsNotNull();
            var refreshedSnapshot = await WaitUntil(() => ReadTask(newTaskPath),
                task => task?["Id"]?.GetValue<string>() == newTaskId,
                "The refreshed canonical file no longer matches the newly created task.");
            await Assert.That(refreshedSnapshot!["Id"]!.GetValue<string>()).IsEqualTo(newTaskId);
            await Assert.That(refreshedTitleEditor!.Text).IsEqualTo(title);
        }
        catch
        {
            DumpFailure(session, taskStoragePath, taskFilesBeforeCreation, newTaskPath, newTaskId);
            try { Capture(session, evidenceDirectory, "new-task-title-failure.png"); }
            catch (Exception exception) { Console.Error.WriteLine($"Failure screenshot unavailable: {exception.Message}"); }
            throw;
        }
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

    private sealed record CreatedTaskSnapshot(string Path, string Id);

    private static CreatedTaskSnapshot? FindCreatedTaskFile(string taskStoragePath, ISet<string> taskFilesBeforeCreation)
    {
        foreach (var path in Directory.EnumerateFiles(taskStoragePath)
                     .Where(path => !taskFilesBeforeCreation.Contains(path) && IsCanonicalTaskFile(path)))
        {
            if (ReadTask(path)?["Id"]?.GetValue<string>() is { Length: > 0 } id)
                return new CreatedTaskSnapshot(path, id);
        }
        return null;
    }

    private static bool IsCanonicalTaskFile(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        return !name.StartsWith('.') && (extension.Length == 0
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static void DumpFailure(DesktopAppSession session, string storagePath, ISet<string> before,
        string? targetPath, string? targetId)
    {
        try
        {
            Console.Error.WriteLine($"New-task refresh failure: targetId='{targetId}'; targetPath='{targetPath}'; " +
                $"cardId='{FindInMainWindow(session, "CurrentTaskIdTextBlock")?.Properties.Name.ValueOrDefault}'; " +
                $"UI title='{FindInMainWindow(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text}'; " +
                $"UI description='{FindInMainWindow(session, "CurrentTaskDescriptionTextBox")?.AsTextBox().Text}'.");
            foreach (var path in Directory.EnumerateFiles(storagePath).Where(path => !before.Contains(path)).Take(20))
            {
                var task = ReadTask(path);
                Console.Error.WriteLine($"New file: '{Path.GetFileName(path)}'; canonical={IsCanonicalTaskFile(path)}; " +
                    $"Id={task?["Id"]}; Title={task?["Title"]}; Description={task?["Description"]}.");
            }
        }
        catch (Exception exception) { Console.Error.WriteLine($"New-task diagnostics unavailable: {exception.Message}"); }
    }

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

    private static void WriteStaleTaskSnapshot(string taskPath, string expectedId, string storageRefreshMarker)
    {
        var task = ReadTask(taskPath) as JsonObject
            ?? throw new InvalidOperationException("The new task file could not be read before the storage refresh.");
        if (task["Id"]?.GetValue<string>() != expectedId)
            throw new InvalidOperationException("The stale snapshot target no longer matches the new task card.");
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
