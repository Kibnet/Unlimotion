using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class RoadmapViewportOverlayContract
{
    public static async Task<RoadmapViewportOverlayScenarioResult> ExecuteRoadmapViewportOverlayScenarioAsync()
    {
        var result = new RoadmapViewportOverlayScenarioResult();
        var roadmapTests = new RoadmapGraphUiTests();

        await roadmapTests.RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls();
        result.StandardViewportControlsAvailable = true;

        await roadmapTests.RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore();
        result.CompactOverlaysRecoverAndRemainInteractive = true;

        return result;
    }

    public static async Task AssertRoadmapViewportOverlayScenarioResultAsync(
        RoadmapViewportOverlayScenarioResult result)
    {
        await Assert.That(result.StandardViewportControlsAvailable).IsTrue();
        await Assert.That(result.CompactOverlaysRecoverAndRemainInteractive).IsTrue();
    }
}

internal sealed class RoadmapViewportOverlayScenarioResult
{
    public bool StandardViewportControlsAvailable { get; set; }

    public bool CompactOverlaysRecoverAndRemainInteractive { get; set; }
}
