using Unlimotion.Notes.Markdown;

namespace Unlimotion.Notes.Review;

public enum FeedReviewPriority
{
    IncompleteCheckbox,
    Deferred,
    WithoutArea,
    Other
}

public sealed record FeedReviewCandidate(
    BlockLocator Locator,
    MarkdownBlock Block,
    DateOnly Day,
    FeedReviewPriority Priority,
    string? DeferredFromSessionId);

public sealed class FeedReviewQueue(IMarkdownDocumentParser parser, ReviewStateStore state)
{
    public IReadOnlyList<FeedReviewCandidate> Build(
        IEnumerable<(string RelativePath, string Raw)> dailyFiles,
        CausalEnvelope currentSessionCausality)
    {
        var unresolved = new List<(BlockLocator Locator, MarkdownBlock Block, DateOnly Day)>();
        foreach (var (relativePath, raw) in dailyFiles)
        {
            var day = ParseDailyDate(relativePath);
            var document = parser.Parse(raw);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var contentBlocks = document.Blocks.Where(static block => block.IsContent).ToArray();
            for (var blockIndex = 0; blockIndex < contentBlocks.Length; blockIndex++)
            {
                var block = contentBlocks[blockIndex];
                if (block.Kind == MarkdownBlockKind.TaskListItem && block.IsTaskCompleted == true)
                {
                    continue;
                }

                var area = block.AreaId ?? block.AreaName;
                var occurrenceKey = string.Join('|', area, block.Kind, block.ContentHash);
                occurrences.TryGetValue(occurrenceKey, out var occurrence);
                occurrences[occurrenceKey] = occurrence + 1;
                var locator = CreateLocator(relativePath, contentBlocks, blockIndex, occurrence);
                unresolved.Add((locator, block, day));
            }
        }

        var currentLocators = unresolved.Select(static candidate => candidate.Locator).ToArray();
        var candidates = new List<FeedReviewCandidate>(unresolved.Count);
        foreach (var (locator, block, day) in unresolved)
        {
            var area = block.AreaId ?? block.AreaName;
            var effective = state.Resolve(locator, currentLocators);
            if (effective.IsTerminal)
            {
                continue;
            }

            string? deferredFrom = null;
            if (effective.Event?.Decision == ReviewDecision.Deferred)
            {
                deferredFrom = effective.Event.ReviewSessionId;
                if (!string.IsNullOrWhiteSpace(deferredFrom)
                    && !state.SessionIsClosedBefore(deferredFrom, currentSessionCausality))
                {
                    continue;
                }
            }

            var priority = block.Kind == MarkdownBlockKind.TaskListItem
                ? FeedReviewPriority.IncompleteCheckbox
                : effective.Event?.Decision == ReviewDecision.Deferred
                    ? FeedReviewPriority.Deferred
                    : string.IsNullOrWhiteSpace(area)
                        ? FeedReviewPriority.WithoutArea
                        : FeedReviewPriority.Other;
            candidates.Add(new FeedReviewCandidate(locator, block, day, priority, deferredFrom));
        }

        return candidates
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Day)
            .ThenBy(static candidate => candidate.Block.Start)
            .ToArray();
    }

    public static IReadOnlyList<BlockLocator> CoveredLocators(
        string relativePath,
        MarkdownDocument document,
        MarkdownBlockSelection selection)
    {
        var selected = selection.Resolve(document).ToHashSet();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<BlockLocator>();
        var contentBlocks = document.Blocks.Where(static block => block.IsContent).ToArray();
        for (var blockIndex = 0; blockIndex < contentBlocks.Length; blockIndex++)
        {
            var block = contentBlocks[blockIndex];
            var area = block.AreaId ?? block.AreaName;
            var key = string.Join('|', area, block.Kind, block.ContentHash);
            occurrences.TryGetValue(key, out var occurrence);
            occurrences[key] = occurrence + 1;
            if (selected.Contains(block))
            {
                result.Add(CreateLocator(relativePath, contentBlocks, blockIndex, occurrence));
            }
        }

        return result;
    }

    private static BlockLocator CreateLocator(
        string relativePath,
        IReadOnlyList<MarkdownBlock> contentBlocks,
        int blockIndex,
        int occurrence)
    {
        var block = contentBlocks[blockIndex];
        return new BlockLocator(
            relativePath,
            block.AreaId ?? block.AreaName,
            block.Kind,
            block.ContentHash,
            occurrence,
            blockIndex > 0 ? contentBlocks[blockIndex - 1].ContentHash : null,
            blockIndex + 1 < contentBlocks.Count ? contentBlocks[blockIndex + 1].ContentHash : null);
    }

    private static DateOnly ParseDailyDate(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return DateOnly.TryParseExact(fileName, "yyyy-MM-dd", out var result) ? result : DateOnly.MinValue;
    }
}
