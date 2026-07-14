using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class SettingsAppearanceContract
{
    public static async Task<SettingsAppearanceScenarioResult> ExecuteAsync()
    {
        var settingsTests = new SettingsViewModelTests();
        var fuzzyTests = new SettingsAppearanceContractTests();
        try
        {
            await settingsTests.ThemeMode_PersistsChoiceAndCompatibilityShimReflectsSelection();
            await settingsTests.FontSize_PersistsAndNormalizesConfiguredValue();
            await settingsTests.LanguageMode_PersistsChoiceAndUpdatesLocalizedStatusText();
            await fuzzyTests.FuzzySearch_PersistsChoice();

            return new SettingsAppearanceScenarioResult
            {
                ThemePassed = true,
                FontSizePassed = true,
                LanguagePassed = true,
                FuzzySearchPersistencePassed = true
            };
        }
        finally
        {
            fuzzyTests.Dispose();
            settingsTests.Dispose();
        }
    }

    public static async Task AssertAsync(SettingsAppearanceScenarioResult result)
    {
        await Assert.That(result.ThemePassed).IsTrue();
        await Assert.That(result.FontSizePassed).IsTrue();
        await Assert.That(result.LanguagePassed).IsTrue();
        await Assert.That(result.FuzzySearchPersistencePassed).IsTrue();
    }
}

internal sealed class SettingsAppearanceScenarioResult
{
    public bool ThemePassed { get; set; }

    public bool FontSizePassed { get; set; }

    public bool LanguagePassed { get; set; }

    public bool FuzzySearchPersistencePassed { get; set; }
}
