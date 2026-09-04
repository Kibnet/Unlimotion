using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record FeedTaskCaptureRequest(
    string VaultId,
    string OperationId,
    DateOnly Date,
    string Capture,
    AreaReference? Area,
    string? ExpectedSourceRevision,
    IReadOnlyList<string> AreaIds,
    bool IsGoal = false);

public sealed record FeedTaskCaptureResult(
    string TaskId,
    string Title,
    string SourcePath,
    string SourceRevision);

/// <summary>
/// Journals the complete capture range before appending it. Conversion and recovery share the
/// same stable operation, so retries never append a second copy or select an identical older block.
/// </summary>
public sealed class FeedTaskCaptureService(
    INoteVault vault,
    DailyNoteService dailyNotes,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations,
    IFeedTaskCreationTarget taskTarget,
    IFeedTaskConversionJournal journal,
    IRevisionStore? revisions = null)
{
    public async Task<FeedTaskCaptureResult> CaptureAsync(
        FeedTaskCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Capture);

        FeedLinkSerializer.ValidateStableId(request.VaultId, nameof(request.VaultId));
        FeedLinkSerializer.ValidateStableId(request.OperationId, nameof(request.OperationId));
        var operation = await journal.LoadAsync(request.VaultId, request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
        {
            var path = dailyNotes.Naming.GetRelativePath(request.Date);
            var original = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedSourceRevision is not null
                && !string.Equals(original?.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
            {
                throw new VaultRevisionConflictException(path, request.ExpectedSourceRevision, original?.Revision);
            }

            var plan = mutations.PlanQuickCapture(original?.Text ?? string.Empty, request.Capture, request.Area);
            var document = parser.Parse(plan.UpdatedText);
            var contents = document.Blocks.Where(block => block.Kind != MarkdownBlockKind.Blank
                && block.Start >= plan.ContentStart
                && block.Start < plan.ContentStart + plan.ContentLength).ToArray();
            if (contents.Length == 0)
            {
                throw new InvalidDataException("The captured task range is empty.");
            }

            var selection = new MarkdownBlockSelection(contents[0].Index, contents[^1].Index - contents[0].Index + 1);
            var selected = selection.Resolve(document);
            var (title, description) = FeedTaskConversionService.ParseTaskContent(selected);
            var hasBom = original?.HasUtf8Bom ?? false;
            var expectedRevision = VaultRevision.Compute(VaultRevision.Encode(plan.UpdatedText, hasBom));
            operation = new FeedTaskConversionRecord(2, request.VaultId, request.OperationId,
                FeedTaskConversionState.Pending, path, expectedRevision, "feed-" + request.OperationId,
                null, DateTimeOffset.UtcNow,
                new FeedTaskConversionRecoveryDescriptor(request.OperationId, selection,
                    FeedOperationHash.Compute(string.Concat(selected.Select(block => block.Raw))),
                    null, title, description, request.IsGoal, request.AreaIds.ToArray(),
                    InputLocators: FeedOperationLocatorFactory.ForSelection(path, document, selection)),
                CaptureIntent: new FeedTaskCaptureIntent(request.Capture, request.Area, original?.Revision,
                    FeedOperationHash.Compute(plan.UpdatedText), hasBom, plan.UpdatedText));
            await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        else if (operation.CaptureIntent is { } intent
                 && (!string.Equals(intent.CaptureText, request.Capture, StringComparison.Ordinal)
                     || intent.Area != request.Area
                     || operation.RecoveryDescriptor?.IsGoal != request.IsGoal
                     || !operation.RecoveryDescriptor.AreaIds.SequenceEqual(request.AreaIds, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The capture operation ID belongs to a different draft.");
        }

        var conversion = new FeedTaskConversionService(
            vault,
            parser,
            mutations,
            taskTarget,
            journal,
            revisions);
        var result = await conversion.ResumeAsync(operation, cancellationToken)
            .ConfigureAwait(false);

        return new FeedTaskCaptureResult(
            result.TaskId,
            result.Title,
            operation.SourcePath,
            result.SourceRevision);
    }
}
