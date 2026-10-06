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
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskStatusPickerActivationUiTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task KeyboardOrPointerActivation_OpensRenderedMenuAndPhysicallyChangesStatus(bool keyboard)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new InMemoryStorage();
            using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var model = new TaskItem
            {
                Id = "status-picker-input-activation", Title = "Physical status activation",
                Status = DomainTaskStatus.Prepared, IsCanBeCompleted = true
            };
            model.EnsureStatusHistory("test");
            await storage.Save(model);
            await repository.Init();
            var task = repository.Tasks.Lookup(model.Id).Value;
            task.IsInitializedProvider = () => true;
            var picker = new TaskStatusPicker
            {
                Task = task, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var window = new Window
            {
                Width = 420, Height = 320, Content = new Border { Padding = new Thickness(32), Child = picker }
            };
            try
            {
                window.Show();
                Pump(window);
                if (keyboard)
                {
                    await Assert.That(picker.Focus()).IsTrue();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                }
                else
                {
                    Click(picker);
                }

                await WaitAsync(window, () => picker.Flyout is MenuFlyout { IsOpen: true },
                    "Input activation did not leave the status menu open.");
                var flyout = (MenuFlyout)picker.Flyout!;
                var row = flyout.Items.OfType<MenuItem>().Single(item =>
                    AutomationProperties.GetAutomationId(item) == "TaskStatusOptionInProgress");
                await WaitAsync(window, () => row.IsAttachedToVisualTree() && row.IsEffectivelyVisible
                    && row.Bounds.Width > 0 && row.Bounds.Height > 0,
                    "The exact status option was not realized in the popup.");
                await Assert.That(row.IsEffectivelyEnabled).IsTrue();
                await Assert.That(row.GetVisualAncestors().OfType<MenuFlyoutPresenter>().Any()).IsTrue();
                Click(row);
                await WaitAsync(window, () => task.Status == DomainTaskStatus.InProgress
                    && task.StatusHistory.Last().Status == DomainTaskStatus.InProgress,
                    "Physical status-option click did not update the task.");
                await Assert.That((await storage.Load(task.Id))!.Status).IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(flyout.IsOpen).IsFalse();
            }
            finally
            {
                if (picker.Flyout is MenuFlyout flyout) flyout.Hide();
                window.Content = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, CancellationToken.None);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)
            ?? throw new InvalidOperationException("The input target has no actual popup/window root.");
        root.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
            ?? throw new InvalidOperationException("The input target has no rendered coordinates.");
        root.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        root.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static async Task WaitAsync(Window window, Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        do { Pump(window); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException(message);
    }
}
