using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedShellUiTests
{
    [Test]
    [Arguments(480)]
    [Arguments(720)]
    public async Task SearchPopup_FitsWindowAndKeepsDateControlsReadable(int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var searchVault = Path.Combine(fixture.FixtureDirectoryPath, "SearchVault");
            Directory.CreateDirectory(searchVault);
            await fixture.MainWindowViewModelTest.Feed.InitializeVaultAsync(searchVault);
            var view = new MainScreen { DataContext = fixture.MainWindowViewModelTest };
            var window = new Window { Width = width, Height = 720, Content = view };
            try
            {
                window.Show();
                fixture.MainWindowViewModelTest.Feed.SearchQuery = "искомое";
                RunLayoutJobs();
                var popup = FindControlByAutomationId<Avalonia.Controls.Primitives.Popup>(view, "GlobalSearchFlyout");
                var body = (Border)popup.Child!;
                await Assert.That(body.Bounds.Width).IsLessThanOrEqualTo(width - 24);
                var period = FindControlByAutomationId<DropDownButton>(body, "GlobalSearchPeriodButton");
                var flyout = (Flyout)period.Flyout!;
                flyout.ShowAt(period);
                RunLayoutJobs();
                var dates = ((Control)flyout.Content!).GetVisualDescendants().OfType<CalendarDatePicker>().ToArray();
                await Assert.That(dates.Length).IsEqualTo(2);
                await Assert.That(dates.All(date => date.Bounds.Width >= 200)).IsTrue();
                dates[0].SelectedDate = new DateTime(2026, 9, 1);
                RunLayoutJobs();
                await Assert.That(fixture.MainWindowViewModelTest.Feed.SearchFromDate?.Day).IsEqualTo(1);
                flyout.Hide();
                await Assert.That(popup.IsOpen).IsTrue();
                fixture.MainWindowViewModelTest.IsFeedMode = true;
                RunLayoutJobs();
                await Assert.That(popup.IsOpen).IsFalse();
                await Assert.That(fixture.MainWindowViewModelTest.Feed.SearchQuery).IsEqualTo("искомое");
                var feedFilters = view.GetVisualDescendants().OfType<FeedSearchFiltersControl>()
                    .Single(control => control.IsEffectivelyVisible);
                await Assert.That(feedFilters.Bounds.Width).IsLessThanOrEqualTo(width);
                var feedPeriod = FindControlByAutomationId<DropDownButton>(feedFilters, "GlobalSearchPeriodButton");
                var feedPeriodFlyout = (Flyout)feedPeriod.Flyout!;
                feedPeriodFlyout.ShowAt(feedPeriod);
                RunLayoutJobs();
                var feedDates = ((Control)feedPeriodFlyout.Content!).GetVisualDescendants()
                    .OfType<CalendarDatePicker>().ToArray();
                await Assert.That(feedDates.Length).IsEqualTo(2);
                await Assert.That(feedDates[0].SelectedDate?.Day).IsEqualTo(1);
                feedDates[1].SelectedDate = new DateTime(2026, 9, 4);
                RunLayoutJobs();
                await Assert.That(fixture.MainWindowViewModelTest.Feed.SearchToDate?.Day).IsEqualTo(4);
                ((Control)feedPeriodFlyout.Content!).GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is string)
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();
                await Assert.That(fixture.MainWindowViewModelTest.Feed.SearchFromDate).IsNull();
                await Assert.That(fixture.MainWindowViewModelTest.Feed.SearchToDate).IsNull();
                feedPeriodFlyout.Hide();
                fixture.MainWindowViewModelTest.IsTasksMode = true;
                RunLayoutJobs();
                await Assert.That(popup.IsOpen).IsTrue();
            }
            finally
            {
                window.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

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
                await Assert.That(search is SafeClipboardTextBox).IsTrue();
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
                var taskToggle = FindControlByAutomationId<ToggleSwitch>(view, "GlobalQuickCaptureTaskToggle");
                await Assert.That(taskToggle.OnContent).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("DialogYes"));
                await Assert.That(taskToggle.OffContent).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("DialogNo"));

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
                window = new Window { Width = 400, Height = 700, Content = view };
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
    [Arguments(390)]
    [Arguments(360)]
    public async Task Shell_PhoneWidthsKeepEveryVisibleAppBarControlInsideWindow(int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                owner.Settings.TaskSpaces.Add(new TaskSpaceOptionViewModel
                {
                    SourceId = "phone-layout-space", DisplayName = "Phone layout space", IsActive = true
                });
                owner.Settings.IsFeedEnabled = true;
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = width, Height = 800, Content = view };
                window.Show();
                // Allow responsive reflow and its deferred second layout to settle.
                for (var frame = 0; frame < 4; frame++)
                {
                    await Task.Delay(20);
                    RunLayoutJobs();
                    window.UpdateLayout();
                }

                var appBar = FindControlByAutomationId<Border>(view, "ShellAppBar");
                var appBarGrid = (Grid)appBar.Child!;
                foreach (var control in appBarGrid.Children.Where(control => control.IsEffectivelyVisible))
                    await AssertInsideAppBarAsync(control);
                var search = FindControlByAutomationId<TextBox>(view, "GlobalSearchBox");
                await Assert.That(search.IsEffectivelyVisible).IsTrue();
                await AssertInsideAppBarAsync(search);
                var overflow = FindControlByAutomationId<DropDownButton>(view, "GlobalOverflowMenuButton");
                await Assert.That(overflow.IsEffectivelyVisible).IsTrue();
                await AssertInsideAppBarAsync(overflow);
                await Assert.That(view.GetVisualDescendants().OfType<MainControl>().Any()).IsFalse();

                var flyout = (MenuFlyout)overflow.Flyout!;
                flyout.ShowAt(overflow);
                RunLayoutJobs();
                var feedMenu = FindMenuFlyoutItem(overflow, "GlobalFeedModeMenuItem");
                var tasksMenu = FindMenuFlyoutItem(overflow, "GlobalTasksModeMenuItem");
                var spaceMenu = FindMenuFlyoutItem(overflow, "GlobalTaskSpaceMenuItem");
                var settingsMenu = FindMenuFlyoutItem(overflow, "GlobalSettingsMenuItem");
                await Assert.That(feedMenu.IsEffectivelyVisible && feedMenu.IsEnabled).IsTrue();
                await Assert.That(feedMenu.Items.OfType<MenuItem>().Count()).IsEqualTo(3);
                await Assert.That(tasksMenu.IsEffectivelyVisible && tasksMenu.IsEnabled).IsTrue();
                foreach (var kind in Enum.GetValues<TaskListKind>())
                    await Assert.That(tasksMenu.Items.OfType<MenuItem>().Any(item =>
                        item.Header?.ToString() == WorkspaceLocation.ForTaskList(kind).Title)).IsTrue();
                var space = FindControlByAutomationId<ComboBox>(view, "TaskSpaceSelector");
                if (!space.IsEffectivelyVisible)
                {
                    await Assert.That(spaceMenu.IsEffectivelyVisible && spaceMenu.IsEnabled).IsTrue();
                    await Assert.That(spaceMenu.ItemsSource!.Cast<object>().Any()).IsTrue();
                }
                await Assert.That(settingsMenu.IsEffectivelyVisible && settingsMenu.IsEnabled).IsTrue();
                settingsMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, settingsMenu));
                flyout.Hide();
                RunLayoutJobs();
                await Assert.That(FindControlByAutomationId<Grid>(view, "GlobalSettingsOverlay").IsEffectivelyVisible).IsTrue();

                async Task AssertInsideAppBarAsync(Control control)
                {
                    var inBar = control.TranslatePoint(default, appBar)
                        ?? throw new InvalidOperationException("App-bar control cannot be translated to its parent.");
                    var inWindow = control.TranslatePoint(default, window)
                        ?? throw new InvalidOperationException("App-bar control cannot be translated to the window.");
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0d);
                    await Assert.That(inBar.X).IsGreaterThanOrEqualTo(-0.5d);
                    await Assert.That(inBar.X + control.Bounds.Width).IsLessThanOrEqualTo(appBar.Bounds.Width + 0.5d);
                    await Assert.That(inWindow.X).IsGreaterThanOrEqualTo(-0.5d);
                    await Assert.That(inWindow.X + control.Bounds.Width).IsLessThanOrEqualTo(window.Bounds.Width + 0.5d);
                    await Assert.That(inWindow.Y).IsGreaterThanOrEqualTo(-0.5d);
                    await Assert.That(inWindow.Y + control.Bounds.Height).IsLessThanOrEqualTo(window.Bounds.Height + 0.5d);
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
                window = new Window { Width = 240, Height = 700, Content = view };
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
    public async Task Shell_RootNavigationUsesActiveWorkspaceHistory_AndPreservesTaskTabContext()
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

                var feedButton = FindControlByAutomationId<Button>(view, "WorkspaceRailFeedButton");
                var tasksButton = FindControlByAutomationId<Button>(view, "WorkspaceRailTasksButton");

                await Assert.That(viewModel.SelectedWorkspaceMode).IsEqualTo(WorkspaceMode.Tasks);
                await Assert.That(viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind)
                    .IsEqualTo(WorkspaceLocationKind.Tasks);

                await Assert.That(await viewModel.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList(TaskListKind.LastUpdated))).IsTrue();
                RunLayoutJobs();
                await Assert.That(viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.TaskListKind)
                    .IsEqualTo(TaskListKind.LastUpdated);
                feedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var feedOpened = WaitFor(() =>
                    viewModel.IsFeedMode
                    && viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind == WorkspaceLocationKind.Feed);

                await Assert.That(feedOpened).IsTrue();
                await Assert.That(view.GetVisualDescendants().OfType<FeedControl>()
                    .Any(control => control.IsEffectivelyVisible)).IsTrue();

                var localBack = FindControlByAutomationId<Button>(view, "WorkspacePaneBackButton");
                await Assert.That(localBack.IsEffectivelyVisible).IsTrue();
                await Assert.That(localBack.IsEnabled).IsTrue();
                localBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var tasksRestored = WaitFor(() =>
                    viewModel.IsTasksMode
                    && viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind == WorkspaceLocationKind.Tasks);

                await Assert.That(tasksRestored).IsTrue();
                await Assert.That(viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.TaskListKind)
                    .IsEqualTo(TaskListKind.LastUpdated);
                await Assert.That(view.GetVisualDescendants().OfType<TaskListDocumentView>()
                    .Any(control => control.IsEffectivelyVisible)).IsTrue();
                await Assert.That(view.GetVisualDescendants().OfType<Control>()
                    .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "MainTabs")).IsFalse();

                tasksButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind)
                    .IsEqualTo(WorkspaceLocationKind.Tasks);

                window.Width = 1600;
                RunLayoutJobs();
                var unlockedShortcut = FindControlByAutomationId<Button>(view,
                    "WorkspaceRailUnlockedButton");
                await Assert.That(unlockedShortcut.IsEffectivelyVisible).IsTrue();
                unlockedShortcut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(WaitFor(() => viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.TaskListKind == TaskListKind.Unlocked))
                    .IsTrue();
                await Assert.That(await viewModel.NavigateWorkspaceBackAsync()).IsTrue();
                await Assert.That(viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.TaskListKind).IsEqualTo(TaskListKind.AllTasks);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_ShowsFeedBesideTask_AndNarrowSelectorSwitchesPanes()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var viewModel = fixture.MainWindowViewModelTest;
                await viewModel.Connect();
                viewModel.AllTasksMode = true;
                var view = new MainScreen { DataContext = viewModel };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await viewModel.OpenWorkspaceRootAsync(WorkspaceMode.Feed)).IsTrue();
                var task = TestHelpers.GetTask(viewModel, MainWindowViewModelFixture.RootTask1Id);
                await Assert.That(await viewModel.OpenWorkspaceTaskAsync(
                    task,
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();

                var panes = view.GetVisualDescendants().OfType<WorkspacePaneView>().ToArray();
                await Assert.That(panes.Length).IsEqualTo(2);
                var primary = panes.Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var secondary = panes.Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspaceSecondaryPane");
                var navigationRail = FindControlByAutomationId<Border>(view, "WorkspaceNavigationRail");
                var topModeSelector = FindControlByAutomationId<StackPanel>(view, "ShellModeSelector");
                await Assert.That(navigationRail.IsEffectivelyVisible).IsTrue();
                await Assert.That(navigationRail.Width).IsEqualTo(50);
                await Assert.That(topModeSelector.IsEffectivelyVisible).IsFalse();
                await Assert.That(primary.FeedView.IsEffectivelyVisible).IsTrue();
                await Assert.That(secondary.TasksView.IsEffectivelyVisible).IsTrue();
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(task);

                var secondTask = TestHelpers.GetTask(viewModel, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(await viewModel.OpenWorkspaceTaskAsync(secondTask)).IsTrue();
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(secondTask);
                await Assert.That(await viewModel.NavigateWorkspaceBackAsync()).IsTrue();
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(task);
                await Assert.That(primary.FeedView.IsEffectivelyVisible).IsTrue();

                window.Width = 1600;
                RunLayoutJobs();
                await Assert.That(navigationRail.Width).IsEqualTo(170);
                window.Width = 480;
                RunLayoutJobs();
                await Assert.That(navigationRail.IsEffectivelyVisible).IsFalse();
                await Assert.That(topModeSelector.IsEffectivelyVisible).IsTrue();
                var secondarySelector = FindControlByAutomationId<Button>(view, "WorkspaceSecondaryPaneSelector");
                await Assert.That(secondarySelector.IsEffectivelyVisible).IsTrue();
                var primarySelector = FindControlByAutomationId<Button>(view, "WorkspacePrimaryPaneSelector");
                primarySelector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();
                await Assert.That(primary.FeedView.IsEffectivelyVisible).IsTrue();
                await Assert.That(secondary.TasksView.IsEffectivelyVisible).IsFalse();

                secondarySelector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();
                await Assert.That(primary.FeedView.IsEffectivelyVisible).IsFalse();
                await Assert.That(secondary.TasksView.IsEffectivelyVisible).IsTrue();
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(task);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_TwoTaskPanesKeepTheirOwnCardsWhenFocusChanges()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                owner.AllTasksMode = true;
                var firstTask = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask1Id);
                var secondTask = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceTaskAsync(firstTask)).IsTrue();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(
                    secondTask, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();

                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var secondary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspaceSecondaryPane");
                await Assert.That(primary.TasksView.RouteTaskItem).IsSameReferenceAs(firstTask);
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(secondTask);
                await Assert.That(primary.TasksView is TaskCardView).IsTrue();
                await Assert.That(primary.GetVisualDescendants().OfType<Control>().Any(control =>
                    control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) is
                        "MainTabs" or "TaskListDocument" or "BreadcrumbsTextBlock" or "DetailsPaneToggleButton")).IsFalse();
                var routeFrame = FindControlByAutomationId<Border>(primary.TasksView,
                    "CurrentTaskDetailsPanelFrame");
                await Assert.That(routeFrame.Classes.Contains("WorkspaceTaskRoute")).IsTrue();
                await Assert.That(routeFrame.BorderThickness).IsEqualTo(new Thickness(0));
                var title = FindControlByAutomationId<TextBox>(primary.TasksView,
                    "CurrentTaskTitleTextBox");
                var description = FindControlByAutomationId<TextBox>(primary.TasksView,
                    "CurrentTaskDescriptionTextBox");
                await Assert.That(title.FontSize).IsEqualTo(22);
                await Assert.That(description.BorderThickness).IsEqualTo(new Thickness(0));
                await Assert.That(description.Bounds.Height).IsLessThanOrEqualTo(50);
                await Assert.That(FindControlByAutomationId<TextBlock>(primary.TasksView,
                    "CurrentTaskIdTextBlock").IsEffectivelyVisible).IsFalse();

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.PrimaryPane);
                RunLayoutJobs();
                await Assert.That(primary.TasksView.RouteTaskItem).IsSameReferenceAs(firstTask);
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(secondTask);

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.SecondaryPane!);
                RunLayoutJobs();
                await Assert.That(primary.TasksView.RouteTaskItem).IsSameReferenceAs(firstTask);
                await Assert.That(secondary.TasksView.RouteTaskItem).IsSameReferenceAs(secondTask);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_TaskCategoriesStayWithTheirPaneAndHistory()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList(TaskListKind.InProgress),
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();
                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var secondary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspaceSecondaryPane");
                RunLayoutJobs();
                await Assert.That(secondary.TasksView is TaskListDocumentView).IsTrue();
                await Assert.That(primary.TasksView is TaskListDocumentView).IsTrue();
                await Assert.That(FindControlByAutomationId<TreeView>(secondary.TasksView!, "InProgressTree").IsEffectivelyVisible).IsTrue();
                await Assert.That(FindControlByAutomationId<TreeView>(primary.TasksView!, "AllTasksTree").IsEffectivelyVisible).IsTrue();
                await Assert.That(view.GetVisualDescendants().OfType<Control>().Any(control =>
                    control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) is "MainTabs" or "TaskCardDocument")).IsFalse();
                await Assert.That(owner.InProgressMode).IsTrue();

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.PrimaryPane);
                RunLayoutJobs();
                await Assert.That(owner.AllTasksMode).IsTrue();
                await Assert.That(owner.InProgressMode).IsFalse();
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane!.CurrentLocation?.TaskListKind).IsEqualTo(TaskListKind.InProgress);

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.SecondaryPane!);
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Feed)).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                RunLayoutJobs();
                await Assert.That(FindControlByAutomationId<TreeView>(secondary.TasksView!, "InProgressTree").IsEffectivelyVisible).IsTrue();
                await Assert.That(owner.InProgressMode).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.CurrentLocation?.TaskListKind).IsEqualTo(TaskListKind.AllTasks);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_ClickingAlreadySelectedTaskTitleOpensItsRoute()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var task = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask1Id);
                owner.CurrentTaskItem = task;
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Tasks)).IsTrue();
                RunLayoutJobs();
                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var title = primary.TasksView!.GetVisualDescendants().OfType<Control>()
                    .First(control => AutomationProperties.GetAutomationId(control) == "TaskTitle_" + task.Id
                        && (control.DataContext is TaskItemViewModel item && item.Id == task.Id
                            || control.DataContext is TaskWrapperViewModel wrapper
                            && wrapper.TaskItem.Id == task.Id));
                var point = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)
                    ?? throw new InvalidOperationException("The task title is not visible in the workspace.");
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                RunLayoutJobs();

                await Assert.That(WaitFor(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation is
                    { Kind: WorkspaceLocationKind.Task, Id: var id } && id == task.Id)).IsTrue();
                await Assert.That(primary.TasksView.RouteTaskItem).IsSameReferenceAs(task);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_TwoNotePanesKeepDifferentDocumentsVisible()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "WorkspaceNotes");
                Directory.CreateDirectory(vault);
                await File.WriteAllTextAsync(Path.Combine(vault, "First.md"), "Первая заметка\n");
                await File.WriteAllTextAsync(Path.Combine(vault, "Second.md"), "Вторая заметка\n");
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForNote("First.md", "Первая заметка"))).IsTrue();
                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForNote("Second.md", "Вторая заметка"),
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();

                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var secondary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspaceSecondaryPane");
                await Assert.That(primary.FeedView.IsEffectivelyVisible).IsTrue();
                await Assert.That(secondary.FeedView.IsEffectivelyVisible).IsTrue();
                await Assert.That(primary.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("First.md");
                await Assert.That(secondary.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("Second.md");
                await Assert.That(primary.FeedView.ShowChronology).IsFalse();
                await Assert.That(secondary.FeedView.ShowChronology).IsFalse();

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.PrimaryPane);
                RunLayoutJobs();
                await Assert.That(primary.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("First.md");
                await Assert.That(secondary.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("Second.md");

                var remainingPane = owner.WorkspaceNavigation.SecondaryPane!;
                var remainingTab = remainingPane.ActiveTab!;
                var remainingHistory = remainingTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                FindControlByAutomationId<Button>(primary.FeedView, "FeedThematicFileCloseButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(WaitFor(() => ReferenceEquals(owner.WorkspaceNavigation.PrimaryPane, remainingPane)
                    && !owner.WorkspaceNavigation.HasSecondaryPane)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.ActiveTab).IsSameReferenceAs(remainingTab);
                await Assert.That(remainingTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(remainingHistory)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs).Count()).IsEqualTo(1);
                await Assert.That(remainingTab.CurrentLocation?.Id).IsEqualTo("Second.md");
                await Assert.That(owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
                    .Any(tab => tab.CurrentLocation?.Id == "First.md" || tab.CurrentLocation?.Kind == WorkspaceLocationKind.Tasks)).IsFalse();
                var promotedView = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                await Assert.That(promotedView.FeedView.IsEffectivelyVisible).IsTrue();
                await Assert.That(promotedView.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("Second.md");
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_NoteHistoryRestoresEachEntryScrollPosition()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "WorkspaceScrollNotes");
                Directory.CreateDirectory(vault);
                var longText = string.Join("\n\n", Enumerable.Range(1, 150).Select(i => $"Paragraph {i}"));
                await File.WriteAllTextAsync(Path.Combine(vault, "First.md"), longText);
                await File.WriteAllTextAsync(Path.Combine(vault, "Second.md"), longText);
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1000, Height = 600, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForNote("First.md", "First"))).IsTrue();
                RunLayoutJobs();
                var pane = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == "WorkspacePrimaryPane");
                var scroller = pane.FeedView.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DocumentScroller");
                scroller.Offset = new Vector(0, 120);
                RunLayoutJobs();
                await Assert.That(scroller.Offset.Y).IsGreaterThan(0);

                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForNote("Second.md", "Second"))).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.History[1].Location.ScrollOffset ?? 0)
                    .IsGreaterThan(0);
                RunLayoutJobs();
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                RunLayoutJobs();
                await Assert.That(pane.FeedView.DisplayedDocument?.RelativePath).IsEqualTo("First.md");
                await Assert.That(scroller.Offset.Y).IsGreaterThan(0);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_FeedCannotOpenBesideItself_AndBackRestoresItsPosition()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "WorkspaceDailyNotes");
                var daily = Path.Combine(vault, "Ежедневные");
                Directory.CreateDirectory(daily);
                var latest = new DateOnly(2026, 9, 25);
                for (var index = 0; index < 40; index++)
                    await File.WriteAllTextAsync(Path.Combine(daily, $"{latest.AddDays(-index):yyyy-MM-dd}.md"),
                        $"# День {index}\n\n" + string.Join("\n\n", Enumerable.Range(0, 8)
                            .Select(paragraph => $"Содержимое {index}-{paragraph}")));
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 650, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Feed)).IsTrue();
                RunLayoutJobs();
                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == "WorkspacePrimaryPane");
                var primaryScroll = FindControlByAutomationId<ScrollViewer>(primary.FeedView, "FeedChronologyList");
                var initialLocator = primary.FeedView.CaptureWorkspaceLocation();
                await Assert.That(initialLocator?.Id).IsEqualTo($"Ежедневные/{latest:yyyy-MM-dd}.md");
                await Assert.That(initialLocator?.Anchor).IsNotNull();
                primaryScroll.Offset = new Vector(0, 600);
                RunLayoutJobs();
                await Assert.That(primaryScroll.Offset.Y).IsGreaterThan(0);
                var offsetBeforeAppend = primaryScroll.Offset.Y;
                var loadedBeforeAppend = owner.Feed.Days.Count;
                await Assert.That(owner.Feed.HasMoreDays).IsTrue();
                await owner.Feed.LoadOlderDaysAsync();
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                {
                    RunLayoutJobs();
                    return owner.Feed.Days.Count > loadedBeforeAppend;
                }, TimeSpan.FromSeconds(5))).IsTrue();
                await Assert.That(owner.Feed.Days.Count).IsGreaterThan(loadedBeforeAppend);
                await Assert.That(Math.Abs(primaryScroll.Offset.Y - offsetBeforeAppend)).IsLessThan(0.5);

                var savedOffset = primaryScroll.Offset.Y;
                await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot,
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();
                await Assert.That(owner.WorkspaceNavigation.HasSecondaryPane).IsFalse();
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.ActiveTab?.CurrentLocation?.Id)
                    .IsNotEqualTo("feed");
                await Assert.That(savedOffset).IsGreaterThan(0);
                await Assert.That(primaryScroll.Offset.Y).IsEqualTo(savedOffset);

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.PrimaryPane);
                var captured = owner.WorkspaceNavigation.ActiveTab.CurrentLocation!;
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Tasks)).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                RunLayoutJobs();
                await Assert.That(owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Id).IsEqualTo(captured.Id);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Anchor).IsEqualTo(captured.Anchor);
                await Assert.That(primaryScroll.Offset.Y).IsGreaterThan(0);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_AlreadyOpenRootFocusesExistingPane_WithoutChangingEitherHistory()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 700, Content = view };
                window.Show();
                RunLayoutJobs();

                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.FeedRoot, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                var feedTab = owner.WorkspaceNavigation.ActiveTab;
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Tasks)).IsTrue();
                RunLayoutJobs();

                await Assert.That(owner.WorkspaceNavigation.ActivePane)
                    .IsSameReferenceAs(owner.WorkspaceNavigation.PrimaryPane);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Count).IsEqualTo(1);
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane!.Tabs.Count).IsEqualTo(1);
                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var back = FindControlByAutomationId<Button>(primary, "WorkspacePaneBackButton");
                await Assert.That(back.IsEnabled).IsFalse();
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.ActiveTab!.History.Count).IsEqualTo(1);
                await Assert.That(feedTab.History.Count).IsEqualTo(1);
                await Assert.That(feedTab.CurrentLocation?.Kind).IsEqualTo(WorkspaceLocationKind.Feed);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_AllAndUnlockedPanesKeepGoalAndSearchFiltersSeparate()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 700, Content = view };
                window.Show();
                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList(TaskListKind.Unlocked),
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();

                var primary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var secondary = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspaceSecondaryPane");
                var allFilters = FindControlByAutomationId<DropDownButton>(primary.TasksView,
                    "AllTasksFiltersButton");
                var unlockedFilters = FindControlByAutomationId<DropDownButton>(secondary.TasksView,
                    "UnlockedFiltersButton");
                ((Flyout)allFilters.Flyout!).ShowAt(allFilters);
                ((Flyout)unlockedFilters.Flyout!).ShowAt(unlockedFilters);
                RunLayoutJobs();
                var allGoal = FindControlByAutomationId<ComboBox>(
                    (Control)((Flyout)allFilters.Flyout!).Content!, "AllTasksGoalFilterComboBox");
                var unlockedGoal = FindControlByAutomationId<ComboBox>(
                    (Control)((Flyout)unlockedFilters.Flyout!).Content!, "UnlockedGoalFilterComboBox");
                allGoal.SelectedItem = TaskGoalFilterOption.Find(TaskGoalFilterMode.Goals);
                RunLayoutJobs();
                await Assert.That(owner.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Goals);
                await Assert.That(owner.UnlockedGoalFilterMode).IsEqualTo(TaskGoalFilterMode.All);
                await Assert.That(unlockedGoal.SelectedItem)
                    .IsEqualTo(TaskGoalFilterOption.Find(TaskGoalFilterMode.All));
                unlockedGoal.SelectedItem = TaskGoalFilterOption.Find(TaskGoalFilterMode.Regular);
                RunLayoutJobs();
                await Assert.That(owner.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Goals);
                await Assert.That(owner.UnlockedGoalFilterMode).IsEqualTo(TaskGoalFilterMode.Regular);

                owner.Search.SearchText = "all-only";
                owner.UnlockedSearch.SearchText = "unlocked-only";
                await Assert.That(owner.Search.SearchText).IsEqualTo("all-only");
                await Assert.That(owner.UnlockedSearch.SearchText).IsEqualTo("unlocked-only");

                owner.ActivateWorkspacePane(owner.WorkspaceNavigation.PrimaryPane);
                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList(TaskListKind.LastCreated),
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                RunLayoutJobs();
                var lastCreatedFilters = FindControlByAutomationId<DropDownButton>(secondary.TasksView,
                    "LastCreatedFiltersButton");
                ((Flyout)lastCreatedFilters.Flyout!).ShowAt(lastCreatedFilters);
                RunLayoutJobs();
                var lastCreatedGoal = FindControlByAutomationId<ComboBox>(
                    (Control)((Flyout)lastCreatedFilters.Flyout!).Content!, "LastCreatedGoalFilterComboBox");
                lastCreatedGoal.SelectedItem = TaskGoalFilterOption.Find(TaskGoalFilterMode.Regular);
                RunLayoutJobs();
                await Assert.That(owner.LastCreatedFilter.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Regular);
                await Assert.That(owner.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Goals);
                await Assert.That(owner.UnlockedGoalFilterMode).IsEqualTo(TaskGoalFilterMode.Regular);
                owner.LastCreatedFilter.Search.SearchText = "created-only";
                await Assert.That(owner.Search.SearchText).IsEqualTo("all-only");
                await Assert.That(owner.UnlockedSearch.SearchText).IsEqualTo("unlocked-only");

                ((NotificationManagerWrapperMock)owner.ManagerWrapper).AskResult = true;
                ((Flyout)allFilters.Flyout!).ShowAt(allFilters);
                RunLayoutJobs();
                var allReset = FindControlByAutomationId<Button>(
                    (Control)((Flyout)allFilters.Flyout!).Content!, "AllTasksResetFiltersButton");
                allReset.Command!.Execute(allReset.CommandParameter);
                RunLayoutJobs();
                await Assert.That(owner.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.All);
                await Assert.That(owner.Search.SearchText).IsEmpty();
                await Assert.That(owner.LastCreatedFilter.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Regular);
                await Assert.That(owner.LastCreatedFilter.Search.SearchText).IsEqualTo("created-only");
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceShell_ReopeningFeedReusesItsTabAndKeepsAreaFilter()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "WorkspaceAreaFilters");
                var daily = Path.Combine(vault, "Ежедневные");
                Directory.CreateDirectory(daily);
                await File.WriteAllTextAsync(Path.Combine(daily, "2026-09-25.md"),
                    "Без области\n\n## Работа <!-- unlimotion-area:work -->\nРабочий блок\n");
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 650, Content = view };
                window.Show();
                RunLayoutJobs();
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Feed)).IsTrue();
                await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot,
                    WorkspaceOpenDisposition.NewTab)).IsTrue();
                RunLayoutJobs();

                var panes = view.GetVisualDescendants().OfType<WorkspacePaneView>().ToArray();
                var primary = panes.Single(control => AutomationProperties.GetAutomationId(control) == "WorkspacePrimaryPane");
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Count).IsEqualTo(1);
                await Assert.That(owner.WorkspaceNavigation.HasSecondaryPane).IsFalse();
                var workBlock = owner.Feed.Days.Single().MarkdownEditor.Blocks
                    .Single(block => block.PreviewText == "Рабочий блок");
                bool IsWorkVisible(FeedControl feedView) => feedView.GetVisualDescendants().OfType<Control>()
                    .Any(control => AutomationProperties.GetAutomationId(control) == workBlock.PreviewAutomationId
                        && control.IsEffectivelyVisible);
                await Assert.That(IsWorkVisible(primary.FeedView)).IsTrue();

                var filterButton = FindControlByAutomationId<DropDownButton>(primary.FeedView,
                    "FeedAreaFilterButton");
                var filterFlyout = (Flyout)filterButton.Flyout!;
                filterFlyout.ShowAt(filterButton);
                RunLayoutJobs();
                var options = ((Control)filterFlyout.Content!).GetVisualDescendants().OfType<CheckBox>().ToArray();
                options.Single(option => option.DataContext is Unlimotion.ViewModel.Feed.FeedAreaFilterOptionViewModel
                    { IsAll: true }).IsChecked = false;
                options.Single(option => option.DataContext is Unlimotion.ViewModel.Feed.FeedAreaFilterOptionViewModel
                    { Identity: "" }).IsChecked = true;
                RunLayoutJobs();

                await Assert.That(primary.FeedView.DisplayAreaFilterOptions!
                    .Single(option => option.IsAll).IsSelected).IsFalse();
                await Assert.That(primary.FeedView.DisplayAreaFilterOptions!
                    .Single(option => option.Identity == string.Empty).IsSelected).IsTrue();
                await Assert.That(owner.Feed.Days.Single().MarkdownEditor.Blocks
                    .Single(block => block.PreviewText == "Рабочий блок").IsFeedFilterVisible).IsTrue();
                await Assert.That(IsWorkVisible(primary.FeedView)).IsFalse();

                filterFlyout.Hide();
                await Assert.That(await owner.OpenWorkspaceRootAsync(WorkspaceMode.Tasks)).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                RunLayoutJobs();
                await Assert.That(primary.FeedView.DisplayAreaFilterOptions!
                    .Single(option => option.Identity == string.Empty).IsSelected).IsTrue();
                await Assert.That(IsWorkVisible(primary.FeedView)).IsFalse();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task GlobalSearchOpenActions_AreTouchAccessibleAndCreateNewTaskTab()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            using var directory = new TempNotesDirectory();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                owner.AllTasksMode = true;
                owner.Feed.TaskOwner = owner;
                owner.Feed.TaskResolver = taskId => owner.taskRepository?.Tasks.Items.FirstOrDefault(task =>
                    string.Equals(task.Id, taskId, StringComparison.Ordinal));
                await owner.Feed.InitializeVaultAsync(directory.Path);
                var target = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("The search task fixture is missing.");
                owner.Feed.SearchQuery = target.Id;
                await Assert.That(WaitFor(() => owner.Feed.SearchResults.Count == 1
                    && owner.Feed.SearchResults[0].Type == Unlimotion.Notes.Search.FeedSearchDocumentType.Task))
                    .IsTrue();

                var sourceTab = owner.WorkspaceNavigation.ActiveTab;
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1200, Height = 800, Content = view };
                window.Show();
                RunLayoutJobs();

                var result = owner.Feed.SearchResults[0];
                var popup = FindControlByAutomationId<Avalonia.Controls.Primitives.Popup>(view, "GlobalSearchFlyout");
                await Assert.That(popup.IsOpen).IsTrue();
                var actions = FindControlByAutomationId<DropDownButton>((Control)popup.Child!, result.ActionsAutomationId);
                await Assert.That(actions.Flyout is MenuFlyout).IsTrue();
                var menu = (MenuFlyout)actions.Flyout!;
                menu.ShowAt(actions);
                RunLayoutJobs();
                await Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray())
                    .IsEquivalentTo(new[]
                    {
                        Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenHere"),
                        Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenInNewTab"),
                        Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenBeside")
                    });

                menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString()
                    == Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenInNewTab"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Hide();
                await Assert.That(WaitFor(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind
                    == WorkspaceLocationKind.Task
                    && owner.WorkspaceNavigation.ActiveTab.CurrentLocation.Id == target.Id)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Count).IsEqualTo(2);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Contains(sourceTab)).IsTrue();
                await Assert.That(sourceTab.CurrentLocation?.Kind).IsEqualTo(WorkspaceLocationKind.Tasks);
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

                var feedButton = FindControlByAutomationId<Button>(view, "WorkspaceRailFeedButton");
                feedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(WaitFor(() => viewModel.IsFeedMode)).IsTrue();

                settings.IsFeedEnabled = false;

                var rollbackApplied = WaitFor(() =>
                    viewModel.IsTasksMode
                    && viewModel.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind == WorkspaceLocationKind.Tasks
                    && !feedButton.IsVisible
                    && view.GetVisualDescendants().OfType<FeedControl>()
                        .All(control => !control.IsEffectivelyVisible));

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
