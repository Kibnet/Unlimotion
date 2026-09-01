using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class RoadmapInteractionsContract
{
    public static async Task<RoadmapInteractionsScenarioResult> ExecuteAsync()
    {
        var result = new RoadmapInteractionsScenarioResult();
        var filterTests = new MainControlFilterToolbarResponsiveUiTests();
        await filterTests.RoadmapFilterToolbar_NarrowViewport_UsesCompactPrimaryActions();
        result.FiltersAvailable = true;

        var roadmapTests = new RoadmapGraphUiTests();
        await RoadmapGraphUiTests.SearchScenario.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode();
        result.SearchAvailable = true;
        await roadmapTests.RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick();
        result.InlineRenameAvailable = true;
        await roadmapTests.RoadmapGraph_NodeClickSelection_AppliesModifierSemanticsAndVisualState();
        result.MultiSelectionAvailable = true;
        await roadmapTests.RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls();
        result.ViewportControlsAvailable = true;
        return result;
    }

    public static async Task AssertAsync(RoadmapInteractionsScenarioResult result)
    {
        await Assert.That(result.FiltersAvailable).IsTrue();
        await Assert.That(result.SearchAvailable).IsTrue();
        await Assert.That(result.InlineRenameAvailable).IsTrue();
        await Assert.That(result.MultiSelectionAvailable).IsTrue();
        await Assert.That(result.ViewportControlsAvailable).IsTrue();
    }
}

internal sealed class RoadmapInteractionsScenarioResult
{
    public bool FiltersAvailable { get; set; }
    public bool SearchAvailable { get; set; }
    public bool InlineRenameAvailable { get; set; }
    public bool MultiSelectionAvailable { get; set; }
    public bool ViewportControlsAvailable { get; set; }
}
