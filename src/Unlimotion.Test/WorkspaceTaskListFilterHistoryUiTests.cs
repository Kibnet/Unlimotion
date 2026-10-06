using System;
using System.Collections.Generic;
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
using Newtonsoft.Json;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceTaskListFilterHistoryUiTests
{
    [Test]
    [Arguments(TaskListKind.LastCreated)]
    [Arguments(TaskListKind.Unlocked)]
    public async Task TwoDocuments_FilterSortExpansionAndViewportRestoreOnBack_WithoutChangingNeighbor(TaskListKind neighborKind)
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
                var tasks = new List<TaskItemViewModel>();
                for (var index = 0; index < 34; index++)
                    tasks.Add(await SeedAsync(owner, $"AC4 filter history {index:D2}", TimeSpan.FromMinutes(20)));
                var child = await owner.taskRepository!.AddChild(tasks[0]);
                child.Title = "AC4 filter history child";
                await child.FlushPendingEditorChangesAsync();
                await owner.taskRepository.TrySetStatusAsync(child.Id, DomainTaskStatus.Prepared);
                var longTask = await SeedAsync(owner, "AC4 filter history long", TimeSpan.FromMinutes(90));
                var notReady = await owner.taskRepository.Add();
                notReady.Title = "AC4 filter history not ready";
                await notReady.FlushPendingEditorChangesAsync();

                var shell = new MainScreen { DataContext = owner };
                window = new Window { Content = shell, Width = 1600, Height = 650 };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.TasksRoot);
                await WaitAsync(window, () => FindList(shell, TaskListKind.AllTasks) is { } list && Search(list) is not null);
                var left = FindList(shell, TaskListKind.AllTasks)!;
                Search(left)!.Text = "AC4 filter history";
                await WaitAsync(window, () => Roots(left).Any(wrapper => wrapper.TaskItem.Id == tasks[0].Id));

                var allPanel = await OpenFiltersAsync(window, left);
                var sort = allPanel.GetVisualDescendants().OfType<ComboBox>()
                    .Single(combo => ReferenceEquals(combo.ItemsSource, owner.SortDefinitions));
                sort.SelectedItem = owner.SortDefinitions.Single(definition => definition.Id == "title-ascending");
                await SetToggleAsync(window, Find<ComboBox>(allPanel, "AllTasksStatusFilterComboBox"),
                    owner.StatusFilters.Single(filter => filter.Status == DomainTaskStatus.NotReady), false);
                FiltersButton(left).Flyout!.Hide();
                await WaitAsync(window, () => !Roots(left).Any(wrapper => wrapper.TaskItem.Id == notReady.Id)
                    && owner.CurrentSortDefinition?.Id == "title-ascending");
                var parentWrapper = Roots(left).Single(wrapper => wrapper.TaskItem.Id == tasks[0].Id);
                parentWrapper.IsExpanded = true;
                left.TaskTree!.SelectedItem = parentWrapper;
                await WaitAsync(window, () => left.GetVisualDescendants().OfType<TreeViewItem>()
                    .Any(item => item.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == child.Id));
                Scroll(left).Offset = new Vector(0, 150);
                Pump(window);
                await Assert.That(Scroll(left).Offset.Y).IsEqualTo(150d);

                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var leftTab = primary.ActiveTab!;
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(neighborKind), WorkspaceOpenDisposition.AdjacentPane);
                await WaitAsync(window, () => FindList(shell, neighborKind) is { } list && Search(list) is not null);
                await WaitAsync(window, () => FindList(shell, TaskListKind.AllTasks) is { } currentLeft
                    && Scroll(currentLeft).Offset.Y == 150);
                left = FindList(shell, TaskListKind.AllTasks)!;
                var right = FindList(shell, neighborKind)!;
                Search(right)!.Text = "AC4 filter history";
                var rightPanel = await OpenFiltersAsync(window, right);
                if (neighborKind == TaskListKind.LastCreated)
                {
                    var date = rightPanel.GetVisualDescendants().OfType<ComboBox>()
                        .Single(combo => ReferenceEquals(combo.ItemsSource, owner.DateFilterDefinitions));
                    date.SelectedItem = owner.DateFilterDefinitions.Single(option => option.Id == "Last Week");
                    await SetToggleAsync(window, Find<ComboBox>(rightPanel, "LastCreatedStatusFilterComboBox"),
                        owner.LastCreatedStatusFilters.Single(filter => filter.Status == DomainTaskStatus.NotReady), false);
                    await Assert.That(owner.LastCreatedDateFilter.CurrentOption.Id).IsEqualTo("Last Week");
                }
                else
                {
                    var duration = rightPanel.GetVisualDescendants().OfType<ComboBox>()
                        .Single(combo => ReferenceEquals(combo.ItemsSource, owner.DurationFilters));
                    await SetToggleAsync(window, duration,
                        owner.DurationFilters.Single(filter => filter.ResourceKey == "DurationFilter5mTo30m"), true);
                    await SetToggleAsync(window, Find<ComboBox>(rightPanel, "UnlockedStatusFilterComboBox"),
                        owner.UnlockedStatusFilters.Single(filter => filter.Status == DomainTaskStatus.NotReady), false);
                }
                FiltersButton(right).Flyout!.Hide();
                try
                {
                    await WaitAsync(window, () => Roots(right).Count() >= 30
                        && Roots(right).Any(wrapper => wrapper.TaskItem.Id == tasks[1].Id)
                        && (neighborKind != TaskListKind.Unlocked || !Roots(right).Any(wrapper => wrapper.TaskItem.Id == longTask.Id)));
                }
                catch (TimeoutException error)
                {
                    var diagnostic = JsonConvert.SerializeObject(new
                    {
                        neighborKind, Now = DateTimeOffset.Now, SearchText = Search(right)?.Text,
                        Filters = right.Document.CaptureFilters(),
                        Date = new { owner.LastCreatedDateFilter.CurrentOption.Id, owner.LastCreatedDateFilter.From,
                            owner.LastCreatedDateFilter.To, owner.LastCreatedDateFilter.IsCustom },
                        Statuses = owner.LastCreatedStatusFilters.Select(filter => new { filter.Status, filter.ShowTasks }),
                        ProjectionCount = owner.LastCreatedItems.Count,
                        Roots = Roots(right).Select(wrapper => new { wrapper.TaskItem.Id, wrapper.TaskItem.Title, wrapper.TaskItem.Status }),
                        Seeded = tasks.Select(task =>
                        {
                            var current = owner.ResolveTaskById(task.Id);
                            return new { task.Id, SameCachedInstance = ReferenceEquals(task, current),
                                Title = current?.Title, Status = current?.Status, CreatedDateTime = current?.CreatedDateTime,
                                PredicateDate = current?.CreatedDateTime.Add(DateTimeOffset.Now.Offset).Date,
                                ModelDate = current?.Model.CreatedDateTime, PlannedDuration = current?.PlannedDuration };
                        })
                    });
                    Console.WriteLine("AC4 neighbor filtering timeout: " + diagnostic);
                    throw new TimeoutException(error.Message + " Diagnostic: " + diagnostic, error);
                }
                right.TaskTree!.SelectedItem = Roots(right).Single(wrapper => wrapper.TaskItem.Id == tasks[1].Id);
                Pump(window);
                Scroll(right).Offset = new Vector(0, 120);
                Pump(window);
                await Assert.That(Scroll(right).Offset.Y).IsEqualTo(120d);
                var secondary = owner.WorkspaceNavigation.SecondaryPane!;
                var rightTab = secondary.ActiveTab!;
                var leftState = (TaskListDocumentState)left.CaptureViewState();
                var rightState = (TaskListDocumentState)right.CaptureViewState();
                await Assert.That(leftState.ExpandedIds.Count).IsGreaterThan(0);
                var leftJson = JsonConvert.SerializeObject(leftState);
                var rightJson = JsonConvert.SerializeObject(rightState);
                var leftHistory = leftTab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var rightHistory = rightTab.History.Select(entry => entry.Location.HistoryKey).ToArray();

                owner.ActivateWorkspacePane(primary);
                await Assert.That(await owner.OpenWorkspaceTaskAsync(tasks[2])).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync(primary)).IsTrue();
                await WaitAsync(window, () => FindList(shell, TaskListKind.AllTasks) is { } restored
                    && JsonConvert.SerializeObject(restored.CaptureViewState()) == leftJson);
                left = FindList(shell, TaskListKind.AllTasks)!;
                right = FindList(shell, neighborKind)!;
                await Assert.That(JsonConvert.SerializeObject(right.CaptureViewState())).IsEqualTo(rightJson);
                await Assert.That(rightTab.History.Select(entry => entry.Location.HistoryKey).ToArray()).IsEquivalentTo(rightHistory);
                await Assert.That(leftTab.CurrentIndex).IsEqualTo(leftHistory.Length - 1);
                await Assert.That(leftTab.CurrentEntry!.ViewState is TaskListDocumentState).IsTrue();
                await Assert.That(JsonConvert.SerializeObject(leftTab.CurrentEntry.ViewState)).IsEqualTo(leftJson);

                owner.ActivateWorkspacePane(secondary);
                await Assert.That(await owner.OpenWorkspaceTaskAsync(tasks[3])).IsTrue();
                await Assert.That(await owner.NavigateWorkspaceBackAsync(secondary)).IsTrue();
                await WaitAsync(window, () => FindList(shell, neighborKind) is { } restored
                    && JsonConvert.SerializeObject(restored.CaptureViewState()) == rightJson);
                right = FindList(shell, neighborKind)!;
                left = FindList(shell, TaskListKind.AllTasks)!;
                await Assert.That(JsonConvert.SerializeObject(left.CaptureViewState())).IsEqualTo(leftJson);
                await Assert.That(JsonConvert.SerializeObject(rightTab.CurrentEntry!.ViewState)).IsEqualTo(rightJson);
                await Assert.That(rightTab.CurrentIndex).IsEqualTo(rightHistory.Length - 1);
                await Assert.That(leftTab.History.Take(leftHistory.Length).Select(entry => entry.Location.HistoryKey).ToArray())
                    .IsEquivalentTo(leftHistory);
                await Assert.That(leftTab.History.Count).IsEqualTo(leftHistory.Length + 1);
                await Assert.That(rightTab.History.Count).IsEqualTo(rightHistory.Length + 1);
                await Assert.That(Scroll(left).Offset.Y).IsEqualTo(150d);
                await Assert.That(Scroll(right).Offset.Y).IsEqualTo(120d);
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task<TaskItemViewModel> SeedAsync(MainWindowViewModel owner, string title, TimeSpan duration)
    {
        var task = await owner.taskRepository!.Add();
        task.Title = title;
        task.PlannedDuration = duration;
        await task.FlushPendingEditorChangesAsync();
        await owner.taskRepository.TrySetStatusAsync(task.Id, DomainTaskStatus.Prepared);
        return task;
    }

    private static TaskListDocumentView? FindList(Control shell, TaskListKind kind) => shell.GetVisualDescendants()
        .OfType<TaskListDocumentView>().SingleOrDefault(view => view.IsEffectivelyVisible && view.Kind == kind);
    private static TextBox? Search(TaskListDocumentView list) => list.GetVisualDescendants().OfType<TextBox>()
        .SingleOrDefault(input => AutomationProperties.GetAutomationId(input) == "TaskListSearchBox");
    private static IEnumerable<TaskWrapperViewModel> Roots(TaskListDocumentView list) =>
        list.TaskTree!.ItemsSource?.Cast<TaskWrapperViewModel>() ?? [];
    private static ScrollViewer Scroll(TaskListDocumentView list) => list.TaskTree!.GetVisualDescendants().OfType<ScrollViewer>().First();
    private static DropDownButton FiltersButton(TaskListDocumentView list) => Find<DropDownButton>(list, $"{list.Kind}FiltersButton");
    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static async Task<Control> OpenFiltersAsync(Window window, TaskListDocumentView list)
    {
        var button = FiltersButton(list);
        await ClickAsync(window, button);
        var flyout = (Flyout)button.Flyout!;
        await WaitAsync(window, () => flyout.IsOpen);
        return (Control)flyout.Content!;
    }

    private static async Task SetToggleAsync(Window window, ComboBox combo, object filter, bool value)
    {
        var index = combo.Items.Cast<object>().ToList().FindIndex(item => ReferenceEquals(item, filter));
        await Assert.That(index).IsGreaterThanOrEqualTo(0);
        combo.IsDropDownOpen = true;
        await WaitAsync(window, () => combo.ContainerFromIndex(index)?.GetVisualDescendants().OfType<CheckBox>().Any() == true);
        var check = combo.ContainerFromIndex(index)!.GetVisualDescendants().OfType<CheckBox>().Single();
        await Assert.That(check.IsEffectivelyVisible).IsTrue();
        if (check.IsChecked != value) await ClickAsync(window, check);
        await Assert.That(check.IsChecked).IsEqualTo(value);
        combo.IsDropDownOpen = false;
        Pump(window);
    }

    private static async Task ClickAsync(Window window, Control control)
    {
        control.BringIntoView();
        await Task.Delay(20);
        Pump(window);
        var root = TopLevel.GetTopLevel(control) ?? window;
        root.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException("Filter control is detached.");
        root.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        root.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Pump(window);
    }

    private static async Task WaitAsync(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Pump(window); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException("The task-list filter/history UI did not reach the saved state.");
    }

    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
