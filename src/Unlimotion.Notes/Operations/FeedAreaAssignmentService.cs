using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record FeedAreaAssignmentRequest(
    string SourcePath,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    AreaReference? DestinationArea);

public sealed record FeedAreaAssignmentResult(
    string SourceRevision,
    MarkdownBlockSelection OutputSelection,
    IReadOnlyList<BlockLocator> InputLocators,
    IReadOnlyList<BlockLocator> OutputLocators);

public sealed class FeedAreaAssignmentService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations)
{
    public async Task<FeedAreaAssignmentResult> AssignAsync(
        FeedAreaAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
        if (!string.Equals(source.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(request.SourcePath, request.ExpectedSourceRevision, source.Revision);
        }

        var document = parser.Parse(source.Text);
        var selected = request.Selection.Resolve(document);
        var inputLocators = FeedReviewQueue.CoveredLocators(request.SourcePath, document, request.Selection);
        var signatures = selected.Select(static block => (block.Kind, block.ContentHash)).ToArray();
        var updated = mutations.MoveSelectionToArea(source.Text, request.Selection, request.DestinationArea);
        var write = await vault.WriteAsync(
                request.SourcePath,
                updated,
                request.ExpectedSourceRevision,
                source.HasUtf8Bom,
                cancellationToken)
            .ConfigureAwait(false);

        var outputDocument = parser.Parse(updated);
        var outputSelection = FindMovedSelection(outputDocument, signatures, request.DestinationArea);
        var outputLocators = FeedReviewQueue.CoveredLocators(request.SourcePath, outputDocument, outputSelection);
        return new FeedAreaAssignmentResult(write.Revision, outputSelection, inputLocators, outputLocators);
    }

    private static MarkdownBlockSelection FindMovedSelection(
        MarkdownDocument document,
        IReadOnlyList<(MarkdownBlockKind Kind, string ContentHash)> signatures,
        AreaReference? destinationArea)
    {
        for (var start = document.Blocks.Count - signatures.Count; start >= 0; start--)
        {
            var matches = true;
            for (var offset = 0; offset < signatures.Count; offset++)
            {
                var block = document.Blocks[start + offset];
                var signature = signatures[offset];
                if (block.Kind != signature.Kind || !string.Equals(block.ContentHash, signature.ContentHash, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches && SelectionBelongsToArea(document, start, signatures.Count, destinationArea))
            {
                return new MarkdownBlockSelection(start, signatures.Count);
            }
        }

        throw new InvalidDataException("The moved Markdown selection could not be resolved after the atomic write.");
    }

    private static bool SelectionBelongsToArea(
        MarkdownDocument document,
        int start,
        int count,
        AreaReference? destinationArea)
    {
        var firstContent = document.Blocks
            .Skip(start)
            .Take(count)
            .FirstOrDefault(static block => block.IsContent);
        if (firstContent is null)
        {
            return false;
        }

        if (destinationArea is null)
        {
            return string.IsNullOrWhiteSpace(firstContent.AreaId)
                && string.IsNullOrWhiteSpace(firstContent.AreaName);
        }

        return !string.IsNullOrWhiteSpace(destinationArea.Id)
            && string.Equals(firstContent.AreaId, destinationArea.Id, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(destinationArea.Id)
            && string.IsNullOrWhiteSpace(firstContent.AreaId)
            && string.Equals(firstContent.AreaName, destinationArea.Name, StringComparison.OrdinalIgnoreCase);
    }
}
