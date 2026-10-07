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
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskPresentationTouchUiTests
{
    [Test]
    [Arguments(TaskListKind.AllTasks)]
    [Arguments(TaskListKind.LastCreated)]
    [Arguments(TaskListKind.LastUpdated)]
    [Arguments(TaskListKind.Unlocked)]
    [Arguments(TaskListKind.InProgress)]
    [Arguments(TaskListKind.Completed)]
    [Arguments(TaskListKind.Archived)]
    [Arguments(TaskListKind.LastOpened)]
    public async Task EveryTaskList_ExposesTouchOpeningActionsAndReusesTheCorrectCardAtNarrowWidth(TaskListKind kind)
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
                var task = owner.ResolveTaskById(MainWindowViewModelFixture.RootTask1Id)!;
                var status = kind switch
                {
                    TaskListKind.InProgress => DomainTaskStatus.InProgress,
                    TaskListKind.Completed => DomainTaskStatus.Completed,
                    TaskListKind.Archived => DomainTaskStatus.Archived,
                    _ => DomainTaskStatus.Prepared
                };
                await owner.taskRepository!.TrySetStatusAsync(task.Id, status);
                await Assert.That(task.Status).IsEqualTo(status);
                foreach (var date in new[] { owner.LastCreatedDateFilter, owner.LastUpdatedDateFilter,
                             owner.CompletedDateFilter, owner.ArchivedDateFilter })
                {
                    date.CurrentOption = DateFilterDefinition.AllTime;
                    date.SetDateTimes(DateFilterDefinition.AllTime);
                }
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 640, Height = 800, Content = shell };
                window.Show();
                if (kind == TaskListKind.LastOpened)
                {
                    await Assert.That(await owner.OpenWorkspaceTaskAsync(task)).IsTrue();
                    await WaitAsync(window, () => shell.GetVisualDescendants().OfType<TaskCardView>()
                        .Any(card => card.IsEffectivelyVisible && card.RouteTaskItem?.Id == task.Id));
                }
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(kind));
                await WaitAsync(window, () => FindList(shell, kind)?.TaskTree?.Items.OfType<TaskWrapperViewModel>()
                    .Any(wrapper => wrapper.TaskItem.Id == task.Id) == true);
                var sourcePane = owner.WorkspaceNavigation.PrimaryPane;
                var sourceTab = sourcePane.ActiveTab!;
                var history = sourceTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var list = FindList(shell, kind)!;
                var action = await FindActionAsync(window, list, task.Id, kind);
                AssertActionGeometry(window, action, task.Id);
                await ClickAsync(window, action);
                await WaitAsync(window, () => action.ContextMenu?.IsOpen == true);
                var choices = action.ContextMenu!.Items.OfType<MenuItem>().Where(item => item.IsVisible).ToArray();
                await Assert.That(choices.Select(item => item.Header?.ToString()).ToArray()).IsEquivalentTo(new[]
                    { Localization.Get("WorkspaceOpenHere"), Localization.Get("WorkspaceOpenInNewTab"), Localization.Get("WorkspaceOpenBeside") });
                await Assert.That(choices.All(item => item.IsEnabled && item.MinHeight >= 44)).IsTrue();
                var beside = (int)kind % 2 == 1;
                var desired = Localization.Get(beside ? "WorkspaceOpenBeside" : "WorkspaceOpenInNewTab");
                await ClickAsync(window, choices.Single(item => Equals(item.Header, desired)));
                await WaitAsync(window, () => owner.WorkspaceNavigation.ActiveTab.CurrentLocation is
                    { Kind: WorkspaceLocationKind.Task } current && current.Id == task.Id
                    && shell.GetVisualDescendants().OfType<TaskCardView>().Any(card => card.IsEffectivelyVisible
                        && card.RouteTaskItem?.Id == task.Id));
                var cardTab = owner.WorkspaceNavigation.ActiveTab;
                var cardPane = owner.WorkspaceNavigation.ActivePane;
                await Assert.That(cardTab).IsNotSameReferenceAs(sourceTab);
                await Assert.That(owner.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count)).IsEqualTo(2);
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane is not null).IsEqualTo(beside);
                await Assert.That(sourceTab.CurrentLocation!.TaskListKind).IsEqualTo(kind);
                await Assert.That(sourceTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(history)).IsTrue();

                var sourceButtonId = beside ? "WorkspacePrimaryPaneSelector" : "WorkspaceTab-" + sourceTab.Id.ToString("N");
                await WaitAsync(window, () => shell.GetVisualDescendants().OfType<Button>().Any(button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == sourceButtonId));
                var sourceButton = shell.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == sourceButtonId);
                await ClickAsync(window, sourceButton);
                await WaitAsync(window, () => ReferenceEquals(owner.WorkspaceNavigation.ActiveTab, sourceTab));
                list = FindList(shell, kind)!;
                action = await FindActionAsync(window, list, task.Id, kind);
                AssertActionGeometry(window, action, task.Id);
                await ClickAsync(window, action);
                await WaitAsync(window, () => action.ContextMenu?.IsOpen == true);
                var reuse = action.ContextMenu!.Items.OfType<MenuItem>().Where(item => item.IsVisible).ToArray();
                await Assert.That(reuse.Length).IsEqualTo(1);
                await Assert.That(reuse[0].Header).IsEqualTo(Localization.Get("WorkspaceShowExisting"));
                await ClickAsync(window, reuse[0]);
                await WaitAsync(window, () => ReferenceEquals(owner.WorkspaceNavigation.ActiveTab, cardTab)
                    && ReferenceEquals(owner.WorkspaceNavigation.ActivePane, cardPane));
                await Assert.That(owner.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count)).IsEqualTo(2);
                await Assert.That(sourceTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(history)).IsTrue();
                await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Single(card => card.IsEffectivelyVisible)
                    .RouteTaskItem?.Id).IsEqualTo(task.Id);
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static TaskListDocumentView? FindList(Control root, TaskListKind kind) => root.GetVisualDescendants()
        .OfType<TaskListDocumentView>().SingleOrDefault(list => list.IsEffectivelyVisible && list.Kind == kind);

    private static async Task<Button> FindActionAsync(Window window, TaskListDocumentView list, string id, TaskListKind kind)
    {
        Button? action = null;
        await WaitAsync(window, () =>
        {
            list.TaskTree!.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(row =>
                row.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == id)?.BringIntoView();
            action = list.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.IsEffectivelyVisible
                && AutomationProperties.GetAutomationId(button) == "TaskOpenActions_" + id);
            action?.BringIntoView();
            // BringIntoView queues a scrolling/layout operation. Its existing bounds
            // alone do not mean a physical click can reach an off-screen row yet.
            return action is { Bounds.Width: >= 32, Bounds.Height: >= 32 }
                && new Rect(window.Bounds.Size).Contains(BoundsInWindow(window, action));
        }, $"{kind} has the exact task row {id}, but did not expose its visible touch action button.");
        return action!;
    }

    private static void AssertActionGeometry(Window window, Button action, string id)
    {
        var actionRect = BoundsInWindow(window, action);
        if (!new Rect(window.Bounds.Size).Contains(actionRect))
            throw new InvalidOperationException($"Touch action for {id} is clipped at 640px: {actionRect}.");
        var row = action.GetVisualAncestors().OfType<TreeViewItem>().First();
        var title = row.GetVisualDescendants().OfType<Control>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "TaskTitle_" + id);
        if (actionRect.Intersects(BoundsInWindow(window, title)))
            throw new InvalidOperationException($"Touch action overlaps the title for {id}.");
        foreach (var remove in row.GetVisualDescendants().OfType<Button>()
                     .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("TaskRowRemoveButton")))
            if (actionRect.Intersects(BoundsInWindow(window, remove)))
                throw new InvalidOperationException($"Touch action overlaps the remove command for {id}.");
    }

    private static Rect BoundsInWindow(Window window, Control control) => new(
        control.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("Touch target is detached."),
        control.Bounds.Size);

    private static async Task ClickAsync(Window window, Control control)
    {
        control.BringIntoView();
        await Task.Delay(20);
        Pump(window);
        var root = TopLevel.GetTopLevel(control) ?? window;
        root.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException("Touch command has no input position.");
        root.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        root.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await Task.Delay(20);
        Pump(window);
    }

    private static async Task WaitAsync(Window window, Func<bool> condition, string? message = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Pump(window); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException(message ?? "The touch opening command did not produce the correct workspace document.");
    }

    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
