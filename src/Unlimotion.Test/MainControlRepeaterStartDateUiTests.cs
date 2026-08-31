using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlRepeaterStartDateUiTests
{
    [Test]
    [Arguments(1400)]
    [Arguments(390)]
    public async Task OpeningWithoutStart_HidesSection_WithoutErasingLegacyPattern(int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            vm.DetailsAreOpen = true;
            TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RepeateTask9Id);
            vm.SelectCurrentTask();
            var task = vm.CurrentTaskItem!;
            var authoritative = task.Model;
            authoritative.PlannedBeginDateTime = null;
            task.Update(authoritative);
            var view = new MainControl { DataContext = vm };
            var window = new Window { Width = width, Height = 1000, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(Find<Border>(view, "CurrentTaskRepeaterSection").IsVisible).IsFalse();
                await Assert.That(task.Repeater!.Model.Equals(authoritative.Repeater)).IsTrue();
                await Assert.That(Find<ComboBox>(view, "CurrentTaskRepeaterSelector").Focus()).IsFalse();
            }
            finally
            {
                window.Content = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(1400, false)]
    [Arguments(1400, true)]
    [Arguments(390, false)]
    [Arguments(390, true)]
    public async Task ClearingStart_HidesSectionAndPersistsReset_WithoutRestoringRepeater(int width, bool viaMenu)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var previousThrottle = TaskItemViewModel.DefaultThrottleTime;
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(30);
            try
            {
                await using var fixture = new MainWindowViewModelFixture();
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RepeateTask9Id);
                vm.SelectCurrentTask();
                var task = vm.CurrentTaskItem!;
                var end = task.PlannedEndDateTime;
                var duration = task.PlannedDuration;
                var view = new MainControl { DataContext = vm };
                var window = new Window { Width = width, Height = 1000, Content = view };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var section = Find<Border>(view, "CurrentTaskRepeaterSection");
                    var picker = Find<CalendarDatePicker>(view, "CurrentTaskPlannedBeginPicker");
                    await Assert.That(section.IsVisible).IsTrue();
                    await Assert.That(task.Repeater).IsNotNull();

                    if (viaMenu)
                    {
                        var button = Find<DropDownButton>(view, "CurrentTaskSetBeginButton");
                        button.Flyout!.ShowAt(button);
                        Dispatcher.UIThread.RunJobs();
                        var menu = (MenuFlyout)button.Flyout;
                        var none = menu.Items.OfType<MenuItem>()
                            .Single(item => ReferenceEquals(item.Command, task.Commands.SetBeginNone));
                        none.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        menu.Hide();
                    }
                    else
                    {
                        // Edit the actual picker text to exercise its two-way binding and commit.
                        var editor = picker.GetVisualDescendants().OfType<TextBox>().First();
                        editor.Focus();
                        editor.Text = string.Empty;
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                        picker.Focus();
                    }

                    await WaitAsync(() => task.PlannedBeginDateTime == null);
                    await Assert.That(section.IsVisible).IsFalse();
                    await Assert.That(section.GetVisualDescendants().OfType<Control>().Any(c => c.IsEffectivelyVisible)).IsFalse();
                    await Assert.That(Find<ComboBox>(view, "CurrentTaskRepeaterSelector").Focus()).IsFalse();
                    await Assert.That(task.Repeater).IsNull();
                    await Assert.That(task.RepeaterListMarker).IsEqualTo(string.Empty);
                    await Assert.That(task.PlannedEndDateTime).IsEqualTo(end);
                    await Assert.That(task.PlannedDuration).IsEqualTo(duration);

                    var path = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
                    await WaitAsync(() => ReadTask(path) is { PlannedBeginDateTime: null, Repeater: null });
                    await task.WaitForPendingSavesAsync();
                    var persisted = ReadTask(path)!;
                    await Assert.That(persisted.PlannedEndDateTime?.LocalDateTime).IsEqualTo(end);
                    await Assert.That(persisted.PlannedDuration).IsEqualTo(duration);

                    // Reopen from disk, then restore only the date through the picker.
                    task.Update(persisted);
                    picker.SelectedDate = DateTime.Today;
                    await WaitAsync(() => section.IsVisible);
                    await Assert.That(task.Repeater).IsNull();
                    await WaitAsync(() => ReadTask(path) is { PlannedBeginDateTime: not null, Repeater: null });
                }
                finally
                {
                    window.Content = null;
                    window.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally
            {
                TaskItemViewModel.DefaultThrottleTime = previousThrottle;
            }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants()
        .OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static TaskItem? ReadTask(string path)
    {
        try { return JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var succeeded = await TestHelpers.WaitUntilAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return condition();
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20));
        await Assert.That(succeeded).IsTrue();
    }
}
