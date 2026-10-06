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
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceTaskProjectionUiTests
{
    [Test]
    public async Task SharedTaskCardEdits_UpdateBothListsWithoutChangingTheirIndependentState()
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
                var shared = await SeedTaskAsync(owner, "Projection Shared task");
                var neighbor = await SeedTaskAsync(owner, "Projection Shared neighbor");
                var leftOnly = await SeedTaskAsync(owner, "Projection Only left");
                await owner.taskRepository!.TrySetStatusAsync(shared.Id, DomainTaskStatus.Prepared);
                await Assert.That(shared.Status).IsEqualTo(DomainTaskStatus.Prepared);
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.TasksRoot);
                await WaitAsync(window, () => FindList(shell, TaskListKind.AllTasks)?.GetVisualDescendants()
                    .OfType<TextBox>().Any(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox") == true);
                var left = FindList(shell, TaskListKind.AllTasks)!;
                Find<TextBox>(left, "TaskListSearchBox").Text = "Projection";
                await WaitAsync(window, () => Rows(left).Length == 3);
                left.TaskTree!.SelectedItem = left.TaskTree.ItemsSource!.Cast<TaskWrapperViewModel>()
                    .Single(item => item.TaskItem.Id == neighbor.Id);
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var primaryTab = primary.ActiveTab ?? throw new InvalidOperationException("Primary tab is missing.");
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(TaskListKind.LastCreated),
                    WorkspaceOpenDisposition.AdjacentPane);
                await WaitAsync(window, () => FindList(shell, TaskListKind.LastCreated)?.GetVisualDescendants()
                    .OfType<TextBox>().Any(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox") == true);
                var right = FindList(shell, TaskListKind.LastCreated)!;
                Find<TextBox>(right, "TaskListSearchBox").Text = "Projection Shared";
                owner.LastCreatedDateFilter.CurrentOption = DateFilterDefinition.AllTime;
                owner.LastCreatedDateFilter.SetDateTimes(DateFilterDefinition.AllTime);
                owner.LastCreatedStatusFilters.Single(filter => filter.Status == DomainTaskStatus.Completed).ShowTasks = false;
                await WaitAsync(window, () => Rows(right).Length == 2);
                right.TaskTree!.SelectedItem = right.TaskTree.ItemsSource!.Cast<TaskWrapperViewModel>()
                    .Single(item => item.TaskItem.Id == shared.Id);
                Pump(window);
                var secondary = owner.WorkspaceNavigation.SecondaryPane!;
                var secondaryTab = secondary.ActiveTab ?? throw new InvalidOperationException("Secondary tab is missing.");
                var historyLeft = primaryTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var historyRight = secondaryTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var indexLeft = primaryTab.CurrentIndex;
                var indexRight = secondaryTab.CurrentIndex;
                var filtersLeft = JsonConvert.SerializeObject(left.Document.CaptureFilters());
                var filtersRight = JsonConvert.SerializeObject(right.Document.CaptureFilters());
                var offsetLeft = Scroll(left).Offset;
                var offsetRight = Scroll(right).Offset;
                await Assert.That(Rows(left)).Contains(leftOnly.Id);
                await Assert.That(Rows(right)).DoesNotContain(leftOnly.Id);

                await Assert.That(await owner.OpenWorkspaceTaskAsync(shared)).IsTrue();
                await WaitAsync(window, () => shell.GetVisualDescendants().OfType<TaskCardView>()
                    .Any(card => card.IsEffectivelyVisible && ReferenceEquals(card.RouteTaskItem, shared)));
                var card = shell.GetVisualDescendants().OfType<TaskCardView>().Single(control => control.IsEffectivelyVisible);
                await Assert.That(card.GetVisualDescendants().OfType<TaskListDocumentView>().Any()).IsFalse();
                const string description = "Shared projection context edited through the standalone card";
                var descriptionInput = Find<TextBox>(card, "CurrentTaskDescriptionTextBox");
                descriptionInput.Focus();
                descriptionInput.Text = description;
                Find<TextBox>(card, "CurrentTaskTitleTextBox").Focus();
                var datePickers = card.GetVisualDescendants().OfType<CalendarDatePicker>()
                    .Where(picker => ReferenceEquals(picker.DataContext, shared)).ToArray();
                await Assert.That(datePickers.Length).IsEqualTo(2);
                // InProgress is intentionally unavailable before a task's planned start.
                var begin = DateTime.Today.AddDays(-1);
                var end = DateTime.Today.AddDays(1);
                datePickers[0].SelectedDate = begin;
                datePickers[1].SelectedDate = end;
                var duration = Find<DropDownButton>(card, "CurrentTaskSetDurationButton");
                await ClickAsync(window, duration);
                var durationFlyout = (MenuFlyout)duration.Flyout!;
                await Assert.That(durationFlyout.IsOpen).IsTrue();
                var twentyMinutes = durationFlyout.Items.OfType<MenuItem>()
                    .Single(item => ReferenceEquals(item.Command, shared.SetDurationCommands.TwentyMinutesCommand));
                await Assert.That(twentyMinutes.IsEnabled).IsTrue();
                await ClickAsync(window, twentyMinutes);
                durationFlyout.Hide();
                var status = Find<TaskStatusPicker>(card, "CurrentTaskStatusButton");
                await ClickAsync(window, status);
                var statusFlyout = (MenuFlyout)status.Flyout!;
                await Assert.That(statusFlyout.IsOpen).IsTrue();
                var inProgress = statusFlyout.Items.OfType<MenuItem>().Single(item =>
                    AutomationProperties.GetAutomationId(item) == "TaskStatusOptionInProgress");
                await Assert.That(inProgress.IsEnabled).IsTrue();
                await ClickAsync(window, inProgress);
                statusFlyout.Hide();
                await WaitAsync(window, () => shared.Status == DomainTaskStatus.InProgress);
                await Assert.That(await owner.NavigateWorkspaceBackAsync()).IsTrue();
                await WaitAsync(window, () => FindList(shell, TaskListKind.LastCreated) is { } restored
                    && Rows(restored).Length == 2 && FindRenderedStatus(left, shared.Id)?.Task?.Status == DomainTaskStatus.InProgress
                    && FindRenderedStatus(restored, shared.Id)?.Task?.Status == DomainTaskStatus.InProgress);
                right = FindList(shell, TaskListKind.LastCreated)!;

                await Assert.That(owner.WorkspaceNavigation.PrimaryPane).IsSameReferenceAs(primary);
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane).IsSameReferenceAs(secondary);
                await Assert.That(primary.ActiveTab).IsSameReferenceAs(primaryTab);
                await Assert.That(secondary.ActiveTab).IsSameReferenceAs(secondaryTab);
                await Assert.That(primaryTab.CurrentIndex).IsEqualTo(indexLeft);
                await Assert.That(secondaryTab.CurrentIndex).IsEqualTo(indexRight);
                await Assert.That(primaryTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(historyLeft)).IsTrue();
                await Assert.That(secondaryTab.History.Count).IsEqualTo(historyRight.Length + 1);
                await Assert.That(secondaryTab.History.Take(historyRight.Length)
                    .Select(entry => entry.Location.HistoryKey).SequenceEqual(historyRight)).IsTrue();
                await Assert.That(secondaryTab.History.Last().Location.Id).IsEqualTo(shared.Id);
                await Assert.That(JsonConvert.SerializeObject(left.Document.CaptureFilters())).IsEqualTo(filtersLeft);
                await Assert.That(JsonConvert.SerializeObject(right.Document.CaptureFilters())).IsEqualTo(filtersRight);
                await Assert.That(((TaskWrapperViewModel)left.TaskTree!.SelectedItem!).TaskItem.Id).IsEqualTo(neighbor.Id);
                await Assert.That(((TaskWrapperViewModel)right.TaskTree!.SelectedItem!).TaskItem.Id).IsEqualTo(shared.Id);
                await Assert.That(Scroll(left).Offset).IsEqualTo(offsetLeft);
                await Assert.That(Scroll(right).Offset).IsEqualTo(offsetRight);
                await Assert.That(Rows(left).Order().SequenceEqual(new[] { shared.Id, neighbor.Id, leftOnly.Id }.Order())).IsTrue();
                await Assert.That(Rows(right).Order().SequenceEqual(new[] { shared.Id, neighbor.Id }.Order())).IsTrue();
                // Lists expose status and title, not editable description/date fields.
                // Validate their actual visible status; validate every edited field on disk.
                var file = Path.Combine(fixture.DefaultTasksFolderPath, shared.Id);
                TaskItem? saved = null;
                await WaitAsync(window, () =>
                {
                    try { saved = JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(file)); }
                    catch (IOException) { return false; }
                    return saved?.Description == description && saved.Status == DomainTaskStatus.InProgress
                        && saved.PlannedDuration == TimeSpan.FromMinutes(20)
                        && saved.PlannedBeginDateTime?.Date == begin && saved.PlannedEndDateTime?.Date == end;
                });
                await Assert.That(saved!.Description).IsEqualTo(description);
                await Assert.That(saved.PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(20));
                await Assert.That(saved.PlannedBeginDateTime?.Date).IsEqualTo(begin);
                await Assert.That(saved.PlannedEndDateTime?.Date).IsEqualTo(end);
                await Assert.That(saved.Status).IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(shared.HasPendingEditorPersistence).IsFalse();
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task<TaskItemViewModel> SeedTaskAsync(MainWindowViewModel owner, string title)
    {
        var task = await owner.taskRepository!.Add();
        var initialized = task.IsInitializedProvider;
        task.IsInitializedProvider = () => false;
        try { task.Title = title; await owner.taskRepository.Update(task); }
        finally { task.IsInitializedProvider = initialized; }
        return task;
    }

    private static TaskListDocumentView? FindList(Control shell, TaskListKind kind) => shell.GetVisualDescendants()
        .OfType<TaskListDocumentView>().SingleOrDefault(list => list.IsEffectivelyVisible && list.Kind == kind);
    private static string[] Rows(TaskListDocumentView list) => list.TaskTree!.ItemsSource!
        .Cast<TaskWrapperViewModel>().Select(item => item.TaskItem.Id).ToArray();
    private static ScrollViewer Scroll(TaskListDocumentView list) => list.TaskTree!.GetVisualDescendants().OfType<ScrollViewer>().First();
    private static TaskStatusPicker? FindRenderedStatus(TaskListDocumentView list, string id) => list.GetVisualDescendants()
        .OfType<TaskStatusPicker>().FirstOrDefault(control => control.IsEffectivelyVisible && control.Task?.Id == id);
    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Single(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == id);
    private static async Task ClickAsync(Window window, Control control)
    {
        control.BringIntoView();
        await Task.Delay(20);
        Pump(window);
        var inputRoot = TopLevel.GetTopLevel(control) ?? window;
        inputRoot.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), inputRoot)
            ?? throw new InvalidOperationException("The card command is not attached to an input surface.");
        inputRoot.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        inputRoot.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Pump(window);
        await Task.Delay(20);
    }
    private static async Task WaitAsync(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Pump(window); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException("The shared task projections did not reach the expected UI/storage state.");
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
