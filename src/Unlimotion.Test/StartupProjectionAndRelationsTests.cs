using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace Unlimotion.Test;

public class StartupProjectionAndRelationsTests : BaseModelTests
{
    [Fact]
    public void TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds()
    {
        foreach (var task in taskRepository.Tasks.Items)
        {
            var containsIds = task.ContainsTasks.Select(item => item.Id).ToHashSet();
            var parentIds = task.ParentsTasks.Select(item => item.Id).ToHashSet();
            var blocksIds = task.BlocksTasks.Select(item => item.Id).ToHashSet();
            var blockedByIds = task.BlockedByTasks.Select(item => item.Id).ToHashSet();

            Assert.Equal(task.Contains.ToHashSet(), containsIds);
            Assert.Equal(task.Parents.ToHashSet(), parentIds);
            Assert.Equal(task.Blocks.ToHashSet(), blocksIds);
            Assert.Equal(task.BlockedBy.ToHashSet(), blockedByIds);
        }
    }

    [Fact]
    public void HeavyProjections_ShouldBeLoadedLazilyAfterTabActivation()
    {
        Assert.Empty(mainWindowVM.CompletedItems);
        Assert.Empty(mainWindowVM.Graph.Tasks);
        Assert.Empty(mainWindowVM.Graph.UnlockedTasks);
        mainWindowVM.GraphMode = true;

        var graphReady = SpinWait.SpinUntil(() => mainWindowVM.Graph.Tasks.Count > 0, TimeSpan.FromSeconds(3));
        Assert.True(graphReady);
    }
}
