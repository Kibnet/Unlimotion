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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlWantedUiTests
{
    [Test]
    public async Task CurrentTaskWantedCheckBox_WhenConfirmed_ShouldUpdateDescendants()
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

                var parent = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchild = await vm.taskRepository!.AddChild(child!);
                grandchild.Title = "Wanted UI cascade grandchild";
                parent!.Wanted = false;
                child!.Wanted = true;
                grandchild.Wanted = false;
                await TestHelpers.WaitThrottleTime();

                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var wantedCheckBox = FindControlByAutomationId<CheckBox>(view, "CurrentTaskWantedCheckBox");
                await ClickControlAsync(window, wantedCheckBox);
                await TestHelpers.WaitThrottleTime();

                await Assert.That(parent.Wanted).IsTrue();
                await Assert.That(child.Wanted).IsTrue();
                await Assert.That(grandchild.Wanted).IsTrue();
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

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        var point = GetControlCenterPoint(window, control);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static Point GetControlCenterPoint(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value;
    }
}
