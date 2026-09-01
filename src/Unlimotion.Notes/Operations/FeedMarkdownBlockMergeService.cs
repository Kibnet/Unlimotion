using System.Text.RegularExpressions;
using Unlimotion.Notes.Markdown;

namespace Unlimotion.Notes.Operations;

public sealed record FeedMarkdownBlockMergePlan(
    int TargetBlockIndex,
    int CaretIndex,
    int SelectionStart,
    int SelectionLength,
    string OriginalRaw,
    string ReplacementRaw,
    string UpdatedDocumentRaw);

public sealed partial class FeedMarkdownBlockMergeService(IMarkdownDocumentParser parser)
{
    public FeedMarkdownBlockMergePlan? CreatePlan(
        string documentRaw,
        int currentBlockIndex,
        string editedCurrentText)
    {
        ArgumentNullException.ThrowIfNull(documentRaw);
        ArgumentNullException.ThrowIfNull(editedCurrentText);

        var document = parser.Parse(documentRaw);
        var currentPosition = document.Blocks
            .Select((block, position) => (block, position))
            .FirstOrDefault(candidate => candidate.block.Index == currentBlockIndex)
            .position;
        if (currentPosition <= 0 || currentPosition >= document.Blocks.Count)
        {
            return null;
        }

        var current = document.Blocks[currentPosition];
        if (!IsMergeCompatible(current))
        {
            return null;
        }

        var previousPosition = currentPosition - 1;
        while (previousPosition >= 0 && document.Blocks[previousPosition].Kind == MarkdownBlockKind.Blank)
        {
            previousPosition--;
        }

        if (previousPosition < 0)
        {
            return null;
        }

        var previous = document.Blocks[previousPosition];
        if (!IsMergeCompatible(previous)
            || document.Blocks.Skip(previousPosition + 1).Take(currentPosition - previousPosition - 1)
                .Any(static block => block.Kind != MarkdownBlockKind.Blank))
        {
            return null;
        }

        var previousText = StripStructuralLineEnding(previous.Raw);
        var currentText = StripStructuralPrefix(
            NormalizeNewLines(editedCurrentText, document.NewLine),
            current.Kind);
        var replacement = previousText + currentText + GetStructuralLineEnding(current.Raw);
        var selectionStart = previous.Start;
        var selectionLength = current.Start + current.Length - previous.Start;
        var original = documentRaw.Substring(selectionStart, selectionLength);
        var updated = documentRaw[..selectionStart]
                      + replacement
                      + documentRaw[(selectionStart + selectionLength)..];

        return new FeedMarkdownBlockMergePlan(
            previous.Index,
            previousText.Length,
            selectionStart,
            selectionLength,
            original,
            replacement,
            updated);
    }

    private static bool IsMergeCompatible(MarkdownBlock block) => block.Kind is
        MarkdownBlockKind.Heading
        or MarkdownBlockKind.Paragraph
        or MarkdownBlockKind.ListItem
        or MarkdownBlockKind.TaskListItem
        or MarkdownBlockKind.BlockQuote;

    private static string StripStructuralPrefix(string value, MarkdownBlockKind kind)
    {
        var regex = kind switch
        {
            MarkdownBlockKind.Heading => HeadingPrefixRegex(),
            MarkdownBlockKind.TaskListItem => TaskPrefixRegex(),
            MarkdownBlockKind.ListItem => ListPrefixRegex(),
            MarkdownBlockKind.BlockQuote => QuotePrefixRegex(),
            _ => null
        };
        return regex?.Replace(value, string.Empty, 1) ?? value;
    }

    private static string StripStructuralLineEnding(string raw)
    {
        if (raw.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return raw[..^2];
        }

        return raw.EndsWith('\r') || raw.EndsWith('\n') ? raw[..^1] : raw;
    }

    private static string GetStructuralLineEnding(string raw)
    {
        if (raw.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (raw.EndsWith('\r'))
        {
            return "\r";
        }

        return raw.EndsWith('\n') ? "\n" : string.Empty;
    }

    private static string NormalizeNewLines(string value, string targetNewLine) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Replace("\n", targetNewLine, StringComparison.Ordinal);

    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}[ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"^[ \t]*[-+*][ \t]+\[[ xX]\][ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex TaskPrefixRegex();

    [GeneratedRegex(@"^[ \t]*(?:[-+*]|\d+[.)])[ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex ListPrefixRegex();

    [GeneratedRegex(@"^[ \t]*(?:>[ \t]*)+", RegexOptions.CultureInvariant)]
    private static partial Regex QuotePrefixRegex();
}
