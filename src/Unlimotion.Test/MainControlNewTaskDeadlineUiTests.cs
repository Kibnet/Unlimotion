using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
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
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
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
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task NewRootTask_ShouldNotCopyPlannedDuration_FromFocusedPreviousTaskEditor()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
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
                var createButton = view.GetVisualDescendants()
                    .OfType<Button>()
                    .First(button => ReferenceEquals(button.Command, vm.Create));

                await ClickControlAsync(window, createButton);

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != currentTask.Id);
                await Assert.That(created).IsTrue();

                var newTask = vm.CurrentTaskItem!;
                await Assert.That(newTask.PlannedDuration).IsNull();

                var newTaskDurationTextBox = FindPlannedDurationTextBox(view, newTask);
                await Assert.That(newTaskDurationTextBox.Text).IsNull();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static async Task RunCreateTaskScenarioAsync(
        string selectedTaskId,
        Func<MainWindowViewModel, System.Windows.Input.ICommand> commandSelector,
        bool setDatesThroughPicker = false,
        bool selectedTaskCreatedInFuture = false)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
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
                var createButton = view.GetVisualDescendants()
                    .OfType<Button>()
                    .First(button => ReferenceEquals(button.Command, createCommand));

                await ClickControlAsync(window, createButton);

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
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
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
}
