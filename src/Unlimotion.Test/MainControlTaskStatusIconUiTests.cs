using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTaskStatusIconUiTests
{
    [Test]
    public async Task TaskCardStatusRecovery_AuthoritativeReadRunsOffUiThread()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Unlimotion.ReloadThread", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                using var storage = new DelayedCardReloadFileStorage(directory);
                await storage.Save(new TaskItem { Id = "thread-check", Title = "fixture" });
                await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
                var result = await storage.ReloadTaskAsync("thread-check");
                await Assert.That(result.Outcome).IsEqualTo(TaskReloadOutcome.Loaded);
                await Assert.That(storage.ReadOnUiThread).IsFalse();
                await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
            }
            finally
            {
                System.IO.Directory.Delete(directory, true);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskCardStatusRecovery_ExternalDeleteDuringReloadKeepsCopyableDraft()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            DelayedCardReloadFileStorage storage = null!;
            var fixture = new MainWindowViewModelFixture(path =>
                new UnifiedTaskStorage(new TaskTreeManager(storage = new DelayedCardReloadFileStorage(path))));
            Window? window = null;
            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var card = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                vm.CurrentTaskItem = card;
                vm.DetailsAreOpen = true;
                vm.SelectCurrentTask();
                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                var title = WaitForAutomationControl<TextBox>(view, "CurrentTaskTitleTextBox");
                var description = WaitForAutomationControl<TextBox>(view, "CurrentTaskDescriptionTextBox");
                title.Text = "Draft title to copy";
                description.Text = "Draft description to copy";
                Dispatcher.UIThread.RunJobs();
                var file = System.IO.Path.Combine(fixture.DefaultTasksFolderPath, card.Id);
                storage.BlockRead = true;
                var read = card.ReloadTaskAsync();
                await storage.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                System.IO.File.Delete(file);
                // The same cache event is used by a confirmed watcher/hub deletion.
                storage.PublishRemoved(card.Id);
                Dispatcher.UIThread.RunJobs();
                storage.ReleaseRead.TrySetResult();
                await read.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                using (Assert.Multiple())
                {
                    await Assert.That(vm.CurrentTaskItem).IsSameReferenceAs(card);
                    await Assert.That(card.IsMissingFromStorage).IsTrue();
                    await Assert.That(storage.ReadOnUiThread).IsFalse();
                    await Assert.That(title.IsEffectivelyVisible).IsTrue();
                    await Assert.That(title.Text).IsEqualTo("Draft title to copy");
                    await Assert.That(description.Text).IsEqualTo("Draft description to copy");
                    await Assert.That(WaitForAutomationControl<TextBlock>(view, "CurrentTaskOperationErrorText").IsEffectivelyVisible).IsTrue();
                    await Assert.That(WaitForAutomationControl<Button>(view, "CurrentTaskStatusButton").IsEnabled).IsFalse();
                    await Assert.That(OpenTaskReloadMenu(view).IsEnabled).IsFalse();
                    await Assert.That(vm.taskRepository!.Tasks.Lookup(card.Id).HasValue).IsFalse();
                }
                await card.SealPendingSaves();
                await Assert.That(System.IO.File.Exists(file)).IsFalse();
                vm.CurrentTaskItem = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(vm.CurrentTaskItem).IsNotSameReferenceAs(card);
            }
            finally
            {
                storage.ReleaseRead.TrySetResult();
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private sealed class DelayedCardReloadFileStorage(string path) : FileStorage(path)
    {
        public bool BlockRead { get; set; }
        public bool ReadOnUiThread { get; private set; }
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task OnTaskReloadSnapshotReadAsync(string taskId)
        {
            ReadOnUiThread = Dispatcher.UIThread.CheckAccess();
            if (!BlockRead) return;
            ReadEntered.TrySetResult();
            await ReleaseRead.Task;
        }
        public void PublishRemoved(string taskId) => RaiseUpdating(new TaskStorageUpdateEventArgs
        {
            Id = taskId, Type = UpdateType.Removed
        });
    }

    [Test]
    [Arguments("ru", "Light", 1400)]
    [Arguments("ru", "Dark", 1400)]
    [Arguments("en", "Light", 1400)]
    [Arguments("en", "Dark", 1400)]
    [Arguments("ru", "Light", 760)]
    [Arguments("ru", "Dark", 760)]
    [Arguments("en", "Light", 760)]
    [Arguments("en", "Dark", 760)]
    public async Task TaskCardStatusRecovery_ErrorAndReloadRemainVisible(string language, string theme, int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(StatusRecoveryRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var app = Application.Current!;
            var previousTheme = app.RequestedThemeVariant;
            var previousLanguage = Unlimotion.ViewModel.Localization.LocalizationService.Current.LanguageMode;
            var taskFile = System.IO.Path.Combine(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask1Id);
            var originalBytes = await System.IO.File.ReadAllBytesAsync(taskFile);
            Window? window = null;
            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                Unlimotion.ViewModel.Localization.Localization.SetLanguage(language);
                app.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                var task = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                vm.CurrentTaskItem = task;
                vm.DetailsAreOpen = true;
                vm.SelectCurrentTask();
                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Width = width;
                window.Show();
                // Corrupt only the fixture file to exercise a real failed read and bound error.
                await System.IO.File.WriteAllTextAsync(taskFile, "not JSON");
                await task.ReloadTaskAsync();
                var reload = OpenTaskReloadMenu(view);
                var error = WaitForAutomationControl<TextBlock>(view, "CurrentTaskOperationErrorText");
                var details = WaitForAutomationControl<Expander>(view, "CurrentTaskOperationDetails");
                await Assert.That(details.Header).IsEqualTo(language == "ru" ? "Подробности" : "Details");
                details.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                var detailsHeader = details.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                    .Single(button => button.Name == "PART_HeaderSite");
                var history = WaitForAutomationControl<Expander>(view, "StatusHistoryExpander");
                var historyHeader = history.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                    .Single(button => button.Name == "PART_HeaderSite");
                using (Assert.Multiple())
                {
                    await Assert.That(AutomationProperties.GetName(detailsHeader))
                        .IsEqualTo(language == "ru" ? "Подробности" : "Details");
                    await Assert.That(AutomationProperties.GetName(historyHeader))
                        .IsEqualTo(Unlimotion.ViewModel.Localization.LocalizationService.Current.Get("TaskHistory"));
                    await Assert.That(reload.IsEffectivelyVisible).IsTrue();
                    await Assert.That(reload.IsEnabled).IsTrue();
                    await Assert.That(AutomationProperties.GetName(reload))
                        .IsEqualTo(language == "ru" ? "Обновить задачу" : "Reload task");
                    await Assert.That(error.IsEffectivelyVisible).IsTrue();
                    await Assert.That(error.Text).IsEqualTo(task.TaskOperationError);
                    await Assert.That(error.TextWrapping).IsEqualTo(TextWrapping.Wrap);
                    await Assert.That(error.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(error.Bounds.Width).IsLessThanOrEqualTo(window.Bounds.Width);
                    await Assert.That(WaitForAutomationControl<TextBlock>(view, "CurrentTaskOperationDetailsText").IsEffectivelyVisible).IsTrue();
                }
                // Avalonia selects its renderer once per process. Run this matrix alone
                // with the flag below for pixel evidence; the full suite also hosts
                // semantic Headless tests that can initialize the stub renderer first.
                if (Environment.GetEnvironmentVariable("UNLIMOTION_STATUS_RECOVERY_RENDERED_EVIDENCE") == "1")
                {
                    using var frame = window.CaptureRenderedFrame();
                    await Assert.That(frame).IsNotNull();
                    var stride = frame!.PixelSize.Width * 4;
                    var pixels = new byte[stride * frame.PixelSize.Height];
                    var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);
                    try
                    {
                        frame.CopyPixels(new PixelRect(frame.PixelSize), buffer, pixels.Length, stride);
                        System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, pixels.Length);
                        await Assert.That(pixels.Distinct().Count()).IsGreaterThan(2)
                            .Because("A flat Headless stub frame is not rendered UI evidence.");
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
                    }
                    var directory = System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts", "status-recovery");
                    System.IO.Directory.CreateDirectory(directory);
                    frame.Save(System.IO.Path.Combine(directory, $"error-{language}-{theme}-{width}.png"));
                    var menuRoot = TopLevel.GetTopLevel(reload);
                    using var menuFrame = menuRoot?.CaptureRenderedFrame();
                    await Assert.That(menuFrame).IsNotNull();
                    menuFrame!.Save(System.IO.Path.Combine(directory, $"reload-menu-{language}-{theme}-{width}.png"));
                }
            }
            finally
            {
                await System.IO.File.WriteAllBytesAsync(taskFile, originalBytes);
                window?.Close();
                app.RequestedThemeVariant = previousTheme;
                Unlimotion.ViewModel.Localization.Localization.SetLanguage(previousLanguage);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    public static class StatusRecoveryRenderedAppBuilder
    {
        public static Avalonia.AppBuilder BuildAvaloniaApp() => Avalonia.AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    [Test]
    public async Task TaskCardStatusRecovery_HasAccessibleRefreshAction()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            DelayedCardReloadFileStorage storage = null!;
            var fixture = new MainWindowViewModelFixture(path =>
                new UnifiedTaskStorage(new TaskTreeManager(storage = new DelayedCardReloadFileStorage(path))));
            Window? window = null;
            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.CurrentTaskItem = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                vm.DetailsAreOpen = true;
                vm.SelectCurrentTask();
                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                await Assert.That(view.GetVisualDescendants().OfType<Button>()
                    .Any(button => AutomationProperties.GetAutomationId(button) == "CurrentTaskReloadButton")).IsFalse();
                var reload = OpenTaskReloadMenu(view);
                await Assert.That(reload.IsEffectivelyVisible).IsTrue();
                await Assert.That(reload.IsEnabled).IsTrue();
                await Assert.That(AutomationProperties.GetName(reload)).IsNotNullOrEmpty();
                await Assert.That(reload.Header).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("TaskReload"));
                await Assert.That(reload.Command).IsSameReferenceAs(vm.CurrentTaskItem!.ReloadTaskCommand);

                var card = vm.CurrentTaskItem!;
                storage.BlockRead = true;
                var busy = card.ReloadTaskAsync();
                await storage.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(reload.IsEnabled).IsFalse();
                storage.ReleaseRead.TrySetResult();
                await busy;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(reload.IsEnabled).IsTrue();
            }
            finally
            {
                storage.ReleaseRead.TrySetResult();
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static MenuItem OpenTaskReloadMenu(MainControl view)
    {
        var actions = WaitForAutomationControl<DropDownButton>(view, "CurrentTaskActionsMenuButton");
        var flyout = actions.Flyout as MenuFlyout
            ?? throw new InvalidOperationException("Task actions should expose a MenuFlyout.");
        flyout.ShowAt(actions);
        Dispatcher.UIThread.RunJobs();
        return flyout.Items.OfType<MenuItem>().Single(item =>
            AutomationProperties.GetAutomationId(item) == "CurrentTaskReloadButton");
    }

    [Test]
    public async Task TaskTreeStatusControl_UsesCompactVectorIconInsteadOfTextGlyph()
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
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var statusPicker = WaitForTaskStatusPicker(allTasksTree!);
                var task = statusPicker.Task ?? statusPicker.DataContext as TaskItemViewModel;
                var statusIcons = statusPicker.GetVisualDescendants()
                    .OfType<TaskStatusIcon>()
                    .ToList();
                var leakedGlyphText = statusPicker.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty)
                    .Any(text => text.Contains('▣') || text.Contains('☑') || text.Contains('□'));
                var nestedComboBoxes = statusPicker.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .ToList();
                var visibleDropDownArrows = statusPicker.GetVisualDescendants()
                    .OfType<PathIcon>()
                    .Where(icon => icon.IsVisible)
                    .ToList();

                await Assert.That(task).IsNotNull();
                await Assert.That(statusPicker.Classes.Contains("TaskStatusPicker")).IsTrue();
                await Assert.That(statusPicker.Bounds.Width).IsEqualTo(20);
                await Assert.That(statusPicker.Bounds.Height).IsEqualTo(20);
                await Assert.That(statusPicker.Margin.Right).IsEqualTo(8);
                await Assert.That(statusIcons).HasSingleItem();
                await Assert.That(statusIcons[0].Bounds.Width).IsEqualTo(20);
                await Assert.That(statusIcons[0].Bounds.Height).IsEqualTo(20);
                await Assert.That(statusIcons[0].Status).IsEqualTo(task!.Status);
                await Assert.That(leakedGlyphText).IsFalse();
                await Assert.That(nestedComboBoxes).IsEmpty();
                await Assert.That(visibleDropDownArrows).IsEmpty();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskTreeStatusControl_ClickOpensStatusFlyout()
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
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var statusPicker = WaitForTaskStatusPicker(allTasksTree!);
                var task = statusPicker.Task ?? statusPicker.DataContext as TaskItemViewModel;
                var flyout = await OpenStatusFlyoutAsync(window, statusPicker);

                await Assert.That(flyout).IsNotNull();
                var menuItems = flyout.Items.OfType<MenuItem>().ToList();
                var automationIds = menuItems
                    .Select(AutomationProperties.GetAutomationId)
                    .ToList();

                await Assert.That(menuItems.Count).IsEqualTo(Enum.GetValues<DomainTaskStatus>().Length - 1);
                await Assert.That(automationIds).DoesNotContain($"TaskStatusOption{task!.Status}");
                foreach (var option in task.StatusOptions.Where(option => option.Status != task.Status))
                {
                    var item = menuItems.Single(candidate =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(candidate),
                            $"TaskStatusOption{option.Status}",
                            StringComparison.Ordinal));

                    await Assert.That(item.IsEnabled).IsEqualTo(option.IsEnabled);
                    await Assert.That(AutomationProperties.GetHelpText(item))
                        .IsEqualTo(option.AutomationHelpText);
                }

                await Assert.That(flyout.IsOpen).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCardStatusChange_UpdatesAllTasksTreeStatusIcon()
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
                vm.AllTasksMode = true;
                var task = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id)
                    ?? throw new InvalidOperationException("Root task was not found.");
                task.Status = DomainTaskStatus.Prepared;
                task.IsCanBeCompleted = true;
                vm.CurrentTaskItem = task;
                vm.DetailsAreOpen = true;
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                var treeStatusPicker = WaitForTaskStatusPicker(allTasksTree!, task.Id);
                var treeStatusIcon = treeStatusPicker.GetVisualDescendants()
                    .OfType<TaskStatusIcon>()
                    .Single();
                await Assert.That(treeStatusIcon.Status).IsEqualTo(DomainTaskStatus.Prepared);

                var currentTaskStatusPicker = WaitForAutomationControl<TaskStatusPicker>(
                    view,
                    "CurrentTaskStatusButton");
                var flyout = await OpenStatusFlyoutAsync(window, currentTaskStatusPicker);
                var inProgressItem = flyout!.Items
                    .OfType<MenuItem>()
                    .Single(item =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(item),
                            "TaskStatusOptionInProgress",
                            StringComparison.Ordinal));

                InvokeMenuItemClick(inProgressItem);

                var iconUpdated = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return task.Status == DomainTaskStatus.InProgress &&
                           treeStatusIcon.Status == DomainTaskStatus.InProgress;
                }, TimeSpan.FromSeconds(2));

                await Assert.That(iconUpdated).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCardCompletedStatusChange_RemovesNestedTaskFromAllTasksTreeWhenCompletedIsHidden()
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
                vm.AllTasksMode = true;
                var parentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("Parent task was not found.");
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
                    ?? throw new InvalidOperationException("Child task was not found.");
                childTask.Status = DomainTaskStatus.Prepared;
                childTask.IsCanBeCompleted = true;
                vm.CurrentTaskItem = childTask;
                vm.DetailsAreOpen = true;
                vm.SelectCurrentTask();
                var parentWrapper = WaitForTaskWrapper(vm, parentTask);
                parentWrapper.IsExpanded = true;
                _ = parentWrapper.SubTasks;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                _ = WaitForTaskStatusPicker(allTasksTree!, childTask.Id);

                var currentTaskStatusPicker = WaitForAutomationControl<TaskStatusPicker>(
                    view,
                    "CurrentTaskStatusButton");
                var flyout = await OpenStatusFlyoutAsync(window, currentTaskStatusPicker);
                var completedItem = flyout!.Items
                    .OfType<MenuItem>()
                    .Single(item =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(item),
                            "TaskStatusOptionCompleted",
                            StringComparison.Ordinal));

                InvokeMenuItemClick(completedItem);

                var childRemoved = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return childTask.Status == DomainTaskStatus.Completed &&
                           allTasksTree!.GetVisualDescendants()
                               .OfType<TaskStatusPicker>()
                               .All(candidate => !string.Equals(candidate.Task?.Id, childTask.Id, StringComparison.Ordinal));
                }, TimeSpan.FromSeconds(2));

                await Assert.That(childRemoved).IsTrue();
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_MatchesStandaloneCheckBoxIndicatorSize()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-size-task",
                    Status = DomainTaskStatus.NotReady
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var statusPicker = new TaskStatusPicker
            {
                Task = task
            };
            var checkBox = new CheckBox();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    statusPicker,
                    checkBox
                }
            };
            var window = CreateWindow(panel);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var statusIcon = statusPicker.GetVisualDescendants().OfType<TaskStatusIcon>().Single();
                var checkBoxIndicator = FindCheckBoxIndicator(checkBox);

                await Assert.That(checkBoxIndicator).IsNotNull();
                await Assert.That(statusPicker.Bounds.Width).IsEqualTo(checkBoxIndicator!.Bounds.Width);
                await Assert.That(statusPicker.Bounds.Height).IsEqualTo(checkBoxIndicator.Bounds.Height);
                await Assert.That(statusIcon.Bounds.Width).IsEqualTo(checkBoxIndicator.Bounds.Width);
                await Assert.That(statusIcon.Bounds.Height).IsEqualTo(checkBoxIndicator.Bounds.Height);
                await Assert.That(GetStatusBorderThickness(scale: 1d))
                    .IsEqualTo(checkBoxIndicator.BorderThickness.Left);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_DimsUnavailableTaskLikeTaskText()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-unavailable-task",
                    Status = DomainTaskStatus.Prepared,
                    IsCanBeCompleted = false
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var statusPicker = new TaskStatusPicker
            {
                Task = task
            };
            var window = CreateWindow(statusPicker);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(statusPicker.Opacity).IsEqualTo(0.4);

                await Task.Run(() => task.IsCanBeCompleted = true);
                var becameAvailable = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return statusPicker.Opacity == 1d;
                }, TimeSpan.FromSeconds(2));

                await Assert.That(becameAvailable).IsTrue();

                await Task.Run(() => task.IsCanBeCompleted = false);
                var becameUnavailable = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return statusPicker.Opacity == 0.4;
                }, TimeSpan.FromSeconds(2));

                await Assert.That(becameUnavailable).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("Light")]
    [Arguments("Dark")]
    public async Task TaskStatusIcon_CompletedMatchesCheckedCheckBoxIndicatorForTheme(string themeName)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
            var previousTheme = app.RequestedThemeVariant;
            var theme = string.Equals(themeName, "Dark", StringComparison.Ordinal)
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
            var statusIcon = new TaskStatusIcon
            {
                Status = DomainTaskStatus.Completed,
                Width = 20,
                Height = 20
            };
            var checkBox = new CheckBox
            {
                IsChecked = true
            };
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    statusIcon,
                    checkBox
                }
            };
            var window = CreateWindow(panel);

            try
            {
                app.RequestedThemeVariant = theme;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var checkBoxIndicator = FindCheckBoxIndicator(checkBox);
                var checkGlyph = FindCheckBoxCheckGlyph(checkBox);
                var statusBorderBrush = GetStatusBorderBrush(statusIcon, DomainTaskStatus.Completed, isEnabled: true);
                var statusFillBrush = GetStatusBoxFillBrush(statusIcon, DomainTaskStatus.Completed, isEnabled: true);
                var statusGlyphBrush = GetStatusCompletedGlyphBrush(statusIcon, isEnabled: true);
                var statusGlyphGeometry = GetStatusCompletedGlyphGeometry(statusIcon);
                var statusGlyphTarget = GetStatusCompletedGlyphTargetRect(statusGlyphGeometry, scale: 1d);

                await Assert.That(checkBoxIndicator).IsNotNull();
                await Assert.That(checkGlyph).IsNotNull();
                var checkGlyphOffset = checkGlyph!.TranslatePoint(default, checkBoxIndicator!);
                await Assert.That(checkGlyphOffset).IsNotNull();
                await Assert.That(statusFillBrush).IsNotNull();
                await Assert.That(statusIcon.Bounds.Width).IsEqualTo(checkBoxIndicator.Bounds.Width);
                await Assert.That(statusIcon.Bounds.Height).IsEqualTo(checkBoxIndicator.Bounds.Height);
                await Assert.That(GetStatusBorderThickness(scale: 1d))
                    .IsEqualTo(checkBoxIndicator.BorderThickness.Left);
                await Assert.That(GetStatusBoxCornerRadius(statusIcon))
                    .IsEqualTo(checkBoxIndicator.CornerRadius.TopLeft);
                await Assert.That(GetSolidColor(statusFillBrush, "TaskStatusIcon completed fill brush"))
                    .IsEqualTo(GetSolidColor(checkBoxIndicator.Background, "CheckBox checked indicator background"));
                await Assert.That(GetSolidColor(statusBorderBrush, "TaskStatusIcon completed border brush"))
                    .IsEqualTo(GetSolidColor(checkBoxIndicator.BorderBrush, "CheckBox checked indicator border brush"));
                await Assert.That(GetSolidColor(statusGlyphBrush, "TaskStatusIcon completed glyph brush"))
                    .IsEqualTo(GetSolidColor(checkGlyph.Fill, "CheckBox checked glyph fill"));
                await Assert.That(statusGlyphGeometry.Bounds).IsEqualTo(checkGlyph.Data!.Bounds);
                await Assert.That(statusGlyphTarget.X).IsEqualTo(checkGlyphOffset!.Value.X).Within(0.1);
                await Assert.That(statusGlyphTarget.Y).IsEqualTo(checkGlyphOffset.Value.Y).Within(0.1);
                await Assert.That(checkGlyph.Bounds.Width).IsEqualTo(9);
            }
            finally
            {
                window.Close();
                app.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("Light")]
    [Arguments("Dark")]
    public async Task TaskStatusIcon_UsesCheckBoxUncheckedBorderBrushForTheme(string themeName)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
            var previousTheme = app.RequestedThemeVariant;
            var theme = string.Equals(themeName, "Dark", StringComparison.Ordinal)
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
            var statusIcon = new TaskStatusIcon
            {
                Status = DomainTaskStatus.NotReady,
                Width = 20,
                Height = 20
            };
            var checkBox = new CheckBox();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    statusIcon,
                    checkBox
                }
            };
            var window = CreateWindow(panel);

            try
            {
                app.RequestedThemeVariant = theme;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var checkBoxIndicator = FindCheckBoxIndicator(checkBox);
                var statusBorderBrush = GetStatusBorderBrush(statusIcon, DomainTaskStatus.NotReady, isEnabled: true);

                await Assert.That(checkBoxIndicator).IsNotNull();
                await Assert.That(GetSolidColor(statusBorderBrush, "TaskStatusIcon border brush"))
                    .IsEqualTo(GetSolidColor(checkBoxIndicator!.BorderBrush, "CheckBox indicator border brush"));
            }
            finally
            {
                window.Close();
                app.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("Prepared", "#008575")]
    [Arguments("InProgress", "#0F6CBD")]
    public async Task TaskStatusIcon_PreparedAndInProgressUseDistinctStatusBorderBrush(
        string statusName,
        string expectedColor)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var status = Enum.Parse<DomainTaskStatus>(statusName);
            var statusIcon = new TaskStatusIcon
            {
                Status = status,
                Width = 20,
                Height = 20
            };
            var checkBox = new CheckBox();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    statusIcon,
                    checkBox
                }
            };
            var window = CreateWindow(panel);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var checkBoxIndicator = FindCheckBoxIndicator(checkBox);
                var statusBorderBrush = GetStatusBorderBrush(statusIcon, status, isEnabled: true);
                var statusBorderColor = GetSolidColor(statusBorderBrush, "TaskStatusIcon border brush");

                await Assert.That(checkBoxIndicator).IsNotNull();
                await Assert.That(statusBorderColor).IsEqualTo(Color.Parse(expectedColor));
                await Assert.That(statusBorderColor)
                    .IsNotEqualTo(GetSolidColor(checkBoxIndicator!.BorderBrush, "CheckBox indicator border brush"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusIcon_NotReadyBorderBrushTracksThemeChangesOnSameControl()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
            var previousTheme = app.RequestedThemeVariant;
            var statusIcon = new TaskStatusIcon
            {
                Status = DomainTaskStatus.NotReady,
                Width = 20,
                Height = 20
            };
            var checkBox = new CheckBox();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    statusIcon,
                    checkBox
                }
            };
            var window = CreateWindow(panel);

            try
            {
                app.RequestedThemeVariant = ThemeVariant.Dark;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var darkCheckBoxIndicator = FindCheckBoxIndicator(checkBox);
                var darkStatusBorder = GetSolidColor(
                    GetStatusBorderBrush(statusIcon, DomainTaskStatus.NotReady, isEnabled: true),
                    "dark TaskStatusIcon border brush");

                await Assert.That(darkCheckBoxIndicator).IsNotNull();
                await Assert.That(darkStatusBorder)
                    .IsEqualTo(GetSolidColor(darkCheckBoxIndicator!.BorderBrush, "dark CheckBox indicator border brush"));

                app.RequestedThemeVariant = ThemeVariant.Light;
                Dispatcher.UIThread.RunJobs();

                var lightCheckBoxIndicator = FindCheckBoxIndicator(checkBox);
                var lightStatusBorder = GetSolidColor(
                    GetStatusBorderBrush(statusIcon, DomainTaskStatus.NotReady, isEnabled: true),
                    "light TaskStatusIcon border brush");

                await Assert.That(lightCheckBoxIndicator).IsNotNull();
                await Assert.That(lightStatusBorder)
                    .IsEqualTo(GetSolidColor(lightCheckBoxIndicator!.BorderBrush, "light CheckBox indicator border brush"));
                await Assert.That(lightStatusBorder).IsNotEqualTo(darkStatusBorder);
            }
            finally
            {
                window.Close();
                app.RequestedThemeVariant = previousTheme;
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskItemViewModel_CompletionCriterionChange_SavesOnMainThreadAfterThrottle()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        try
        {
            await session.DispatchAsync(async () =>
            {
                var storage = new RecordingTaskStorage();
                using var task = new TaskItemViewModel(
                    new TaskItem
                    {
                        Id = "completion-criterion-thread-task",
                        Title = "Completion criterion thread task",
                        Status = DomainTaskStatus.Prepared,
                        IsCanBeCompleted = true
                    },
                    storage);

                task.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromMilliseconds(20);
                task.AddCompletionCriterionCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                var criterion = task.CompletionCriteria.Single();
                criterion.Text = "Проверить результат";

                var savedAfterThrottle = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        return storage.UpdateAccessChecks.Count >= 2;
                    },
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromMilliseconds(10));

                await Assert.That(savedAfterThrottle).IsTrue();
                foreach (var updateWasOnMainThread in storage.UpdateAccessChecks)
                {
                    await Assert.That(updateWasOnMainThread).IsTrue();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task TaskStatusIconPreview_RendersOneVectorIconForEachLifecycleStatus()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var statuses = Enum.GetValues<DomainTaskStatus>();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            foreach (var status in statuses)
            {
                var icon = new TaskStatusIcon
                {
                    Status = status,
                    Width = 24,
                    Height = 24
                };
                AutomationProperties.SetAutomationId(icon, $"TaskStatusIcon{status}");
                panel.Children.Add(icon);
            }

            var window = CreateWindow(panel);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var icons = panel.GetVisualDescendants()
                    .OfType<TaskStatusIcon>()
                    .ToList();

                await Assert.That(icons.Count).IsEqualTo(statuses.Length);
                await Assert.That(icons.Select(icon => icon.Status)).IsEquivalentTo(statuses);
                foreach (var icon in icons)
                {
                    await Assert.That(icon.Bounds.Width).IsGreaterThanOrEqualTo(24);
                    await Assert.That(icon.Bounds.Height).IsGreaterThanOrEqualTo(24);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task InProgressTree_DisplaysStartedDateTimeInLocalTime()
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

                var utcInstant = new DateTimeOffset(2026, 01, 02, 12, 34, 00, TimeSpan.Zero);
                var localOffset = TimeZoneInfo.Local.GetUtcOffset(utcInstant.UtcDateTime);
                var sourceOffset = localOffset == TimeSpan.Zero ? TimeSpan.FromHours(3) : TimeSpan.Zero;
                var startedAt = utcInstant.ToOffset(sourceOffset);
                var expectedLocalText = startedAt.LocalDateTime.ToString("yyyy.MM.dd HH:mm");
                var rawSourceText = startedAt.ToString("yyyy.MM.dd HH:mm");
                var task = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id)
                    ?? throw new InvalidOperationException("Root task was not found.");
                task.IsInitializedProvider = () => false;
                task.Status = DomainTaskStatus.InProgress;
                task.StartedDateTime = startedAt;
                vm.InProgressMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                SelectTab(view, "InProgressTabItem");

                var startedLabel = WaitForAutomationControl<Label>(view, "InProgressStartedDateLabel");
                var elapsedLabel = WaitForAutomationControl<Label>(view, "InProgressElapsedLabel");

                await Assert.That(startedLabel.Content?.ToString()).IsEqualTo(expectedLocalText);
                await Assert.That(startedLabel.Padding.Left).IsEqualTo(0);
                await Assert.That(startedLabel.Margin.Right).IsEqualTo(16);
                await Assert.That(elapsedLabel.Padding.Left).IsEqualTo(0);
                await Assert.That(elapsedLabel.Margin.Right).IsEqualTo(16);
                if (!string.Equals(expectedLocalText, rawSourceText, StringComparison.Ordinal))
                {
                    await Assert.That(startedLabel.Content?.ToString()).IsNotEqualTo(rawSourceText);
                }
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPickerFlyout_ExposesAllNonCurrentOptionsAndDisabledReasons()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-task",
                    Status = DomainTaskStatus.Completed,
                    IsCanBeCompleted = true
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var flyout = BuildStatusFlyout(task);
            var items = flyout.Items.OfType<MenuItem>().ToList();
            var transitionOptions = task.StatusOptions
                .Where(option => option.Status != task.Status)
                .ToList();

            await Assert.That(items.Count).IsEqualTo(transitionOptions.Count);
            foreach (var option in transitionOptions)
            {
                var item = items.Single(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        $"TaskStatusOption{option.Status}",
                        StringComparison.Ordinal));
                var header = item.Header as StackPanel;

                await Assert.That(header).IsNotNull();
                var icon = header!.Children.OfType<TaskStatusIcon>().Single();
                var textBlocks = header.GetVisualDescendants().OfType<TextBlock>().ToList();

                await Assert.That(icon.Status).IsEqualTo(option.Status);
                await Assert.That(item.IsEnabled).IsEqualTo(option.IsEnabled);
                await Assert.That(textBlocks.Select(static text => text.Text)).Contains(option.Title);
                await Assert.That(AutomationProperties.GetHelpText(item))
                    .IsEqualTo(option.AutomationHelpText);
                if (!option.IsEnabled)
                {
                    await Assert.That(option.ReasonText).IsNotEmpty();
                    await Assert.That(textBlocks.Select(static text => text.Text)).Contains(option.ReasonText);
                }
            }

            await Assert.That(items.Select(AutomationProperties.GetAutomationId))
                .DoesNotContain($"TaskStatusOption{task.Status}");
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_SelectingStatusOption_UpdatesTaskStatusHistory()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new InMemoryStorage();
            var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var storedTask = new TaskItem
            {
                Id = "status-picker-transition-task",
                Title = "Status picker transition task",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                CreatedDateTime = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            storedTask.EnsureStatusHistory("owner");
            await storage.Save(storedTask);
            await repository.Init();

            Window? window = null;

            try
            {
                var taskLookup = repository.Tasks.Lookup(storedTask.Id);
                await Assert.That(taskLookup.HasValue).IsTrue();
                var task = taskLookup.Value;
                task.IsInitializedProvider = () => true;
                var statusPicker = new TaskStatusPicker
                {
                    Task = task,
                    Width = 28,
                    Height = 24
                };

                window = CreateWindow(statusPicker);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var flyout = await OpenStatusFlyoutAsync(window, statusPicker);

                await Assert.That(flyout).IsNotNull();
                var inProgressItem = flyout.Items
                    .OfType<MenuItem>()
                    .Single(item =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(item),
                            "TaskStatusOptionInProgress",
                            StringComparison.Ordinal));

                await Assert.That(inProgressItem.IsEnabled).IsTrue();
                InvokeMenuItemClick(inProgressItem);

                var changed = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return task.Status == DomainTaskStatus.InProgress &&
                           task.StartedDateTime.HasValue &&
                           task.StatusHistory.LastOrDefault()?.Status == DomainTaskStatus.InProgress;
                }, TimeSpan.FromSeconds(2));

                await Assert.That(changed).IsTrue();
                await Assert.That(task.StatusHistory.Select(entry => entry.Status))
                    .Contains(DomainTaskStatus.Prepared);
                await Assert.That(task.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(task.InProgressElapsed).IsNotEmpty();

                var persisted = await storage.Load(task.Id);
                await Assert.That(persisted).IsNotNull();
                await Assert.That(persisted!.Status).IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(persisted.StatusHistory.Select(entry => entry.Status))
                    .Contains(DomainTaskStatus.Prepared);
                await Assert.That(persisted.StatusHistory.Last().Status).IsEqualTo(DomainTaskStatus.InProgress);
            }
            finally
            {
                window?.Close();
                repository.Dispose();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_SelectingArchivedOptionOffersAndArchivesContainedTasks()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new InMemoryStorage();
            var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var parentTask = new TaskItem
            {
                Id = "status-picker-archive-parent",
                Title = "Status picker archive parent",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                ContainsTasks = ["status-picker-archive-child"],
                CreatedDateTime = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            var childTask = new TaskItem
            {
                Id = "status-picker-archive-child",
                Title = "Status picker archive child",
                Status = DomainTaskStatus.InProgress,
                IsCanBeCompleted = true,
                ParentTasks = [parentTask.Id],
                CreatedDateTime = DateTimeOffset.UtcNow.AddMinutes(-9)
            };
            parentTask.EnsureStatusHistory("owner");
            childTask.EnsureStatusHistory("owner");
            await storage.Save(parentTask);
            await storage.Save(childTask);
            await repository.Init();

            Window? window = null;
            try
            {
                var parentLookup = repository.Tasks.Lookup(parentTask.Id);
                var childLookup = repository.Tasks.Lookup(childTask.Id);
                await Assert.That(parentLookup.HasValue).IsTrue();
                await Assert.That(childLookup.HasValue).IsTrue();
                var parent = parentLookup.Value;
                var child = childLookup.Value;
                parent.ApplyRelations([child], [], [], []);
                parent.IsInitializedProvider = () => true;
                parent.NotificationManager = new NotificationManagerWrapperMock { AskResult = true };
                var statusPicker = new TaskStatusPicker
                {
                    Task = parent,
                    Width = 28,
                    Height = 24
                };

                window = CreateWindow(statusPicker);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var flyout = await OpenStatusFlyoutAsync(window, statusPicker);
                var archiveItem = flyout.Items
                    .OfType<MenuItem>()
                    .Single(item => string.Equals(
                        AutomationProperties.GetAutomationId(item),
                        "TaskStatusOptionArchived",
                        StringComparison.Ordinal));
                await Assert.That(archiveItem.IsEnabled).IsTrue();
                InvokeMenuItemClick(archiveItem);

                var archived = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return parent.Status == DomainTaskStatus.Archived &&
                           child.Status == DomainTaskStatus.Archived;
                }, TimeSpan.FromSeconds(2));
                var persistedParent = await storage.Load(parent.Id);
                var persistedChild = await storage.Load(child.Id);

                await Assert.That(archived).IsTrue();
                await Assert.That(parent.Status).IsEqualTo(DomainTaskStatus.Archived);
                await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Archived);
                await Assert.That(persistedParent?.Status).IsEqualTo(DomainTaskStatus.Archived);
                await Assert.That(persistedChild?.Status).IsEqualTo(DomainTaskStatus.Archived);
                await Assert.That(parent.NotificationManager)
                    .IsTypeOf<NotificationManagerWrapperMock>();
                var notifications = (NotificationManagerWrapperMock)parent.NotificationManager!;
                await Assert.That(notifications.ConfirmationCount).IsEqualTo(1);
                await Assert.That(notifications.LastAskHeader)
                    .IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("ArchiveContainedTasksHeader"));
            }
            finally
            {
                window?.Close();
                repository.Dispose();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_ImmediateRepeatingCompletion_PreservesEditorFieldsAndCreatesNextTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new InMemoryStorage();
            var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var storedChild = new TaskItem
            {
                Id = "status-picker-immediate-repeat-child",
                Title = "Daily review child",
                Status = DomainTaskStatus.Completed,
                IsCanBeCompleted = true,
                ParentTasks = ["status-picker-immediate-repeat"]
            };
            storedChild.EnsureStatusHistory("owner");
            var storedTask = new TaskItem
            {
                Id = "status-picker-immediate-repeat",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                ContainsTasks = [storedChild.Id]
            };
            storedTask.EnsureStatusHistory("owner");
            await storage.Save(storedChild);
            await storage.Save(storedTask);
            await repository.Init();
            Window? window = null;

            try
            {
                var task = repository.Tasks.Lookup(storedTask.Id).Value;
                task.IsInitializedProvider = () => true;
                var plannedBegin = DateTime.Now.AddDays(-1);
                task.Title = "Daily review";
                task.PlannedBeginDateTime = plannedBegin;
                task.Repeater = new RepeaterPatternViewModel
                {
                    Type = RepeaterType.Daily,
                    Period = 1
                };
                var statusPicker = new TaskStatusPicker
                {
                    Task = task,
                    Width = 28,
                    Height = 24
                };

                window = CreateWindow(statusPicker);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var flyout = await OpenStatusFlyoutAsync(window, statusPicker);
                var completedItem = flyout.Items
                    .OfType<MenuItem>()
                    .Single(item => string.Equals(
                        AutomationProperties.GetAutomationId(item),
                        "TaskStatusOptionCompleted",
                        StringComparison.Ordinal));

                InvokeMenuItemClick(completedItem);

                var completed = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return task.Status == DomainTaskStatus.Completed && repository.Tasks.Count == 4;
                }, TimeSpan.FromSeconds(2));
                var next = repository.Tasks.Items.Single(item => item.Id != task.Id && item.Title == "Daily review");
                var nextChild = repository.Tasks.Items.Single(item =>
                    item.Id != storedChild.Id && item.Title == storedChild.Title);

                using (Assert.Multiple())
                {
                    await Assert.That(completed).IsTrue();
                    await Assert.That(task.Title).IsEqualTo("Daily review");
                    await Assert.That(task.PlannedBeginDateTime).IsEqualTo(plannedBegin);
                    await Assert.That(task.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
                    await Assert.That(next.Title).IsEqualTo("Daily review");
                    await Assert.That(next.Status).IsEqualTo(DomainTaskStatus.Prepared);
                    await Assert.That(next.PlannedBeginDateTime).IsEqualTo(plannedBegin.AddDays(1));
                    await Assert.That(next.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
                    await Assert.That(next.Contains).IsEquivalentTo([nextChild.Id]);
                    await Assert.That(nextChild.Parents).IsEquivalentTo([next.Id]);
                    await Assert.That(nextChild.Status).IsEqualTo(DomainTaskStatus.NotReady);
                    await Assert.That(nextChild.Id).IsNotEqualTo(storedChild.Id);
                }
            }
            finally
            {
                window?.Close();
                repository.Dispose();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var storage = new InMemoryStorage();
            var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var storedTask = new TaskItem
            {
                Id = "status-picker-criteria-task",
                Title = "Status picker criteria task",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                CompletionCriteria =
                [
                    new TaskCompletionCriterion
                    {
                        Text = "Проверить результат",
                        IsSatisfied = false
                    }
                ]
            };
            storedTask.EnsureStatusHistory("owner");
            await storage.Save(storedTask);
            await repository.Init();
            var task = repository.Tasks.Lookup(storedTask.Id).Value;
            task.IsInitializedProvider = () => true;
            var statusPicker = new TaskStatusPicker
            {
                Task = task,
                Width = 28,
                Height = 24
            };
            Window? window = null;

            try
            {
                window = CreateWindow(statusPicker);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var flyout = await OpenStatusFlyoutAsync(window, statusPicker);
                var completedItem = flyout.Items
                    .OfType<MenuItem>()
                    .SingleOrDefault(item =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(item),
                            "TaskStatusOptionCompleted",
                            StringComparison.Ordinal));

                await Assert.That(completedItem).IsNotNull();
                await Assert.That(completedItem!.IsEnabled).IsFalse();
                await Assert.That(AutomationProperties.GetHelpText(completedItem)).IsNotEmpty();
                flyout.Hide();

                task.CompletionCriteria.Single().IsSatisfied = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(task.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed).IsEnabled)
                    .IsTrue();
                flyout = await OpenStatusFlyoutAsync(window, statusPicker);
                completedItem = flyout.Items
                    .OfType<MenuItem>()
                    .Single(item =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(item),
                            "TaskStatusOptionCompleted",
                            StringComparison.Ordinal));
                await Assert.That(completedItem.IsEnabled).IsTrue();
                await Assert.That(AutomationProperties.GetHelpText(completedItem)).IsEmpty();

                InvokeMenuItemClick(completedItem);
                var completed = await TestHelpers.WaitUntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return task.Status == DomainTaskStatus.Completed;
                }, TimeSpan.FromSeconds(2));
                var persisted = await storage.Load(task.Id);

                await Assert.That(completed).IsTrue();
                await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Completed);
                await Assert.That(persisted?.CompletionCriteria.Single().IsSatisfied).IsTrue();
            }
            finally
            {
                window?.Close();
                repository.Dispose();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_DetachedFromVisualTree_UnsubscribesFromTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-detach-task",
                    Title = "Status picker detach task",
                    Status = DomainTaskStatus.Prepared
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var statusPicker = new TaskStatusPicker
            {
                Task = task
            };
            var subscribedTaskField = typeof(TaskStatusPicker).GetField(
                "_subscribedTask",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("TaskStatusPicker._subscribedTask was not found.");
            var window = CreateWindow(statusPicker);

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(subscribedTaskField.GetValue(statusPicker)).IsSameReferenceAs(task);

                window.Content = null;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(subscribedTaskField.GetValue(statusPicker)).IsNull();
            }
            finally
            {
                window.Close();
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

    private static MenuFlyout BuildStatusFlyout(TaskStatusPicker statusPicker)
    {
        var task = statusPicker.Task ?? statusPicker.DataContext as TaskItemViewModel;
        if (task == null)
        {
            throw new InvalidOperationException("Task status picker is not bound to a task.");
        }

        return BuildStatusFlyout(task);
    }

    private static MenuFlyout BuildStatusFlyout(TaskItemViewModel task)
    {
        var buildFlyout = typeof(TaskStatusPicker).GetMethod(
            "BuildStatusFlyout",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TaskStatusPicker.BuildStatusFlyout was not found.");

        return (MenuFlyout)buildFlyout.Invoke(null, [task])!;
    }

    private static async Task<MenuFlyout> OpenStatusFlyoutAsync(Window window, TaskStatusPicker statusPicker)
    {
        PressControl(window, statusPicker);

        var opened = await TestHelpers.WaitUntilAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return statusPicker.Flyout is MenuFlyout { IsOpen: true };
        }, TimeSpan.FromSeconds(2));

        if (!opened || statusPicker.Flyout is not MenuFlyout flyout)
        {
            throw new InvalidOperationException("Task status flyout was not opened.");
        }

        return flyout;
    }

    private static void PressControl(Window window, Control control)
    {
        var point = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);

        control.RaiseEvent(new PointerPressedEventArgs(
            control,
            pointer,
            control,
            point,
            0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));
        Dispatcher.UIThread.RunJobs();
        control.RaiseEvent(new PointerReleasedEventArgs(
            control,
            pointer,
            control,
            point,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SelectTab(Control root, string automationId)
    {
        var tab = root.GetVisualDescendants()
            .OfType<TabItem>()
            .First(control => AutomationProperties.GetAutomationId(control) == automationId);

        tab.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
    }

    private static TControl WaitForAutomationControl<TControl>(
        Control root,
        string automationId,
        int timeoutMilliseconds = 3000)
        where TControl : Control
    {
        TControl? control = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            control = root.GetVisualDescendants()
                .OfType<TControl>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal));
            return control != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || control == null)
        {
            throw new InvalidOperationException($"Control '{automationId}' was not found.");
        }

        return control;
    }

    private static void InvokeMenuItemClick(MenuItem menuItem)
    {
        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem));
        Dispatcher.UIThread.RunJobs();
    }

    private static Border? FindCheckBoxIndicator(CheckBox checkBox)
    {
        return checkBox.GetVisualDescendants()
            .OfType<Border>()
            .Where(border =>
                border.Bounds.Width > 0 &&
                border.Bounds.Height > 0 &&
                Math.Abs(border.Bounds.Width - border.Bounds.Height) < 0.1 &&
                border.Bounds.Width >= 16 &&
                border.Bounds.Width <= 24)
            .OrderBy(border => Math.Abs(border.Bounds.Width - 20))
            .FirstOrDefault();
    }

    private static Path? FindCheckBoxCheckGlyph(CheckBox checkBox)
    {
        return checkBox.GetVisualDescendants()
            .OfType<Path>()
            .FirstOrDefault(path => string.Equals(path.Name, "CheckGlyph", StringComparison.Ordinal));
    }

    private static Color GetSolidColor(IBrush? brush, string source)
    {
        if (brush is ISolidColorBrush solidColorBrush)
        {
            return solidColorBrush.Color;
        }

        throw new InvalidOperationException($"{source} is not a solid color brush.");
    }

    private static IBrush GetStatusBorderBrush(TaskStatusIcon statusIcon, DomainTaskStatus status, bool isEnabled)
    {
        var getBorderBrush = typeof(TaskStatusIcon).GetMethod(
            "GetBorderBrush",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetBorderBrush was not found.");

        return (IBrush)getBorderBrush.Invoke(statusIcon, [status, isEnabled])!;
    }

    private static IBrush? GetStatusBoxFillBrush(TaskStatusIcon statusIcon, DomainTaskStatus status, bool isEnabled)
    {
        var getBoxFillBrush = typeof(TaskStatusIcon).GetMethod(
            "GetBoxFillBrush",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetBoxFillBrush was not found.");

        return (IBrush?)getBoxFillBrush.Invoke(statusIcon, [status, isEnabled]);
    }

    private static IBrush GetStatusCompletedGlyphBrush(TaskStatusIcon statusIcon, bool isEnabled)
    {
        var getCompletedGlyphBrush = typeof(TaskStatusIcon).GetMethod(
            "GetCompletedGlyphBrush",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetCompletedGlyphBrush was not found.");

        return (IBrush)getCompletedGlyphBrush.Invoke(statusIcon, [isEnabled])!;
    }

    private static Geometry GetStatusCompletedGlyphGeometry(TaskStatusIcon statusIcon)
    {
        var getCompletedGlyphGeometry = typeof(TaskStatusIcon).GetMethod(
            "GetCompletedGlyphGeometry",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetCompletedGlyphGeometry was not found.");

        return (Geometry)getCompletedGlyphGeometry.Invoke(statusIcon, [])!;
    }

    private static Rect GetStatusCompletedGlyphTargetRect(Geometry geometry, double scale)
    {
        var getCompletedGlyphTargetRect = typeof(TaskStatusIcon).GetMethod(
            "GetCompletedGlyphTargetRect",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetCompletedGlyphTargetRect was not found.");

        return (Rect)getCompletedGlyphTargetRect.Invoke(null, [geometry, scale])!;
    }

    private static double GetStatusBoxCornerRadius(TaskStatusIcon statusIcon)
    {
        var getBoxCornerRadius = typeof(TaskStatusIcon).GetMethod(
            "GetBoxCornerRadius",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetBoxCornerRadius was not found.");

        return (double)getBoxCornerRadius.Invoke(statusIcon, [])!;
    }

    private static double GetStatusBorderThickness(double scale)
    {
        var getBorderThickness = typeof(TaskStatusIcon).GetMethod(
            "GetBorderThickness",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TaskStatusIcon.GetBorderThickness was not found.");

        return (double)getBorderThickness.Invoke(null, [scale])!;
    }

    private static TaskStatusPicker WaitForTaskStatusPicker(
        TreeView tree,
        int timeoutMilliseconds = 3000)
    {
        TaskStatusPicker? statusPicker = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            statusPicker = tree.GetVisualDescendants()
                .OfType<TaskStatusPicker>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        "TaskStatusButton",
                        StringComparison.Ordinal) &&
                    candidate.Task is TaskItemViewModel &&
                    candidate.Bounds.Width > 0 &&
                    candidate.Bounds.Height > 0 &&
                    candidate.IsEffectivelyVisible);

            return statusPicker != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || statusPicker == null)
        {
            throw new InvalidOperationException("Task status picker was not found.");
        }

        return statusPicker;
    }

    private static TaskStatusPicker WaitForTaskStatusPicker(
        TreeView tree,
        string taskId,
        int timeoutMilliseconds = 3000)
    {
        TaskStatusPicker? statusPicker = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            statusPicker = tree.GetVisualDescendants()
                .OfType<TaskStatusPicker>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        "TaskStatusButton",
                        StringComparison.Ordinal) &&
                    string.Equals(candidate.Task?.Id, taskId, StringComparison.Ordinal));

            return statusPicker != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || statusPicker == null)
        {
            throw new InvalidOperationException($"Task status picker for task '{taskId}' was not found.");
        }

        return statusPicker;
    }

    private static TaskWrapperViewModel WaitForTaskWrapper(
        MainWindowViewModel vm,
        TaskItemViewModel task,
        int timeoutMilliseconds = 3000)
    {
        TaskWrapperViewModel? wrapper = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            wrapper = vm.FindTaskWrapperViewModel(task, vm.CurrentAllTasksItems);
            return wrapper != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || wrapper == null)
        {
            throw new InvalidOperationException($"Wrapper for task '{task.Id}' was not found.");
        }

        return wrapper;
    }

    private sealed class RecordingTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);
        public ITaskRelationsIndex Relations => throw new NotSupportedException();
        public TaskTreeManager TaskTreeManager { get; } = new(new InMemoryStorage());
        public List<bool> UpdateAccessChecks { get; } = [];
        public event EventHandler<EventArgs>? Initiated
        {
            add { }
            remove { }
        }

        public Task Init() => Task.CompletedTask;

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change)
        {
            UpdateAccessChecks.Add(Dispatcher.UIThread.CheckAccess());
            return Task.FromResult(change);
        }

        public Task<TaskItemViewModel> Update(TaskItem change)
        {
            UpdateAccessChecks.Add(Dispatcher.UIThread.CheckAccess());
            return Task.FromResult<TaskItemViewModel>(null!);
        }

        public Task<TaskOperationResult> TrySetStatusAsync(
            string taskId,
            Unlimotion.Domain.TaskStatus requestedStatus,
            string? author = null) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();
    }
}
