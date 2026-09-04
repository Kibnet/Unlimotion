using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

/// <summary>A restored draft keeps its original identity even when saved on another day.</summary>
public sealed class FeedCaptureDraftRecoveryService(INoteVault vault, IFeedTaskConversionJournal journal)
{
    public async Task SaveAsync(FeedTaskConversionRecord operation, DailyNoteNaming naming, DateOnly today,
        string capture, AreaReference? area, CancellationToken cancellationToken = default)
    {
        if (operation.CaptureIntent is null || operation.RecoveryResolution != FeedOperationRecoveryResolution.KeptBoth)
            throw new InvalidOperationException("Only a retained capture draft may be saved as a note.");
        if (operation.RecoveredCaptureWrite is { } pending && (pending.CaptureText != capture || pending.Area != area))
            throw new InvalidOperationException("The retained draft write belongs to a different capture. Finish recovery first.");
        if (operation.RecoveredCaptureWrite is null)
        {
            var path = naming.GetRelativePath(today);
            var source = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            var parser = new MarkdownDocumentParser();
            var text = new MarkdownMutationService(parser).PlanQuickCapture(source?.Text ?? string.Empty, capture, area).UpdatedText;
            operation = operation with
            {
                RecoveredCaptureWrite = new FeedRecoveredCaptureWrite(path, text, source?.Revision, source?.HasUtf8Bom ?? false, capture, area),
                ReviewApplied = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        await ResumeAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResumeAsync(FeedTaskConversionRecord operation, CancellationToken cancellationToken = default)
    {
        var write = operation.RecoveredCaptureWrite ?? throw new InvalidDataException("The retained draft has no write intent.");
        var source = await vault.ReadAsync(write.RelativePath, cancellationToken).ConfigureAwait(false);
        var outputRevision = VaultRevision.Compute(VaultRevision.Encode(write.Text, write.HasUtf8Bom));
        if (source?.Revision != outputRevision)
        {
            if (source?.Revision != write.ExpectedRevision)
                throw new VaultRevisionConflictException(write.RelativePath, write.ExpectedRevision, source?.Revision);
            if (source is null)
                await vault.CreateAsync(write.RelativePath, write.Text, write.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
            else
                await vault.WriteAsync(write.RelativePath, write.Text, write.ExpectedRevision, write.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
        }
        await journal.MarkReviewAppliedAsync(operation.VaultId, operation.OperationId, cancellationToken).ConfigureAwait(false);
    }
}
