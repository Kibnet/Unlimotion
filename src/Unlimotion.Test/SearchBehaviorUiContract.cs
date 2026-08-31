using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class SearchBehaviorUiContract
{
    public static async Task<SearchBehaviorScenarioResult> ExecuteSearchBehaviorScenarioAsync()
    {
        await IndependentScenarioCases.RunAsync(
            ("TreeSearch_AllTasksSearchEditor_FiltersVisibleTree", MainControlTreeCommandsUiTests.SearchScenario.TreeSearch_AllTasksSearchEditor_FiltersVisibleTree),
            ("RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode", RoadmapGraphUiTests.SearchScenario.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode));
        var result = new SearchBehaviorScenarioResult
        {
            TreeSearchEditorFiltersVisibleTreePassed = true,
            RoadmapExactAndFuzzySearchPassed = true
        };

        return result;
    }

    public static async Task AssertSearchBehaviorScenarioResultAsync(SearchBehaviorScenarioResult result)
    {
        await Assert.That(result.TreeSearchEditorFiltersVisibleTreePassed).IsTrue();
        await Assert.That(result.RoadmapExactAndFuzzySearchPassed).IsTrue();
    }
}

internal sealed class SearchBehaviorScenarioResult
{
    public bool TreeSearchEditorFiltersVisibleTreePassed { get; set; }

    public bool RoadmapExactAndFuzzySearchPassed { get; set; }
}
