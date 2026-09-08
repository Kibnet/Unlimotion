using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Test;

public sealed class TaskClassificationRoundTripTests
{
    [Test]
    public async Task LocalAndRemoteMolds_PreserveGoalAndMultipleAreas()
    {
        var clientMapper = global::Unlimotion.AppModelMapping.ConfigureMapping();
        var serverMapper = global::Unlimotion.Server.AppModelMapping.ConfigureMapping();
        var source = CreateClassifiedTask();

        var serviceMold = clientMapper.Map<TaskItemMold>(source);
        var localRoundTrip = clientMapper.Map<TaskItem>(serviceMold);
        var hubMold = clientMapper.Map<TaskItemHubMold>(source);
        var serverTask = serverMapper.Map<TaskItem>(hubMold);
        var receivedMold = serverMapper.Map<ReceiveTaskItem>(serverTask);
        var remoteRoundTrip = clientMapper.Map<TaskItem>(receivedMold);

        await Assert.That(serviceMold.IsGoal).IsTrue();
        await Assert.That(serviceMold.AreaIds).IsEquivalentTo(source.AreaIds);
        await Assert.That(localRoundTrip.IsGoal).IsTrue();
        await Assert.That(localRoundTrip.AreaIds).IsEquivalentTo(source.AreaIds);

        await Assert.That(hubMold.TaskClassificationSchemaVersion)
            .IsEqualTo(TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion);
        await Assert.That(hubMold.IsGoal).IsTrue();
        await Assert.That(hubMold.AreaIds!).IsEquivalentTo(source.AreaIds);
        await Assert.That(serverTask.IsGoal).IsTrue();
        await Assert.That(serverTask.AreaIds).IsEquivalentTo(source.AreaIds);

        await Assert.That(receivedMold.IsGoal).IsTrue();
        await Assert.That(receivedMold.AreaIds).IsEquivalentTo(source.AreaIds);
        await Assert.That(remoteRoundTrip.IsGoal).IsTrue();
        await Assert.That(remoteRoundTrip.AreaIds).IsEquivalentTo(source.AreaIds);
    }

    private static TaskItem CreateClassifiedTask() => new()
    {
        Id = "classified-task",
        UserId = "owner",
        Title = "Goal with several areas",
        Description = "Classification round trip",
        IsGoal = true,
        AreaIds = ["area/work", "area/product"],
        ContainsTasks = new List<string>(),
        ParentTasks = new List<string>(),
        BlocksTasks = new List<string>(),
        BlockedByTasks = new List<string>()
    };
}
