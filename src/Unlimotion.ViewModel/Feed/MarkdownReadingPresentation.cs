using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unlimotion.Notes.Markdown;

namespace Unlimotion.ViewModel.Feed;

public static class MarkdownReadingPresentation
{
    public static bool TryGetServiceFrontMatter(string raw, out int areaCount)
    {
        ArgumentNullException.ThrowIfNull(raw);
        areaCount = 0;
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .TrimEnd('\n').Split('\n');
        if (lines.Length < 3 || lines[0] != "---" || lines[^1] != "---") return false;

        var seenId = false;
        var seenAreas = false;
        var inAreaSequence = false;
        var sequenceIndent = -1;
        var sequenceNeedsItem = false;
        var count = 0;
        // Recognize only a deliberately small, unambiguous YAML subset. Unsupported valid YAML
        // stays visible too: this classifier must never guess what user metadata means.
        for (var index = 1; index < lines.Length - 1; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (inAreaSequence && line.StartsWith(' '))
            {
                var indent = line.TakeWhile(character => character == ' ').Count();
                if (sequenceIndent >= 0 && indent != sequenceIndent) return false;
                sequenceIndent = indent;
                var item = line[indent..];
                if (!item.StartsWith("- ", StringComparison.Ordinal) || !IsSimpleScalar(item[2..])) return false;
                sequenceNeedsItem = false;
                count++;
                continue;
            }

            if (sequenceNeedsItem) return false;
            inAreaSequence = false;
            var colon = line.IndexOf(':');
            if (colon <= 0 || colon + 1 < line.Length && line[colon + 1] != ' ') return false;
            var key = line[..colon];
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "unlimotion-id" when !seenId:
                    if (!IsSimpleScalar(value)) return false;
                    seenId = true;
                    break;
                case "areas" when !seenAreas:
                case "unlimotion-areas" when !seenAreas:
                    seenAreas = true;
                    if (value.Length == 0)
                    {
                        inAreaSequence = true;
                        sequenceNeedsItem = true;
                    }
                    else if (value == "[]") { }
                    else if (value.StartsWith('[') && value.EndsWith(']'))
                    {
                        var items = value[1..^1].Split(',');
                        if (items.Any(item => !IsSimpleScalar(item))) return false;
                        count += items.Length;
                    }
                    else return false;
                    break;
                default:
                    return false;
            }
        }

        if (sequenceNeedsItem || !seenId && !seenAreas) return false;
        areaCount = count;
        return true;
    }

    private static bool IsSimpleScalar(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Any(char.IsControl)) return false;
        if (value[0] is '\'' or '"')
        {
            if (value.Length < 3 || value[^1] != value[0]) return false;
            var content = value[1..^1];
            return !string.IsNullOrWhiteSpace(content)
                && !content.Any(character => character == value[0] || character == '\\');
        }

        if ("-?:".Contains(value[0]) || value is "~" or "null" or "Null" or "NULL") return false;
        return !value.Any(character => ":[]{},#&*!|>'\"%@`\\".Contains(character));
    }

    public static bool HasMatchingThematicHeading(string? raw, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(displayName)) return false;
        var first = new MarkdownDocumentParser().Parse(raw).Blocks
            .FirstOrDefault(block => block.Kind is not (MarkdownBlockKind.Blank or MarkdownBlockKind.FrontMatter));
        if (first is not { Kind: MarkdownBlockKind.Heading, HeadingLevel: 1 }) return false;
        var heading = first.Raw.TrimEnd('\r', '\n')[1..].Trim();
        if (heading.Length == 0 || heading.Any(character => "#*_[]<>`\\!".Contains(character))) return false;
        var title = NormalizeTitle(displayName);
        return title.Length > 0 && string.Equals(NormalizeTitle(heading), title, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = Regex.Replace(title.Trim(), @"\s+", " ");
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? normalized[..^3].TrimEnd() : normalized;
    }
}
