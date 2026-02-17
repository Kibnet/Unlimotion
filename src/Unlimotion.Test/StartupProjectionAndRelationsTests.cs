using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.Test;

[NotInParallel]
public class StartupProjectionAndRelationsTests : BaseModelTests
{
    [Test]
    public async Task TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds()
    {
        foreach (var task in taskRepository.Tasks.Items)
        {
            var containsIds = task.ContainsTasks.Select(item => item.Id).ToHashSet();
            var parentIds = task.ParentsTasks.Select(item => item.Id).ToHashSet();
            var blocksIds = task.BlocksTasks.Select(item => item.Id).ToHashSet();
            var blockedByIds = task.BlockedByTasks.Select(item => item.Id).ToHashSet();

            await Assert.That(containsIds.SetEquals(task.Contains)).IsTrue();
            await Assert.That(parentIds.SetEquals(task.Parents)).IsTrue();
            await Assert.That(blocksIds.SetEquals(task.Blocks)).IsTrue();
            await Assert.That(blockedByIds.SetEquals(task.BlockedBy)).IsTrue();
        }
    }

    [Test]
    public async Task HeavyProjections_ShouldBeLoadedLazilyAfterTabActivation()
    {
        await Assert.That(mainWindowVM.CompletedItems).IsEmpty();
        await Assert.That(mainWindowVM.Graph.Tasks).IsEmpty();
        await Assert.That(mainWindowVM.Graph.UnlockedTasks).IsEmpty();
        mainWindowVM.GraphMode = true;

        var graphReady = SpinWait.SpinUntil(() => mainWindowVM.Graph.Tasks.Count > 0, TimeSpan.FromSeconds(3));
        await Assert.That(graphReady).IsTrue();
    }
}
