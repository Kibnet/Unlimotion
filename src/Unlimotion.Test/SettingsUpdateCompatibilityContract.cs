using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class SettingsUpdateCompatibilityContract
{
    public static async Task<SettingsUpdateCompatibilityScenarioResult> ExecuteAsync()
    {
        var settingsTests = new SettingsViewModelTests();
        var settingsUiTests = new SettingsControlResponsiveUiTests();
        var compatibilityUiTests = new PackageUpdateCompatibilityUiTests();
        try
        {
            await settingsTests.Updates_AreDisabled_WhenUpdateServiceIsUnsupported();
            await settingsTests.DownloadUpdateAsync_SetsReadyToApply_WhenUpdateWasFound();
            await settingsTests.ApplyUpdateAsync_CallsUpdateServiceRestart_WhenUpdateIsReady();
            await settingsUiTests.SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate();
            await compatibilityUiTests.RoadmapDropAndFolderPickerCompatibility_Work();

            return new SettingsUpdateCompatibilityScenarioResult
            {
                UpdateControlStatePassed = true,
                UpdateControlsUiPassed = true,
                CompatibilityUiPassed = true
            };
        }
        finally
        {
            settingsTests.Dispose();
        }
    }

    public static async Task AssertAsync(SettingsUpdateCompatibilityScenarioResult result)
    {
        await Assert.That(result.UpdateControlStatePassed).IsTrue();
        await Assert.That(result.UpdateControlsUiPassed).IsTrue();
        await Assert.That(result.CompatibilityUiPassed).IsTrue();
    }
}

internal sealed class SettingsUpdateCompatibilityScenarioResult
{
    public bool UpdateControlStatePassed { get; set; }

    public bool UpdateControlsUiPassed { get; set; }

    public bool CompatibilityUiPassed { get; set; }
}
