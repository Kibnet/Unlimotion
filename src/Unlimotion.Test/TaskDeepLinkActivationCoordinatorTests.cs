using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace Unlimotion.Test;

public class TaskDeepLinkActivationCoordinatorTests
{
    [Test]
    public async Task ColdRequests_WaitForReadiness_ThenDrainInOrderExactlyOnce()
    {
        var ready = false;
        var delivered = new List<TaskDeepLink>();
        using var coordinator = new TaskDeepLinkActivationCoordinator(() => ready, delivered.Add, action => action());
        coordinator.Queue(new("first"));
        coordinator.Queue(new("second"));
        await Assert.That(delivered).IsEmpty();
        ready = true;
        coordinator.ProcessPending();
        coordinator.ProcessPending();
        await Assert.That(delivered).IsEquivalentTo(new[] { new TaskDeepLink("first"), new TaskDeepLink("second") });
        await Assert.That(delivered[0].TaskId).IsEqualTo("first");
        await Assert.That(delivered[1].TaskId).IsEqualTo("second");
    }

    [Test]
    public async Task PendingAndWarmSourceRequests_AreDeliveredOnce_AndDetachedOnDispose()
    {
        using var source = new AvaloniaTaskDeepLinkActivationSource();
        source.ProcessActivation(new ProtocolActivatedEventArgs(new Uri("unlimotion://task/cold")));
        var delivered = new List<TaskDeepLink>();
        var jobs = new Queue<Action>();
        var coordinator = new TaskDeepLinkActivationCoordinator(() => true, delivered.Add, jobs.Enqueue);
        coordinator.Attach(source);
        coordinator.Attach(source);
        source.ProcessActivation(new ProtocolActivatedEventArgs(new Uri("unlimotion://task/warm")));
        await Assert.That(delivered.Count).IsEqualTo(1);
        jobs.Dequeue()();
        source.ProcessActivation(new ProtocolActivatedEventArgs(new Uri("unlimotion://task/late")));
        coordinator.Dispose();
        jobs.Dequeue()();
        await Assert.That(delivered).IsEquivalentTo(new[] { new TaskDeepLink("cold"), new TaskDeepLink("warm") });
    }
}
