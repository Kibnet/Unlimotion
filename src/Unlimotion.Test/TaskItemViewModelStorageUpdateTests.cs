using System;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskItemViewModelStorageUpdateTests
{
    [Test]
    [Arguments(nameof(TaskItemViewModel.Title))]
    [Arguments(nameof(TaskItemViewModel.Description))]
    [Arguments(nameof(TaskItemViewModel.PlannedBeginDateTime))]
    [Arguments(nameof(TaskItemViewModel.Importance))]
    [Arguments(nameof(TaskItemViewModel.Wanted))]
    [Arguments(nameof(TaskItemViewModel.Repeater))]
    [Arguments(nameof(TaskItemViewModel.CompletionCriteria))]
    public async Task StorageUpdate_PreservesOnlyTheLocallyChangedEditableField(string changedField)
    {
        using var storage = new TestTaskStorage();
        TaskCompletionSource? releaseCompletionCriteriaSave = null;
        if (changedField == nameof(TaskItemViewModel.CompletionCriteria))
        {
            releaseCompletionCriteriaSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.UpdateHandler = _ => releaseCompletionCriteriaSave.Task;
        }

        using var viewModel = new TaskItemViewModel(CreateTask(), storage, () => true)
        {
            PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromDays(1)
        };

        ApplyLocalChange(viewModel, changedField);
        var authoritative = CreateTask() with
        {
            Title = "storage title",
            Description = "storage description",
            PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(4),
            PlannedEndDateTime = DateTimeOffset.UtcNow.AddDays(5),
            PlannedDuration = TimeSpan.FromHours(6),
            Importance = 2,
            Wanted = false,
            Repeater = new RepeaterPattern { Type = RepeaterType.Monthly, Period = 3 },
            CompletionCriteria = [new TaskCompletionCriterion { Id = "storage", Text = "storage criterion" }],
            Status = DomainTaskStatus.Prepared
        };

        var accepted = viewModel.Update(authoritative, storageRevision: 1);
        releaseCompletionCriteriaSave?.TrySetResult();

        await Assert.That(accepted).IsTrue();
        await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(viewModel.Title).IsEqualTo(
            changedField == nameof(TaskItemViewModel.Title) ? "local title" : "storage title");
        await Assert.That(viewModel.Description).IsEqualTo(
            changedField == nameof(TaskItemViewModel.Description) ? "local description" : "storage description");
        await Assert.That(viewModel.PlannedBeginDateTime).IsEqualTo(
            changedField == nameof(TaskItemViewModel.PlannedBeginDateTime)
                ? LocalPlanningBegin.LocalDateTime
                : authoritative.PlannedBeginDateTime!.Value.LocalDateTime);
        await Assert.That(viewModel.Importance).IsEqualTo(
            changedField == nameof(TaskItemViewModel.Importance) ? 9 : 2);
        await Assert.That(viewModel.Wanted).IsEqualTo(
            changedField == nameof(TaskItemViewModel.Wanted));
        await Assert.That(viewModel.Repeater!.Type).IsEqualTo(
            changedField == nameof(TaskItemViewModel.Repeater) ? RepeaterType.Daily : RepeaterType.Monthly);
        await Assert.That(viewModel.CompletionCriteria.Single().Text).IsEqualTo(
            changedField == nameof(TaskItemViewModel.CompletionCriteria) ? "local criterion" : "storage criterion");

        switch (changedField)
        {
            case nameof(TaskItemViewModel.Title):
                await Assert.That(viewModel.Title).IsEqualTo("local title");
                break;
            case nameof(TaskItemViewModel.Description):
                await Assert.That(viewModel.Description).IsEqualTo("local description");
                break;
            case nameof(TaskItemViewModel.PlannedBeginDateTime):
                await Assert.That(viewModel.PlannedBeginDateTime).IsEqualTo(LocalPlanningBegin.LocalDateTime);
                break;
            case nameof(TaskItemViewModel.Importance):
                await Assert.That(viewModel.Importance).IsEqualTo(9);
                break;
            case nameof(TaskItemViewModel.Wanted):
                await Assert.That(viewModel.Wanted).IsTrue();
                break;
            case nameof(TaskItemViewModel.Repeater):
                await Assert.That(viewModel.Repeater!.Type).IsEqualTo(RepeaterType.Daily);
                break;
            case nameof(TaskItemViewModel.CompletionCriteria):
                await Assert.That(viewModel.CompletionCriteria.Single().Text).IsEqualTo("local criterion");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changedField), changedField, null);
        }
    }

    [Test]
    public async Task SaveAcknowledgement_LeavesANewerLocalChangePending()
    {
        using var storage = new TestTaskStorage();
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UpdateHandler = async _ =>
        {
            firstWriteStarted.TrySetResult();
            await releaseFirstWrite.Task;
        };

        using var viewModel = new TaskItemViewModel(CreateTask(), storage, () => true)
        {
            PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromDays(1)
        };
        viewModel.Title = "first local title";
        var firstSave = viewModel.SaveItemCommand.Execute().ToTask();
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Title = "newer local title";
        releaseFirstWrite.TrySetResult();
        await firstSave.WaitAsync(TimeSpan.FromSeconds(5));

        var authoritative = CreateTask() with { Title = "stale storage title" };
        viewModel.Update(authoritative, storageRevision: 1);
        await Assert.That(viewModel.Title).IsEqualTo("newer local title");

        storage.UpdateHandler = null;
        await viewModel.SaveItemCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(storage.Updates.Last().Title).IsEqualTo("newer local title");
    }

    [Test]
    public async Task StorageUpdate_UsesAuthoritativeStateWhenThereIsNoPendingLocalChange()
    {
        using var storage = new TestTaskStorage();
        using var viewModel = new TaskItemViewModel(CreateTask(), storage, () => true);
        var authoritative = CreateTask() with
        {
            Title = "authoritative title",
            Description = "authoritative description",
            Importance = 8,
            Wanted = true,
            Status = DomainTaskStatus.Prepared
        };

        var accepted = viewModel.Update(authoritative, storageRevision: 1);

        using (Assert.Multiple())
        {
            await Assert.That(accepted).IsTrue();
            await Assert.That(viewModel.Title).IsEqualTo("authoritative title");
            await Assert.That(viewModel.Description).IsEqualTo("authoritative description");
            await Assert.That(viewModel.Importance).IsEqualTo(8);
            await Assert.That(viewModel.Wanted).IsTrue();
            await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Prepared);
        }
    }

    [Test]
    public async Task StorageUpdate_PreservesNestedRepeaterPatternChange()
    {
        using var storage = new TestTaskStorage();
        using var viewModel = new TaskItemViewModel(CreateTask(), storage, () => true)
        {
            PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromDays(1)
        };
        viewModel.Repeater!.Period = 2;

        var authoritative = CreateTask() with
        {
            Repeater = new RepeaterPattern { Type = RepeaterType.Monthly, Period = 3 }
        };
        viewModel.Update(authoritative, storageRevision: 1);

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.Repeater!.Type).IsEqualTo(RepeaterType.Daily);
            await Assert.That(viewModel.Repeater.Period).IsEqualTo(2);
        }
    }

    [Test]
    [Arguments(nameof(TaskCompletionCriterion.Text))]
    [Arguments(nameof(TaskCompletionCriterion.IsSatisfied))]
    public async Task StorageUpdate_PreservesNestedCompletionCriterionChange(string changedProperty)
    {
        using var storage = new TestTaskStorage();
        using var viewModel = new TaskItemViewModel(CreateTask(), storage, () => true)
        {
            PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromDays(1)
        };
        var criterion = viewModel.CompletionCriteria.Single();
        if (changedProperty == nameof(TaskCompletionCriterion.Text))
        {
            criterion.Text = "local nested criterion";
        }
        else
        {
            criterion.IsSatisfied = true;
        }

        var authoritative = CreateTask() with
        {
            CompletionCriteria =
            [
                new TaskCompletionCriterion
                {
                    Id = "storage",
                    Text = "storage criterion",
                    IsSatisfied = false
                }
            ]
        };
        viewModel.Update(authoritative, storageRevision: 1);

        var preserved = viewModel.CompletionCriteria.Single();
        await Assert.That(preserved.Text).IsEqualTo(
            changedProperty == nameof(TaskCompletionCriterion.Text)
                ? "local nested criterion"
                : "initial criterion");
        await Assert.That(preserved.IsSatisfied).IsEqualTo(
            changedProperty == nameof(TaskCompletionCriterion.IsSatisfied));
    }

    private static readonly DateTimeOffset LocalPlanningBegin = new(2030, 4, 5, 10, 0, 0, TimeSpan.Zero);

    private static TaskItem CreateTask() => new()
    {
        Id = "task",
        Title = "initial title",
        Description = "initial description",
        Status = DomainTaskStatus.NotReady,
        IsCanBeCompleted = true,
        PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(1),
        PlannedEndDateTime = DateTimeOffset.UtcNow.AddDays(2),
        PlannedDuration = TimeSpan.FromHours(1),
        Importance = 1,
        Wanted = false,
        Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Period = 1 },
        CompletionCriteria = [new TaskCompletionCriterion { Id = "initial", Text = "initial criterion" }]
    };

    private static void ApplyLocalChange(TaskItemViewModel viewModel, string changedField)
    {
        switch (changedField)
        {
            case nameof(TaskItemViewModel.Title):
                viewModel.Title = "local title";
                break;
            case nameof(TaskItemViewModel.Description):
                viewModel.Description = "local description";
                break;
            case nameof(TaskItemViewModel.PlannedBeginDateTime):
                viewModel.PlannedBeginDateTime = LocalPlanningBegin.LocalDateTime;
                break;
            case nameof(TaskItemViewModel.Importance):
                viewModel.Importance = 9;
                break;
            case nameof(TaskItemViewModel.Wanted):
                viewModel.Wanted = true;
                break;
            case nameof(TaskItemViewModel.Repeater):
                viewModel.Repeater = new RepeaterPatternViewModel(new RepeaterPattern
                {
                    Type = RepeaterType.Daily,
                    Period = 2
                });
                break;
            case nameof(TaskItemViewModel.CompletionCriteria):
                viewModel.CompletionCriteria.Clear();
                viewModel.CompletionCriteria.Add(new TaskCompletionCriterion { Id = "local", Text = "local criterion" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changedField), changedField, null);
        }
    }

    private sealed class TestTaskStorage : ITaskStorage, IDisposable
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);
        public ITaskRelationsIndex Relations { get; } = new TaskRelationsIndex();
        public TaskTreeManager TaskTreeManager { get; } = new(new InMemoryStorage());
        public Func<TaskItem, Task>? UpdateHandler { get; set; }
        public System.Collections.Generic.List<TaskItem> Updates { get; } = [];
        public event EventHandler<EventArgs>? Initiated;

        public Task Init()
        {
            Initiated?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) => throw new NotSupportedException();
        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) => throw new NotSupportedException();
        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) => throw new NotSupportedException();
        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) => throw new NotSupportedException();
        public Task<TaskItemViewModel> Update(TaskItemViewModel change) => Update(change.Model);

        public async Task<TaskItemViewModel> Update(TaskItem change)
        {
            var snapshot = TaskItemSnapshot.Clone(change);
            Updates.Add(snapshot);
            if (UpdateHandler is not null)
            {
                await UpdateHandler(snapshot);
            }

            return null!;
        }
        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) => throw new NotSupportedException();
        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) => throw new NotSupportedException();
        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) => throw new NotSupportedException();
        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) => throw new NotSupportedException();
        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) => throw new NotSupportedException();
        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) => throw new NotSupportedException();

        public void Dispose() => Tasks.Dispose();
    }
}
