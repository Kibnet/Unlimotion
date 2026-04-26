using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class MainScreenLoadingUiTests
{
    [Test]
    public async Task MainScreen_TogglesTasksLoadingOverlay_WithLoadingState()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlay = FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay");
                var spinner = FindControlByAutomationId<TextBlock>(view, "TasksLoadingSpinner");

                await Assert.That(overlay.IsVisible).IsFalse();

                SetTasksLoading(vm, true);
                var becameVisible = WaitFor(() => overlay.IsVisible && spinner.IsVisible);
                await Assert.That(becameVisible).IsTrue();

                SetTasksLoading(vm, false);
                var becameHidden = WaitFor(() => !overlay.IsVisible);
                await Assert.That(becameHidden).IsTrue();
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

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static void SetTasksLoading(MainWindowViewModel vm, bool value)
    {
        var property = typeof(MainWindowViewModel).GetProperty(
            nameof(MainWindowViewModel.IsTasksLoading),
            BindingFlags.Instance | BindingFlags.Public);
        var setter = property?.GetSetMethod(nonPublic: true);

        if (setter == null)
        {
            throw new InvalidOperationException("Could not access MainWindowViewModel.IsTasksLoading setter.");
        }

        setter.Invoke(vm, [value]);
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
