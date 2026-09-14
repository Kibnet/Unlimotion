using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskDurationMenuStartupUiTests
{
    [Test]
    public async Task FirstOpenedDurationMenu_UpdatesTheSelectedTaskAndCanClearDuration()
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
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var task = vm.CurrentTaskItem!;
                var view = new MainControl { DataContext = vm };
                window = new Window { Width = 1400, Height = 1000, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var button = view.GetVisualDescendants().OfType<DropDownButton>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "CurrentTaskSetDurationButton");
                var menu = (MenuFlyout)button.Flyout!;
                menu.ShowAt(button);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(menu.IsOpen).IsTrue();
                var oneHour = menu.Items.OfType<MenuItem>().Single(item =>
                    ReferenceEquals(item.Command, task.SetDurationCommands.OneHourCommand));
                await Assert.That(oneHour.IsEnabled).IsTrue();
                oneHour.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, oneHour));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(task.PlannedDuration).IsEqualTo(TimeSpan.FromHours(1));

                menu.ShowAt(button);
                Dispatcher.UIThread.RunJobs();
                var clear = menu.Items.OfType<MenuItem>().Single(item =>
                    ReferenceEquals(item.Command, task.SetDurationCommands.NoneCommand));
                await Assert.That(clear.IsEnabled).IsTrue();
                clear.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clear));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(task.PlannedDuration).IsNull();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }
}
