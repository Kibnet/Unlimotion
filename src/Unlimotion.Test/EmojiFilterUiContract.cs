using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class EmojiFilterUiContract
{
    public static async Task<EmojiFilterScenarioResult> ExecuteEmojiFilterScenarioAsync()
    {
        await IndependentScenarioCases.RunAsync(
            ("Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing", MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing),
            ("Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter", MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter),
            ("Toolbar_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList", MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.Toolbar_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList),
            ("Toolbar_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup", MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.Toolbar_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup),
            ("RoadmapToolbar_EmojiFilters_UsesSearchableMultiSelectDropdown", MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.RoadmapToolbar_EmojiFilters_UsesSearchableMultiSelectDropdown));

        var result = new EmojiFilterScenarioResult
        {
            IncludeExcludeSearchAndFlyoutSemanticsPassed = true,
            AllItemTogglePassed = true,
            NoMatchesBehaviorPassed = true,
            KeyboardFlowPassed = true,
            RoadmapFlyoutPassed = true
        };

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
