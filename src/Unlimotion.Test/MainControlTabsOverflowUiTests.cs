using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTabsOverflowUiTests
{
    [Test]
    [Arguments(2200)]
    [Arguments(1280)]
    [Arguments(640)]
    [Arguments(390)]
    public async Task WorkspaceTabs_MultipleDocumentsKeepBoundedHeaderAndAccessibleActions(int width)
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
                window = new Window { Width = width, Height = 800, Content = view };
                window.Show();
                foreach (var kind in new[] { TaskListKind.LastCreated, TaskListKind.LastUpdated,
                    TaskListKind.Unlocked, TaskListKind.InProgress })
                    await Assert.That(await owner.OpenWorkspaceLocationAsync(
                        WorkspaceLocation.ForTaskList(kind), WorkspaceOpenDisposition.NewTab)).IsTrue();
                PumpLayout(window);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Count).IsEqualTo(5);
                var pane = Find<WorkspacePaneView>(view, "WorkspacePrimaryPane");
                var activeId = owner.WorkspaceNavigation.ActiveTab.Id.ToString("N");
                var active = Find<Button>(pane, "WorkspaceTab-" + activeId);
                var actions = Find<Button>(pane, "WorkspaceTabActions-" + activeId);
                var viewport = active.GetVisualAncestors().OfType<ScrollViewer>().First();
                await Assert.That(viewport.Bounds.Width).IsGreaterThan(0);
                await Assert.That(viewport.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                await Assert.That(viewport.VerticalScrollBarVisibility).IsEqualTo(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
                if (width <= 640)
                    await Assert.That(viewport.Extent.Width).IsGreaterThan(viewport.Viewport.Width);
                foreach (var id in new[] { "WorkspacePaneBackButton", "WorkspacePaneForwardButton", "WorkspacePaneHistoryButton" })
                    await AssertBoundedAsync(window, Find<Button>(pane, id));
                AssertNoNestedTaskComposition(view);

                // Horizontal scrolling exposes tab actions without allowing the
                // tab strip to expand the window or displace pane navigation.
                viewport.Offset = new Vector(Math.Max(0, viewport.Extent.Width - viewport.Viewport.Width), 0);
                PumpLayout(window);
                await AssertBoundedAsync(window, actions);
                actions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpLayout(window);
                await Assert.That(actions.ContextMenu?.IsOpen == true).IsTrue();
                var labels = actions.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
                await Assert.That(labels).Contains(Localization.Get("WorkspaceMoveTab"));
                await Assert.That(labels).Contains(Localization.Get("WorkspaceCloseTab"));
                await Assert.That(active.ContextMenu?.Items.OfType<MenuItem>().Any(item =>
                    Equals(item.Header, Localization.Get("WorkspaceCloseTab"))) == true).IsTrue();
                actions.ContextMenu.Close();
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
    [Arguments(640)]
    public async Task WorkspaceTabs_NarrowBackAndHistoryOperateOnTheActiveDocument(int width)
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
                window = new Window { Width = width, Height = 800, Content = view };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.LastUpdated), WorkspaceOpenDisposition.NewTab);
                var rootTab = owner.WorkspaceNavigation.PrimaryPane.Tabs.First();
                var rootIndex = rootTab.CurrentIndex;
                var rootHistory = rootTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var activeTab = owner.WorkspaceNavigation.ActiveTab;
                await owner.OpenWorkspaceTaskAsync(TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask1Id));
                PumpLayout(window);
                var pane = Find<WorkspacePaneView>(view, "WorkspacePrimaryPane");
                await Assert.That(pane.TasksView is TaskCardView).IsTrue();
                var back = Find<Button>(pane, "WorkspacePaneBackButton");
                await AssertBoundedAsync(window, back);
                await Assert.That(back.IsEnabled).IsTrue();
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Yield();
                PumpLayout(window);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(activeTab);
                await Assert.That(activeTab.CurrentLocation?.TaskListKind).IsEqualTo(TaskListKind.LastUpdated);
                await Assert.That(pane.TasksView is TaskListDocumentView).IsTrue();
                await Assert.That(rootTab.CurrentIndex).IsEqualTo(rootIndex);
                await Assert.That(rootTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(rootHistory)).IsTrue();
                var history = Find<Button>(pane, "WorkspacePaneHistoryButton");
                await AssertBoundedAsync(window, history);
                history.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpLayout(window);
                await Assert.That(history.ContextMenu?.Items.OfType<MenuItem>().Count()).IsEqualTo(2);
                await Assert.That(history.ContextMenu?.IsOpen == true).IsTrue();
                history.ContextMenu!.Close();
                AssertNoNestedTaskComposition(view);
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
    [Arguments(640)]
    public async Task WorkspaceTabs_ResizeToDesktopPreservesDocumentsAndNavigation(int initialWidth)
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
                window = new Window { Width = initialWidth, Height = 800, Content = view };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.InProgress), WorkspaceOpenDisposition.NewTab);
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.Unlocked), WorkspaceOpenDisposition.NewTab);
                var before = owner.WorkspaceNavigation.PrimaryPane.Tabs.Select(tab => (tab.Id, tab.CurrentIndex, tab.History.Count)).ToArray();
                var activeId = owner.WorkspaceNavigation.ActiveTab.Id;
                PumpLayout(window);
                window.Width = 2200;
                await Task.Yield();
                PumpLayout(window);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane.Tabs.Select(tab => (tab.Id, tab.CurrentIndex, tab.History.Count)).SequenceEqual(before)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ActiveTab.Id).IsEqualTo(activeId);
                var pane = Find<WorkspacePaneView>(view, "WorkspacePrimaryPane");
                foreach (var tab in owner.WorkspaceNavigation.PrimaryPane.Tabs)
                    await AssertBoundedAsync(window, Find<Button>(pane, "WorkspaceTab-" + tab.Id.ToString("N")));
                await AssertBoundedAsync(window, Find<Button>(pane, "WorkspacePaneBackButton"));
                AssertNoNestedTaskComposition(view);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WorkspaceTabs_ClosingSelectedBackgroundTabRestoresItsListWithoutChangingPrimary()
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
                for (var index = 0; index < 30; index++)
                {
                    var task = await owner.taskRepository!.Add();
                    task.Title = $"Background snapshot row {index:D2}";
                }
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 500, Content = view };
                window.Show();
                PumpLayout(window);
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var primaryTab = primary.ActiveTab!;
                var primaryView = Find<WorkspacePaneView>(view, "WorkspacePrimaryPane");
                await WaitForLayoutAsync(window, () => primaryView.GetVisualDescendants().OfType<TextBox>()
                    .Any(input => input.IsEffectivelyVisible &&
                        AutomationProperties.GetAutomationId(input) == "TaskListSearchBox"));
                var primarySearch = Find<TextBox>(primaryView, "TaskListSearchBox");
                primarySearch.Text = "Root";
                await WaitForLayoutAsync(window, () => owner.Search.SearchText == "Root");

                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.LastCreated),
                    WorkspaceOpenDisposition.AdjacentPane);
                var secondary = owner.WorkspaceNavigation.SecondaryPane!;
                var restoringTab = secondary.ActiveTab!;
                var secondaryView = Find<WorkspacePaneView>(view, "WorkspaceSecondaryPane");
                await WaitForLayoutAsync(window, () => secondaryView.TasksView is TaskListDocumentView
                    { Kind: TaskListKind.LastCreated } readyList && readyList.GetVisualDescendants().OfType<TextBox>()
                    .Any(input => input.IsEffectivelyVisible &&
                        AutomationProperties.GetAutomationId(input) == "TaskListSearchBox"));
                var list = (TaskListDocumentView)secondaryView.TasksView;
                Find<TextBox>(list, "TaskListSearchBox").Text = "Background snapshot";
                await WaitForLayoutAsync(window, () => list.TaskTree!.ItemsSource!.Cast<TaskWrapperViewModel>().Count() == 30);
                var selected = list.TaskTree!.ItemsSource!.Cast<TaskWrapperViewModel>().First();
                list.TaskTree.SelectedItem = selected;
                var scroll = list.TaskTree.GetVisualDescendants().OfType<ScrollViewer>().First();
                PumpLayout(window);
                scroll.Offset = new Vector(0, 150);
                PumpLayout(window);
                var savedOffset = scroll.Offset.Y;
                await Assert.That(savedOffset).IsGreaterThan(50d);
                var savedHistory = restoringTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.LastUpdated),
                    WorkspaceOpenDisposition.NewTab);
                var closingTab = secondary.ActiveTab!;
                owner.ActivateWorkspacePane(primary);
                PumpLayout(window);
                var primaryHistory = primaryTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var primaryIndex = primaryTab.CurrentIndex;
                var primaryTree = ((TaskListDocumentView)primaryView.TasksView).TaskTree!;
                var primarySelection = primaryTree.SelectedItem;
                var primaryOffset = primaryTree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset;
                Find<Button>(secondaryView, "WorkspaceCloseActiveTabButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForLayoutAsync(window, () => !secondary.Tabs.Contains(closingTab)
                    && secondaryView.TasksView is TaskListDocumentView { Kind: TaskListKind.LastCreated });
                var restored = (TaskListDocumentView)(secondaryView.TasksView
                    ?? throw new InvalidOperationException("Restored list view is missing."));
                try
                {
                    await WaitForLayoutAsync(window, () => restored.TaskTree!.SelectedItem is TaskWrapperViewModel item
                        && item.TaskItem.Id == selected.TaskItem.Id
                        && Math.Abs(restored.TaskTree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y - savedOffset) < 2);
                }
                catch (TimeoutException error)
                {
                    var restoredScroll = restored.TaskTree!.GetVisualDescendants().OfType<ScrollViewer>().First();
                    throw new TimeoutException($"Background-list restore failed: expected selected={selected.TaskItem.Id}, " +
                        $"actual selected={(restored.TaskTree.SelectedItem as TaskWrapperViewModel)?.TaskItem.Id ?? "<none>"}, " +
                        $"expected offset={savedOffset}, actual offset={restoredScroll.Offset.Y}, " +
                        $"extent={restoredScroll.Extent}, viewport={restoredScroll.Viewport}, " +
                        $"rows={restored.TaskTree.ItemsSource!.Cast<TaskWrapperViewModel>().Count()}, " +
                        $"search={restored.Document.Search.SearchText}, activePrimary={ReferenceEquals(owner.WorkspaceNavigation.ActivePane, primary)}, " +
                        $"stored snapshot={Newtonsoft.Json.JsonConvert.SerializeObject(restoringTab.CurrentEntry?.ViewState)}", error);
                }
                await Assert.That(Find<TextBox>(restored, "TaskListSearchBox").Text).IsEqualTo("Background snapshot");
                await Assert.That(secondary.ActiveTab).IsSameReferenceAs(restoringTab);
                await Assert.That(restoringTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(savedHistory)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ActivePane).IsSameReferenceAs(primary);
                await Assert.That(primary.ActiveTab).IsSameReferenceAs(primaryTab);
                await Assert.That(primaryTab.CurrentIndex).IsEqualTo(primaryIndex);
                await Assert.That(primaryTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(primaryHistory)).IsTrue();
                await Assert.That(primarySearch.Text).IsEqualTo("Root");
                await Assert.That(primaryTree.SelectedItem).IsSameReferenceAs(primarySelection);
                await Assert.That(primaryTree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset).IsEqualTo(primaryOffset);

                // Restoring a deliberately off-screen selection must suppress only
                // that restore's scroll, not normal subsequent keyboard navigation.
                await Assert.That(restored.TaskTree!.AutoScrollToSelectedItem).IsTrue();
                var nextRow = restored.TaskTree.ItemsSource!.Cast<TaskWrapperViewModel>().Skip(1).First();
                var keyboardSource = restored.TaskTree.GetVisualDescendants().OfType<TreeViewItem>()
                    .Single(item => item.IsSelected && item.DataContext is TaskWrapperViewModel wrapper
                        && wrapper.TaskItem.Id == selected.TaskItem.Id);
                await Assert.That(keyboardSource.Focus()).IsTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                try
                {
                    await WaitForLayoutAsync(window, () => restored.TaskTree.SelectedItem is TaskWrapperViewModel moved
                        && moved.TaskItem.Id == nextRow.TaskItem.Id
                        && restored.TaskTree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y < savedOffset - 50);
                }
                catch (TimeoutException error)
                {
                    throw new TimeoutException($"Keyboard navigation after restore failed: expected next={nextRow.TaskItem.Id}, " +
                        $"actual selected={(restored.TaskTree.SelectedItem as TaskWrapperViewModel)?.TaskItem.Id ?? "<none>"}, " +
                        $"offset={restored.TaskTree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y}, " +
                        $"savedOffset={savedOffset}, autoScroll={restored.TaskTree.AutoScrollToSelectedItem}, " +
                        $"focused={window.FocusManager?.GetFocusedElement()?.GetType().Name}", error);
                }
                var selectedRow = restored.TaskTree.GetVisualDescendants().OfType<TreeViewItem>()
                    .Single(item => item.IsSelected && item.DataContext is TaskWrapperViewModel wrapper
                        && wrapper.TaskItem.Id == nextRow.TaskItem.Id);
                var finalScroll = restored.TaskTree.GetVisualDescendants().OfType<ScrollViewer>().First();
                var rowTop = selectedRow.TranslatePoint(new Point(0, 0), finalScroll)!.Value.Y;
                await Assert.That(rowTop).IsGreaterThanOrEqualTo(0d);
                await Assert.That(rowTop + selectedRow.Bounds.Height).IsLessThanOrEqualTo(finalScroll.Bounds.Height);
                await Assert.That(restored.TaskTree.AutoScrollToSelectedItem).IsTrue();
            }
            finally { window?.Close(); await fixture.CleanTasksAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("move")]
    [Arguments("merge")]
    [Arguments("close-secondary")]
    [Arguments("close-tab")]
    public async Task WorkspaceTabs_LayoutOperationsPreserveActualNoteViewport(string operation)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "LayoutNotes");
                Directory.CreateDirectory(vault);
                var text = "# Target\n\n" + string.Join("\n\n", Enumerable.Range(1, 150).Select(i => $"Paragraph {i}"));
                await File.WriteAllTextAsync(Path.Combine(vault, "First.md"), text);
                await File.WriteAllTextAsync(Path.Combine(vault, "Second.md"), text);
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 600, Content = view };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote("First.md", "First", "Target"));
                var firstTab = owner.WorkspaceNavigation.ActiveTab;
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                PumpLayout(window);
                var firstPane = Find<WorkspacePaneView>(view, "WorkspacePrimaryPane");
                var scroll = NoteScroll(firstPane);
                scroll.Offset = new Vector(0, 1200);
                PumpLayout(window);
                var savedOffset = scroll.Offset.Y;
                await Assert.That(savedOffset).IsGreaterThan(500d);
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote("Second.md", "Second"),
                    operation == "close-tab" ? WorkspaceOpenDisposition.NewTab : WorkspaceOpenDisposition.AdjacentPane);
                var closingTab = owner.WorkspaceNavigation.ActiveTab;
                if (operation != "close-tab") owner.ActivateWorkspacePane(primary);
                switch (operation)
                {
                    case "move": await owner.MoveWorkspaceTabAsync(primary, firstTab); break;
                    case "merge": await owner.MergeWorkspacePanesAsync(); break;
                    case "close-secondary": await owner.CloseWorkspaceSecondaryPaneAsync(); break;
                    case "close-tab": await owner.CloseWorkspaceTabAsync(primary, closingTab); break;
                }
                await WaitForLayoutAsync(window, () => view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Any(pane => pane.FeedView.DisplayedDocument?.RelativePath == "First.md"
                        && Math.Abs(NoteScroll(pane).Offset.Y - savedOffset) < 2));
                var displayed = view.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => pane.FeedView.DisplayedDocument?.RelativePath == "First.md");
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(firstTab);
                await Assert.That(Math.Abs(NoteScroll(displayed).Offset.Y - savedOffset)).IsLessThan(2d);

                // Repeating the same explicit anchor is a reveal request, not a new
                // history entry, even after manual scrolling away from that anchor.
                var historyCount = firstTab.History.Count;
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote("First.md", "First", "Target"));
                await WaitForLayoutAsync(window, () => NoteScroll(displayed).Offset.Y < 60);
                await Assert.That(firstTab.History.Count).IsEqualTo(historyCount);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(firstTab);
            }
            finally { window?.Close(); await fixture.CleanTasksAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("feed")]
    [Arguments("note")]
    [Arguments("task")]
    public async Task WorkspaceTabs_ClosingLastPrimaryPromotesSecondaryAndActivatesItsGlobalContext(string target)
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
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "PromotionNotes");
                Directory.CreateDirectory(vault);
                await File.WriteAllTextAsync(Path.Combine(vault, "Remaining.md"), "# Remaining\n\nPromotion content");
                await owner.Feed.InitializeVaultAsync(vault);
                var view = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 600, Content = view };
                window.Show();
                if (target == "task")
                    await Assert.That(await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot)).IsTrue();
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var closingTab = primary.ActiveTab ?? throw new InvalidOperationException("Closing primary tab is missing.");
                var task = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask4Id)!;
                var location = target switch
                {
                    "feed" => WorkspaceLocation.FeedRoot,
                    "note" => WorkspaceLocation.ForNote("Remaining.md", "Remaining"),
                    _ => WorkspaceLocation.ForTask(task.Id, task.Title)
                };
                await Assert.That(await owner.OpenWorkspaceLocationAsync(location,
                    WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                var remaining = owner.WorkspaceNavigation.SecondaryPane!;
                var remainingTab = remaining.ActiveTab ?? throw new InvalidOperationException("Remaining secondary tab is missing.");
                var history = remainingTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                owner.ActivateWorkspacePane(primary);
                PumpLayout(window);
                await Assert.That(owner.IsFeedMode).IsEqualTo(target == "task");

                await Assert.That(await owner.CloseWorkspaceTabAsync(primary, closingTab)).IsTrue();
                await WaitForLayoutAsync(window, () =>
                    ReferenceEquals(owner.WorkspaceNavigation.PrimaryPane, remaining) &&
                    owner.IsTasksMode == (target == "task") &&
                    (target != "task" || view.GetVisualDescendants().OfType<TaskCardView>()
                        .Any(card => ReferenceEquals(card.RouteTaskItem, task))) &&
                    (target != "note" || view.GetVisualDescendants().OfType<WorkspacePaneView>()
                        .Any(pane => pane.FeedView.DisplayedDocument?.RelativePath == "Remaining.md")));
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane).IsNull();
                await Assert.That(owner.WorkspaceNavigation.ActivePane).IsSameReferenceAs(remaining);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(remainingTab);
                await Assert.That(remaining.Tabs.Count).IsEqualTo(1);
                await Assert.That(remainingTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(history)).IsTrue();
                await Assert.That(owner.IsFeedMode).IsEqualTo(target != "task");
                await Assert.That(owner.IsTasksMode).IsEqualTo(target == "task");
                if (target == "task")
                    await Assert.That(owner.CurrentTaskItem?.Id).IsEqualTo(task.Id);
                else if (target == "note")
                    await Assert.That(owner.Feed.DocumentWorkspace.ActiveDocument?.RelativePath).IsEqualTo("Remaining.md");
                else
                    await Assert.That(owner.Feed.DocumentWorkspace.ActiveDocument).IsNull();
                AssertNoNestedTaskComposition(view);
            }
            finally { window?.Close(); await fixture.CleanTasksAsync(); }
        }, CancellationToken.None);
    }

    private static ScrollViewer NoteScroll(WorkspacePaneView pane) => pane.FeedView.GetVisualDescendants()
        .OfType<ScrollViewer>().Single(control => control.Name == "DocumentScroller");

    private static async Task WaitForLayoutAsync(Window window, Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
            PumpLayout(window);
            if (condition()) return;
        }
        throw new TimeoutException("The workspace did not restore its expected physical document state.");
    }

    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id)
        ?? throw new InvalidOperationException($"Workspace control {id} was not found.");

    private static async Task AssertBoundedAsync(Window window, Control control)
    {
        await Assert.That(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0).IsTrue();
        var position = control.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("Workspace control has no window coordinates.");
        await Assert.That(position.X).IsGreaterThanOrEqualTo(-1);
        await Assert.That(position.X + control.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width + 1);
    }

    private static void AssertNoNestedTaskComposition(MainScreen view)
    {
        if (view.GetVisualDescendants().OfType<MainControl>().Any() || view.GetVisualDescendants().OfType<Control>()
            .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "MainTabs"))
            throw new InvalidOperationException("The workspace mounted the obsolete composite task mode strip.");
    }

    private static void PumpLayout(Window window)
    {
        for (var index = 0; index < 8; index++) Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
