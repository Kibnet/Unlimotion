using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.TelegramBot;

namespace Unlimotion.Test;

internal static class TelegramGitTimerConflictSafetyContract
{
    public static async Task AssertPullTimerSkipsPullWhenConflictResolutionIsInProgressAsync()
    {
        var result = TriggerPullTimer(conflictResolutionInProgress: true);

        await Assert.That(result.ConflictChecks).IsEqualTo(1);
        await Assert.That(result.PullCalls).IsEqualTo(0);
        await Assert.That(result.PushCalls).IsEqualTo(0);
    }

    public static async Task AssertPushTimerSkipsCommitAndPushWhenConflictResolutionIsInProgressAsync()
    {
        var result = TriggerPushTimer(conflictResolutionInProgress: true);

        await Assert.That(result.ConflictChecks).IsEqualTo(1);
        await Assert.That(result.PullCalls).IsEqualTo(0);
        await Assert.That(result.PushCalls).IsEqualTo(0);
        await Assert.That(result.PushMessages).IsEmpty();
    }

    public static async Task AssertTimersRunGitOperationsWhenNoConflictResolutionIsInProgressAsync()
    {
        var result = TriggerPullAndPushTimers(conflictResolutionInProgress: false);

        await Assert.That(result.ConflictChecks).IsEqualTo(2);
        await Assert.That(result.PullCalls).IsEqualTo(1);
        await Assert.That(result.PushCalls).IsEqualTo(1);
        await Assert.That(result.PushMessages).IsEquivalentTo([
            TelegramGitTimerHandler.AutomaticCommitMessage
        ]);
    }

    public static TelegramGitTimerExecutionResult TriggerPullAndPushTimers(bool conflictResolutionInProgress)
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = conflictResolutionInProgress
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.PullLatestChanges();
        handler.CommitAndPushChanges();

        return CreateResult(git);
    }

    private static TelegramGitTimerExecutionResult TriggerPullTimer(bool conflictResolutionInProgress)
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = conflictResolutionInProgress
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.PullLatestChanges();

        return CreateResult(git);
    }

    private static TelegramGitTimerExecutionResult TriggerPushTimer(bool conflictResolutionInProgress)
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = conflictResolutionInProgress
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.CommitAndPushChanges();

        return CreateResult(git);
    }

    private static TelegramGitTimerExecutionResult CreateResult(RecordingTelegramGitSyncOperations git)
    {
        return new TelegramGitTimerExecutionResult(
            git.ConflictChecks,
            git.PullCalls,
            git.PushCalls,
            git.PushMessages);
    }

    private sealed class RecordingTelegramGitSyncOperations : ITelegramGitSyncOperations
    {
        public bool ConflictResolutionInProgress { get; init; }

        public int ConflictChecks { get; private set; }

        public int PullCalls { get; private set; }

        public int PushCalls { get; private set; }

        public List<string> PushMessages { get; } = [];

        public bool IsConflictResolutionInProgress()
        {
            ConflictChecks++;
            return ConflictResolutionInProgress;
        }

        public void PullLatestChanges()
        {
            PullCalls++;
        }

        public void CommitAndPushChanges(string message)
        {
            PushCalls++;
            PushMessages.Add(message);
        }
    }
}

internal sealed record TelegramGitTimerExecutionResult(
    int ConflictChecks,
    int PullCalls,
    int PushCalls,
    IReadOnlyList<string> PushMessages);
