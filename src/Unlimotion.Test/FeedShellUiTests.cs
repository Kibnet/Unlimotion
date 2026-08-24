using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedShellUiTests
{
    [Test]
    public async Task Shell_SwitchesBetweenFeedAndTasks_AndPreservesTaskTabContext()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var viewModel = fixture.MainWindowViewModelTest;
                var view = new MainScreen { DataContext = viewModel };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                var feedButton = FindControlByAutomationId<RadioButton>(view, "FeedModeButton");
                var tasksButton = FindControlByAutomationId<RadioButton>(view, "TasksModeButton");
                var feedModeRoot = FindControlByAutomationId<Grid>(view, "FeedModeRoot");
                var tasksModeRoot = FindControlByAutomationId<Grid>(view, "TasksModeRoot");
                var mainTabs = FindControlByAutomationId<TabControl>(view, "MainTabs");

                await Assert.That(viewModel.SelectedWorkspaceMode).IsEqualTo(WorkspaceMode.Tasks);
                await Assert.That(tasksModeRoot.IsVisible).IsTrue();
                await Assert.That(feedModeRoot.IsVisible).IsFalse();

                mainTabs.SelectedIndex = 2;
                RunLayoutJobs();
                feedButton.IsChecked = true;

                var feedOpened = WaitFor(() =>
                    viewModel.IsFeedMode
                    && feedModeRoot.IsVisible
                    && !tasksModeRoot.IsVisible);

                await Assert.That(feedOpened).IsTrue();
                await Assert.That(FindControlByAutomationId<Control>(view, "FeedRoot").IsVisible).IsTrue();

                tasksButton.IsChecked = true;
                var tasksRestored = WaitFor(() =>
                    viewModel.IsTasksMode
                    && tasksModeRoot.IsVisible
                    && !feedModeRoot.IsVisible);

                await Assert.That(tasksRestored).IsTrue();
                await Assert.That(mainTabs.SelectedIndex).IsEqualTo(2);
                await Assert.That(mainTabs.DataContext).IsSameReferenceAs(viewModel);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Shell_DisablingFeedFlag_ReturnsToTasksAndPreservesVaultFiles()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var viewModel = fixture.MainWindowViewModelTest;
                var settings = viewModel.Settings;
                var vaultPath = Path.Combine(fixture.FixtureDirectoryPath, "RollbackVault");
                var dailyPath = Path.Combine(vaultPath, "Daily", "2026-08-24.md");
                const string markdown = "# Existing daily note";
                Directory.CreateDirectory(Path.GetDirectoryName(dailyPath)!);
                await File.WriteAllTextAsync(dailyPath, markdown);
                settings.NoteVaultRootPath = vaultPath;

                var app = Application.Current as App
                    ?? throw new InvalidOperationException("Headless App instance is unavailable.");
                var wireFeed = typeof(App).GetMethod(
                    "WireNoteVaultFeed",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Feed settings wiring method was not found.");
                wireFeed.Invoke(app, [settings, viewModel]);

                var view = new MainScreen { DataContext = viewModel };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                var feedButton = FindControlByAutomationId<RadioButton>(view, "FeedModeButton");
                var feedModeRoot = FindControlByAutomationId<Grid>(view, "FeedModeRoot");
                var tasksModeRoot = FindControlByAutomationId<Grid>(view, "TasksModeRoot");

                feedButton.IsChecked = true;
                await Assert.That(WaitFor(() => viewModel.IsFeedMode)).IsTrue();

                settings.IsFeedEnabled = false;

                var rollbackApplied = WaitFor(() =>
                    viewModel.IsTasksMode
                    && tasksModeRoot.IsVisible
                    && !feedButton.IsVisible
                    && !feedModeRoot.IsVisible);

                await Assert.That(rollbackApplied).IsTrue();
                await Assert.That(settings.NoteVaultRootPath).IsEqualTo(vaultPath);
                await Assert.That(File.Exists(dailyPath)).IsTrue();
                await Assert.That(await File.ReadAllTextAsync(dailyPath)).IsEqualTo(markdown);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => string.Equals(
                AutomationProperties.GetAutomationId(candidate),
                automationId,
                StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static void RunLayoutJobs()
    {
        for (var index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
