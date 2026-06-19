using System.Threading.Tasks;

namespace Unlimotion.Test;

public class TelegramBotGitTimerConflictSafetyTests
{
    [Test]
    public async Task PullTimer_SkipsPullWhenConflictResolutionIsInProgress()
    {
        await TelegramGitTimerConflictSafetyContract
            .AssertPullTimerSkipsPullWhenConflictResolutionIsInProgressAsync();
    }

    [Test]
    public async Task PushTimer_SkipsCommitAndPushWhenConflictResolutionIsInProgress()
    {
        await TelegramGitTimerConflictSafetyContract
            .AssertPushTimerSkipsCommitAndPushWhenConflictResolutionIsInProgressAsync();
    }

    [Test]
    public async Task Timers_RunGitOperationsWhenNoConflictResolutionIsInProgress()
    {
        await TelegramGitTimerConflictSafetyContract
            .AssertTimersRunGitOperationsWhenNoConflictResolutionIsInProgressAsync();
    }
}
