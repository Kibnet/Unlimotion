using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.TelegramBot;

namespace Unlimotion.Test;

public class TelegramBotGitTimerConflictSafetyTests
{
    [Test]
    public async Task PullTimer_SkipsPullWhenConflictResolutionIsInProgress()
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = true
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.PullLatestChanges();

        await Assert.That(git.ConflictChecks).IsEqualTo(1);
        await Assert.That(git.PullCalls).IsEqualTo(0);
        await Assert.That(git.PushCalls).IsEqualTo(0);
    }

    [Test]
    public async Task PushTimer_SkipsCommitAndPushWhenConflictResolutionIsInProgress()
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = true
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.CommitAndPushChanges();

        await Assert.That(git.ConflictChecks).IsEqualTo(1);
        await Assert.That(git.PullCalls).IsEqualTo(0);
        await Assert.That(git.PushCalls).IsEqualTo(0);
        await Assert.That(git.PushMessages).IsEmpty();
    }

    [Test]
    public async Task Timers_RunGitOperationsWhenNoConflictResolutionIsInProgress()
    {
        var git = new RecordingTelegramGitSyncOperations
        {
            ConflictResolutionInProgress = false
        };
        var handler = new TelegramGitTimerHandler(git);

        handler.PullLatestChanges();
        handler.CommitAndPushChanges();

        await Assert.That(git.ConflictChecks).IsEqualTo(2);
        await Assert.That(git.PullCalls).IsEqualTo(1);
        await Assert.That(git.PushCalls).IsEqualTo(1);
        await Assert.That(git.PushMessages).IsEquivalentTo([
            TelegramGitTimerHandler.AutomaticCommitMessage
        ]);
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
