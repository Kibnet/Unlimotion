using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class RoadmapProjectionContract
{
    public static async Task<RoadmapProjectionScenarioResult> ExecuteRoadmapProjectionScenarioAsync()
    {
        var result = new RoadmapProjectionScenarioResult();
        var roadmapTests = new RoadmapGraphUiTests();

        await roadmapTests.RoadmapGraphProjection_BuildsNodesAndTypedConnections();
        result.CurrentTaskModelProjected = true;

        await roadmapTests.RoadmapGraph_NodifyView_RendersTasksAndKeepsAutomationIds();
        result.RoadmapViewRendersTaskGraph = true;

        return result;
    }

    public static async Task AssertRoadmapProjectionScenarioResultAsync(
        RoadmapProjectionScenarioResult result)
    {
        await Assert.That(result.CurrentTaskModelProjected).IsTrue();
        await Assert.That(result.RoadmapViewRendersTaskGraph).IsTrue();
    }
}

internal sealed class RoadmapProjectionScenarioResult
{
    public bool CurrentTaskModelProjected { get; set; }

    public bool RoadmapViewRendersTaskGraph { get; set; }
}
