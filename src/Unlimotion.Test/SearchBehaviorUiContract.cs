using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class SearchBehaviorUiContract
{
    public static async Task<SearchBehaviorScenarioResult> ExecuteSearchBehaviorScenarioAsync()
    {
        var result = new SearchBehaviorScenarioResult();

        var treeTests = new MainControlTreeCommandsUiTests();
        await treeTests.TreeSearch_AllTasksSearchEditor_FiltersVisibleTree();
        result.TreeSearchEditorFiltersVisibleTreePassed = true;

        var roadmapTests = new RoadmapGraphUiTests();
        await roadmapTests.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode();
        result.RoadmapExactAndFuzzySearchPassed = true;

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
