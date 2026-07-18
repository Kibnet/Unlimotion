using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class EmojiFilterUiContract
{
    public static async Task<EmojiFilterScenarioResult> ExecuteEmojiFilterScenarioAsync()
    {
        var result = new EmojiFilterScenarioResult();
        var tests = new MainControlFilterToolbarResponsiveUiTests();

        await tests.Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing();
        result.IncludeExcludeSearchAndFlyoutSemanticsPassed = true;

        await tests.Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter();
        result.AllItemTogglePassed = true;

        await tests.Toolbar_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList();
        result.NoMatchesBehaviorPassed = true;

        await tests.Toolbar_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup();
        result.KeyboardFlowPassed = true;

        await tests.RoadmapToolbar_EmojiFilters_UsesSearchableMultiSelectDropdown();
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
