using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;

namespace Unlimotion.Notes.Operations;

internal static class FeedOperationLocatorFactory
{
    public static IReadOnlyList<BlockLocator> ForSelection(
        string relativePath,
        MarkdownDocument document,
        MarkdownBlockSelection selection) =>
        FeedReviewQueue.CoveredLocators(relativePath, document, selection);

    public static IReadOnlyList<BlockLocator> ForMatchingBlock(
        string relativePath,
        MarkdownDocument document,
        Func<MarkdownBlock, bool> predicate)
    {
        var block = document.Blocks.FirstOrDefault(value => value.IsContent && predicate(value))
            ?? throw new InvalidDataException("The journaled operation output block could not be resolved.");
        return FeedReviewQueue.CoveredLocators(
            relativePath,
            document,
            new MarkdownBlockSelection(block.Index, 1));
    }

    public static IReadOnlyList<BlockLocator> ForRawRange(
        string relativePath,
        MarkdownDocument document,
        int start,
        int length)
    {
        if (start < 0 || length <= 0 || start + length > document.Raw.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        var end = start + length;
        return document.Blocks
            .Where(block => block.IsContent && block.Start >= start && block.Start < end)
            .SelectMany(block => FeedReviewQueue.CoveredLocators(
                relativePath,
                document,
                new MarkdownBlockSelection(block.Index, 1)))
            .DistinctBy(static locator => locator.SemanticKey)
            .ToArray();
    }

    public static bool SequenceEqual(
        IReadOnlyList<BlockLocator>? left,
        IReadOnlyList<BlockLocator>? right)
    {
        var leftKeys = (left ?? []).Select(static locator => locator.SemanticKey).ToArray();
        var rightKeys = (right ?? []).Select(static locator => locator.SemanticKey).ToArray();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    public static string NormalizeForDocument(string value, string newLine) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Replace("\n", newLine, StringComparison.Ordinal);
}
