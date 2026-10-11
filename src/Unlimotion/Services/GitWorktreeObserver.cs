using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class GitWorktreeObserver : IGitWorktreeObserver
{
    public Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Read(request, cancellationToken), cancellationToken);

    private static GitWorktreeObservation Read(GitObservationRequest request, CancellationToken cancellationToken)
    {
        if (!request.Applicable) return new(request, DateTimeOffset.UtcNow, GitObservationKind.NotApplicable);
        var entered = false;
        try
        {
            // Same cooperative lock as backup operations. No runner lease, settings projection, or Git writes.
            var deadline = Environment.TickCount64 + 2000;
            while (!(entered = Monitor.TryEnter(BackupViaGitService.GitLock, 50)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount64 >= deadline) return Unknown("LockTimeout");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!Repository.IsValid(request.RepositoryPath)) return Unknown("RepositoryMissing");
            using var repository = new Repository(request.RepositoryPath);
            // Read status without refreshing/writing the index.
            var status = repository.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true });
            var dirty = status.Where(entry => entry.State != FileStatus.Unaltered && entry.State != FileStatus.Ignored)
                .Select(entry => entry.FilePath).Distinct(StringComparer.Ordinal).Count();
            var conflicts = repository.Index.Conflicts.Any();
            int? ahead = null, behind = null;
            if (!repository.Info.IsHeadDetached && !repository.Info.IsHeadUnborn && repository.Head.TrackedBranch?.Tip != null)
            {
                var divergence = repository.ObjectDatabase.CalculateHistoryDivergence(repository.Head.Tip, repository.Head.TrackedBranch.Tip);
                ahead = divergence.AheadBy;
                behind = divergence.BehindBy;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(request, DateTimeOffset.UtcNow, GitObservationKind.Known, dirty, conflicts, ahead, behind);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unknown("ReadFailed"); }
        finally { if (entered) Monitor.Exit(BackupViaGitService.GitLock); }
        GitWorktreeObservation Unknown(string code) => new(request, DateTimeOffset.UtcNow, GitObservationKind.Unknown, ErrorCode: code);
    }
}
