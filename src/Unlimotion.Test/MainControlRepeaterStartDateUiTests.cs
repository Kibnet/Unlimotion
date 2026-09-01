using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlRepeaterStartDateUiTests
{
    [Test]
    public async Task CompletingRepeatingTask_SelectsTemplateAfterReload()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            vm.AllTasksMode = true;
            vm.DetailsAreOpen = true;
            var source = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RepeateTask9Id);
            source.Title = "UI repeating source";
            source.Status = DomainTaskStatus.Prepared;
            source.IsCanBeCompleted = true;
            source.PlannedBeginDateTime = DateTime.Today.AddDays(-1);
            source.Repeater = new RepeaterPatternViewModel
            {
                Type = RepeaterType.Daily,
                Period = 3,
                AfterComplete = true
            };
            var readySource = source.Model;
            readySource.Status = DomainTaskStatus.Prepared;
            readySource.IsCanBeCompleted = true;
            await vm.taskRepository!.TaskTreeManager.Storage.Save(readySource);
            source.Update(readySource);
            vm.CurrentTaskItem = source;
            vm.SelectCurrentTask();

            var countBeforeCompletion = vm.taskRepository.Tasks.Count;
            var view = new MainControl { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1000, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var statusPicker = Find<TaskStatusPicker>(view, "CurrentTaskStatusButton");
                var buildFlyout = typeof(TaskStatusPicker).GetMethod(
                    "BuildStatusFlyout",
                    BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("TaskStatusPicker.BuildStatusFlyout was not found.");
                var flyout = (MenuFlyout)buildFlyout.Invoke(null, [source])!;
                statusPicker.Flyout = flyout;
                flyout.ShowAt(statusPicker);
                Dispatcher.UIThread.RunJobs();
                var completed = flyout.Items.OfType<MenuItem>().Single(item =>
                    AutomationProperties.GetAutomationId(item) == "TaskStatusOptionCompleted");
                await Assert.That(completed.IsEnabled).IsTrue();
                completed.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, completed));
                Dispatcher.UIThread.RunJobs();

                await WaitAsync(() =>
                    source.Status == DomainTaskStatus.Completed &&
                    vm.taskRepository.Tasks.Count == countBeforeCompletion + 1);
                var next = vm.taskRepository.Tasks.Items.Single(task =>
                    task.Id != source.Id && task.Title == source.Title);
                TestHelpers.SetCurrentTask(vm, next.Id);
                vm.SelectCurrentTask();
                Dispatcher.UIThread.RunJobs();

                var selector = Find<ComboBox>(view, "CurrentTaskRepeaterSelector");
                var selected = selector.SelectedItem as RepeaterPatternViewModel;
                using (Assert.Multiple())
                {
                    await Assert.That(selected).IsNotNull();
                    await Assert.That(selected!.Type).IsEqualTo(RepeaterType.Daily);
                    await Assert.That(next.Repeater!.Period).IsEqualTo(3);
                    await Assert.That(next.Repeater.AfterComplete).IsTrue();
                }

                var path = Path.Combine(fixture.DefaultTasksFolderPath, next.Id);
                await WaitAsync(() => File.Exists(path));
                using var reloadedRepository = new UnifiedTaskStorage(new TaskTreeManager(
                    new FileTaskStorage(new FileTaskStorageOptions
                    {
                        Path = fixture.DefaultTasksFolderPath,
                        UseWatcher = false
                    })));
                await reloadedRepository.Init();
                var reloadedNext = reloadedRepository.Tasks.Lookup(next.Id).Value;
                vm.CurrentTaskItem = reloadedNext;
                Dispatcher.UIThread.RunJobs();
                selected = selector.SelectedItem as RepeaterPatternViewModel;
                await Assert.That(selected).IsNotNull();
                await Assert.That(selected!.Type).IsEqualTo(RepeaterType.Daily);
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
    public async Task HydratedWeeklyRepeaters_SelectExactTemplatesInComboBox()
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
            var view = new MainControl { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1000, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var selector = Find<ComboBox>(view, "CurrentTaskRepeaterSelector");

                task.Repeater = new RepeaterPatternViewModel
                {
                    Type = RepeaterType.Weekly,
                    WorkDays = true
                };
                Dispatcher.UIThread.RunJobs();
                await Assert.That(selector.SelectedItem).IsSameReferenceAs(task.Repeaters[2]);

                task.Repeater = new RepeaterPatternViewModel
                {
                    Type = RepeaterType.Weekly,
                    Monday = true,
                    Saturday = true
                };
                Dispatcher.UIThread.RunJobs();
                await Assert.That(selector.SelectedItem).IsSameReferenceAs(task.Repeaters[3]);
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
    public async Task HydratedDailyRepeater_IsSelectedInTemplateComboBox()
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
            task.Repeater = new RepeaterPatternViewModel
            {
                Type = RepeaterType.Daily,
                Period = 3,
                AfterComplete = true
            };
            var view = new MainControl { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1000, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var selector = Find<ComboBox>(view, "CurrentTaskRepeaterSelector");
                await Assert.That(selector.SelectedItem)
                    .IsSameReferenceAs(task.SelectedRepeaterTemplate);
                await Assert.That(task.Repeater.Period).IsEqualTo(3);
                await Assert.That(task.Repeater.AfterComplete).IsTrue();
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
