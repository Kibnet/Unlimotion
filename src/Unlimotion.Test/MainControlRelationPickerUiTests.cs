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
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlRelationPickerUiTests
{
    [Test]
    [Arguments(MainWindowViewModelFixture.BlockedTask7Id, "CurrentTaskParentsRelationAddButton", "CurrentTaskParentsRelationAddInput")]
    [Arguments(MainWindowViewModelFixture.BlockedTask7Id, "CurrentTaskBlockingRelationAddButton", "CurrentTaskBlockingRelationAddInput")]
    [Arguments(MainWindowViewModelFixture.RootTask1Id, "CurrentTaskContainingRelationAddButton", "CurrentTaskContainingRelationAddInput")]
    [Arguments(MainWindowViewModelFixture.RootTask7Id, "CurrentTaskBlockedRelationAddButton", "CurrentTaskBlockedRelationAddInput")]
    public async Task TaskCardRelationEditor_OpenTargetsExpectedInput(
        string currentTaskId,
        string addButtonAutomationId,
        string inputAutomationId)
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
                var currentTask = TestHelpers.SetCurrentTask(vm, currentTaskId);
                await Assert.That(currentTask).IsNotNull();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                var focusRequestBefore = vm.CurrentRelationEditor.FocusRequestVersion;
                var addButton = WaitForControl<Button>(view, addButtonAutomationId);
                await ClickControlAsync(window, addButton);

                var input = WaitForControl<TextBox>(view, inputAutomationId);
                var focusRequested = WaitFor(() =>
                    vm.CurrentRelationEditor.FocusRequestVersion > focusRequestBefore &&
                    string.Equals(vm.CurrentRelationEditor.InputAutomationId, inputAutomationId, StringComparison.Ordinal),
                    5000);

                using (Assert.Multiple())
                {
                    await Assert.That(input.IsVisible).IsTrue();
                    await Assert.That(focusRequested).IsTrue();
                }
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskCardRelationEditor_AddParentFromCard_UpdatesStorage()
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
                var currentTask = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.BlockedTask7Id);
                await Assert.That(currentTask).IsNotNull();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                var addButton = WaitForControl<Button>(view, "CurrentTaskParentsRelationAddButton");
                await ClickControlAsync(window, addButton);

                var input = WaitForControl<TextBox>(view, "CurrentTaskParentsRelationAddInput");
                await ClickControlAsync(window, input);
                input.Text = "Root Task 1";
                Dispatcher.UIThread.RunJobs();

                var pickerReady = WaitFor(() =>
                    vm.CurrentRelationEditor.CanConfirm &&
                    vm.CurrentRelationEditor.Suggestions.Any(candidate =>
                        candidate.Task.Id == MainWindowViewModelFixture.RootTask1Id),
                    5000);
                await Assert.That(pickerReady).IsTrue();

                var confirmButton = WaitForControl<Button>(view, "CurrentTaskParentsRelationAddConfirmButton");
                vm.CurrentRelationEditor.SelectedCandidate = vm.CurrentRelationEditor.Suggestions
                    .First(candidate => candidate.Task.Id == MainWindowViewModelFixture.RootTask1Id);
                Dispatcher.UIThread.RunJobs();

                confirmButton.Command?.Execute(confirmButton.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var storedUpdated = WaitFor(() =>
                    TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.BlockedTask7Id)?.ParentTasks.Contains(MainWindowViewModelFixture.RootTask1Id) == true &&
                    TestHelpers.GetStorageTaskItem(
                        fixture.DefaultTasksFolderPath,
                        MainWindowViewModelFixture.RootTask1Id)?.ContainsTasks.Contains(MainWindowViewModelFixture.BlockedTask7Id) == true,
                    10000);

                var currentStored = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.BlockedTask7Id);
                var parentStored = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.RootTask1Id);

                await Assert.That(currentStored).IsNotNull();
                await Assert.That(parentStored).IsNotNull();
                await Assert.That(storedUpdated).IsTrue();
                await Assert.That(currentStored!.ParentTasks).Contains(MainWindowViewModelFixture.RootTask1Id);
                await Assert.That(parentStored!.ContainsTasks).Contains(MainWindowViewModelFixture.BlockedTask7Id);
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
            Width = 1600,
            Height = 2200,
            Content = content
        };
    }

    private static async Task ClickControlAsync(
        Window window,
        Control control,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        if (control is Button buttonControl &&
            button == MouseButton.Left &&
            modifiers == RawInputModifiers.None)
        {
            buttonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, buttonControl));
            Dispatcher.UIThread.RunJobs();
            await Task.CompletedTask;
            return;
        }

        window.MouseDown(point.Value, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point.Value, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static T WaitForControl<T>(Control root, string automationId, int timeoutMilliseconds = 2000)
        where T : Control
    {
        T? control = null;
        var ready = WaitFor(() =>
        {
            control = root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsVisible &&
                    candidate.IsEnabled);
            return control != null;
        }, timeoutMilliseconds);

        if (!ready || control == null)
        {
            throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
        }

        return control;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }
}
