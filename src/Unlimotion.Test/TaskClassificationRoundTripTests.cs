using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Test;

public sealed class TaskClassificationRoundTripTests
{
    [Test]
    public async Task LocalAndRemoteMolds_PreserveMultipleAreas()
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

        await Assert.That(serviceMold.AreaIds).IsEquivalentTo(source.AreaIds);
        await Assert.That(localRoundTrip.AreaIds).IsEquivalentTo(source.AreaIds);

        await Assert.That(hubMold.TaskClassificationSchemaVersion)
            .IsEqualTo(TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion);
        await Assert.That(hubMold.AreaIds!).IsEquivalentTo(source.AreaIds);
        await Assert.That(serverTask.AreaIds).IsEquivalentTo(source.AreaIds);

        await Assert.That(receivedMold.AreaIds).IsEquivalentTo(source.AreaIds);
        await Assert.That(remoteRoundTrip.AreaIds).IsEquivalentTo(source.AreaIds);

        foreach (var mold in new object[] { serviceMold, hubMold, receivedMold })
        {
            await Assert.That(JObject.Parse(JsonConvert.SerializeObject(mold)).Property("IsGoal")).IsNull();
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LegacyTransportGoal_IsIgnoredWhileAreasSurvive(bool legacyGoal)
    {
        var json = new JObject
        {
            ["Id"] = "old-client-task",
            ["IsGoal"] = legacyGoal,
            ["AreaIds"] = new JArray("area/work", "area/shared")
        }.ToString();
        var serviceMold = JsonConvert.DeserializeObject<TaskItemMold>(json)!;
        var hubMold = JsonConvert.DeserializeObject<TaskItemHubMold>(json)!;
        var receivedMold = JsonConvert.DeserializeObject<ReceiveTaskItem>(json)!;
        foreach (var mold in new object[] { serviceMold, hubMold, receivedMold })
        {
            var saved = JObject.Parse(JsonConvert.SerializeObject(mold));
            await Assert.That(saved.Property("IsGoal")).IsNull();
            await Assert.That(saved["AreaIds"]!.ToObject<string[]>()).IsEquivalentTo(["area/work", "area/shared"]);
        }
    }

    private static TaskItem CreateClassifiedTask() => new()
    {
        Id = "classified-task",
        UserId = "owner",
        Title = "Task with several areas",
        Description = "Classification round trip",
        AreaIds = ["area/work", "area/product"],
        ContainsTasks = new List<string>(),
        ParentTasks = new List<string>(),
        BlocksTasks = new List<string>(),
        BlockedByTasks = new List<string>()
    };
}
