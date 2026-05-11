using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class ToastNotificationUiTests
{
    [Test]
    public async Task MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var toastManager = new AppToastNotificationManager(TimeSpan.FromMinutes(1));
                var vm = fixture.MainWindowViewModelTest;
                vm.ToastNotificationManager = toastManager;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var wrapper = new NotificationManagerWrapper(toastManager);
                wrapper.ErrorToast("Toast failure");

                var toast = WaitForControl<Border>(view, "ToastNotificationError");
                var message = toast.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .FirstOrDefault(textBlock => textBlock.Text == "Toast failure");
                await Assert.That(message).IsNotNull();

                var closeButton = WaitForControl<Button>(view, "ToastNotificationErrorCloseButton");
                await ClickControlAsync(window, closeButton);

                var removed = WaitFor(() => !HasControlWithAutomationId(view, "ToastNotificationError"));
                await Assert.That(removed).IsTrue();
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
            Width = 1200,
            Height = 800,
            Content = content
        };
    }

    private static T WaitForControl<T>(Control root, string automationId)
        where T : Control
    {
        T? control = null;
        var found = WaitFor(() =>
        {
            control = root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal));
            return control != null;
        });

        if (!found || control == null)
        {
            throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
        }

        return control;
    }

    private static bool HasControlWithAutomationId(Control root, string automationId)
    {
        return root.GetVisualDescendants()
            .OfType<Control>()
            .Any(control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    automationId,
                    StringComparison.Ordinal));
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        if (point == null)
        {
            throw new InvalidOperationException("Could not translate control point to window coordinates.");
        }

        window.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
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
