using System.Security.Cryptography;
using System.Text;

namespace Unlimotion.Notes.Markdown;

public enum MarkdownBlockKind
{
    FrontMatter,
    Blank,
    Heading,
    AreaHeading,
    Paragraph,
    ListItem,
    TaskListItem,
    BlockQuote,
    FencedCode,
    HorizontalRule,
    Raw
}

public sealed record MarkdownBlock(
    int Index,
    MarkdownBlockKind Kind,
    string Raw,
    int Start,
    int Length,
    int LineNumber,
    string? AreaId = null,
    string? AreaName = null,
    int ListDepth = 0,
    bool? IsTaskCompleted = null,
    int HeadingLevel = 0)
{
    public bool IsContent => Kind is MarkdownBlockKind.Paragraph
        or MarkdownBlockKind.ListItem
        or MarkdownBlockKind.TaskListItem
        or MarkdownBlockKind.BlockQuote
        or MarkdownBlockKind.FencedCode
        or MarkdownBlockKind.HorizontalRule
        or MarkdownBlockKind.Raw
        || Kind == MarkdownBlockKind.Heading && HeadingLevel >= 3;

    public string ContentHash => MarkdownContentHasher.Hash(Raw);
}

public sealed class MarkdownDocument
{
    public MarkdownDocument(string raw, IReadOnlyList<MarkdownBlock> blocks, string newLine)
    {
        Raw = raw;
        Blocks = blocks;
        NewLine = newLine;
    }

    public string Raw { get; }

    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    public string NewLine { get; }

    public string ReplaceBlocks(int startIndex, int count, string replacement)
    {
        if (startIndex < 0 || count <= 0 || startIndex + count > Blocks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        var first = Blocks[startIndex];
        var last = Blocks[startIndex + count - 1];
        return Raw[..first.Start] + replacement + Raw[(last.Start + last.Length)..];
    }
}

public static class MarkdownContentHasher
{
    public static string Hash(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim()
            .Normalize(NormalizationForm.FormC);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
