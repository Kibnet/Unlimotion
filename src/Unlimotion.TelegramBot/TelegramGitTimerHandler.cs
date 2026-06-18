using LibGit2Sharp;
using Serilog;

namespace Unlimotion.TelegramBot;

internal sealed class TelegramGitTimerHandler(ITelegramGitSyncOperations gitOperations)
{
    public const string AutomaticCommitMessage = "Автоматический коммит изменений.";

    public void PullLatestChanges()
    {
        if (ShouldSkipSync())
        {
            return;
        }

        Log.Information("Выполняется автоматический pull изменений из репозитория.");
        gitOperations.PullLatestChanges();
    }

    public void CommitAndPushChanges()
    {
        if (ShouldSkipSync())
        {
            return;
        }

        Log.Information("Выполняется автоматический commit/push изменений в репозиторий.");
        gitOperations.CommitAndPushChanges(AutomaticCommitMessage);
    }

    private static bool IsConflictOperationInProgress(CurrentOperation operation)
    {
        return operation is CurrentOperation.Merge
            or CurrentOperation.Rebase
            or CurrentOperation.RebaseInteractive
            or CurrentOperation.RebaseMerge
            or CurrentOperation.CherryPick
            or CurrentOperation.CherryPickSequence
            or CurrentOperation.Revert
            or CurrentOperation.RevertSequence
            or CurrentOperation.ApplyMailbox
            or CurrentOperation.ApplyMailboxOrRebase;
    }

    private bool ShouldSkipSync()
    {
        if (!gitOperations.IsConflictResolutionInProgress())
        {
            return false;
        }

        Log.Information("Автоматическая Git-синхронизация Telegram bot пропущена: идет разрешение конфликтов.");
        return true;
    }

    internal static bool HasConflictResolutionInProgress(Repository repo)
    {
        return repo.Index.Conflicts.Any() ||
               IsConflictOperationInProgress(repo.Info.CurrentOperation);
    }
}

internal interface ITelegramGitSyncOperations
{
    bool IsConflictResolutionInProgress();

    void PullLatestChanges();

    void CommitAndPushChanges(string message);
}

internal sealed class GitServiceTelegramGitSyncOperations(GitService gitService) : ITelegramGitSyncOperations
{
    public bool IsConflictResolutionInProgress()
    {
        return gitService.IsConflictResolutionInProgress();
    }

    public void PullLatestChanges()
    {
        gitService.PullLatestChanges();
    }

    public void CommitAndPushChanges(string message)
    {
        gitService.CommitAndPushChanges(message);
    }
}
