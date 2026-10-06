using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskDateFilterTimezoneTests
{
    [Test]
    [Arguments(TaskListKind.LastCreated)]
    [Arguments(TaskListKind.LastUpdated)]
    [Arguments(TaskListKind.Completed)]
    [Arguments(TaskListKind.Archived)]
    public async Task Today_RendersSameLateEveningInstantRegardlessOfStoredOffset(TaskListKind kind)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var synthetic = new List<TaskItemViewModel>();
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                // Fixed wall-clock sample, not DateTimeOffset.Now: reproduce the evening
                // rollover even if this test runs before 21:00 in the local time zone.
                var wallClock = DateTime.Today.AddHours(21).AddMinutes(30);
                var local = new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock));
                var samples = new[] { local, local.ToUniversalTime(), local.ToOffset(TimeSpan.FromHours(14)) };
                var status = kind == TaskListKind.Completed ? DomainTaskStatus.Completed
                    : kind == TaskListKind.Archived ? DomainTaskStatus.Archived : DomainTaskStatus.Prepared;
                foreach (var (sample, index) in samples.Append(local.AddDays(-1)).Select((sample, index) => (sample, index)))
                {
                    var model = new TaskItem
                    {
                        Id = $"date-offset-{kind}-{index}", Title = $"Date offset {kind} {index}",
                        Status = status, CreatedDateTime = sample, UpdatedDateTime = sample,
                        CompletedDateTime = sample, ArchiveDateTime = sample
                    };
                    var item = new TaskItemViewModel(model, owner.taskRepository!, () => false);
                    synthetic.Add(item);
                    owner.taskRepository!.Tasks.AddOrUpdate(item);
                }

                var filter = kind switch
                {
                    TaskListKind.LastCreated => owner.LastCreatedDateFilter,
                    TaskListKind.LastUpdated => owner.LastUpdatedDateFilter,
                    TaskListKind.Completed => owner.CompletedDateFilter,
                    _ => owner.ArchivedDateFilter
                };
                filter.IsCustom = false;
                filter.CurrentOption = DateFilterDefinition.AllTime;
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Content = shell, Width = 1300, Height = 700 };
                window.Show();
                await owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(kind));
                TaskListDocumentView? List() => shell.GetVisualDescendants().OfType<TaskListDocumentView>()
                    .SingleOrDefault(view => view.IsEffectivelyVisible && view.Kind == kind);
                string[] VisibleIds() => List()?.TaskTree?.ItemsSource?.Cast<TaskWrapperViewModel>()
                    .Select(wrapper => wrapper.TaskItem.Id).Where(id => id.StartsWith($"date-offset-{kind}-", StringComparison.Ordinal))
                    .Order().ToArray() ?? [];
                await WaitAsync(window, () => VisibleIds().Length == 4);
                filter.CurrentOption = DateFilterDefinition.Today;
                filter.SetDateTimes(DateFilterDefinition.Today);
                var expected = synthetic.Take(3).Select(item => item.Id).Order().ToArray();
                await WaitAsync(window, () => VisibleIds().SequenceEqual(expected));
                await Assert.That(VisibleIds().SequenceEqual(expected)).IsTrue();
                window.UpdateLayout();
                var rendered = List()!.GetVisualDescendants().OfType<TreeViewItem>()
                    .Where(row => row.IsEffectivelyVisible && row.Bounds.Height > 0)
                    .Select(row => (row.DataContext as TaskWrapperViewModel)?.TaskItem.Id).ToArray();
                await Assert.That(expected.All(id => rendered.Contains(id))).IsTrue();
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                foreach (var item in synthetic)
                {
                    fixture.MainWindowViewModelTest.taskRepository!.Tasks.RemoveKey(item.Id);
                    item.Dispose();
                }
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task WaitAsync(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); if (condition()) return; await Task.Delay(20); }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException("The date-filter UI projection did not become ready.");
    }
}
