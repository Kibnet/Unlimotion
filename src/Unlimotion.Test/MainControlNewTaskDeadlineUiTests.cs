using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Behavior;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlNewTaskDeadlineUiTests
{
    [Test]
    public async Task NewRootTask_ShouldNotCopyPlannedDates_FromPreviouslySelectedTask()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RepeateTask9Id,
            commandSelector: vm => vm.Create);
    }

    [Test]
    public async Task NewSiblingTask_ShouldNotCopyPlannedDates_FromPreviouslySelectedTask()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RepeateTask9Id,
            commandSelector: vm => vm.CreateSibling);
    }

    [Test]
    public async Task NewSiblingTask_ShouldNotKeepFutureCreatedSelectedTaskAsCurrent()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RepeateTask9Id,
            commandSelector: vm => vm.CreateSibling,
            selectedTaskCreatedInFuture: true);
    }

    [Test]
    public async Task NewInnerTask_ShouldNotCopyPlannedDates_FromPreviouslySelectedTask()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RepeateTask9Id,
            commandSelector: vm => vm.CreateInner);
    }

    [Test]
    public async Task NewInnerTask_ShouldNotKeepFutureCreatedSelectedTaskAsCurrent()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RepeateTask9Id,
            commandSelector: vm => vm.CreateInner,
            selectedTaskCreatedInFuture: true);
    }

    [Test]
    public async Task NewRootTask_ShouldNotCopyPlannedDates_SetThroughDatePicker()
    {
        await RunCreateTaskScenarioAsync(
            selectedTaskId: MainWindowViewModelFixture.RootTask2Id,
            commandSelector: vm => vm.Create,
            setDatesThroughPicker: true);
    }

    [Test]
    public async Task NewSiblingTask_ShouldNotCopyPlannedDates_WhenCreatedByHotkeyFromFocusedDatePicker()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var taskWithDeadline = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RepeateTask9Id);
                await Assert.That(taskWithDeadline).IsNotNull();
                TestHelpers.SetCurrentTask(vm, taskWithDeadline!.Id);
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                var deadlinePickers = view.GetVisualDescendants()
                    .OfType<CalendarDatePicker>()
                    .Where(picker => ReferenceEquals(picker.DataContext, taskWithDeadline))
                    .ToArray();
                await Assert.That(deadlinePickers.Length).IsEqualTo(2);

                var focused = deadlinePickers[0].Focus();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(focused).IsTrue();

                var taskCountBefore = vm.taskRepository!.Tasks.Count;
                PressHotkey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != taskWithDeadline.Id);
                await Assert.That(created).IsTrue();

                var newTask = vm.CurrentTaskItem!;
                await Assert.That(newTask.PlannedBeginDateTime).IsNull();
                await Assert.That(newTask.PlannedEndDateTime).IsNull();

                var newTaskPickers = view.GetVisualDescendants()
                    .OfType<CalendarDatePicker>()
                    .Where(picker => ReferenceEquals(picker.DataContext, newTask))
                    .ToArray();
                await Assert.That(newTaskPickers.Length).IsEqualTo(2);
                foreach (var picker in newTaskPickers)
                {
                    await Assert.That(picker.SelectedDate).IsNull();
                }
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task NewRootTask_ShouldNotCopyPlannedDuration_FromFocusedPreviousTaskEditor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var currentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(currentTask).IsNotNull();
                TestHelpers.SetCurrentTask(vm, currentTask!.Id);
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var durationTextBox = FindPlannedDurationTextBox(view, currentTask);
                durationTextBox.Focus();
                await Assert.That(durationTextBox.IsFocused).IsTrue();
                durationTextBox.Text = "5h";
                Dispatcher.UIThread.RunJobs();

                var taskCountBefore = vm.taskRepository!.Tasks.Count;
                ExecuteCreateCommandThroughMenu(view, vm.Create);
                Dispatcher.UIThread.RunJobs();

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != currentTask.Id);
                await Assert.That(created).IsTrue();

                var newTask = vm.CurrentTaskItem!;
                await Assert.That(newTask.PlannedDuration).IsNull();

                var newTaskDurationTextBox = FindPlannedDurationTextBox(view, newTask);
                await Assert.That(string.IsNullOrEmpty(newTaskDurationTextBox.Text)).IsTrue();
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task PlannedDurationEditor_ShouldCommitNewText_AfterFocusedDataContextSwitch()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            Window? window = null;

            try
            {
                var previousTask = new object();
                var newTask = new object();
                var durationTextBox = new TextBox { DataContext = previousTask };
                var blurTarget = new Button { Content = "Blur target" };
                LostFocusUpdateBindingBehavior.SetText(durationTextBox, "");

                var root = new StackPanel();
                root.Children.Add(durationTextBox);
                root.Children.Add(blurTarget);

                window = CreateWindow(root);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                durationTextBox.Focus();
                await Assert.That(durationTextBox.IsFocused).IsTrue();
                durationTextBox.Text = "5h";
                Dispatcher.UIThread.RunJobs();

                durationTextBox.DataContext = newTask;
                Dispatcher.UIThread.RunJobs();
                durationTextBox.Text = "2h";
                Dispatcher.UIThread.RunJobs();

                blurTarget.Focus();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(LostFocusUpdateBindingBehavior.GetText(durationTextBox)).IsEqualTo("2h");
            }
            finally
            {
                CloseWindow(window);
            }
        }, CancellationToken.None);
    }

    private static async Task RunCreateTaskScenarioAsync(
        string selectedTaskId,
        Func<MainWindowViewModel, System.Windows.Input.ICommand> commandSelector,
        bool setDatesThroughPicker = false,
        bool selectedTaskCreatedInFuture = false)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                var taskWithDeadline = TestHelpers.GetTask(vm, selectedTaskId);
                await Assert.That(taskWithDeadline).IsNotNull();
                if (selectedTaskCreatedInFuture)
                {
                    taskWithDeadline!.CreatedDateTime = DateTimeOffset.UtcNow.AddDays(1);
                }

                TestHelpers.SetCurrentTask(vm, taskWithDeadline!.Id);
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();

                var deadlinePickers = view.GetVisualDescendants()
                    .OfType<CalendarDatePicker>()
                    .Where(picker => ReferenceEquals(picker.DataContext, taskWithDeadline))
                    .ToArray();
                await Assert.That(deadlinePickers.Length).IsEqualTo(2);

                if (setDatesThroughPicker)
                {
                    var beginDate = new DateTime(2030, 1, 10);
                    var endDate = new DateTime(2030, 1, 12);
                    deadlinePickers[0].SelectedDate = beginDate;
                    deadlinePickers[1].SelectedDate = endDate;
                    Dispatcher.UIThread.RunJobs();
                }

                await Assert.That(taskWithDeadline.PlannedBeginDateTime).IsNotNull();
                await Assert.That(taskWithDeadline.PlannedEndDateTime).IsNotNull();
                await Assert.That(deadlinePickers.Select(picker => picker.SelectedDate))
                    .Contains(taskWithDeadline.PlannedBeginDateTime);
                await Assert.That(deadlinePickers.Select(picker => picker.SelectedDate))
                    .Contains(taskWithDeadline.PlannedEndDateTime);

                var taskCountBefore = vm.taskRepository!.Tasks.Count;
                var createCommand = commandSelector(vm);
                ExecuteCreateCommandThroughMenu(view, createCommand);
                Dispatcher.UIThread.RunJobs();

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != taskWithDeadline.Id,
                    5000);
                await Assert.That(created).IsTrue();

                var newTask = vm.CurrentTaskItem!;
                await Assert.That(newTask.PlannedBeginDateTime).IsNull();
                await Assert.That(newTask.PlannedEndDateTime).IsNull();

                var newTaskPickers = view.GetVisualDescendants()
                    .OfType<CalendarDatePicker>()
                    .Where(picker => ReferenceEquals(picker.DataContext, newTask))
                    .ToArray();
                await Assert.That(newTaskPickers.Length).IsEqualTo(2);
                foreach (var picker in newTaskPickers)
                {
                    await Assert.That(picker.SelectedDate).IsNull();
                }
            }
            finally
            {
                CloseWindow(window);
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

    private static void ExecuteCreateCommandThroughMenu(
        Control root,
        System.Windows.Input.ICommand createCommand)
    {
        var createMenuButton = root.GetVisualDescendants()
            .OfType<DropDownButton>()
            .First(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "GlobalTaskCreateMenuButton",
                    StringComparison.Ordinal) &&
                button.IsVisible &&
                button.IsEnabled);

        if (!createCommand.CanExecute(null))
        {
            throw new InvalidOperationException("Expected create command to be executable from the global create menu.");
        }

        createCommand.Execute(null);
    }

    private static TextBox FindPlannedDurationTextBox(Control root, TaskItemViewModel task)
    {
        return root.GetVisualDescendants()
            .OfType<TextBox>()
            .First(textBox =>
                ReferenceEquals(textBox.DataContext, task) &&
                ToolTip.GetTip(textBox)?.ToString()?.Contains("1d, 5h, 20m", StringComparison.Ordinal) == true);
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        if (control is Button { Command: { } command } button)
        {
            if (!command.CanExecute(button.CommandParameter))
            {
                throw new InvalidOperationException($"Button command for {button.GetType().Name} cannot execute.");
            }

            command.Execute(button.CommandParameter);
            RunLayoutJobs();
            await Task.CompletedTask;
            return;
        }

        RunLayoutJobs();

        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        RunLayoutJobs();
        await Task.CompletedTask;
    }

    private static void PressHotkey(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CloseWindow(Window? window)
    {
        if (window == null)
        {
            return;
        }

        window.Content = null;
        RunLayoutJobs();
        window.Close();
        RunLayoutJobs();
    }
}
