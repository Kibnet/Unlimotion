using System;
using System.Threading.Tasks;

namespace Unlimotion.Test;

internal static class GitConflictResolutionContract
{
    public static async Task<GitConflictResolutionScenarioResult> ExecuteAsync()
    {
        var tests = new BackupViaGitServiceTests();
        try
        {
            await tests.ResolveConflict_UseCurrentVersion_CommitsAndPushesResolution();
            await tests.ResolveConflictFields_UsesSelectedVersionsAndMergedFields();

            return new GitConflictResolutionScenarioResult
            {
                FileResolutionPassed = true,
                FieldResolutionPassed = true
            };
        }
        finally
        {
            tests.Dispose();
        }
    }

    public static async Task AssertAsync(GitConflictResolutionScenarioResult result)
    {
        await Assert.That(result.FileResolutionPassed).IsTrue();
        await Assert.That(result.FieldResolutionPassed).IsTrue();
    }
}

internal sealed class GitConflictResolutionScenarioResult
{
    public bool FileResolutionPassed { get; set; }

    public bool FieldResolutionPassed { get; set; }
}
