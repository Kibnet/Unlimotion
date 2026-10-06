using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Notes.Operations;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed partial class MainWindowHeadlessTests
{
    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task TaskViews_AreIndependentDocuments()
    {
        RequireRenderedStoryMode();
        HeadlessRuntime.Dispatch(() =>
        {
            Session.Inner.MainWindow.Width = 1600;
            Session.Inner.MainWindow.Height = 800;
            Session.Inner.MainWindow.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        await CaptureStoryScreenshotAsync("independent-task-view-launch.png", 1600);
        var category = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                TryFindNativeControlByAutomationId<Button>("WorkspaceRailInProgressButton")),
            button => button is { IsEffectivelyVisible: true }, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Expanded navigation did not expose In Progress. Inspect the launch PNG.")!;
        InvokeNativeButton(category);
        WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                GetHeadlessMainWindowViewModel().WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.TaskListKind),
            kind => kind == TaskListKind.InProgress,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The In Progress rail action did not open its task view document.");
        await CaptureStoryScreenshotAsync("independent-task-view-after.png");

        var visibleLegacyControls = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow
            .GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsVisible && control.GetVisualAncestors().OfType<Control>()
                .All(parent => parent.IsVisible))
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => id is "MainTabs" or "CurrentTaskCard")
            .Distinct().ToArray());
        await Assert.That(visibleLegacyControls).IsEmpty();
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    [Arguments("WorkspaceRailAllTasksButton", "AllTasksTree")]
    [Arguments("WorkspaceRailLastCreatedButton", "LastCreatedTree")]
    [Arguments("WorkspaceRailLastUpdatedButton", "LastUpdatedTree")]
    [Arguments("WorkspaceRailUnlockedButton", "UnlockedTree")]
    [Arguments("WorkspaceRailInProgressButton", "InProgressTree")]
    [Arguments("WorkspaceRailCompletedButton", "CompletedTree")]
    [Arguments("WorkspaceRailArchivedButton", "ArchivedTree")]
    [Arguments("WorkspaceRailLastOpenedButton", "LastOpenedTree")]
    [Arguments("WorkspaceRailRoadmapButton", "RoadmapRoot")]
    public async Task TaskViews_AllNineAreStandalone(string railId, string contentId)
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("all-views-launch.png", 1600);
        var rail = WaitUntil(() => HeadlessRuntime.Dispatch(() => TryFindNativeControlByAutomationId<Button>(railId)),
            button => button is { IsEffectivelyVisible: true }, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Navigation does not expose {railId}.")!;
        var openingLabels = HeadlessRuntime.Dispatch(() => rail.ContextMenu?.Items.OfType<MenuItem>()
            .Select(item => item.Header?.ToString()).ToArray() ?? []);
        await Assert.That(openingLabels).Contains(Localization.Get("WorkspaceOpenHere"));
        await Assert.That(openingLabels).Contains(Localization.Get("WorkspaceOpenInNewTab"));
        await Assert.That(openingLabels).Contains(Localization.Get("WorkspaceOpenBeside"));
        InvokeNativeButton(rail);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Control>()
                .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == contentId)),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: $"Standalone document did not render {contentId}.");
        await AssertNoLegacyTaskCompositionAsync(expectCard: false);
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.ActiveTab
            .CurrentLocation!.Title)).IsNotEqualTo(Localization.Get("TasksMode"));
        await CaptureStoryScreenshotAsync($"view-{contentId}.png");
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX20_TwoTaskViewsHaveIndependentSearchAndHistory()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("independent-views-launch.png", 1600)
            .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>("WorkspaceRailInProgressButton")));
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        var leftTabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        var leftHistoryCount = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.History.Count);
        var unlockedRail = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>("WorkspaceRailUnlockedButton"));
        InvokeOpenTargetMenu(unlockedRail, "WorkspaceOpenBeside");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.HasSecondaryPane),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Unlocked did not open beside In Progress.");
        var left = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Control>("WorkspacePrimaryPane"));
        var right = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Control>("WorkspaceSecondaryPane"));
        var searches = WaitUntil(() => HeadlessRuntime.Dispatch(() => new[] { left, right }.Select(pane => pane
                .GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control => control.IsEffectivelyVisible &&
                    AutomationProperties.GetAutomationId(control) == "TaskListSearchBox")).ToArray()),
            controls => controls.All(control => control is not null), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Each list must expose its own search box.");
        HeadlessRuntime.Dispatch(() =>
        {
            searches[0]!.Focus(); searches[0]!.Text = "Already finished";
            searches[1]!.Focus(); searches[1]!.Text = "Five-minute";
            Dispatcher.UIThread.RunJobs();
        });
        await Assert.That(HeadlessRuntime.Dispatch(() => searches[0]!.Text)).IsEqualTo("Already finished");
        await Assert.That(HeadlessRuntime.Dispatch(() => searches[1]!.Text)).IsEqualTo("Five-minute");
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            var matches = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var rows = await RunParityUiAsync(async () =>
                {
                    await Task.Yield();
                    Dispatcher.UIThread.RunJobs();
                    Session.Inner.MainWindow.UpdateLayout();
                    return new[] { left, right }.Select(pane => pane.GetVisualDescendants()
                        .OfType<TreeView>().Where(tree => tree.IsEffectivelyVisible)
                        .SelectMany(tree => tree.ItemsSource?.OfType<Unlimotion.ViewModel.TaskWrapperViewModel>()
                            .SelectMany(FlattenStoryTask) ?? Enumerable.Empty<Unlimotion.ViewModel.TaskItemViewModel>())
                        .Select(task => task.Id).Distinct().ToArray()).ToArray();
                }).ConfigureAwait(false);
                matches = rows[0].Contains("ux08-finished") && !rows[0].Contains("ux08-active") &&
                    rows[1].Contains("ux08-short") && !rows[1].Contains("ux08-long");
                if (matches) break;
                await Task.Delay(50).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }
            if (!matches) throw new TimeoutException("Search values did not independently filter the two visible task trees.");
        }
        catch (TimeoutException)
        {
            var diagnostics = await RunParityUiAsync(async () =>
            {
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                var rows = new[] { left, right }.Select(pane => pane.GetVisualDescendants()
                    .OfType<TreeView>().Where(tree => tree.IsEffectivelyVisible)
                    .SelectMany(tree => tree.ItemsSource?.OfType<Unlimotion.ViewModel.TaskWrapperViewModel>()
                        .SelectMany(FlattenStoryTask) ?? Enumerable.Empty<Unlimotion.ViewModel.TaskItemViewModel>())
                    .Select(task => task.Id).Distinct().ToArray()).ToArray();
                return $"Independent search diagnostics: leftInput={searches[0]!.Text}; rightInput={searches[1]!.Text}; " +
                    $"InProgressSearch={owner.InProgressFilter.Search.SearchText}; UnlockedSearch={owner.UnlockedSearch.SearchText}\n" +
                    "Visible row IDs: " + string.Join(" | ", rows.Select(ids => string.Join(",", ids)));
            }).ConfigureAwait(false);
            Console.WriteLine(diagnostics);
            await CaptureStoryScreenshotAsync("independent-search-failure.png", 1280)
                .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            throw;
        }
        // Dispatch completion can inline a continuation on Headless's UI thread.
        // Complete through the default scheduler, and snapshot all final UI state
        // asynchronously instead of issuing a blocking Dispatch after the poll.
        var finalState = await RunParityUiAsync(async () =>
        {
            await Task.Yield();
            return (TabId: owner.WorkspaceNavigation.PrimaryPane.ActiveTab!.Id,
                HistoryCount: owner.WorkspaceNavigation.PrimaryPane.ActiveTab!.History.Count,
                VisibleIds: Session.Inner.MainWindow.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible).Select(AutomationProperties.GetAutomationId).ToArray());
        }).ConfigureAwait(false);
        await Assert.That(finalState.TabId).IsEqualTo(leftTabId);
        await Assert.That(finalState.HistoryCount).IsEqualTo(leftHistoryCount);
        await Assert.That(finalState.VisibleIds).DoesNotContain("MainTabs");
        await Assert.That(finalState.VisibleIds).Contains("TaskListDocument");
        await Assert.That(finalState.VisibleIds).DoesNotContain("TaskCardDocument");
        await Assert.That(finalState.VisibleIds).DoesNotContain("CurrentTaskCard");
        await CaptureStoryScreenshotAsync("two-independent-lists-1280.png")
            .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await CaptureStoryScreenshotAsync("two-independent-lists-640.png", 640)
            .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await CaptureStoryScreenshotAsync("two-independent-lists-390.png", 390)
            .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX21_ListTaskBackIsCardOnlyAndRestoresList()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("list-card-launch.png", 1600);
        SelectStoryTaskFromTree("AllTasksTree", "ux13-unprepared");
        WaitForVisibleTaskCard("UX13 Needs planning");
        await AssertNoLegacyTaskCompositionAsync(expectCard: true);
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        var tabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        await CaptureStoryScreenshotAsync("task-card-only-1280.png");
        HeadlessRuntime.Dispatch(() =>
        {
            Session.Inner.MainWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemOpenBrackets,
                KeyModifiers = KeyModifiers.Control, Source = Session.Inner.MainWindow
            });
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind),
            kind => kind == WorkspaceLocationKind.Tasks, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Ctrl+[ did not return from standalone card to its list.");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id)).IsEqualTo(tabId);
        await AssertNoLegacyTaskCompositionAsync(expectCard: false);
        await CaptureStoryScreenshotAsync("back-to-list.png");
    }

    private async Task AssertNoLegacyTaskCompositionAsync(bool expectCard)
    {
        var visibleIds = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Control>().Where(control => control.IsEffectivelyVisible)
            .Select(AutomationProperties.GetAutomationId).ToArray());
        await Assert.That(visibleIds).DoesNotContain("MainTabs");
        if (expectCard)
        {
            await Assert.That(visibleIds).Contains("TaskCardDocument");
            await Assert.That(visibleIds).DoesNotContain("TaskListDocument");
        }
        else
        {
            await Assert.That(visibleIds).Contains("TaskListDocument");
            await Assert.That(visibleIds).DoesNotContain("TaskCardDocument");
            await Assert.That(visibleIds).DoesNotContain("CurrentTaskCard");
        }
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX22_TaskMenuNewTabAdjacentAndReuseKeepTargets()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("task-menu-launch.png", 1600);
        OpenStoryTaskView("WorkspaceRailAllTasksButton");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        var sourceTabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        var firstTitle = WaitForTaskTitle("ux13-unprepared");
        InvokeOpenTargetMenu(firstTitle, "WorkspaceOpenInNewTab");
        WaitForVisibleTaskCard("UX13 Needs planning");
        var firstCardTabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        await Assert.That(firstCardTabId).IsNotEqualTo(sourceTabId);
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.Tabs
            .Any(tab => tab.Id == sourceTabId && tab.CurrentLocation?.Kind == WorkspaceLocationKind.Tasks))).IsTrue();
        await AssertNoLegacyTaskCompositionAsync(expectCard: true);
        OpenStoryTaskView("WorkspaceRailAllTasksButton");
        InvokeOpenTargetMenu(WaitForTaskTitle("ux13-stale"), "WorkspaceOpenBeside");
        WaitForVisibleTaskCard("UX13 Obsolete task");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.HasSecondaryPane),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Task menu did not create an adjacent card.");
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>(
            "WorkspaceTab-" + firstCardTabId.ToString("N"))));
        WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>()
                .Where(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox")
                .Select(control => control.Text).OrderBy(title => title).ToArray()),
            titles => titles.SequenceEqual(new[] { "UX13 Needs planning", "UX13 Obsolete task" }),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Two task cards substituted one another's target.");
        await AssertNoLegacyTaskCompositionAsync(expectCard: true);
        await CaptureStoryScreenshotAsync("two-distinct-task-cards.png");
        var before = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
            .Select(tab => (tab.Id, tab.CurrentIndex, Count: tab.History.Count)).ToArray());
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync("ux13-unprepared", WorkspaceOpenDisposition.AdjacentPane));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id)).IsEqualTo(firstCardTabId);
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
            .Select(tab => (tab.Id, tab.CurrentIndex, Count: tab.History.Count)).SequenceEqual(before))).IsTrue();
    }

    private Control WaitForTaskTitle(string taskId) => WaitUntil(() => HeadlessRuntime.Dispatch(() =>
            Session.Inner.MainWindow.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible &&
                AutomationProperties.GetAutomationId(control) == "TaskTitle_" + taskId)),
        control => control is not null, timeout: TimeSpan.FromSeconds(10),
        timeoutMessage: $"Task {taskId} has no visible title opening target.")!;

    [Test, NotInParallel(DesktopUiConstraint)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UX23_SourceLocatorReusesFilteredFeedAndBackRestoresOwningState(bool sourceIsOwningTab)
    {
        RequireRenderedStoryMode();
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Source fixture vault did not initialize.");
        var identity = new FeedTaskSourceIdentity("independent-source", "independent-storage");
        HeadlessRuntime.Dispatch(() =>
        {
            owner.Feed.TaskOwner = owner;
            owner.Feed.ConfigureTaskSourceParents(() => identity, (_, _, _) => null, (_, _, _, _) => Task.CompletedTask);
            owner.Feed.TaskCreationTarget = new TaskStorageFeedTaskCreationTarget(() => owner.taskRepository, () => identity);
        });
        Page.WorkspaceRailFeedButton.Invoke();
        const string marker = "Precise source must override the restrictive Feed filter";
        CaptureViaHotkey(marker);
        Page.GlobalReviewButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.CurrentReview?.SelectedMarkdown == marker && !owner.Feed.IsBusy),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Source fixture was not selected for review.");
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewTaskActionButton));
        Page.FeedReviewConfirmButton.Invoke();
        var created = WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.taskRepository!.Tasks.Items.FirstOrDefault(task => task.Title == marker)),
            task => task is not null, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Source-backed task was not created.")!;
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.Feed.IsBusy), timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Conversion did not settle.");
        Page.FeedFinishReviewButton.Invoke();
        var location = (await RunParityUiAsync(() => owner.Feed.FindTaskSourceLocationsAsync(created.Id))).Single();
        var priorDay = HeadlessRuntime.Dispatch(() => owner.Feed.Days.First(day => day.RelativePath != location.Id));
        await RunParityUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForFeedDay(priorDay.RelativePath, priorDay.DisplayDate)));
        var owningFeed = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Unlimotion.Views.FeedControl>().Single(control => control.IsEffectivelyVisible));
        var filterButton = HeadlessRuntime.Dispatch(() => owningFeed.GetVisualDescendants().OfType<DropDownButton>()
            .Single(control => AutomationProperties.GetAutomationId(control) == "FeedAreaFilterButton"));
        InvokeNativeButton(filterButton);
        HeadlessRuntime.Dispatch(() =>
        {
            var filterContent = (filterButton.Flyout as Flyout)?.Content as Control
                ?? throw new InvalidOperationException("The Feed document has no area-filter popup.");
            var allAreas = filterContent.GetVisualDescendants().OfType<CheckBox>()
                .Single(control => control.DataContext is FeedAreaFilterOptionViewModel { IsAll: true });
            allAreas.IsChecked = false;
            filterButton.Flyout!.Hide();
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owningFeed.DisplayDays?.Count == 0),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Restrictive source precondition did not hide every day.");
        var owningTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab);
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(created.Id, WorkspaceOpenDisposition.NewTab));
        var taskTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab);
        var taskHistory = HeadlessRuntime.Dispatch(() => taskTab.History.Count);
        var countBefore = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count));
        if (sourceIsOwningTab)
        {
            InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>("WorkspaceTab-" + owningTab.Id.ToString("N"))));
            await RunParityUiAsync(() => owner.Feed.OpenTaskSourceAsync(created.Id, location));
        }
        else
        {
            var source = WaitUntil(() => HeadlessRuntime.Dispatch(() => TryFindNativeControlByAutomationId<Button>("CurrentTaskSourceButton")),
                button => button is { IsEffectivelyVisible: true }, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Task card has no source action.")!;
            InvokeNativeButton(source);
        }
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Anchor == location.Anchor &&
                owningFeed.DisplayDays?.Any(day => day.RelativePath == location.Id) == true),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Source did not reveal its precise location in the existing filtered Feed.");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id)).IsEqualTo(owningTab.Id);
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count))).IsEqualTo(countBefore);
        await Assert.That(HeadlessRuntime.Dispatch(() => taskTab.History.Count)).IsEqualTo(taskHistory);
        await CaptureStoryScreenshotAsync("reuse-source-target.png");
        InvokeWorkspaceBack();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Id == priorDay.RelativePath &&
                owningFeed.DisplayAreaFilterOptions?.All(option => !option.IsSelected) == true),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Back did not restore the owning Feed's prior day and restrictive filter.");
        await Assert.That(HeadlessRuntime.Dispatch(() => taskTab.History.Count)).IsEqualTo(taskHistory);
        await CaptureStoryScreenshotAsync("source-back-restored.png");
    }

    private void InvokeOpenTargetMenu(Control source, string resourceKey)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            // ContextRequested carries the right-clicked row, while the menu is
            // attached to the enclosing tree. Avalonia requires that actual owner.
            source.RaiseEvent(new ContextRequestedEventArgs { Source = source });
            Dispatcher.UIThread.RunJobs();
            var menuOwner = source.ContextMenu is not null ? source : source.GetVisualAncestors().OfType<Control>()
                .FirstOrDefault(control => control.ContextMenu is not null)
                ?? throw new InvalidOperationException("The document entry has no standard opening context menu.");
            var menu = menuOwner.ContextMenu!;
            if (!menu.IsOpen) menu.Open(menuOwner);
            Dispatcher.UIThread.RunJobs();
            var item = menu.Items.OfType<MenuItem>().SingleOrDefault(command => Equals(command.Header, Localization.Get(resourceKey)))
                ?? throw new InvalidOperationException($"Opening menu does not offer {resourceKey}.");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            menu.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX24_NarrowTouchMenuOpensRealAdjacentTaskView()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("touch-launch.png", 1600);
        OpenStoryTaskView("WorkspaceRailInProgressButton");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        var sourceTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab);
        var sourceHistory = HeadlessRuntime.Dispatch(() => sourceTab.History.Select(entry => entry.Location.HistoryKey).ToArray());
        var sourceIndex = HeadlessRuntime.Dispatch(() => sourceTab.CurrentIndex);
        var sourceSearch = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            return Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control =>
                control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "TaskListSearchBox");
        }), control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "In Progress document did not render its local filter before touch navigation.")!;
        var sourceFilter = HeadlessRuntime.Dispatch(() => sourceSearch.Text);
        await CaptureStoryScreenshotAsync("touch-before-menu-390.png", 390);
        var overflow = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<DropDownButton>("GlobalOverflowMenuButton"));

        void ClickRenderedControl(Control control) => HeadlessRuntime.Dispatch(() =>
        {
            control.BringIntoView();
            var root = TopLevel.GetTopLevel(control)
                ?? throw new InvalidOperationException("Touch menu control has no rendered TopLevel.");
            root.UpdateLayout();
            var point = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
                ?? throw new InvalidOperationException("Touch menu control has no pointer position.");
            if (control is MenuItem before)
                Console.WriteLine($"TOUCH_MENU before '{before.Header}': root={root.GetType().Name}; point={point}; hasSubMenu={before.HasSubMenu}; open={before.IsSubMenuOpen}; count={before.Items.Count}; classes={before.Classes}; visible={before.IsEffectivelyVisible}");
            root.MouseDown(point, MouseButton.Left);
            if (control is MenuItem pressed)
                Console.WriteLine($"TOUCH_MENU pressed '{pressed.Header}': open={pressed.IsSubMenuOpen}; hasSubMenu={pressed.HasSubMenu}; count={pressed.Items.Count}");
            root.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (control is MenuItem after)
                Console.WriteLine($"TOUCH_MENU after '{after.Header}': root={TopLevel.GetTopLevel(after)?.GetType().Name}; open={after.IsSubMenuOpen}; count={after.Items.Count}; visible={after.IsEffectivelyVisible}");
        });

        MenuItem WaitRenderedMenuItem(Func<MenuItem?> find, string label) => WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var item = find();
            TopLevel.GetTopLevel((Control?)item ?? overflow)?.UpdateLayout();
            return item is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } &&
                item.Bounds.Width > 0 && item.Bounds.Height > 0 && TopLevel.GetTopLevel(item) is not null ? item : null;
        }), item => item is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Physical touch navigation did not render {label}.")!;

        ClickRenderedControl(overflow);
        var tasks = WaitRenderedMenuItem(() => (overflow.Flyout as MenuFlyout)?.Items.OfType<MenuItem>()
            .SingleOrDefault(item => AutomationProperties.GetAutomationId(item) == "GlobalTasksModeMenuItem"), "Tasks");
        ClickRenderedControl(tasks);
        var available = WaitRenderedMenuItem(() => tasks.Items.OfType<MenuItem>()
            .SingleOrDefault(item => Equals(item.Header, Localization.Get("Unlocked"))), "Tasks → Unlocked");
        ClickRenderedControl(available);
        var adjacent = WaitRenderedMenuItem(() => available.Items.OfType<MenuItem>()
            .SingleOrDefault(item => Equals(item.Header, Localization.Get("WorkspaceOpenBeside"))), "Unlocked → Open beside");
        var labels = HeadlessRuntime.Dispatch(() => available.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray());
        await Assert.That(labels).Contains(Localization.Get("WorkspaceOpenHere"));
        await Assert.That(labels).Contains(Localization.Get("WorkspaceOpenInNewTab"));
        await Assert.That(labels).Contains(Localization.Get("WorkspaceOpenBeside"));
        await CaptureStoryScreenshotAsync("touch-open-commands-390.png", 390);
        ClickRenderedControl(adjacent);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.ActivePane.CurrentLocation?.TaskListKind),
            kind => kind == TaskListKind.Unlocked, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Touch Adjacent did not activate the real Unlocked document.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.HasSecondaryPane)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.SecondaryPane?.ActiveTab?.CurrentLocation?.TaskListKind))
            .IsEqualTo(TaskListKind.Unlocked);
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.ActiveTab)).IsSameReferenceAs(sourceTab);
        await Assert.That(HeadlessRuntime.Dispatch(() => sourceTab.CurrentLocation?.TaskListKind)).IsEqualTo(TaskListKind.InProgress);
        await Assert.That(HeadlessRuntime.Dispatch(() => sourceTab.CurrentIndex == sourceIndex &&
            sourceTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(sourceHistory))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => sourceSearch.Text)).IsEqualTo(sourceFilter);
        await AssertNoLegacyTaskCompositionAsync(expectCard: false);
        await CaptureStoryScreenshotAsync("touch-adjacent-list-390.png", 390);
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    [Arguments("Light", 1280)]
    [Arguments("Light", 640)]
    [Arguments("Light", 390)]
    [Arguments("Dark", 1280)]
    [Arguments("Dark", 640)]
    [Arguments("Dark", 390)]
    public async Task UX25_StandaloneDocumentsRemainReadableAcrossThemesAndWidths(string theme, int width)
    {
        RequireRenderedStoryMode();
        HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.RequestedThemeVariant =
            theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        await CaptureStoryScreenshotAsync($"responsive-launch-{theme}.png", 1600);
        OpenStoryTaskView("WorkspaceRailInProgressButton");
        await CaptureStoryScreenshotAsync($"responsive-list-{theme}-{width}.png", width);
        await AssertNoLegacyTaskCompositionAsync(expectCard: false);
        var list = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Control>("TaskListDocument"));
        await Assert.That(HeadlessRuntime.Dispatch(() => list.ActualThemeVariant?.ToString())).IsEqualTo(theme);
        await Assert.That(HeadlessRuntime.Dispatch(() => list.Bounds.Width > 0 && list.Bounds.Width <= width)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => list.GetVisualDescendants().OfType<TextBox>()
            .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "TaskListSearchBox"))).IsTrue();

        // Changing route uses the same real task tree entry as the everyday work flow,
        // then checks the card at the requested viewport instead of only its title.
        await CaptureStoryScreenshotAsync($"responsive-before-card-{theme}.png", 1600);
        OpenStoryTaskView("WorkspaceRailAllTasksButton");
        SelectStoryTaskFromTree("AllTasksTree", "ux13-unprepared");
        WaitForVisibleTaskCard("UX13 Needs planning");
        await CaptureStoryScreenshotAsync($"responsive-card-{theme}-{width}.png", width);
        await AssertNoLegacyTaskCompositionAsync(expectCard: true);
        var card = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Control>("TaskCardDocument"));
        await Assert.That(HeadlessRuntime.Dispatch(() => card.ActualThemeVariant?.ToString())).IsEqualTo(theme);
        await Assert.That(HeadlessRuntime.Dispatch(() => card.Bounds.Width > 0 && card.Bounds.Width <= width)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => card.GetVisualDescendants().OfType<TextBox>()
            .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox"))).IsTrue();
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX26_NoteTabPreservesManualScrollWithoutRestoringNeighborFeedCollapse()
    {
        RequireRenderedStoryMode();
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "The Note scroll fixture vault did not initialize.");
        const string path = "Notes/Manual scroll regression.md";
        var root = HeadlessRuntime.Dispatch(() => owner.Feed.VaultRootPath)!;
        Directory.CreateDirectory(Path.Combine(root, "Notes"));
        await File.WriteAllTextAsync(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)),
            "# Manual scroll regression\n\n" + string.Join("\n\n", Enumerable.Range(1, 150)
                .Select(index => $"Paragraph {index}: a separate long-note block used to verify retained reading position.")));
        await CaptureStoryScreenshotAsync("note-scroll-launch.png", 1600);
        Page.WorkspaceRailFeedButton.Invoke();
        var feedTabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        var feedControl = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Unlimotion.Views.FeedControl>().Single(control => control.IsEffectivelyVisible));
        var day = HeadlessRuntime.Dispatch(() => feedControl.DisplayDays!.First());
        HeadlessRuntime.Dispatch(() => day.IsCollapsed = false);
        await RunParityUiAsync(() => owner.OpenWorkspaceLocationAsync(
            WorkspaceLocation.ForNote(path, "Manual scroll regression"), WorkspaceOpenDisposition.AdjacentPane));
        var noteTabId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        await CaptureStoryScreenshotAsync("note-before-manual-scroll.png");
        var noteScroller = WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
                .OfType<ScrollViewer>().FirstOrDefault(control => control.IsEffectivelyVisible &&
                    AutomationProperties.GetAutomationId(control) == "FeedDocumentScrollViewer" && control.Extent.Height > 1500)),
            control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Long Note did not render a scrollable document.")!;
        HeadlessRuntime.Dispatch(() => noteScroller.Offset = new Avalonia.Vector(0, 1100));
        await CaptureStoryScreenshotAsync("note-manually-scrolled.png");
        var expectedOffset = HeadlessRuntime.Dispatch(() => noteScroller.Offset.Y);
        await Assert.That(expectedOffset > 1000).IsTrue();

        // Capture the Note state by leaving its tab, change the neighboring Feed,
        // then return through the real tab buttons. Note restoration must not touch Feed.
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(
            UnlimotionAutomationScenarioData.FeedCurrentTaskId, WorkspaceOpenDisposition.NewTab));
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>(
            "WorkspaceTab-" + feedTabId.ToString("N"))));
        HeadlessRuntime.Dispatch(() =>
        {
            var collapse = feedControl.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .First(control => ReferenceEquals(control.DataContext, day) && control.IsEffectivelyVisible &&
                    control.Classes.Contains("FeedDayCollapseToggle"));
            collapse.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        });
        await Assert.That(HeadlessRuntime.Dispatch(() => day.IsCollapsed)).IsTrue();
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>(
            "WorkspaceTab-" + noteTabId.ToString("N"))));
        WaitUntil(() => HeadlessRuntime.Dispatch(() => noteScroller.IsEffectivelyVisible &&
                Math.Abs(noteScroller.Offset.Y - expectedOffset) < 2),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Returning to Note reset its manually chosen reading position.");
        await Assert.That(HeadlessRuntime.Dispatch(() => day.IsCollapsed)).IsTrue()
            .Because("Restoring a Note must not replay its stale snapshot over the neighboring Feed's collapse state.");
        await CaptureStoryScreenshotAsync("note-scroll-and-neighbor-collapse-restored.png");
    }

    private static void SeedScrollableInProgressTasks(string tasksPath)
    {
        var now = DateTimeOffset.Now;
        for (var index = 0; index < 80; index++)
        {
            var task = new Unlimotion.Domain.TaskItem
            {
                Id = $"ux27-scroll-{index:D3}", Title = $"Scrollable active task {index:D3}",
                Description = "Synthetic row for independent pane selection and scroll regression.",
                Status = Unlimotion.Domain.TaskStatus.InProgress,
                StatusHistory = [new Unlimotion.Domain.TaskStatusHistoryEntry
                { Status = Unlimotion.Domain.TaskStatus.InProgress, ChangedAt = now, Author = "headless-regression" }],
                IsCanBeCompleted = true, CreatedDateTime = now, UpdatedDateTime = now, Version = 1
            };
            File.WriteAllText(Path.Combine(tasksPath, task.Id), Newtonsoft.Json.JsonConvert.SerializeObject(task));
        }
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX27_AdjacentCardDoesNotChangeListSelectionOrScroll()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("selection-isolation-launch.png", 1600);
        OpenStoryTaskView("WorkspaceRailInProgressButton");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        var leftTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab);
        var tree = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<TreeView>("InProgressTree"));
        var selected = WaitUntil(() => HeadlessRuntime.Dispatch(() => tree.ItemsSource?
                .OfType<Unlimotion.ViewModel.TaskWrapperViewModel>().SelectMany(FlattenStoryWrapper)
                .FirstOrDefault(wrapper => wrapper.TaskItem.Id == "ux08-active")),
            wrapper => wrapper is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Task A was not loaded into the actual In Progress tree.")!;
        HeadlessRuntime.Dispatch(() => { tree.SelectedItem = selected; Dispatcher.UIThread.RunJobs(); });
        await CaptureStoryScreenshotAsync("selection-isolation-before-scroll.png");
        var scroller = HeadlessRuntime.Dispatch(() => tree.GetVisualDescendants().OfType<ScrollViewer>()
            .First(control => control.IsEffectivelyVisible));
        await Assert.That(HeadlessRuntime.Dispatch(() => scroller.Extent.Height > scroller.Viewport.Height + 400)).IsTrue();
        HeadlessRuntime.Dispatch(() => scroller.Offset = new Avalonia.Vector(0, 350));
        await CaptureStoryScreenshotAsync("selection-isolation-list-scrolled.png");
        var offsetBefore = HeadlessRuntime.Dispatch(() => scroller.Offset.Y);
        var leftHistoryBefore = HeadlessRuntime.Dispatch(() => leftTab.History.Select(entry => entry.Location.HistoryKey).ToArray());
        var leftIndexBefore = HeadlessRuntime.Dispatch(() => leftTab.CurrentIndex);
        await Assert.That(offsetBefore > 300).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.CurrentInProgressItem?.TaskItem.Id)).IsEqualTo("ux08-active");

        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync("ux08-finished", WorkspaceOpenDisposition.AdjacentPane));
        WaitForVisibleTaskCard("UX08 Already finished in reality");
        await CaptureStoryScreenshotAsync("selection-isolation-card-b-beside.png");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.ActiveTab!.Id)).IsEqualTo(leftTab.Id);
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.SecondaryPane!.ActiveTab!.CurrentLocation!.Id)).IsEqualTo("ux08-finished");
        await Assert.That(HeadlessRuntime.Dispatch(() => tree.IsEffectivelyVisible)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => ReferenceEquals(tree.SelectedItem, selected))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.CurrentInProgressItem?.TaskItem.Id)).IsEqualTo("ux08-active");
        await Assert.That(HeadlessRuntime.Dispatch(() => Math.Abs(scroller.Offset.Y - offsetBefore) < 2)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => leftTab.CurrentIndex)).IsEqualTo(leftIndexBefore);
        await Assert.That(HeadlessRuntime.Dispatch(() => leftTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(leftHistoryBefore))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.SecondaryPane!.ActiveTab!.History.Count)).IsEqualTo(1);
    }
}
