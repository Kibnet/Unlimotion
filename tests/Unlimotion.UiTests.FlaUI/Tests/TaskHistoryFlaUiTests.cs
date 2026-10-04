using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LibGit2Sharp;
using System.Text.Json.Nodes;
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

        CreateHistory(taskStoragePath, currentTaskPath);

        using var session = DesktopAppSession.Launch(options);
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        session.MainWindow.Focus();

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
        var evidenceDirectory = Path.Combine(
            Directory.GetCurrentDirectory(), "artifacts", "ui-evidence", "task-history");
        Directory.CreateDirectory(evidenceDirectory);
        using (var historyScreenshot = session.MainWindow.Capture())
            historyScreenshot.Save(Path.Combine(evidenceDirectory, "history-list.png"));
        showDetails.AsButton().Invoke();
        var detailPanel = await WaitUntil(
            () => session.MainWindow.FindAllDescendants(session.ConditionFactory.ByAutomationId("TaskHistoryDetailsPanel"))
                .FirstOrDefault(element => !element.IsOffscreen),
            element => element is { IsOffscreen: false },
            "The full value did not become visible after opening details.");
        await Assert.That(detailPanel).IsNotNull();
        await Assert.That(detailPanel!.FindAllDescendants()
            .Any(element => element.Properties.Name.ValueOrDefault == new string('h', 460))).IsTrue();
        using var screenshot = session.MainWindow.Capture();
        screenshot.Save(Path.Combine(evidenceDirectory, "full-value.png"));
        var collapse = detailPanel.FindFirstDescendant(session.ConditionFactory.ByAutomationId("TaskHistoryCloseDetailsButton"));
        await Assert.That(collapse).IsNotNull();
        collapse!.AsButton().Invoke();
        await WaitUntil(
            () => FindText(historyItems, new string('h', 460)),
            element => element is null || element.IsOffscreen,
            "The full text remained visible after collapsing the row.");
    }

    private static void CreateHistory(string taskStoragePath, string currentTaskPath)
    {
        Repository.Init(taskStoragePath);
        using var repository = new Repository(taskStoragePath);
        var signature = new Signature(
            "Unlimotion UI Test",
            "ui-test@unlimotion.local",
            DateTimeOffset.UtcNow);

        var original = JsonNode.Parse(File.ReadAllText(currentTaskPath))!.AsObject();
        original.Remove("PlannedDuration");
        File.WriteAllText(currentTaskPath, original.ToJsonString());

        Commands.Stage(repository, "*");
        repository.Commit("Initial task snapshot", signature, signature);

        var task = JsonNode.Parse(File.ReadAllText(currentTaskPath))!.AsObject();
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
