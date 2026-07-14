using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class SettingsStorageGitContract
{
    public static async Task<SettingsStorageGitScenarioResult> ExecuteAsync()
    {
        var settingsTests = new SettingsViewModelTests();
        var uiTests = new SettingsControlResponsiveUiTests();
        try
        {
            await settingsTests.CanConnectStorage_FollowsSelectedModeRequirements();
            await settingsTests.CanSyncRepository_RequiresBackupRemoteAndPushRefSpecWithoutConnectedState();
            await settingsTests.ConflictResolutionMode_DisablesSyncAndEnablesSelectedConflictActions();
            await uiTests.SettingsControl_SyncConflictResolutionMode_ShowsOpenResolverAction();

            return new SettingsStorageGitScenarioResult
            {
                StorageReadinessPassed = true,
                GitReadinessPassed = true,
                ConflictActionsPassed = true,
                ConflictActionUiPassed = true
            };
        }
        finally
        {
            settingsTests.Dispose();
        }
    }

    public static async Task AssertAsync(SettingsStorageGitScenarioResult result)
    {
        await Assert.That(result.StorageReadinessPassed).IsTrue();
        await Assert.That(result.GitReadinessPassed).IsTrue();
        await Assert.That(result.ConflictActionsPassed).IsTrue();
        await Assert.That(result.ConflictActionUiPassed).IsTrue();
    }
}

internal sealed class SettingsStorageGitScenarioResult
{
    public bool StorageReadinessPassed { get; set; }

    public bool GitReadinessPassed { get; set; }

    public bool ConflictActionsPassed { get; set; }

    public bool ConflictActionUiPassed { get; set; }
}
