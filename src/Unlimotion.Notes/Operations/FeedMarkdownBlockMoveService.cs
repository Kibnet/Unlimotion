using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record FeedMarkdownBlockMoveRequest(
    string SourcePath,
    string ExpectedSourceRevision,
    IReadOnlyList<BlockLocator> SelectedBlocks,
    BlockLocator? InsertBefore = null,
    AreaReference? DestinationArea = null);

public sealed record FeedMarkdownBlockMoveResult(
    string SourceRevision,
    string UpdatedText,
    bool HasUtf8Bom,
    IReadOnlyList<BlockLocator> InputLocators,
    IReadOnlyList<BlockLocator> OutputLocators,
    IReadOnlyList<int> OutputBlockIndices);

public sealed class FeedMarkdownBlockMoveService(
    INoteVault vault,
    IMarkdownDocumentParser parser)
{
    public async Task<FeedMarkdownBlockMoveResult> MoveAsync(
        FeedMarkdownBlockMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedSourceRevision);
        ArgumentNullException.ThrowIfNull(request.SelectedBlocks);
        if (request.SelectedBlocks.Count == 0)
        {
            throw new ArgumentException("At least one Markdown content block must be selected.", nameof(request));
        }

        if (request.SelectedBlocks.Any(locator => !SamePath(locator.RelativePath, request.SourcePath))
            || request.InsertBefore is not null && !SamePath(request.InsertBefore.RelativePath, request.SourcePath))
        {
            throw new InvalidOperationException("Markdown blocks can only be moved inside one document.");
        }

        var source = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
        if (!string.Equals(source.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(request.SourcePath, request.ExpectedSourceRevision, source.Revision);
        }

        var document = parser.Parse(source.Text);
        var located = BuildLocatedContent(request.SourcePath, document);
        var selected = request.SelectedBlocks
            .Select(locator => ResolveUnique(located, locator, "selected block"))
            .DistinctBy(static value => value.Block.Index)
            .OrderBy(static value => value.Block.Start)
            .ToArray();
        if (selected.Length != request.SelectedBlocks.Count)
        {
            throw new InvalidOperationException("The selected Markdown locators do not resolve to distinct blocks.");
        }

        foreach (var item in selected)
        {
            EnsureMovable(item.Block);
        }

        var ranges = BuildMoveRanges(document, selected);
        var expandedSelection = located
            .Where(candidate => ranges.Any(range => candidate.Block.Start >= range.Start
                && candidate.Block.Start < range.Start + range.Length))
            .OrderBy(static candidate => candidate.Block.Start)
            .ToArray();
        var insertion = request.InsertBefore is null
            ? ResolveAreaInsertion(document, request.DestinationArea)
            : ResolveUnique(located, request.InsertBefore, "drop target").Block.Start;
        if (ranges.Any(range => insertion >= range.Start && insertion < range.Start + range.Length))
        {
            throw new InvalidOperationException("A block selection cannot be dropped inside itself.");
        }

        var insertionAfterRemoval = insertion - ranges
            .Where(range => range.Start < insertion)
            .Sum(static range => range.Length);
        var movedRaw = JoinSelectedBlocks(
            ranges.Select(range => source.Text.Substring(range.Start, range.Length)),
            document.NewLine);
        var withoutSelection = source.Text;
        foreach (var range in ranges.OrderByDescending(static value => value.Start))
        {
            withoutSelection = withoutSelection.Remove(range.Start, range.Length);
        }

        if (insertionAfterRemoval < 0 || insertionAfterRemoval > withoutSelection.Length)
        {
            throw new InvalidOperationException("The Markdown insertion boundary is no longer valid.");
        }

        if (insertionAfterRemoval < withoutSelection.Length
            && !withoutSelection.AsSpan(insertionAfterRemoval).StartsWith(document.NewLine.AsSpan(), StringComparison.Ordinal)
            && !movedRaw.EndsWith(document.NewLine + document.NewLine, StringComparison.Ordinal))
        {
            movedRaw += document.NewLine;
        }

        var updated = withoutSelection.Insert(insertionAfterRemoval, movedRaw);
        if (string.Equals(updated, source.Text, StringComparison.Ordinal))
        {
            return new FeedMarkdownBlockMoveResult(
                source.Revision,
                source.Text,
                source.HasUtf8Bom,
                expandedSelection.Select(static value => value.Locator).ToArray(),
                expandedSelection.Select(static value => value.Locator).ToArray(),
                expandedSelection.Select(static value => value.Block.Index).ToArray());
        }

        var write = await vault.WriteAsync(
                request.SourcePath,
                updated,
                request.ExpectedSourceRevision,
                source.HasUtf8Bom,
                cancellationToken)
            .ConfigureAwait(false);
        var outputDocument = parser.Parse(updated);
        var outputLocated = BuildLocatedContent(request.SourcePath, outputDocument);
        var signatures = expandedSelection.Select(static value => (value.Block.Kind, value.Block.ContentHash)).ToArray();
        var output = FindMovedSequence(outputLocated, signatures, insertionAfterRemoval);
        return new FeedMarkdownBlockMoveResult(
            write.Revision,
            updated,
            source.HasUtf8Bom,
            expandedSelection.Select(static value => value.Locator).ToArray(),
            output.Select(static value => value.Locator).ToArray(),
            output.Select(static value => value.Block.Index).ToArray());
    }

    private static void EnsureMovable(MarkdownBlock block)
    {
        if (block.Kind is MarkdownBlockKind.FrontMatter or MarkdownBlockKind.Blank or MarkdownBlockKind.Raw
            || block.Raw.Contains("unlimotion://task/", StringComparison.Ordinal)
            || block.Raw.Contains("<!-- unlimotion-note:", StringComparison.Ordinal)
            || block.Raw.Contains("<!-- unlimotion-recovery:", StringComparison.Ordinal)
            || block.Raw.Contains("#^", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This Markdown block is structural or generated and cannot be moved.");
        }
    }

    private static IReadOnlyList<MoveRange> BuildMoveRanges(
        MarkdownDocument document,
        IReadOnlyList<LocatedContent> selected)
    {
        var ranges = selected.Select(item =>
        {
            if (item.Block.Kind != MarkdownBlockKind.AreaHeading)
            {
                return new MoveRange(item.Block.Start, item.Block.Length);
            }

            var nextAreaStart = document.Blocks
                .Skip(item.Block.Index + 1)
                .FirstOrDefault(static block => block.Kind == MarkdownBlockKind.AreaHeading)
                ?.Start ?? document.Raw.Length;
            return new MoveRange(item.Block.Start, nextAreaStart - item.Block.Start);
        }).OrderBy(static range => range.Start).ToArray();

        var merged = new List<MoveRange>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.Start > merged[^1].Start + merged[^1].Length)
            {
                merged.Add(range);
                continue;
            }

            var previous = merged[^1];
            var end = Math.Max(previous.Start + previous.Length, range.Start + range.Length);
            merged[^1] = new MoveRange(previous.Start, end - previous.Start);
        }

        return merged;
    }

    private static int ResolveAreaInsertion(MarkdownDocument document, AreaReference? area)
    {
        if (area is null)
        {
            return document.Blocks.FirstOrDefault(static block => block.Kind == MarkdownBlockKind.AreaHeading)?.Start
                ?? document.Raw.Length;
        }

        var heading = document.Blocks.FirstOrDefault(block =>
            block.Kind == MarkdownBlockKind.AreaHeading
            && (!string.IsNullOrWhiteSpace(area.Id)
                && string.Equals(block.AreaId, area.Id, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(area.Id)
                && string.Equals(block.AreaName, area.Name, StringComparison.OrdinalIgnoreCase)));
        if (heading is null)
        {
            throw new InvalidOperationException("The destination area no longer exists in this daily note.");
        }

        return document.Blocks
                   .Skip(heading.Index + 1)
                   .FirstOrDefault(static block => block.Kind == MarkdownBlockKind.AreaHeading)
                   ?.Start
               ?? document.Raw.Length;
    }

    private static LocatedContent ResolveUnique(
        IReadOnlyList<LocatedContent> current,
        BlockLocator requested,
        string role)
    {
        var exact = current.Where(candidate => candidate.Locator.SemanticKey == requested.SemanticKey).ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        var compatible = current.Where(candidate =>
            SamePath(candidate.Locator.RelativePath, requested.RelativePath)
            && string.Equals(candidate.Locator.AreaIdentity, requested.AreaIdentity, StringComparison.Ordinal)
            && candidate.Locator.BlockKind == requested.BlockKind
            && string.Equals(candidate.Locator.ContentHash, requested.ContentHash, StringComparison.Ordinal)
            && candidate.Locator.Occurrence == requested.Occurrence).ToArray();
        if (compatible.Length == 1)
        {
            return compatible[0];
        }

        throw new InvalidOperationException($"The {role} no longer resolves uniquely in the current Markdown revision.");
    }

    private static IReadOnlyList<LocatedContent> FindMovedSequence(
        IReadOnlyList<LocatedContent> current,
        IReadOnlyList<(MarkdownBlockKind Kind, string ContentHash)> signatures,
        int expectedStart)
    {
        var matches = new List<IReadOnlyList<LocatedContent>>();
        for (var start = 0; start + signatures.Count <= current.Count; start++)
        {
            var sequence = current.Skip(start).Take(signatures.Count).ToArray();
            if (sequence.Select(static value => (value.Block.Kind, value.Block.ContentHash)).SequenceEqual(signatures))
            {
                matches.Add(sequence);
            }
        }

        return matches
                   .OrderBy(sequence => Math.Abs(sequence[0].Block.Start - expectedStart))
                   .FirstOrDefault()
               ?? throw new InvalidDataException(
                   "The moved Markdown block group could not be resolved after the atomic write. "
                   + "Expected: " + string.Join(", ", signatures.Select(static value => $"{value.Kind}:{value.ContentHash[..8]}"))
                   + "; actual: " + string.Join(", ", current.Select(static value =>
                       $"{value.Block.Kind}:{value.Block.ContentHash[..8]}")));
    }

    private static string JoinSelectedBlocks(IEnumerable<string> blocks, string newLine)
    {
        var selected = blocks.ToArray();
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < selected.Length; index++)
        {
            builder.Append(selected[index]);
            if (index + 1 >= selected.Length)
            {
                continue;
            }

            if (!builder.ToString().EndsWith(newLine, StringComparison.Ordinal))
            {
                builder.Append(newLine);
            }

            if (!builder.ToString().EndsWith(newLine + newLine, StringComparison.Ordinal))
            {
                builder.Append(newLine);
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<LocatedContent> BuildLocatedContent(string relativePath, MarkdownDocument document)
    {
        var result = new List<LocatedContent>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var content = document.Blocks.Where(static block => block.Kind is not (
            MarkdownBlockKind.Blank or MarkdownBlockKind.FrontMatter)).ToArray();
        for (var index = 0; index < content.Length; index++)
        {
            var block = content[index];
            var area = block.AreaId ?? block.AreaName;
            var occurrenceKey = string.Join('|', area, block.Kind, block.ContentHash);
            occurrences.TryGetValue(occurrenceKey, out var occurrence);
            occurrences[occurrenceKey] = occurrence + 1;
            result.Add(new LocatedContent(
                block,
                new BlockLocator(
                    relativePath,
                    area,
                    block.Kind,
                    block.ContentHash,
                    occurrence,
                    index > 0 ? content[index - 1].ContentHash : null,
                    index + 1 < content.Length ? content[index + 1].ContentHash : null)));
        }

        return result;
    }

    private static bool SamePath(string left, string right) => string.Equals(
        left.Replace('\\', '/'),
        right.Replace('\\', '/'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record LocatedContent(MarkdownBlock Block, BlockLocator Locator);

    private sealed record MoveRange(int Start, int Length);
}
