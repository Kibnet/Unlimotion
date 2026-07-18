using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class GitRemoteAuthContract
{
    public static async Task<GitRemoteAuthScenarioResult> ExecuteAsync()
    {
        var tests = new BackupViaGitServiceTests();
        try
        {
            await tests.SwitchRemoteConnectionType_CreatesSshRemoteForSingleHttpRemote();
            await tests.SwitchRemoteConnectionType_SelectsExistingCanonicalTargetWithoutDuplicate();
            await tests.GetCredentials_ReturnsSshPrivateKeyCredentialsForConfiguredSshUrl();
            await tests.GetSshPublicKeys_ReadsConfiguredSshKeyStoragePath();

            return new GitRemoteAuthScenarioResult
            {
                SshRemoteSelectionPassed = true,
                TokenHttpRemoteSelectionPassed = true,
                SshCredentialsPassed = true,
                SshKeyStoragePassed = true
            };
        }
        finally
        {
            tests.Dispose();
        }
    }

    public static async Task AssertAsync(GitRemoteAuthScenarioResult result)
    {
        await Assert.That(result.SshRemoteSelectionPassed).IsTrue();
        await Assert.That(result.TokenHttpRemoteSelectionPassed).IsTrue();
        await Assert.That(result.SshCredentialsPassed).IsTrue();
        await Assert.That(result.SshKeyStoragePassed).IsTrue();
    }
}

internal sealed class GitRemoteAuthScenarioResult
{
    public bool SshRemoteSelectionPassed { get; set; }

    public bool TokenHttpRemoteSelectionPassed { get; set; }

    public bool SshCredentialsPassed { get; set; }

    public bool SshKeyStoragePassed { get; set; }
}
