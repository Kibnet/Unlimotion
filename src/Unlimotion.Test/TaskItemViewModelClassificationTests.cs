using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DynamicData;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public sealed class TaskItemViewModelClassificationTests
{
    [Test]
    public async Task Model_RoundTrip_PreservesGoalAndSeveralAreas()
    {
        var storage = new CapturingTaskStorage();
        using var viewModel = new TaskItemViewModel(
            CreateTask(isGoal: true, ["area/work", "area/product"]),
            storage,
            () => false);

        var model = viewModel.Model;

        await Assert.That(viewModel.IsGoal).IsTrue();
        await Assert.That(viewModel.AreaIds).IsEquivalentTo(["area/work", "area/product"]);
        await Assert.That(model.IsGoal).IsTrue();
        await Assert.That(model.AreaIds).IsEquivalentTo(["area/work", "area/product"]);
    }

    [Test]
    public async Task Update_ReplacesClassificationWithoutLosingOtherFields()
    {
        var storage = new CapturingTaskStorage();
        using var viewModel = new TaskItemViewModel(
            CreateTask(isGoal: false, ["area/old"]),
            storage,
            () => false);
        var updated = CreateTask(isGoal: true, ["area/new", "area/shared"]);
        updated.Title = "Updated title";

        viewModel.Update(updated);

        await Assert.That(viewModel.Title).IsEqualTo("Updated title");
        await Assert.That(viewModel.IsGoal).IsTrue();
        await Assert.That(viewModel.AreaIds).IsEquivalentTo(["area/new", "area/shared"]);
    }

    [Test]
    public async Task Model_RoundTrip_PreservesUnknownExtensionData()
    {
        var storage = new CapturingTaskStorage();
        var source = CreateTask(isGoal: false, []);
        source.ExtensionData = new Dictionary<string, JToken>
        {
            ["futureField"] = JObject.Parse("{\"nested\":[1,2,3]}")
        };
        using var viewModel = new TaskItemViewModel(source, storage, () => false);

        var roundTrip = viewModel.Model;

        await Assert.That(roundTrip.ExtensionData).IsNotNull();
        await Assert.That(JToken.DeepEquals(
            source.ExtensionData["futureField"],
            roundTrip.ExtensionData!["futureField"])).IsTrue();
        await Assert.That(ReferenceEquals(
            source.ExtensionData["futureField"],
            roundTrip.ExtensionData["futureField"])).IsFalse();
    }

    private static TaskItem CreateTask(bool isGoal, List<string> areaIds) => new()
    {
        Id = "task-1",
        Title = "Task",
        IsGoal = isGoal,
        AreaIds = areaIds,
        ContainsTasks = [],
        ParentTasks = [],
        BlocksTasks = [],
        BlockedByTasks = []
    };

    private sealed class CapturingTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);
        public ITaskRelationsIndex Relations => null!;
        public TaskTreeManager TaskTreeManager => null!;
        public event EventHandler<EventArgs>? Initiated;

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
        public Task<bool> MoveInto(
            TaskItemViewModel change,
            TaskItemViewModel[] additionalParents,
            TaskItemViewModel? currentTask) => throw new NotSupportedException();
        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();
        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();
        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();
    }
}
