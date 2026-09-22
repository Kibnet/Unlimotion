using System;
using System.Threading.Tasks;
using Unlimotion.Desktop.Services;

namespace Unlimotion.Test;

public class TaskDeepLinkActivationBrokerTests
{
    [Test]
    [NotInParallel]
    public async Task SecondaryBroker_ForwardsActivationExactlyOnceToOwner()
    {
        var channelName = "Unlimotion.TaskDeepLinkActivation.Tests." + Guid.NewGuid().ToString("N");
        using var owner = new TaskDeepLinkActivationBroker(channelName);
        using var secondary = new TaskDeepLinkActivationBroker(channelName);
        var received = new TaskCompletionSource<TaskDeepLink>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationCount = 0;
        owner.ActivationRequested += (_, args) =>
        {
            activationCount++;
            received.TrySetResult(args.Link);
        };

        var link = new TaskDeepLink("feed-12422d3acca249db950bccce95f0d723");
        var forwarded = await secondary.TryForwardAsync(link);
        var activated = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(owner.IsOwner).IsTrue();
        await Assert.That(secondary.IsOwner).IsFalse();
        await Assert.That(forwarded).IsTrue();
        await Assert.That(activated).IsEqualTo(link);
        await Assert.That(activationCount).IsEqualTo(1);
    }
}
