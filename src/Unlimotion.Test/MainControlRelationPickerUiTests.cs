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

[NotInParallel]
public class MainControlRelationPickerUiTests
{
    [Test]
    public async Task TaskCardRelationPicker_AddParentFromCard_UpdatesStorage()
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
                var currentTask = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.BlockedTask7Id);
                await Assert.That(currentTask).IsNotNull();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var addButton = WaitForControl<Button>(view, "CurrentTaskParentsRelationAddButton");
                await ClickControlAsync(window, addButton);

                var input = WaitForControl<AutoCompleteBox>(view, "CurrentTaskParentsRelationAddInput");
                input.Text = "Task 1";
                Dispatcher.UIThread.RunJobs();

                var pickerReady = WaitFor(() =>
                {
                    var picker = vm.CurrentItemParentsPicker;
                    var candidate = picker?.Suggestions.FirstOrDefault(candidate =>
                        candidate.Task.Id == MainWindowViewModelFixture.RootTask1Id);
                    if (picker == null || candidate == null)
                    {
                        return false;
                    }

                    input.SelectedItem = candidate;
                    Dispatcher.UIThread.RunJobs();
                    return picker.CanConfirm;
                });
                await Assert.That(pickerReady).IsTrue();

                var confirmButton = WaitForControl<Button>(view, "CurrentTaskParentsRelationAddConfirmButton");
                await ClickControlAsync(window, confirmButton);
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var currentStored = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.BlockedTask7Id);
                var parentStored = TestHelpers.GetStorageTaskItem(
                    fixture.DefaultTasksFolderPath,
                    MainWindowViewModelFixture.RootTask1Id);

                await Assert.That(currentStored).IsNotNull();
                await Assert.That(parentStored).IsNotNull();
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

        window.MouseDown(point.Value, button, modifiers);
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
                        StringComparison.Ordinal));
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
