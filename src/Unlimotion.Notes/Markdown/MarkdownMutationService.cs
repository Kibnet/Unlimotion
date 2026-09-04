using System.Text;
using System.Text.RegularExpressions;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Markdown;

public sealed record AreaReference(string? Id, string Name)
{
    public bool MatchUnmarkedByName { get; init; }
}

public sealed record MarkdownBlockSelection(int StartBlockIndex, int BlockCount)
{
    public IReadOnlyList<MarkdownBlock> Resolve(MarkdownDocument document)
    {
        if (StartBlockIndex < 0 || BlockCount <= 0 || StartBlockIndex + BlockCount > document.Blocks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(StartBlockIndex));
        }

        var selected = document.Blocks.Skip(StartBlockIndex).Take(BlockCount).ToArray();
        if (selected.Any(static block => block.Kind == MarkdownBlockKind.AreaHeading))
        {
            throw new InvalidOperationException("An area heading cannot be part of a content selection.");
        }

        return selected;
    }
}

public sealed class MarkdownMutationService(IMarkdownDocumentParser parser)
{
    public string AppendQuickCapture(string raw, string capture, AreaReference? area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capture);
        var document = parser.Parse(raw);
        var newLine = document.NewLine;
        var normalizedCapture = NormalizeForDocument(capture, newLine).TrimEnd('\r', '\n');

        if (area is null)
        {
            var firstArea = document.Blocks.FirstOrDefault(static block => block.Kind == MarkdownBlockKind.AreaHeading);
            var insertion = firstArea?.Start ?? raw.Length;
            return InsertSeparated(raw, insertion, normalizedCapture, newLine);
        }

        var stableAreaId = string.IsNullOrWhiteSpace(area.Id) ? null : area.Id;
        if (stableAreaId is not null)
        {
            FeedLinkSerializer.ValidateStableId(stableAreaId, nameof(area));
        }

        var safeAreaName = Regex.Replace(area.Name, @"[\u0000-\u001F\u007F]+", " ").Trim();
        if (safeAreaName.Length == 0)
        {
            throw new ArgumentException("An area name cannot be empty.", nameof(area));
        }

        var heading = document.Blocks.FirstOrDefault(block =>
            block.Kind == MarkdownBlockKind.AreaHeading
            && ((stableAreaId is not null
                    && !string.IsNullOrEmpty(block.AreaId)
                    && string.Equals(block.AreaId, stableAreaId, StringComparison.Ordinal))
                || (area.MatchUnmarkedByName
                    && string.IsNullOrEmpty(block.AreaId)
                    && string.Equals(block.AreaName, area.Name, StringComparison.OrdinalIgnoreCase))));
        if (heading is null)
        {
            var headingText = stableAreaId is null
                ? $"## {safeAreaName}"
                : $"## {safeAreaName} <!-- unlimotion-area:{stableAreaId} -->";
            var section = string.Concat(headingText, newLine, newLine, normalizedCapture);
            return InsertSeparated(raw, raw.Length, section, newLine);
        }

        var nextHeading = document.Blocks
            .Skip(heading.Index + 1)
            .FirstOrDefault(static block => block.Kind == MarkdownBlockKind.AreaHeading);
        return InsertSeparated(raw, nextHeading?.Start ?? raw.Length, normalizedCapture, newLine);
    }

    public string ReplaceSelection(string raw, MarkdownBlockSelection selection, string replacement)
    {
        var document = parser.Parse(raw);
        selection.Resolve(document);
        var normalized = NormalizeForDocument(replacement, document.NewLine).TrimEnd('\r', '\n') + document.NewLine;
        return document.ReplaceBlocks(selection.StartBlockIndex, selection.BlockCount, normalized);
    }

    public string MoveSelectionToArea(string raw, MarkdownBlockSelection selection, AreaReference? destination)
    {
        var document = parser.Parse(raw);
        var selected = selection.Resolve(document);
        var selectedRaw = string.Concat(selected.Select(static block => block.Raw)).TrimEnd('\r', '\n');
        var withoutSelection = document.ReplaceBlocks(selection.StartBlockIndex, selection.BlockCount, string.Empty);
        return AppendQuickCapture(withoutSelection, selectedRaw, destination);
    }

    private static string InsertSeparated(string raw, int index, string insertion, string newLine)
    {
        var before = raw[..index];
        var after = raw[index..];
        var prefix = before.Length == 0
            ? string.Empty
            : before.EndsWith(newLine + newLine, StringComparison.Ordinal)
                ? string.Empty
                : before.EndsWith(newLine, StringComparison.Ordinal) ? newLine : newLine + newLine;
        var suffix = after.Length == 0
            ? newLine
            : after.StartsWith(newLine, StringComparison.Ordinal) ? newLine : newLine + newLine;
        return before + prefix + insertion + suffix + after.TrimStart('\r', '\n');
    }

    private static string NormalizeForDocument(string text, string newLine) => text
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Replace("\n", newLine, StringComparison.Ordinal);
}

public static partial class FeedLinkSerializer
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex StableIdRegex();

    public static string Task(string taskId, string fallbackTitle)
    {
        ValidateStableId(taskId, nameof(taskId));
        var label = CollapseToSingleLine(fallbackTitle);
        label = label.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
        return $"[{label}](unlimotion://task/{taskId})";
    }

    public static string Note(string safeRelativePath, string title, string noteId)
    {
        ValidateStableId(noteId, nameof(noteId));
        var target = RemoveMarkdownExtension(safeRelativePath).Replace('\\', '/');
        ValidateWikiTarget(target);
        var aliasAllowed = title.IndexOfAny(['|', '#', '[', ']', '\\', '\r', '\n']) < 0;
        var link = aliasAllowed ? $"[[{target}|{title}]]" : $"[[{target}]]";
        return $"{link} <!-- unlimotion-note:{noteId} -->";
    }

    public static string MovedBlock(
        string destinationRelativePath,
        string anchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativePath);
        ValidateStableId(anchor, nameof(anchor));
        var normalizedPath = destinationRelativePath.Replace('\\', '/');
        var target = RemoveMarkdownExtension(normalizedPath);
        ValidateWikiTarget(target);
        var destinationLabel = Path.GetFileNameWithoutExtension(normalizedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLabel);
        return $"[[{target}#^{anchor}|Перенесено на {destinationLabel}]]";
    }

    public static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*#[]".ToCharArray()).ToHashSet();
        var builder = new StringBuilder();
        foreach (var character in CollapseToSingleLine(title).Normalize(NormalizationForm.FormC))
        {
            builder.Append(char.IsControl(character) || invalid.Contains(character) ? '-' : character);
        }

        var result = Regex.Replace(builder.ToString(), "-+", "-").Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result))
        {
            result = "Заметка";
        }

        var stem = result.Split('.')[0];
        if (WindowsReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        return result;
    }

    public static string ChooseAvailableNotePath(string safeFolder, string title, Func<string, bool> exists)
    {
        var fileName = MakeSafeFileName(title);
        var candidate = CombineRelative(safeFolder, fileName + ".md");
        for (var suffix = 2; exists(candidate); suffix++)
        {
            candidate = CombineRelative(safeFolder, $"{fileName} {suffix}.md");
        }

        return candidate;
    }

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static string CollapseToSingleLine(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Regex.Replace(value, @"[\u0000-\u001F\u007F]+", " ").Trim();
    }

    internal static void ValidateStableId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !StableIdRegex().IsMatch(value))
        {
            throw new ArgumentException("Only canonical stable IDs are accepted.", parameterName);
        }
    }

    private static void ValidateWikiTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || target.Contains("..", StringComparison.Ordinal)
            || target.IndexOfAny(['|', '#', '[', ']', '\r', '\n']) >= 0
            || Path.IsPathRooted(target))
        {
            throw new ArgumentException("Unsafe wiki-link target.", nameof(target));
        }
    }

    private static string RemoveMarkdownExtension(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;

    private static string CombineRelative(string folder, string fileName)
    {
        var normalizedFolder = folder.Replace('\\', '/').Trim('/');
        if (normalizedFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or "..")
            || Path.IsPathRooted(folder))
        {
            throw new ArgumentException("The note folder must be a safe relative vault path.", nameof(folder));
        }

        return normalizedFolder.Length == 0 ? fileName : normalizedFolder + "/" + fileName;
    }
}
