using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class MainControlAvailabilityUiTests
{
    [Test]
    public async Task DescendantCheckbox_ShouldBeDisabled_WhenAncestorHasIncompleteBlocker()
    {
        using var session = HeadlessUnitTestSession.StartNew(GetDesktopAppEntryPointType());
        await session.Dispatch(async () =>
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

                var rootWrapper = vm.FindTaskWrapperViewModel(parentTask, vm.CurrentAllTasksItems);
                await Assert.That(rootWrapper).IsNotNull();
                rootWrapper!.IsExpanded = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var childCheckBox = WaitForTaskCheckBox(allTasksTree!, MainWindowViewModelFixture.SubTask22Id);
                var storedChild = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.SubTask22Id);

                await Assert.That(childCheckBox.IsEnabled).IsFalse();
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

    private static CheckBox WaitForTaskCheckBox(TreeView tree, string taskId, int timeoutMilliseconds = 2000)
    {
        CheckBox? checkBox = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            checkBox = tree.GetVisualDescendants()
                .OfType<CheckBox>()
                .FirstOrDefault(control => control.DataContext is TaskItemViewModel task && task.Id == taskId);
            return checkBox != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || checkBox == null)
        {
            throw new InvalidOperationException($"Checkbox for task '{taskId}' was not found.");
        }

        return checkBox;
    }

    private static Type GetDesktopAppEntryPointType()
    {
        return Type.GetType("Unlimotion.Desktop.Program, Unlimotion.Desktop") ??
               throw new InvalidOperationException("Unable to locate Unlimotion.Desktop.Program.");
    }
}
