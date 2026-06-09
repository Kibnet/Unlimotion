using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
                await Assert.That(statusPicker.Bounds.Width).IsGreaterThanOrEqualTo(24);
                await Assert.That(statusPicker.Bounds.Width).IsLessThanOrEqualTo(34);
                await Assert.That(statusIcons).HasSingleItem();
                await Assert.That(statusIcons[0].Bounds.Width).IsGreaterThanOrEqualTo(14);
                await Assert.That(statusIcons[0].Bounds.Height).IsGreaterThanOrEqualTo(14);
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
    public async Task TaskStatusPickerFlyout_ExposesOneIconOptionForEachLifecycleStatus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var task = new TaskItemViewModel(
                new TaskItem
                {
                    Id = "status-picker-task",
                    Status = DomainTaskStatus.Prepared
                },
                new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage())),
                () => false);
            var buildFlyout = typeof(TaskStatusPicker).GetMethod(
                "BuildStatusFlyout",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TaskStatusPicker.BuildStatusFlyout was not found.");

            var flyout = (MenuFlyout)buildFlyout.Invoke(null, [task])!;
            var items = flyout.Items.OfType<MenuItem>().ToList();
            var statuses = Enum.GetValues<DomainTaskStatus>();

            await Assert.That(items.Count).IsEqualTo(statuses.Length);
            foreach (var status in statuses)
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
}
