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
/// Captures ordinary text into today's note and then converts only the newly appended block into
/// a stable task link. A crash before task creation leaves useful source text; a crash after task
/// creation is recovered by <see cref="FeedTaskConversionService"/> and its durable journal.
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

        var appended = await dailyNotes.AppendCaptureAsync(
                request.Date,
                request.Capture,
                request.Area,
                request.ExpectedSourceRevision,
                cancellationToken)
            .ConfigureAwait(false);
        var document = parser.Parse(appended.Text);
        var normalizedCapture = request.Capture.TrimEnd('\r', '\n');
        var appendedBlock = document.Blocks
            .Where(static block => block.IsContent)
            .LastOrDefault(block => string.Equals(
                block.Raw.TrimEnd('\r', '\n'),
                normalizedCapture,
                StringComparison.Ordinal))
            ?? document.Blocks.LastOrDefault(static block => block.IsContent)
            ?? throw new InvalidDataException("The captured task block could not be found in the daily note.");

        var conversion = new FeedTaskConversionService(
            vault,
            parser,
            mutations,
            taskTarget,
            journal,
            revisions);
        var result = await conversion.ConvertAsync(
                new FeedTaskConversionRequest(
                    request.VaultId,
                    request.OperationId,
                    dailyNotes.Naming.GetRelativePath(request.Date),
                    appended.Revision,
                    new MarkdownBlockSelection(appendedBlock.Index, 1),
                    request.AreaIds,
                    request.IsGoal),
                cancellationToken)
            .ConfigureAwait(false);

        return new FeedTaskCaptureResult(
            result.TaskId,
            result.Title,
            dailyNotes.Naming.GetRelativePath(request.Date),
            result.SourceRevision);
    }
}
