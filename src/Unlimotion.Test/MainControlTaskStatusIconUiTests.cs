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
    public async Task TaskTreeStatusControl_UsesCompactVectorIconInsteadOfTextGlyph()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskTreeStatusControl_ClickOpensStatusFlyout()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                PressControl(window, statusPicker);
                Dispatcher.UIThread.RunJobs();

                var flyout = statusPicker.Flyout as MenuFlyout;

                await Assert.That(flyout).IsNotNull();
                var menuItems = flyout!.Items.OfType<MenuItem>().ToList();
                var automationIds = menuItems
                    .Select(AutomationProperties.GetAutomationId)
                    .ToList();

                await Assert.That(menuItems).IsNotEmpty();
                await Assert.That(menuItems.All(item => item.IsEnabled)).IsTrue();
                await Assert.That(automationIds).DoesNotContain($"TaskStatusOption{task!.Status}");
                await Assert.That(flyout.IsOpen).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_MatchesStandaloneCheckBoxIndicatorSize()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPickerFlyout_ExposesOnlyAvailableTransitionOptions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-task",
                    Status = DomainTaskStatus.Prepared,
                    IsCanBeCompleted = true
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var buildFlyout = typeof(TaskStatusPicker).GetMethod(
                "BuildStatusFlyout",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TaskStatusPicker.BuildStatusFlyout was not found.");

            var flyout = (MenuFlyout)buildFlyout.Invoke(null, [task])!;
            var items = flyout.Items.OfType<MenuItem>().ToList();
            var availableTransitionStatuses = task.StatusOptions
                .Where(option => option.Status != task.Status && option.IsEnabled)
                .Select(option => option.Status)
                .ToList();

            await Assert.That(items.Count).IsEqualTo(availableTransitionStatuses.Count);
            foreach (var status in availableTransitionStatuses)
            {
                var item = items.Single(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        $"TaskStatusOption{status}",
                        StringComparison.Ordinal));
                var header = item.Header as StackPanel;

                await Assert.That(header).IsNotNull();
                var icon = header!.Children.OfType<TaskStatusIcon>().Single();
                var text = header.Children.OfType<TextBlock>().Single();

                await Assert.That(icon.Status).IsEqualTo(status);
                await Assert.That(text.Text).IsNotNull();
                await Assert.That(text.Text).IsNotEmpty();
            }

            await Assert.That(items.Select(AutomationProperties.GetAutomationId))
                .DoesNotContain($"TaskStatusOption{task.Status}");
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_SelectingStatusOption_UpdatesTaskStatusHistory()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

                PressControl(window, statusPicker);
                var flyout = statusPicker.Flyout as MenuFlyout;

                await Assert.That(flyout).IsNotNull();
                var inProgressItem = flyout!.Items
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
    public async Task TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
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
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var buildFlyout = typeof(TaskStatusPicker).GetMethod(
                "BuildStatusFlyout",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TaskStatusPicker.BuildStatusFlyout was not found.");

            var flyout = (MenuFlyout)buildFlyout.Invoke(null, [task])!;
            var completedItem = flyout.Items
                .OfType<MenuItem>()
                .SingleOrDefault(item =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(item),
                        "TaskStatusOptionCompleted",
                        StringComparison.Ordinal));

            await Assert.That(completedItem).IsNull();

            task.CompletionCriteria.Single().IsSatisfied = true;
            Dispatcher.UIThread.RunJobs();

            await Assert.That(task.StatusOptions.Single(option => option.Status == DomainTaskStatus.Completed).IsEnabled)
                .IsTrue();
            flyout = (MenuFlyout)buildFlyout.Invoke(null, [task])!;
            completedItem = flyout.Items
                .OfType<MenuItem>()
                .Single(item =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(item),
                        "TaskStatusOptionCompleted",
                        StringComparison.Ordinal));
            await Assert.That(completedItem.IsEnabled).IsTrue();

            InvokeMenuItemClick(completedItem);

            await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Completed);
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskStatusPicker_DetachedFromVisualTree_UnsubscribesFromTask()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

    private static void PressControl(Window window, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
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
                    candidate.Task is TaskItemViewModel);

            return statusPicker != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || statusPicker == null)
        {
            throw new InvalidOperationException("Task status picker was not found.");
        }

        return statusPicker;
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

        public Task<TaskItemViewModel> Update(TaskItem change) =>
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
