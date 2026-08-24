using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.Hubs;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskClassificationCompatibilityTests
{
    [Test]
    public async Task OldClientWholeTaskUpdate_PreservesServerClassification()
    {
        var mapper = global::Unlimotion.Server.AppModelMapping.ConfigureMapping();
        var storedTask = CreateStoredTask();
        var oldClientUpdate = CreateHubUpdate();
        oldClientUpdate.Title = "Legacy title update";
        oldClientUpdate.Status = DomainTaskStatus.InProgress;

        mapper.Map(oldClientUpdate, storedTask);

        await Assert.That(storedTask.Title).IsEqualTo("Legacy title update");
        await Assert.That(storedTask.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(storedTask.IsGoal).IsTrue();
        await Assert.That(storedTask.AreaIds).IsEquivalentTo(["area/original", "area/shared"]);
    }

    [Test]
    public async Task NewClientPresentClassification_UpdatesServerClassification()
    {
        var mapper = global::Unlimotion.Server.AppModelMapping.ConfigureMapping();
        var storedTask = CreateStoredTask();
        var newClientUpdate = CreateHubUpdate();
        newClientUpdate.TaskClassificationSchemaVersion =
            TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion;
        newClientUpdate.IsGoal = false;
        newClientUpdate.AreaIds = ["area/new"];

        mapper.Map(newClientUpdate, storedTask);

        await Assert.That(storedTask.IsGoal).IsFalse();
        await Assert.That(storedTask.AreaIds).IsEquivalentTo(["area/new"]);
    }

    [Test]
    public async Task NewClientOmittedClassificationField_PreservesThatServerField()
    {
        var mapper = global::Unlimotion.Server.AppModelMapping.ConfigureMapping();
        var storedTask = CreateStoredTask();
        var partialUpdate = CreateHubUpdate();
        partialUpdate.TaskClassificationSchemaVersion =
            TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion;
        partialUpdate.IsGoal = false;
        partialUpdate.AreaIds = null;

        mapper.Map(partialUpdate, storedTask);

        await Assert.That(storedTask.IsGoal).IsFalse();
        await Assert.That(storedTask.AreaIds).IsEquivalentTo(["area/original", "area/shared"]);
    }

    [Test]
    public async Task ServerCapabilityEndpoint_ReportsCurrentClassificationSchema()
    {
        var hub = new ChatHub(null!, null!);

        var capabilities = await hub.GetTaskStorageCapabilities();

        await Assert.That(capabilities.TaskClassificationSchemaVersion)
            .IsEqualTo(TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion);
    }

    [Test]
    public async Task ClientCapabilityQuery_MissingEndpointFallsBackToUnsupported()
    {
        var supported = await ServerStorage.QueryTaskStorageCapabilitiesAsync(
            () => Task.FromResult(TaskStorageCapabilities.CreateCurrent()));
        var unsupported = await ServerStorage.QueryTaskStorageCapabilitiesAsync(
            () => Task.FromException<TaskStorageCapabilities>(new MissingMethodException()));

        await Assert.That(supported.TaskClassificationSchemaVersion)
            .IsEqualTo(TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion);
        await Assert.That(unsupported.TaskClassificationSchemaVersion).IsEqualTo(0);
    }

    [Test]
    public async Task NewClientSaveToUnsupportedServer_OmitsClassificationFromOutboundRequest()
    {
        var mapper = global::Unlimotion.AppModelMapping.ConfigureMapping();
        var classifiedTask = CreateStoredTask();

        var outbound = ServerStorage.CreateOutboundTaskMold(
            mapper,
            classifiedTask,
            TaskStorageCapabilities.CreateUnsupported());

        await Assert.That(outbound).IsNotNull();
        await Assert.That(outbound!.TaskClassificationSchemaVersion).IsNull();
        await Assert.That(outbound.IsGoal).IsNull();
        await Assert.That(outbound.AreaIds).IsNull();
    }

    [Test]
    public async Task NewClientSaveToCurrentServer_IncludesClassificationInOutboundRequest()
    {
        var mapper = global::Unlimotion.AppModelMapping.ConfigureMapping();
        var classifiedTask = CreateStoredTask();

        var outbound = ServerStorage.CreateOutboundTaskMold(
            mapper,
            classifiedTask,
            TaskStorageCapabilities.CreateCurrent());

        await Assert.That(outbound).IsNotNull();
        await Assert.That(outbound!.TaskClassificationSchemaVersion)
            .IsEqualTo(TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion);
        await Assert.That(outbound.IsGoal).IsTrue();
        await Assert.That(outbound.AreaIds).IsEquivalentTo(["area/original", "area/shared"]);
    }

    private static TaskItem CreateStoredTask() => new()
    {
        Id = "stored-task",
        UserId = "owner",
        Title = "Stored title",
        Description = "Stored description",
        Status = DomainTaskStatus.Prepared,
        IsGoal = true,
        AreaIds = ["area/original", "area/shared"],
        ContainsTasks = new List<string>(),
        ParentTasks = new List<string>(),
        BlocksTasks = new List<string>(),
        BlockedByTasks = new List<string>()
    };

    private static TaskItemHubMold CreateHubUpdate() => new()
    {
        Id = "stored-task",
        Title = "Updated title",
        Description = "Updated description",
        Status = DomainTaskStatus.Prepared,
        StatusHistory = new List<TaskStatusHistoryEntry>(),
        CompletionCriteria = new List<TaskCompletionCriterion>(),
        ContainsTasks = new List<string>(),
        ParentTasks = new List<string>(),
        BlocksTasks = new List<string>(),
        BlockedByTasks = new List<string>()
    };
}
