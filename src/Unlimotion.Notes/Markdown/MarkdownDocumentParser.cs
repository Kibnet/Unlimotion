using System.Text.RegularExpressions;

namespace Unlimotion.Notes.Markdown;

public interface IMarkdownDocumentParser
{
    MarkdownDocument Parse(string raw);
}

public sealed partial class MarkdownDocumentParser : IMarkdownDocumentParser
{
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-+*])\s+\[(?<state>[ xX])\](?:\s+|$)")]
    private static partial Regex TaskItemRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-+*]|\d+[.)])(?:\s+|$)")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^##\s+(?<name>.*?)(?:\s*<!--\s*unlimotion-area:(?<id>[A-Za-z0-9_-]+)\s*-->)?\s*$")]
    private static partial Regex AreaRegex();

    [GeneratedRegex(@"^(?<marker>#{1,6})(?:[ \t]+|$)")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s{0,3}(?<fence>`{3,}|~{3,})")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})\s*$")]
    private static partial Regex HorizontalRuleRegex();

    public MarkdownDocument Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var lines = SplitLines(raw);
        var lineOffsets = new int[lines.Count + 1];
        for (var index = 0; index < lines.Count; index++)
        {
            lineOffsets[index + 1] = lineOffsets[index] + lines[index].Length;
        }
        var blocks = new List<MarkdownBlock>();
        var newLine = DetectNewLine(raw);
        var cursor = 0;
        var lineNumber = 1;
        string? currentAreaId = null;
        string? currentAreaName = null;

        if (lines.Count > 0 && TrimLineEnding(lines[0]) == "---")
        {
            var end = FindFrontMatterEnd(lines);
            if (end >= 0)
            {
                AddBlock(MarkdownBlockKind.FrontMatter, 0, end + 1);
            }
        }

        while (cursor < lines.Count)
        {
            var line = TrimLineEnding(lines[cursor]);
            if (line.Length == 0)
            {
                var end = cursor + 1;
                while (end < lines.Count && TrimLineEnding(lines[end]).Length == 0)
                {
                    end++;
                }

                AddBlock(MarkdownBlockKind.Blank, cursor, end);
                continue;
            }

            var areaMatch = AreaRegex().Match(line);
            if (areaMatch.Success)
            {
                currentAreaId = NullIfEmpty(areaMatch.Groups["id"].Value);
                currentAreaName = areaMatch.Groups["name"].Value.TrimEnd();
                AddBlock(MarkdownBlockKind.AreaHeading, cursor, cursor + 1, areaId: currentAreaId, areaName: currentAreaName);
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                AddBlock(
                    MarkdownBlockKind.Heading,
                    cursor,
                    cursor + 1,
                    headingLevel: heading.Groups["marker"].Length);
                continue;
            }

            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
                var marker = fence.Groups["fence"].Value;
                var end = cursor + 1;
                while (end < lines.Count)
                {
                    var candidate = TrimLineEnding(lines[end]).TrimStart();
                    end++;
                    if (candidate.StartsWith(marker, StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                AddBlock(MarkdownBlockKind.FencedCode, cursor, end);
                continue;
            }

            var task = TaskItemRegex().Match(line);
            if (task.Success)
            {
                var depth = ComputeListDepth(task.Groups["indent"].Value);
                var completed = !string.Equals(task.Groups["state"].Value, " ", StringComparison.Ordinal);
                AddBlock(MarkdownBlockKind.TaskListItem, cursor, FindItemEnd(lines, cursor), depth, completed);
                continue;
            }

            var item = ListItemRegex().Match(line);
            if (item.Success)
            {
                AddBlock(MarkdownBlockKind.ListItem, cursor, FindItemEnd(lines, cursor), ComputeListDepth(item.Groups["indent"].Value));
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var end = cursor + 1;
                while (end < lines.Count && TrimLineEnding(lines[end]).TrimStart().StartsWith('>'))
                {
                    end++;
                }

                AddBlock(MarkdownBlockKind.BlockQuote, cursor, end);
                continue;
            }

            if (HorizontalRuleRegex().IsMatch(line))
            {
                AddBlock(MarkdownBlockKind.HorizontalRule, cursor, cursor + 1);
                continue;
            }

            var paragraphEnd = cursor + 1;
            while (paragraphEnd < lines.Count && !StartsBlock(lines, paragraphEnd))
            {
                paragraphEnd++;
            }

            AddBlock(MarkdownBlockKind.Paragraph, cursor, paragraphEnd);
        }

        return new MarkdownDocument(raw, blocks, newLine);

        void AddBlock(
            MarkdownBlockKind kind,
            int firstLine,
            int endLine,
            int listDepth = 0,
            bool? isCompleted = null,
            string? areaId = null,
            string? areaName = null,
            int headingLevel = 0)
        {
            var start = lineOffsets[firstLine];
            var blockRaw = string.Concat(lines.Skip(firstLine).Take(endLine - firstLine));
            blocks.Add(new MarkdownBlock(
                blocks.Count,
                kind,
                blockRaw,
                start,
                blockRaw.Length,
                lineNumber,
                areaId ?? currentAreaId,
                areaName ?? currentAreaName,
                listDepth,
                isCompleted,
                headingLevel));
            lineNumber += endLine - firstLine;
            cursor = endLine;
        }
    }

    private static int FindFrontMatterEnd(IReadOnlyList<string> lines)
    {
        for (var index = 1; index < lines.Count; index++)
        {
            if (TrimLineEnding(lines[index]) == "---")
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindItemEnd(IReadOnlyList<string> lines, int start)
    {
        var end = start + 1;
        while (end < lines.Count)
        {
            var line = TrimLineEnding(lines[end]);
            if (line.Length == 0 || IsStructuralStart(line) || TaskItemRegex().IsMatch(line) || ListItemRegex().IsMatch(line))
            {
                break;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            end++;
        }

        return end;
    }

    private static bool StartsBlock(IReadOnlyList<string> lines, int index)
    {
        var line = TrimLineEnding(lines[index]);
        return line.Length == 0
            || IsStructuralStart(line)
            || TaskItemRegex().IsMatch(line)
            || ListItemRegex().IsMatch(line)
            || line.TrimStart().StartsWith('>')
            || HorizontalRuleRegex().IsMatch(line);
    }

    private static bool IsStructuralStart(string line) => HeadingRegex().IsMatch(line) || FenceRegex().IsMatch(line);

    private static int ComputeListDepth(string indentation)
    {
        var columns = 0;
        foreach (var character in indentation)
        {
            columns += character == '\t' ? 4 : 1;
        }

        return columns / 2;
    }

    private static List<string> SplitLines(string raw)
    {
        var result = new List<string>();
        var start = 0;
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] != '\r' && raw[index] != '\n')
            {
                continue;
            }

            if (raw[index] == '\r' && index + 1 < raw.Length && raw[index + 1] == '\n')
            {
                index++;
            }

            result.Add(raw[start..(index + 1)]);
            start = index + 1;
        }

        if (start < raw.Length)
        {
            result.Add(raw[start..]);
        }

        return result;
    }

    private static string TrimLineEnding(string line) => line.TrimEnd('\r', '\n');

    private static string DetectNewLine(string raw)
    {
        var index = raw.IndexOf('\n');
        if (index < 0)
        {
            return Environment.NewLine;
        }

        return index > 0 && raw[index - 1] == '\r' ? "\r\n" : "\n";
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
