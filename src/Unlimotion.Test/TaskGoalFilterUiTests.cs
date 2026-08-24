using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
public sealed class TaskGoalFilterUiTests
{
    private static readonly (int TabIndex, string ButtonId, string ComboBoxId)[] GoalFilterTabs =
    [
        (0, "AllTasksFiltersButton", "AllTasksGoalFilterComboBox"),
        (1, "LastCreatedFiltersButton", "LastCreatedGoalFilterComboBox"),
        (2, "LastUpdatedFiltersButton", "LastUpdatedGoalFilterComboBox"),
        (3, "UnlockedFiltersButton", "UnlockedGoalFilterComboBox"),
        (4, "InProgressFiltersButton", "InProgressGoalFilterComboBox"),
        (5, "CompletedFiltersButton", "CompletedGoalFilterComboBox"),
        (6, "ArchivedFiltersButton", "ArchivedGoalFilterComboBox"),
        (7, "LastOpenedFiltersButton", "LastOpenedGoalFilterComboBox")
    ];

    [Test]
    public async Task GoalFilterComboBox_IsAvailableOnEveryTaskList_WithThreeModes()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                foreach (var (tabIndex, buttonId, comboBoxId) in GoalFilterTabs)
                {
                    SelectTab(view, tabIndex);
                    var comboBox = OpenFilterPanelAndFindComboBox(view, buttonId, comboBoxId);
                    var options = ReadGoalFilterOptions(comboBox);

                    await Assert.That(options.Select(option => option.Mode))
                        .IsEquivalentTo(Enum.GetValues<TaskGoalFilterMode>());
                    await Assert.That(comboBox.SelectedItem)
                        .IsEqualTo(TaskGoalFilterOption.Find(vm.GoalFilterMode));

                    HideFilterPanel(view, buttonId);
                }

                SelectTab(view, 0);
                var allTasksFilter = OpenFilterPanelAndFindComboBox(
                    view,
                    "AllTasksFiltersButton",
                    "AllTasksGoalFilterComboBox");
                allTasksFilter.SelectedItem = TaskGoalFilterOption.Find(TaskGoalFilterMode.Goals);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.Goals);
                vm.ResetCurrentTabFilters();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.GoalFilterMode).IsEqualTo(TaskGoalFilterMode.All);
                await Assert.That(allTasksFilter.SelectedItem)
                    .IsEqualTo(TaskGoalFilterOption.Find(TaskGoalFilterMode.All));
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task GoalFilter_FiltersRootsAndReactsWhenTaskClassificationChanges()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                Dispatcher.UIThread.RunJobs();

                var rootsLoaded = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return vm.CurrentAllTasksItems.Count >= 2;
                }, TimeSpan.FromSeconds(2));
                await Assert.That(rootsLoaded).IsTrue();

                var roots = vm.CurrentAllTasksItems.Take(2).Select(wrapper => wrapper.TaskItem).ToArray();
                var first = roots[0];
                var second = roots[1];
                first.IsGoal = true;
                second.IsGoal = false;

                vm.GoalFilterMode = TaskGoalFilterMode.Goals;
                await AssertProjectionAsync(vm, first.Id, expectedFirst: true, second.Id, expectedSecond: false);

                first.IsGoal = false;
                second.IsGoal = true;
                await AssertProjectionAsync(vm, first.Id, expectedFirst: false, second.Id, expectedSecond: true);

                vm.GoalFilterMode = TaskGoalFilterMode.Regular;
                await AssertProjectionAsync(vm, first.Id, expectedFirst: true, second.Id, expectedSecond: false);

                vm.GoalFilterMode = TaskGoalFilterMode.All;
                await AssertProjectionAsync(vm, first.Id, expectedFirst: true, second.Id, expectedSecond: true);
            }
            finally
            {
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task GoalIndicator_IsShownInTaskRowAndCurrentTaskCardOnlyForGoals()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;
                var task = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                task.IsGoal = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var cardIndicator = FindControlByAutomationId<TextBlock>(view, "CurrentTaskGoalIndicator");
                var rowIndicator = FindTaskRowGoalIndicator(view, task);
                using (Assert.Multiple())
                {
                    await Assert.That(cardIndicator.Text).IsEqualTo("★");
                    await Assert.That(cardIndicator.IsVisible).IsTrue();
                    await Assert.That(rowIndicator.Text).IsEqualTo("★");
                    await Assert.That(rowIndicator.IsVisible).IsTrue();
                }

                task.IsGoal = false;
                Dispatcher.UIThread.RunJobs();
                using (Assert.Multiple())
                {
                    await Assert.That(cardIndicator.IsVisible).IsFalse();
                    await Assert.That(rowIndicator.IsVisible).IsFalse();
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task AssertProjectionAsync(
        MainWindowViewModel vm,
        string firstId,
        bool expectedFirst,
        string secondId,
        bool expectedSecond)
    {
        var updated = await TestHelpers.WaitUntilAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var visibleIds = vm.CurrentAllTasksItems.Select(wrapper => wrapper.TaskItem.Id).ToHashSet();
            return visibleIds.Contains(firstId) == expectedFirst &&
                   visibleIds.Contains(secondId) == expectedSecond;
        }, TimeSpan.FromSeconds(5));

        if (!updated)
        {
            var visibleIds = vm.CurrentAllTasksItems.Select(wrapper => wrapper.TaskItem.Id);
            throw new InvalidOperationException(
                $"Goal projection did not reach the expected state. " +
                $"Mode={vm.GoalFilterMode}; expected {firstId}={expectedFirst}, {secondId}={expectedSecond}; " +
                $"visible roots=[{string.Join(", ", visibleIds)}].");
        }
    }

    private static Window CreateWindow(Control content) => new()
    {
        Width = 1800,
        Height = 1000,
        Content = content
    };

    private static void SelectTab(MainControl view, int index)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static ComboBox OpenFilterPanelAndFindComboBox(
        MainControl view,
        string filtersButtonAutomationId,
        string comboBoxAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        var flyout = filtersButton.Flyout as Flyout
                     ?? throw new InvalidOperationException(
                         $"Filter button '{filtersButtonAutomationId}' must use a Flyout.");
        flyout.ShowAt(filtersButton);
        Dispatcher.UIThread.RunJobs();

        var content = flyout.Content as Control
                      ?? throw new InvalidOperationException(
                          $"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
        return FindControlByAutomationId<ComboBox>(content, comboBoxAutomationId);
    }

    private static void HideFilterPanel(MainControl view, string filtersButtonAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        ((Flyout)filtersButton.Flyout!).Hide();
        Dispatcher.UIThread.RunJobs();
    }

    private static TaskGoalFilterOption[] ReadGoalFilterOptions(ComboBox comboBox)
    {
        var source = comboBox.ItemsSource as IEnumerable
                     ?? throw new InvalidOperationException("Goal filter must be bound to an ItemsSource.");
        return source.Cast<TaskGoalFilterOption>().ToArray();
    }

    private static TextBlock FindTaskRowGoalIndicator(MainControl view, TaskItemViewModel task) =>
        view.GetVisualDescendants()
            .OfType<TextBlock>()
            .First(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    "TaskGoalIndicator",
                    StringComparison.Ordinal) &&
                (ReferenceEquals(candidate.DataContext, task) ||
                 candidate.DataContext is TaskWrapperViewModel wrapper && ReferenceEquals(wrapper.TaskItem, task)));

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot &&
            string.Equals(AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal))
        {
            return typedRoot;
        }

        return root.GetVisualDescendants()
                   .OfType<T>()
                   .FirstOrDefault(candidate =>
                       string.Equals(
                           AutomationProperties.GetAutomationId(candidate),
                           automationId,
                           StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Control with AutomationId '{automationId}' was not found.");
    }
}
