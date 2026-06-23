using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlAvailabilityUiTests
{
    [Test]
    public async Task LastOpenedTaskTitle_ShouldBeDimmed_WhenTaskCannotBeCompleted()
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

                var blockerTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var blockedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);

                await Assert.That(blockerTask).IsNotNull();
                await Assert.That(blockedTask).IsNotNull();

                await vm.taskRepository!.Block(blockedTask!, blockerTask!);
                await Assert.That(blockedTask.IsCanBeCompleted).IsFalse();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, "LastOpenedTabItem");
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask1Id);
                Dispatcher.UIThread.RunJobs();
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                Dispatcher.UIThread.RunJobs();

                var lastOpenedTree = view.FindControl<TreeView>("LastOpenedTree");
                await Assert.That(lastOpenedTree).IsNotNull();

                var titleText = WaitForWrapperTitleText(
                    lastOpenedTree!,
                    MainWindowViewModelFixture.RootTask2Id,
                    blockedTask.Title);

                await Assert.That(titleText.Opacity).IsEqualTo(0.4);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DescendantCompletedStatusOption_ShouldBeDisabled_WhenAncestorHasIncompleteBlocker()
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

                var blockerTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var parentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);

                await Assert.That(blockerTask).IsNotNull();
                await Assert.That(parentTask).IsNotNull();
                await Assert.That(childTask).IsNotNull();
                await Assert.That(childTask!.IsCanBeCompleted).IsTrue();

                await vm.taskRepository!.Block(parentTask!, blockerTask!);

                var rootWrapper = WaitForTaskWrapper(vm, parentTask!);
                rootWrapper!.IsExpanded = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var childStatusPicker = WaitForTaskStatusPicker(allTasksTree!, MainWindowViewModelFixture.SubTask22Id);
                var completedOption = childStatusPicker.Task!.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed);
                var storedChild = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.SubTask22Id);

                await Assert.That(childStatusPicker.IsEnabled).IsTrue();
                await Assert.That(completedOption.IsEnabled).IsFalse();
                await Assert.That(storedChild).IsNotNull();
                await Assert.That(storedChild!.IsCanBeCompleted).IsFalse();
                await Assert.That(storedChild.BlockedByTasks).DoesNotContain(blockerTask.Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1400,
            Height = 900,
            Content = content
        };
    }

    private static void SelectTab(Control root, string automationId)
    {
        var tab = root.GetVisualDescendants()
            .OfType<TabItem>()
            .First(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control) == automationId);

        tab.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
    }

    private static TaskStatusPicker WaitForTaskStatusPicker(TreeView tree, string taskId, int timeoutMilliseconds = 2000)
    {
        TaskStatusPicker? statusPicker = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            statusPicker = tree.GetVisualDescendants()
                .OfType<TaskStatusPicker>()
                .FirstOrDefault(control => control.Task?.Id == taskId);
            return statusPicker != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || statusPicker == null)
        {
            throw new InvalidOperationException($"Status picker for task '{taskId}' was not found.");
        }

        return statusPicker;
    }

    private static TaskWrapperViewModel WaitForTaskWrapper(
        MainWindowViewModel vm,
        TaskItemViewModel task,
        int timeoutMilliseconds = 2000)
    {
        TaskWrapperViewModel? wrapper = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            wrapper = vm.FindTaskWrapperViewModel(task, vm.CurrentAllTasksItems);
            return wrapper != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || wrapper == null)
        {
            throw new InvalidOperationException($"Wrapper for task '{task.Id}' was not found.");
        }

        return wrapper;
    }

    private static EmojiTextBlock WaitForWrapperTitleText(
        TreeView tree,
        string taskId,
        string title,
        int timeoutMilliseconds = 3000)
    {
        EmojiTextBlock? titleText = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            titleText = tree.GetVisualDescendants()
                .OfType<EmojiTextBlock>()
                .FirstOrDefault(control =>
                    control.DataContext is TaskWrapperViewModel wrapper &&
                    wrapper.TaskItem.Id == taskId &&
                    string.Equals(control.EmojiText, title, StringComparison.Ordinal));
            return titleText != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || titleText == null)
        {
            throw new InvalidOperationException($"Title text for task '{taskId}' was not found.");
        }

        return titleText;
    }

}
