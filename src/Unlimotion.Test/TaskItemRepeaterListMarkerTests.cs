using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class TaskItemRepeaterListMarkerTests
{
    [Test]
    public async Task RepeaterListMarker_ShowsOnlyForActiveRepeater()
    {
        using var taskWithoutRepeater = CreateTask(null);
        using var taskWithNoneRepeater = CreateTask(new RepeaterPattern { Type = RepeaterType.None, Period = 1 });
        using var taskWithDailyRepeater = CreateTask(new RepeaterPattern { Type = RepeaterType.Daily, Period = 1 });

        await Assert.That(taskWithoutRepeater.IsHaveRepeater).IsFalse();
        await Assert.That(taskWithoutRepeater.RepeaterListMarker).IsEqualTo(string.Empty);
        await Assert.That(taskWithoutRepeater.RepeaterListMarkerToolTip).IsNull();

        await Assert.That(taskWithNoneRepeater.IsHaveRepeater).IsFalse();
        await Assert.That(taskWithNoneRepeater.RepeaterListMarker).IsEqualTo(string.Empty);
        await Assert.That(taskWithNoneRepeater.RepeaterListMarkerToolTip).IsNull();

        await Assert.That(taskWithDailyRepeater.IsHaveRepeater).IsTrue();
        await Assert.That(taskWithDailyRepeater.RepeaterListMarker).IsEqualTo("↻");
        await Assert.That(taskWithDailyRepeater.RepeaterListMarkerToolTip).IsEqualTo(taskWithDailyRepeater.Repeater.Title);
    }

    [Test]
    public async Task RepeaterListMarker_NotifiesWhenRepeaterChanges()
    {
        using var task = CreateTask(null);
        var changedProperties = TrackChangedProperties(task);

        task.Repeater = new RepeaterPatternViewModel { Type = RepeaterType.Daily, Period = 1 };

        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.IsHaveRepeater));
        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.RepeaterListMarker));
        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.RepeaterListMarkerToolTip));
    }

    [Test]
    public async Task RepeaterListMarker_NotifiesWhenCurrentRepeaterPatternChanges()
    {
        var repeater = new RepeaterPatternViewModel { Type = RepeaterType.None, Period = 1 };
        using var task = CreateTask(null);
        task.Repeater = repeater;
        var changedProperties = TrackChangedProperties(task);

        repeater.Type = RepeaterType.Daily;

        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.IsHaveRepeater));
        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.RepeaterListMarker));
        await Assert.That(changedProperties).Contains(nameof(TaskItemViewModel.RepeaterListMarkerToolTip));
    }

    [Test]
    public async Task RepeaterListMarker_IgnoresOldRepeaterPatternAfterReplacement()
    {
        var oldRepeater = new RepeaterPatternViewModel { Type = RepeaterType.None, Period = 1 };
        var currentRepeater = new RepeaterPatternViewModel { Type = RepeaterType.Daily, Period = 1 };
        using var task = CreateTask(null);
        task.Repeater = oldRepeater;
        task.Repeater = currentRepeater;
        var changedProperties = TrackChangedProperties(task);

        oldRepeater.Type = RepeaterType.Yearly;

        await Assert.That(changedProperties).DoesNotContain(nameof(TaskItemViewModel.IsHaveRepeater));
        await Assert.That(changedProperties).DoesNotContain(nameof(TaskItemViewModel.RepeaterListMarker));
        await Assert.That(changedProperties).DoesNotContain(nameof(TaskItemViewModel.RepeaterListMarkerToolTip));
    }

    private static TaskItemViewModel CreateTask(RepeaterPattern? repeater)
    {
        var model = new TaskItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Task",
            IsCompleted = false,
            IsCanBeCompleted = true,
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        if (repeater != null)
        {
            model.Repeater = repeater;
        }

        return new TaskItemViewModel(model, new StubTaskStorage());
    }

    private static List<string?> TrackChangedProperties(TaskItemViewModel task)
    {
        var changedProperties = new List<string?>();
        ((INotifyPropertyChanged)task).PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        return changedProperties;
    }

    private sealed class StubTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);
        public ITaskRelationsIndex Relations => throw new NotSupportedException();
        public TaskTreeManager TaskTreeManager => throw new NotSupportedException();
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

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) => Task.FromResult(change);

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
