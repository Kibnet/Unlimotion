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
    public async Task Shell_GlobalActions_AreAvailableFromBothModesAndUseOverlays()
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

                var appBar = FindControlByAutomationId<Border>(view, "ShellAppBar");
                var create = FindControlByAutomationId<Button>(view, "GlobalCreateMenuButton");
                var taskSpaceSelector = FindControlByAutomationId<ComboBox>(view, "TaskSpaceSelector");
                var search = FindControlByAutomationId<TextBox>(view, "GlobalSearchBox");
                var settingsButton = FindControlByAutomationId<Button>(view, "GlobalSettingsButton");
                var quickOverlay = FindControlByAutomationId<Grid>(view, "GlobalQuickCaptureOverlay");
                var settingsOverlay = FindControlByAutomationId<Grid>(view, "GlobalSettingsOverlay");
                var reviewOverlay = FindControlByAutomationId<Grid>(view, "GlobalReviewOverlay");

                using (Assert.Multiple())
                {
                    await Assert.That(appBar.IsEffectivelyVisible).IsTrue();
                    await Assert.That(create.IsEffectivelyVisible).IsTrue();
                    await Assert.That(create.Bounds.Width).IsEqualTo(42);
                    await Assert.That(create.Bounds.Height).IsEqualTo(42);
                    await Assert.That(create.Content).IsEqualTo("➕");
                    await Assert.That(create.BorderThickness).IsEqualTo(new Thickness(1));
                    await Assert.That(taskSpaceSelector.IsEffectivelyVisible).IsTrue();
                    await Assert.That(search.IsEffectivelyVisible).IsTrue();
                    await Assert.That(settingsButton.IsEffectivelyVisible).IsTrue();
                    await Assert.That(quickOverlay.IsEffectivelyVisible).IsFalse();
                    await Assert.That(settingsOverlay.IsEffectivelyVisible).IsFalse();
                    await Assert.That(quickOverlay.GetValue(Panel.ZIndexProperty))
                        .IsGreaterThan(reviewOverlay.GetValue(Panel.ZIndexProperty));
                }

                viewModel.OpenQuickCapture(isTask: false);
                RunLayoutJobs();
                await Assert.That(quickOverlay.IsEffectivelyVisible).IsTrue();

                viewModel.OpenSettings();
                RunLayoutJobs();
                using (Assert.Multiple())
                {
                    await Assert.That(quickOverlay.IsEffectivelyVisible).IsFalse();
                    await Assert.That(settingsOverlay.IsEffectivelyVisible).IsTrue();
                }

                viewModel.IsFeedMode = true;
                viewModel.CloseSettings();
                viewModel.OpenQuickCapture(isTask: true);
                RunLayoutJobs();
                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.IsFeedMode).IsTrue();
                    await Assert.That(viewModel.IsQuickCaptureTask).IsTrue();
                    await Assert.That(quickOverlay.IsEffectivelyVisible).IsTrue();
                    await Assert.That(taskSpaceSelector.IsEffectivelyVisible).IsTrue();
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Shell_CompactWidthMovesSearchBelowGlobalControlsWithoutOverlap()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var view = new MainScreen { DataContext = fixture.MainWindowViewModelTest };
                window = new Window { Width = 760, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                var search = FindControlByAutomationId<TextBox>(view, "GlobalSearchBox");
                var modeSelector = FindControlByAutomationId<StackPanel>(view, "ShellModeSelector");
                var reviewButton = FindControlByAutomationId<Button>(view, "GlobalReviewButton");
                var searchTop = search.TranslatePoint(default, view)!.Value.Y;
                var controlsBottom = Math.Max(
                    modeSelector.TranslatePoint(default, view)!.Value.Y + modeSelector.Bounds.Height,
                    reviewButton.TranslatePoint(default, view)!.Value.Y + reviewButton.Bounds.Height);

                using (Assert.Multiple())
                {
                    await Assert.That(Grid.GetRow(search.Parent as Control)).IsEqualTo(1);
                    await Assert.That(searchTop).IsGreaterThanOrEqualTo(controlsBottom);
                    await Assert.That(search.Bounds.Width).IsGreaterThan(300);
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Shell_NarrowWidthMovesNonFittingActionsIntoOverflow()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var view = new MainScreen { DataContext = fixture.MainWindowViewModelTest };
                window = new Window { Width = 500, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                var search = FindControlByAutomationId<TextBox>(view, "GlobalSearchBox");
                var review = FindControlByAutomationId<Button>(view, "GlobalReviewButton");
                var settings = FindControlByAutomationId<Button>(view, "GlobalSettingsButton");
                var overflow = FindControlByAutomationId<DropDownButton>(view, "GlobalOverflowMenuButton");
                var reviewMenu = FindMenuFlyoutItem(overflow, "GlobalReviewMenuItem");
                var settingsMenu = FindMenuFlyoutItem(overflow, "GlobalSettingsMenuItem");

                using (Assert.Multiple())
                {
                    await Assert.That(Grid.GetRow(search.Parent as Control)).IsEqualTo(1);
                    await Assert.That(review.IsEffectivelyVisible).IsFalse();
                    await Assert.That(settings.IsEffectivelyVisible).IsFalse();
                    await Assert.That(overflow.IsEffectivelyVisible).IsTrue();
                    await Assert.That(reviewMenu.IsVisible).IsTrue();
                    await Assert.That(settingsMenu.IsVisible).IsTrue();
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Shell_RecalculatesOverflowWhenReviewCounterGrowsWithoutWindowResize()
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
                window = new Window { Width = 720, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();
                var review = FindControlByAutomationId<Button>(view, "GlobalReviewButton");
                var settings = FindControlByAutomationId<Button>(view, "GlobalSettingsButton");
                var pendingReview = viewModel.Feed.GetType().GetProperty(
                    "PendingReviewBlocks",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("PendingReviewBlocks property was not found.");

                for (var width = 720; width >= 520; width -= 10)
                {
                    window.Width = width;
                    RunLayoutJobs();
                    if (review.IsEffectivelyVisible && settings.IsEffectivelyVisible)
                    {
                        continue;
                    }

                    window.Width = width + 10;
                    RunLayoutJobs();
                    break;
                }

                await Assert.That(review.IsEffectivelyVisible).IsTrue();
                await Assert.That(settings.IsEffectivelyVisible).IsTrue();
                var stableWidth = window.Width;

                pendingReview.SetValue(viewModel.Feed, int.MaxValue);

                await Assert.That(WaitFor(() =>
                    !review.IsEffectivelyVisible || !settings.IsEffectivelyVisible)).IsTrue();
                await Assert.That(window.Width).IsEqualTo(stableWidth);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Shell_ExtremeWidthKeepsSpaceAndModeAvailableInOverflow()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var view = new MainScreen { DataContext = fixture.MainWindowViewModelTest };
                window = new Window { Width = 320, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                var space = FindControlByAutomationId<ComboBox>(view, "TaskSpaceSelector");
                var mode = FindControlByAutomationId<StackPanel>(view, "ShellModeSelector");
                var overflow = FindControlByAutomationId<DropDownButton>(view, "GlobalOverflowMenuButton");
                var spaceMenu = FindMenuFlyoutItem(overflow, "GlobalTaskSpaceMenuItem");
                var feedModeMenu = FindMenuFlyoutItem(overflow, "GlobalFeedModeMenuItem");
                var tasksModeMenu = FindMenuFlyoutItem(overflow, "GlobalTasksModeMenuItem");

                using (Assert.Multiple())
                {
                    await Assert.That(space.IsEffectivelyVisible).IsFalse();
                    await Assert.That(mode.IsEffectivelyVisible).IsFalse();
                    await Assert.That(overflow.IsEffectivelyVisible).IsTrue();
                    await Assert.That(spaceMenu.IsVisible).IsTrue();
                    await Assert.That(feedModeMenu.IsVisible).IsTrue();
                    await Assert.That(tasksModeMenu.IsVisible).IsTrue();
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

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

    private static MenuItem FindMenuFlyoutItem(DropDownButton button, string automationId)
    {
        if (button.Flyout is not MenuFlyout flyout)
        {
            throw new InvalidOperationException("The shell overflow flyout is unavailable.");
        }

        return flyout.Items
                   .OfType<MenuItem>()
                   .FirstOrDefault(item => string.Equals(
                       AutomationProperties.GetAutomationId(item),
                       automationId,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Menu item with AutomationId '{automationId}' was not found.");
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
