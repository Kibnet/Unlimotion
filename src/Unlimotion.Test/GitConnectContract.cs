using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class GitConnectContract
{
    public static async Task<GitConnectScenarioResult> ExecuteAsync()
    {
        var tests = new BackupViaGitServiceTests();
        try
        {
            await tests.PreviewConnectRepository_ChoosesInitialPushForEmptyRemoteAndNonEmptyLocalFolder();
            await tests.PreviewConnectRepository_RequiresConfirmationForNonEmptyRemoteAndNonEmptyLocalFolder();
            await tests.ConnectRepository_InitializesLocalRepositoryAndPushesLocalTasksToEmptyRemote();
            await tests.ConnectRepository_ChecksOutNonEmptyRemoteIntoEmptyLocalFolder();

            return new GitConnectScenarioResult
            {
                PreviewForEmptyRemotePassed = true,
                PreviewForNonEmptyRemotePassed = true,
                InitialPushConnectionPassed = true,
                RemoteCheckoutConnectionPassed = true
            };
        }
        finally
        {
            tests.Dispose();
        }
    }

    public static async Task AssertAsync(GitConnectScenarioResult result)
    {
        await Assert.That(result.PreviewForEmptyRemotePassed).IsTrue();
        await Assert.That(result.PreviewForNonEmptyRemotePassed).IsTrue();
        await Assert.That(result.InitialPushConnectionPassed).IsTrue();
        await Assert.That(result.RemoteCheckoutConnectionPassed).IsTrue();
    }
}

internal sealed class GitConnectScenarioResult
{
    public bool PreviewForEmptyRemotePassed { get; set; }

    public bool PreviewForNonEmptyRemotePassed { get; set; }

    public bool InitialPushConnectionPassed { get; set; }

    public bool RemoteCheckoutConnectionPassed { get; set; }
}
