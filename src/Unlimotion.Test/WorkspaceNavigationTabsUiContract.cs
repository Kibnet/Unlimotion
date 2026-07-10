using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

internal static class WorkspaceNavigationTabsUiContract
{
    private static readonly string[] RequiredTabAutomationIds =
    [
        "AllTasksTabItem",
        "LastCreatedTabItem",
        "LastUpdatedTabItem",
        "UnlockedTabItem",
        "InProgressTabItem",
        "CompletedTabItem",
        "ArchivedTabItem",
        "LastOpenedTabItem",
        "RoadmapTabItem",
        "SettingsTabItem"
    ];

    public static async Task<WorkspaceNavigationTabsScenarioResult> AssertWorkspaceNavigationTabsScenarioAsync()
    {
        var result = await ExecuteWorkspaceNavigationTabsScenarioAsync();

        await AssertWorkspaceNavigationTabsScenarioResultAsync(result);

        return result;
    }

    public static async Task<WorkspaceNavigationTabsScenarioResult> ExecuteWorkspaceNavigationTabsScenarioAsync()
    {
        var result = new WorkspaceNavigationTabsScenarioResult();
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        try
        {
            await session.DispatchAsync(async () =>
            {
                var fixture = new MainWindowViewModelFixture();
                Window? window = null;

                try
                {
                    var vm = fixture.MainWindowViewModelTest;
                    await vm.Connect();
                    vm.AllTasksMode = true;
                    SetDateFilterAllTime(vm.LastCreatedDateFilter);

                    var repository = vm.taskRepository
                        ?? throw new InvalidOperationException("Task repository was not initialized.");
                    var inProgressTask = await CreateTaskWithStatusAsync(
                        repository,
                        DomainTaskStatus.InProgress,
                        "BDD workspace tabs in-progress task");
                    var preparedTask = await CreateTaskWithStatusAsync(
                        repository,
                        DomainTaskStatus.Prepared,
                        "BDD workspace tabs prepared task");

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    result.MainControlOpened = true;

                    result.MainTabsAvailable = RequiredTabAutomationIds
                        .All(id => FindControlByAutomationId<TabItem>(view, id) is not null);

                    SelectTab(view, "AllTasksTabItem");
                    result.AllTasksProjectionAvailable = await WaitUntilAsync(() =>
                        vm.CurrentAllTasksItems.Any() &&
                        ContainsWrapper(vm.CurrentAllTasksItems, inProgressTask.Id) &&
                        ContainsWrapper(vm.CurrentAllTasksItems, preparedTask.Id));

                    SelectTab(view, "LastCreatedTabItem");
                    result.LastCreatedProjectionAvailable = await WaitUntilAsync(() =>
                        ContainsWrapper(vm.LastCreatedItems, preparedTask.Id));

                    vm.CurrentTaskItem = preparedTask;
                    Dispatcher.UIThread.RunJobs();
                    result.LastCreatedSelectionSynced = await WaitUntilAsync(() =>
                        vm.CurrentLastCreated?.TaskItem.Id == preparedTask.Id);

                    SelectTab(view, "InProgressTabItem");
                    result.InProgressProjectionAvailable = await WaitUntilAsync(() =>
                    {
                        var ids = Flatten(vm.InProgressItems)
                            .Select(wrapper => wrapper.TaskItem.Id)
                            .ToList();
                        return ids.Contains(inProgressTask.Id) && !ids.Contains(preparedTask.Id);
                    });

                    vm.CurrentTaskItem = inProgressTask;
                    Dispatcher.UIThread.RunJobs();
                    result.InProgressSelectionSynced = await WaitUntilAsync(() =>
                        vm.CurrentInProgressItem?.TaskItem.Id == inProgressTask.Id);

                    SelectTab(view, "AllTasksTabItem");
                    result.AllTasksSelectionRestored = await WaitUntilAsync(() =>
                        vm.CurrentAllTasksItem?.TaskItem.Id == inProgressTask.Id);
                }
                finally
                {
                    window?.Close();
                    fixture.CleanTasks();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }

        return result;
    }

    public static async Task AssertWorkspaceNavigationTabsScenarioResultAsync(
        WorkspaceNavigationTabsScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.MainTabsAvailable).IsTrue();
        await Assert.That(result.AllTasksProjectionAvailable).IsTrue();
        await Assert.That(result.LastCreatedProjectionAvailable).IsTrue();
        await Assert.That(result.LastCreatedSelectionSynced).IsTrue();
        await Assert.That(result.InProgressProjectionAvailable).IsTrue();
        await Assert.That(result.InProgressSelectionSynced).IsTrue();
        await Assert.That(result.AllTasksSelectionRestored).IsTrue();
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1800,
            Height = 1000,
            Content = content
        };
    }

    private static async Task<TaskItemViewModel> CreateTaskWithStatusAsync(
        ITaskStorage repository,
        DomainTaskStatus status,
        string title)
    {
        var task = await repository.Add();
        task.Title = title;
        var isInitializedProvider = task.IsInitializedProvider;
        task.IsInitializedProvider = () => false;
        try
        {
            task.Status = status;
            await repository.Update(task);
        }
        finally
        {
            task.IsInitializedProvider = isInitializedProvider;
        }

        Dispatcher.UIThread.RunJobs();
        var updated = repository.Tasks.Lookup(task.Id);
        return updated.HasValue ? updated.Value : task;
    }

    private static void SetDateFilterAllTime(DateFilter filter)
    {
        filter.CurrentOption = DateFilterDefinition.AllTime;
        filter.SetDateTimes(DateFilterDefinition.AllTime);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SelectTab(MainControl view, string automationId)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedItem = FindControlByAutomationId<TabItem>(view, automationId);
        Dispatcher.UIThread.RunJobs();
    }

    private static T? FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));
    }

    private static bool ContainsWrapper(
        IEnumerable<TaskWrapperViewModel> roots,
        string taskId)
    {
        return Flatten(roots).Any(wrapper => string.Equals(wrapper.TaskItem.Id, taskId, StringComparison.Ordinal));
    }

    private static IEnumerable<TaskWrapperViewModel> Flatten(IEnumerable<TaskWrapperViewModel> roots)
    {
        foreach (var wrapper in roots)
        {
            yield return wrapper;

            foreach (var child in Flatten(wrapper.SubTasks))
            {
                yield return child;
            }
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        return await TestHelpers.WaitUntilAsync(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return predicate();
            },
            TimeSpan.FromSeconds(2));
    }
}

internal sealed class WorkspaceNavigationTabsScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool MainTabsAvailable { get; set; }

    public bool AllTasksProjectionAvailable { get; set; }

    public bool LastCreatedProjectionAvailable { get; set; }

    public bool LastCreatedSelectionSynced { get; set; }

    public bool InProgressProjectionAvailable { get; set; }

    public bool InProgressSelectionSynced { get; set; }

    public bool AllTasksSelectionRestored { get; set; }
}
