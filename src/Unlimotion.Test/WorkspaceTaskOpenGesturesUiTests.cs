using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceTaskOpenGesturesUiTests
{
    [Test]
    public async Task TaskRows_ControlClickAndKeyboardContextMenuOpenTheCorrectTaskWithoutReplacingSource()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            var keyboardTimeline = new System.Collections.Generic.List<string>(64);
            var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
            var originalContextMenuKeys = hotkeys.OpenContextMenu;
            try
            {
                // Avalonia Headless configures only the Menu key; Win32 additionally
                // configures Shift+F10. Exercise the real Windows input mapping here.
                hotkeys.OpenContextMenu = [.. originalContextMenuKeys, new KeyGesture(Key.F10, KeyModifiers.Shift)];
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.TasksRoot);
                await WaitAsync(window, () => List(shell)?.GetVisualDescendants().OfType<TextBox>()
                    .Any(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox") == true);
                var list = List(shell)!;
                var search = list.GetVisualDescendants().OfType<TextBox>()
                    .Single(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox");
                search.Text = "Root";
                await WaitAsync(window, () => list.TaskTree!.Items.OfType<TaskWrapperViewModel>()
                    .Any(wrapper => wrapper.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id));
                var pane = owner.WorkspaceNavigation.PrimaryPane;
                var sourceTab = pane.ActiveTab ?? throw new InvalidOperationException("Source task-list tab is missing.");
                var sourceHistory = sourceTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var sourceIndex = sourceTab.CurrentIndex;
                var ctrlTitle = await TitleAsync(window, list, MainWindowViewModelFixture.RootTask4Id);
                await ClickAsync(window, ctrlTitle, RawInputModifiers.Control);
                await WaitAsync(window, () => pane.Tabs.Count == 2 && pane.ActiveTab.CurrentLocation is
                    { Kind: WorkspaceLocationKind.Task, Id: MainWindowViewModelFixture.RootTask4Id }
                    && shell.GetVisualDescendants().OfType<TaskCardView>().Any(card => card.IsEffectivelyVisible
                        && card.RouteTaskItem?.Id == MainWindowViewModelFixture.RootTask4Id));
                var firstCardTab = pane.ActiveTab;
                await Assert.That(firstCardTab).IsNotSameReferenceAs(sourceTab);
                await Assert.That(sourceTab.CurrentLocation!.TaskListKind).IsEqualTo(TaskListKind.AllTasks);
                await Assert.That(sourceTab.CurrentIndex).IsEqualTo(sourceIndex);
                await Assert.That(sourceTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(sourceHistory)).IsTrue();

                var sourceButton = shell.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "WorkspaceTab-" + sourceTab.Id.ToString("N"));
                await ClickAsync(window, sourceButton, RawInputModifiers.None);
                await WaitAsync(window, () => ReferenceEquals(pane.ActiveTab, sourceTab) && List(shell) is not null);
                list = List(shell)!;
                await WaitAsync(window, () => list.GetVisualDescendants().OfType<TextBox>()
                    .Any(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox"));
                await Assert.That(list.GetVisualDescendants().OfType<TextBox>()
                    .Single(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox").Text).IsEqualTo("Root");
                var keyboardTitle = await TitleAsync(window, list, MainWindowViewModelFixture.RootTask1Id);
                var keyboardRow = keyboardTitle.GetVisualAncestors().OfType<TreeViewItem>().First();
                var keyboardWrapper = (TaskWrapperViewModel)keyboardRow.DataContext!;
                await Assert.That(keyboardWrapper.TaskItem.Id).IsEqualTo(MainWindowViewModelFixture.RootTask1Id);
                list.TaskTree!.SelectedItem = keyboardWrapper;
                await Assert.That(keyboardRow.Focus()).IsTrue();
                await Assert.That(list.TaskTree.ContextMenu).IsNotNull();
                var keyboardTree = list.TaskTree;
                var initialMenu = keyboardTree.ContextMenu;
                var diagnosticsClock = System.Diagnostics.Stopwatch.StartNew();
                string MenuState(string stage) => $"stage={stage}; elapsedMs={diagnosticsClock.ElapsedMilliseconds}; " +
                    $"windowActive={window.IsActive}; focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}; " +
                    $"rowRoot={TopLevel.GetTopLevel(keyboardRow)?.GetType().Name}; rowFocused={keyboardRow.IsFocused}; " +
                    $"treeRoot={TopLevel.GetTopLevel(keyboardTree)?.GetType().Name}; listRoot={TopLevel.GetTopLevel(list)?.GetType().Name}; " +
                    $"sameTree={ReferenceEquals(List(shell)?.TaskTree, keyboardTree)}; sameMenu={ReferenceEquals(keyboardTree.ContextMenu, initialMenu)}; " +
                    $"menuOpen={keyboardTree.ContextMenu?.IsOpen}; placement={keyboardTree.ContextMenu?.PlacementTarget?.GetType().Name}; " +
                    $"placementRoot={(keyboardTree.ContextMenu?.PlacementTarget is { } target ? TopLevel.GetTopLevel(target)?.GetType().Name : null)}; " +
                    $"route={pane.ActiveTab?.CurrentLocation?.Kind}:{pane.ActiveTab?.CurrentLocation?.Id}; " +
                    $"selected={(keyboardTree.SelectedItem as TaskWrapperViewModel)?.TaskItem.Id}";
                void Trace(string stage) => keyboardTimeline.Add(MenuState(stage));
                var tracedMenus = new System.Collections.Generic.HashSet<ContextMenu>();
                void ObserveMenu(ContextMenu? observed)
                {
                    if (observed is null || !tracedMenus.Add(observed)) return;
                    observed.Opened += (_, _) => Trace("menu.Opened");
                    observed.Closed += (_, _) => Trace("menu.Closed");
                }
                ObserveMenu(initialMenu);
                keyboardTree.PropertyChanged += (_, change) =>
                {
                    if (change.Property != Control.ContextMenuProperty) return;
                    ObserveMenu(keyboardTree.ContextMenu);
                    Trace("tree.ContextMenuChanged");
                };
                keyboardRow.DetachedFromVisualTree += (_, _) => Trace("row.Detached");
                keyboardTree.DetachedFromVisualTree += (_, _) => Trace("tree.Detached");
                list.DetachedFromVisualTree += (_, _) => Trace("list.Detached");
                keyboardRow.PropertyChanged += (_, change) =>
                {
                    if (change.Property == InputElement.IsFocusedProperty) Trace("row.FocusChanged");
                };
                var contextRequests = 0;
                window.AddHandler(Control.ContextRequestedEvent, (_, args) =>
                {
                    contextRequests++;
                    Trace($"ContextRequested.Tunnel source={args.Source?.GetType().Name} handled={args.Handled}");
                },
                    Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
                window.AddHandler(Control.ContextRequestedEvent, (_, args) =>
                    Trace($"ContextRequested.Bubble source={args.Source?.GetType().Name} handled={args.Handled}"),
                    Avalonia.Interactivity.RoutingStrategies.Bubble, true);
                keyboardTimeline.Add($"Keyboard menu target: task={keyboardWrapper.TaskItem.Id}, " +
                    $"focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}, " +
                    $"rowFocused={keyboardRow.IsFocused}, tree={list.TaskTree.Name}, " +
                    $"menuItems={list.TaskTree.ContextMenu!.Items.Count}");
                window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
                Trace("after.KeyPress");
                window.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
                Trace("after.KeyRelease");
                Pump(window);
                Trace("after.Pump");
                keyboardTimeline.Add($"Keyboard menu result: requests={contextRequests}, " +
                    $"focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}, " +
                    $"open={list.TaskTree.ContextMenu.IsOpen}");
                try { await WaitAsync(window, () => list.TaskTree.ContextMenu?.IsOpen == true); }
                catch (TimeoutException error)
                {
                    var diagnostics = MenuState("wait.Open.Timeout");
                    keyboardTimeline.Add("Keyboard popup opening timeout: " + diagnostics);
                    throw new TimeoutException(error.Message + " " + diagnostics, error);
                }
                var menu = list.TaskTree.ContextMenu!;
                var openChoices = menu.Items.OfType<MenuItem>().Where(item => item.IsVisible &&
                    item.Tag?.ToString() is "OpenWorkspaceTaskHere" or "OpenWorkspaceTaskInNewTab" or "OpenWorkspaceTaskBeside").ToArray();
                await Assert.That(openChoices.Select(item => item.Tag!.ToString()).ToArray()).IsEquivalentTo(new[]
                    { "OpenWorkspaceTaskHere", "OpenWorkspaceTaskInNewTab", "OpenWorkspaceTaskBeside" });
                var newTab = openChoices.Single(item => item.Tag!.ToString() == "OpenWorkspaceTaskInNewTab");
                await Assert.That(newTab.IsEnabled).IsTrue();
                try
                {
                    await WaitAsync(window, () =>
                    {
                        var currentMenu = list.TaskTree!.ContextMenu;
                        var currentItem = currentMenu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                            item.Tag?.ToString() == "OpenWorkspaceTaskInNewTab");
                        var popup = currentItem is null ? null : TopLevel.GetTopLevel(currentItem);
                        popup?.UpdateLayout();
                        return currentMenu?.IsOpen == true && currentItem is
                            { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Bounds.Width: > 0, Bounds.Height: > 0 } && popup is not null;
                    });
                }
                catch (TimeoutException error)
                {
                    var currentMenu = list.TaskTree!.ContextMenu;
                    var diagnostics = $"menuOpen={currentMenu?.IsOpen}; focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}; " +
                        string.Join("; ", currentMenu?.Items.OfType<MenuItem>().Where(item => item.Tag?.ToString()?.StartsWith("OpenWorkspace", StringComparison.Ordinal) == true)
                            .Select(item => $"tag={item.Tag},visible={item.IsEffectivelyVisible},enabled={item.IsEnabled},bounds={item.Bounds}," +
                                $"parent={item.Parent?.GetType().Name},root={TopLevel.GetTopLevel(item)?.GetType().Name}") ?? []);
                    keyboardTimeline.Add("Keyboard menu attachment timeout: " + diagnostics);
                    throw new TimeoutException(error.Message + " " + diagnostics, error);
                }
                // Resolve the current rendered item and click in the same UI turn.
                // Do not await or fall back to the main Window for a detached popup item.
                var renderedNewTab = list.TaskTree!.ContextMenu!.Items.OfType<MenuItem>().Single(item =>
                    item.Tag?.ToString() == "OpenWorkspaceTaskInNewTab");
                var menuRoot = TopLevel.GetTopLevel(renderedNewTab)
                    ?? throw new InvalidOperationException("The rendered New Tab command is detached from its popup.");
                var menuPoint = renderedNewTab.TranslatePoint(new Point(renderedNewTab.Bounds.Width / 2, renderedNewTab.Bounds.Height / 2), menuRoot)
                    ?? throw new InvalidOperationException("The rendered New Tab command has no physical popup position.");
                menuRoot.MouseDown(menuPoint, MouseButton.Left, RawInputModifiers.None);
                menuRoot.MouseUp(menuPoint, MouseButton.Left, RawInputModifiers.None);
                Pump(window);
                await WaitAsync(window, () => pane.Tabs.Count == 3 && pane.ActiveTab.CurrentLocation is
                    { Kind: WorkspaceLocationKind.Task, Id: MainWindowViewModelFixture.RootTask1Id }
                    && shell.GetVisualDescendants().OfType<TaskCardView>().Any(card => card.IsEffectivelyVisible
                        && card.RouteTaskItem?.Id == MainWindowViewModelFixture.RootTask1Id));
                await Assert.That(pane.Tabs.Contains(firstCardTab)).IsTrue();
                await Assert.That(pane.Tabs.Contains(sourceTab)).IsTrue();
                await Assert.That(sourceTab.CurrentIndex).IsEqualTo(sourceIndex);
                await Assert.That(sourceTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(sourceHistory)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane).IsNull();
            }
            finally
            {
                if (keyboardTimeline.Count > 0)
                    Console.WriteLine("Keyboard popup timeline:" + Environment.NewLine +
                        string.Join(Environment.NewLine, keyboardTimeline));
                hotkeys.OpenContextMenu = originalContextMenuKeys;
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static TaskListDocumentView? List(Control shell) => shell.GetVisualDescendants().OfType<TaskListDocumentView>()
        .SingleOrDefault(list => list.IsEffectivelyVisible && list.Kind == TaskListKind.AllTasks);

    private static async Task<Control> TitleAsync(Window window, TaskListDocumentView list, string id)
    {
        Control? title = null;
        await WaitAsync(window, () =>
        {
            title = list.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                control.IsEffectivelyVisible &&
                (AutomationProperties.GetAutomationId(control) == "TaskTitle_" + id ||
                 AutomationProperties.GetAutomationId(control) == "InlineTaskTitleTextBlock") &&
                (control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == id ||
                 control.DataContext is TaskItemViewModel task && task.Id == id));
            title?.BringIntoView();
            return title is { Bounds.Width: > 0, Bounds.Height: > 0 };
        });
        return title!;
    }

    private static async Task ClickAsync(Window window, Control control, RawInputModifiers modifiers)
    {
        control.BringIntoView();
        await Task.Delay(20);
        Pump(window);
        var root = TopLevel.GetTopLevel(control) ?? window;
        root.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException("The task-opening gesture target is detached.");
        root.MouseDown(point, MouseButton.Left, modifiers);
        root.MouseUp(point, MouseButton.Left, modifiers);
        await Task.Delay(20);
        Pump(window);
    }

    private static async Task WaitAsync(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Pump(window); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException("The real task-opening gesture did not reach its expected workspace state.");
    }

    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
