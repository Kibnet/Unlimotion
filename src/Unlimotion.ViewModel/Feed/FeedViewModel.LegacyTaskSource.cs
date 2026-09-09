using System;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    private async Task ConfigureLegacyRecoveryCandidateAsync(FeedPendingRecoveryViewModel item)
    {
        if (item.Kind != FeedPendingRecoveryKind.TaskConversion || vaultId is null
            || TaskSourceIdentityProvider?.Invoke() is not { } source) return;
        try
        {
            var capturedVault = vaultId;
            var record = await taskJournalFactory(capturedVault).LoadAsync(capturedVault, item.OperationId, GetSessionToken());
            if (record is not { SchemaVersion: 2, TaskSourceIdentity: null } || !PendingRecoveries.Contains(item)) return;
            item.ConfigureLegacySourceVerification(source.SourceId, () => ExecuteReviewOperationAsync(async token =>
            {
                if (!item.IsOriginalSourceConfirmed || vaultId != capturedVault || vault is null || markdownParser is null
                    || TaskCreationTarget is null) return;
                try
                {
                    FeedTaskSourceIdentity.RequireCurrent(source, TaskSourceIdentityProvider);
                    var journal = taskJournalFactory(capturedVault);
                    var pending = await journal.LoadAsync(capturedVault, item.OperationId, token)
                        ?? throw new InvalidOperationException("FeedLegacySourceUnverifiable");
                    var service = new FeedTaskConversionService(vault, markdownParser, new MarkdownMutationService(markdownParser),
                        TaskCreationTarget, journal, revisionStore, TaskSourceIdentityProvider);
                    var bound = await service.BindLegacyToVerifiedSourceAsync(pending, source, token);
                    item.CanKeepBoth = CanKeepTaskRecovery(bound);
                    item.CompleteLegacySourceVerification();
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    item.Message = L10n.Get(error.Message is "FeedLegacySourceUnverifiable" or "FeedTaskSourceMismatch"
                        ? error.Message : "FeedRecoveryRetryHint");
                }
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Legacy source verification setup: {0}", error);
        }
    }
}
