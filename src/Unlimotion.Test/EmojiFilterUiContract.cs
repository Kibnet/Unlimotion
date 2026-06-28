using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class EmojiFilterUiContract
{
    public static async Task<EmojiFilterScenarioResult> ExecuteEmojiFilterScenarioAsync()
    {
        var result = new EmojiFilterScenarioResult();
        var tests = new MainControlFilterToolbarResponsiveUiTests();

        await tests.FilterFlyout_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing();
        result.IncludeExcludeSearchAndFlyoutSemanticsPassed = true;

        await tests.FilterFlyout_EmojiFilters_AllItemTogglesEveryEmojiFilter();
        result.AllItemTogglePassed = true;

        await tests.FilterFlyout_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList();
        result.NoMatchesBehaviorPassed = true;

        await tests.FilterFlyout_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup();
        result.KeyboardFlowPassed = true;

        await tests.RoadmapFilterFlyout_EmojiFilters_UsesSearchableMultiSelectDropdown();
        result.RoadmapFlyoutPassed = true;

        return result;
    }

    public static async Task AssertEmojiFilterScenarioResultAsync(EmojiFilterScenarioResult result)
    {
        await Assert.That(result.IncludeExcludeSearchAndFlyoutSemanticsPassed).IsTrue();
        await Assert.That(result.AllItemTogglePassed).IsTrue();
        await Assert.That(result.NoMatchesBehaviorPassed).IsTrue();
        await Assert.That(result.KeyboardFlowPassed).IsTrue();
        await Assert.That(result.RoadmapFlyoutPassed).IsTrue();
    }
}

internal sealed class EmojiFilterScenarioResult
{
    public bool IncludeExcludeSearchAndFlyoutSemanticsPassed { get; set; }

    public bool AllItemTogglePassed { get; set; }

    public bool NoMatchesBehaviorPassed { get; set; }

    public bool KeyboardFlowPassed { get; set; }

    public bool RoadmapFlyoutPassed { get; set; }
}
