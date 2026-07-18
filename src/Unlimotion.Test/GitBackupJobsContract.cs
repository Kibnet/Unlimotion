using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class GitBackupJobsContract
{
    public static async Task<GitBackupJobsScenarioResult> ExecuteAsync()
    {
        var jobTests = new GitBackupJobTests();
        var gitTests = new BackupViaGitServiceTests();
        try
        {
            await jobTests.Jobs_RunWhenBackupIsEnabledAndNoConflictResolutionIsInProgress();
            await gitTests.PullExistingRepository_PullsRemoteChanges_WhenTaskFolderIsExistingRepository();
            await gitTests.ConnectRepository_MergesNonEmptyRemoteWithLocalFolderAfterConfirmation();

            return new GitBackupJobsScenarioResult
            {
                JobsExecutePassed = true,
                RemotePullPassed = true,
                TaskPreservationPassed = true
            };
        }
        finally
        {
            gitTests.Dispose();
            jobTests.Dispose();
        }
    }

    public static async Task AssertAsync(GitBackupJobsScenarioResult result)
    {
        await Assert.That(result.JobsExecutePassed).IsTrue();
        await Assert.That(result.RemotePullPassed).IsTrue();
        await Assert.That(result.TaskPreservationPassed).IsTrue();
    }
}

internal sealed class GitBackupJobsScenarioResult
{
    public bool JobsExecutePassed { get; set; }

    public bool RemotePullPassed { get; set; }

    public bool TaskPreservationPassed { get; set; }
}
