using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

internal static class TaskPlanningDatesUiContract
{
    public static async Task<TaskPlanningDatesScenarioResult> ExecuteTaskPlanningDatesScenarioAsync()
    {
        var result = new TaskPlanningDatesScenarioResult();
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
                    vm.DetailsAreOpen = true;
                    TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                    vm.SelectCurrentTask();

                    var currentTask = vm.CurrentTaskItem
                        ?? throw new InvalidOperationException("Current task was not selected.");

                    var isInitializedProvider = currentTask.IsInitializedProvider;
                    currentTask.IsInitializedProvider = () => false;
                    try
                    {
                        currentTask.PlannedBeginDateTime = null;
                        currentTask.PlannedEndDateTime = null;
                        currentTask.PlannedDuration = null;

                        var view = new MainControl { DataContext = vm };
                        window = CreateWindow(view);
                        window.Show();
                        window.Activate();
                        RunLayoutJobs();
                        result.MainControlOpened = true;

                        var beginPicker = FindControlByAutomationId<CalendarDatePicker>(
                            view,
                            "CurrentTaskPlannedBeginPicker");
                        var beginButton = FindControlByAutomationId<DropDownButton>(
                            view,
                            "CurrentTaskSetBeginButton");
                        var durationTextBox = FindControlByAutomationId<TextBox>(
                            view,
                            "CurrentTaskPlannedDurationTextBox");
                        var durationButton = FindControlByAutomationId<DropDownButton>(
                            view,
                            "CurrentTaskSetDurationButton");
                        var endPicker = FindControlByAutomationId<CalendarDatePicker>(
                            view,
                            "CurrentTaskPlannedEndPicker");
                        var endButton = FindControlByAutomationId<DropDownButton>(
                            view,
                            "CurrentTaskSetEndButton");

                        result.PlanningControlsAvailable =
                            beginPicker is not null &&
                            beginButton is not null &&
                            durationTextBox is not null &&
                            durationButton is not null &&
                            endPicker is not null &&
                            endButton is not null;

                        result.ControlsBoundToCurrentTask =
                            ReferenceEquals(beginPicker!.DataContext, currentTask) &&
                            ReferenceEquals(beginButton!.DataContext, currentTask) &&
                            ReferenceEquals(durationTextBox!.DataContext, currentTask) &&
                            ReferenceEquals(durationButton!.DataContext, currentTask) &&
                            ReferenceEquals(endPicker!.DataContext, currentTask) &&
                            ReferenceEquals(endButton!.DataContext, currentTask);

                        await ExecuteMenuItemAsync(beginButton!, 1);
                        result.BeginQuickActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedBeginDateTime == DateEx.Tomorrow &&
                            beginPicker!.SelectedDate == DateEx.Tomorrow);

                        await ExecuteMenuItemAsync(durationButton!, 6);
                        result.DurationQuickActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedDuration == TimeSpan.FromHours(2));

                        await ExecuteMenuItemAsync(endButton!, 4);
                        var expectedEndDate = DateEx.Tomorrow.AddDays(4);
                        result.EndQuickActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedEndDateTime == expectedEndDate &&
                            endPicker!.SelectedDate == expectedEndDate);

                        await ExecuteMenuItemAsync(durationButton!, 12);
                        result.DurationNoneActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedDuration is null);

                        await ExecuteMenuItemAsync(beginButton!, 4);
                        result.BeginNoneActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedBeginDateTime is null);

                        await ExecuteMenuItemAsync(endButton!, 8);
                        result.EndNoneActionWorked = await WaitUntilAsync(() =>
                            currentTask.PlannedEndDateTime is null);
                    }
                    finally
                    {
                        currentTask.IsInitializedProvider = isInitializedProvider;
                    }
                }
                finally
                {
                    await CloseWindowAndDrainAsync(window);
                    await fixture.CleanTasksAsync();
                    await DrainUiThreadAsync();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }

        return result;
    }

    public static async Task AssertTaskPlanningDatesScenarioResultAsync(
        TaskPlanningDatesScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.PlanningControlsAvailable).IsTrue();
        await Assert.That(result.ControlsBoundToCurrentTask).IsTrue();
        await Assert.That(result.BeginQuickActionWorked).IsTrue();
        await Assert.That(result.DurationQuickActionWorked).IsTrue();
        await Assert.That(result.EndQuickActionWorked).IsTrue();
        await Assert.That(result.DurationNoneActionWorked).IsTrue();
        await Assert.That(result.EndNoneActionWorked).IsTrue();
        await Assert.That(result.BeginNoneActionWorked).IsTrue();
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

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => string.Equals(
                AutomationProperties.GetAutomationId(candidate),
                automationId,
                StringComparison.Ordinal));

        if (control is null)
        {
            throw new InvalidOperationException(
                $"Control with automation id '{automationId}' was not found.");
        }

        return control!;
    }

    private static async Task ExecuteMenuItemAsync(DropDownButton button, int index)
    {
        var flyout = button.Flyout as MenuFlyout
            ?? throw new InvalidOperationException("Expected a MenuFlyout.");

        flyout.ShowAt(button);
        RunLayoutJobs();

        var item = flyout.Items.OfType<MenuItem>().ElementAt(index);
        if (item.Command is null)
        {
            throw new InvalidOperationException("Expected menu item command.");
        }

        var commandCanExecute = await WaitUntilAsync(() => item.Command.CanExecute(item.CommandParameter));
        if (!commandCanExecute)
        {
            throw new InvalidOperationException("Expected menu item command to be executable.");
        }

        item.Command.Execute(item.CommandParameter);
        flyout.Hide();
        RunLayoutJobs();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        return await TestHelpers.WaitUntilAsync(
            () =>
            {
                RunLayoutJobs();
                return predicate();
            },
            TimeSpan.FromSeconds(5));
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task CloseWindowAndDrainAsync(Window? window)
    {
        if (window == null)
        {
            return;
        }

        var root = window.Content as Control;
        window.Content = null;
        if (root != null)
        {
            root.DataContext = null;
        }

        RunLayoutJobs();
        window.Close();
        await DrainUiThreadAsync();
    }

    private static async Task DrainUiThreadAsync(int quietMilliseconds = 200)
    {
        var drainUntil = DateTime.UtcNow.AddMilliseconds(quietMilliseconds);
        do
        {
            RunLayoutJobs();
            await Task.Delay(25);
        }
        while (DateTime.UtcNow < drainUntil);

        RunLayoutJobs();
    }
}

internal sealed class TaskPlanningDatesScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool PlanningControlsAvailable { get; set; }

    public bool ControlsBoundToCurrentTask { get; set; }

    public bool BeginQuickActionWorked { get; set; }

    public bool DurationQuickActionWorked { get; set; }

    public bool EndQuickActionWorked { get; set; }

    public bool DurationNoneActionWorked { get; set; }

    public bool EndNoneActionWorked { get; set; }

    public bool BeginNoneActionWorked { get; set; }
}
