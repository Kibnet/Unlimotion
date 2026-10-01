using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;

namespace Unlimotion.Notes.Search;

public enum FeedSearchDocumentType
{
    Daily,
    Note,
    Task
}

public sealed record FeedSearchEntry(
    string Key,
    string RelativePath,
    FeedSearchDocumentType Type,
    string Text,
    string? AreaIdentity,
    DateOnly? Date,
    int BlockIndex,
    string Context,
    string ContentHash,
    DateTimeOffset? UpdatedAt = null)
{
    public IReadOnlyList<string> AreaIdentities { get; init; } = AreaIdentity is null ? [] : [AreaIdentity];

    public bool AreaIdentitiesAreExplicit { get; init; }
}

public sealed record FeedSearchTaskDocument(
    string Id,
    string Title,
    string? Description,
    IReadOnlyCollection<string> AreaIds,
    DateTimeOffset? UpdatedAt);

public sealed record FeedSearchQuery(
    string Text,
    string? AreaIdentity = null,
    DateOnly? From = null,
    DateOnly? To = null,
    FeedSearchDocumentType? Type = null);

public sealed class FeedSearchIndex(IMarkdownDocumentParser parser, DailyNoteNaming? naming = null)
{
    private static readonly Regex FrontMatterAreasKeyRegex = new(
        @"^\s*unlimotion-areas\s*:\s*(?<inline>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FrontMatterAreaItemRegex = new(
        @"^\s*-\s*(?<id>[A-Za-z0-9_-]+)\s*(?:#.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly object sync = new();
    private readonly DailyNoteNaming dailyNaming = naming ?? DailyNoteNaming.Default;
    private readonly Dictionary<string, FeedSearchEntry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> keysByPath = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get
        {
            lock (sync)
            {
                return entries.Count;
            }
        }
    }

    public void IndexMarkdown(string relativePath, string raw, DateTimeOffset? modifiedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(raw);
        lock (sync)
        {
            if (IsInternal(relativePath))
            {
                return;
            }

            Remove(relativePath);
            var normalizedPath = NormalizePath(relativePath);
            var type = IsDaily(normalizedPath, out var day)
                ? FeedSearchDocumentType.Daily
                : FeedSearchDocumentType.Note;
            var documentDate = type == FeedSearchDocumentType.Daily
                ? day
                : IsInDailyDirectory(normalizedPath)
                    ? null
                    : modifiedAt is null
                        ? null
                        : DateOnly.FromDateTime(modifiedAt.Value.LocalDateTime);
            var documentAreas = type == FeedSearchDocumentType.Note
                ? ExtractFrontMatterAreaIdentities(raw)
                : [];
            var document = parser.Parse(raw);
            var content = document.Blocks.Where(static block => block.IsContent).ToArray();
            foreach (var block in content)
            {
                var text = block.Raw.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var blockArea = block.AreaId ?? block.AreaName;
                var areaIdentities = documentAreas.Count > 0
                    ? documentAreas
                    : string.IsNullOrWhiteSpace(blockArea)
                        ? []
                        : [blockArea];
                var key = $"{normalizedPath}:{block.Index}:{block.ContentHash}";
                var context = BuildContext(content, block.Index);
                Add(new FeedSearchEntry(
                    key,
                    normalizedPath,
                    type,
                    text,
                    areaIdentities.FirstOrDefault(),
                    documentDate,
                    block.Index,
                    context,
                    block.ContentHash,
                    type == FeedSearchDocumentType.Note ? modifiedAt : null)
                {
                    AreaIdentities = areaIdentities,
                    AreaIdentitiesAreExplicit = documentAreas.Count > 0
                        || !string.IsNullOrWhiteSpace(block.AreaId)
                });
            }
        }
    }

    public void IndexTask(
        string id,
        string title,
        string? description,
        IReadOnlyCollection<string> areaIds,
        DateTimeOffset? updatedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(areaIds);
        lock (sync)
        {
            var relativePath = $"task:{id}";
            Remove(relativePath);
            var textParts = new List<string> { title };
            if (!string.IsNullOrWhiteSpace(description))
            {
                textParts.Add(description);
            }

            var text = string.Join('\n', textParts);
            var normalizedAreas = areaIds
                .Where(static areaId => !string.IsNullOrWhiteSpace(areaId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Add(new FeedSearchEntry(
                relativePath,
                relativePath,
                FeedSearchDocumentType.Task,
                text,
                normalizedAreas.FirstOrDefault(),
                updatedAt is null ? null : DateOnly.FromDateTime(updatedAt.Value.LocalDateTime),
                0,
                text,
                MarkdownContentHasher.Hash(text),
                updatedAt)
            {
                AreaIdentities = normalizedAreas,
                AreaIdentitiesAreExplicit = true
            });
        }
    }

    public void ReplaceTasks(IEnumerable<FeedSearchTaskDocument> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        lock (sync)
        {
            foreach (var taskPath in keysByPath.Keys
                         .Where(static path => path.StartsWith("task:", StringComparison.Ordinal))
                         .ToArray())
            {
                Remove(taskPath);
            }

            foreach (var task in tasks)
            {
                IndexTask(task.Id, task.Title, task.Description, task.AreaIds, task.UpdatedAt);
            }
        }
    }

    public void Rename(
        string oldRelativePath,
        string newRelativePath,
        string raw,
        DateTimeOffset? modifiedAt = null)
    {
        lock (sync)
        {
            Remove(oldRelativePath);
            IndexMarkdown(newRelativePath, raw, modifiedAt);
        }
    }

    public void Remove(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        lock (sync)
        {
            if (!keysByPath.Remove(NormalizePath(relativePath), out var keys))
            {
                return;
            }

            foreach (var key in keys)
            {
                entries.Remove(key);
            }
        }
    }

    public IReadOnlyList<FeedSearchEntry> Search(FeedSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (sync)
        {
            var tokens = Normalize(query.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<FeedSearchEntry> result = entries.Values;
            if (tokens.Length > 0)
            {
                result = result.Where(entry =>
                {
                    // Context is display-only. Including it in the searchable text makes
                    // neighboring blocks match solely because the actual hit is shown in
                    // their preview context, producing duplicate search results.
                    var haystack = Normalize(entry.Text + " " + entry.RelativePath);
                    return tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
                });
            }

            if (query.AreaIdentity is not null)
            {
                result = query.AreaIdentity.Length == 0
                    ? result.Where(static entry => entry.AreaIdentities.Count == 0)
                    : result.Where(entry => entry.AreaIdentities.Contains(query.AreaIdentity, StringComparer.Ordinal));
            }

            if (query.From is not null)
            {
                result = result.Where(entry => entry.Date is not null && entry.Date >= query.From);
            }

            if (query.To is not null)
            {
                result = result.Where(entry => entry.Date is not null && entry.Date <= query.To);
            }

            if (query.Type is not null)
            {
                result = result.Where(entry => entry.Type == query.Type);
            }

            return result.OrderByDescending(GetSortTimestamp)
                .ThenBy(static entry => entry.RelativePath, StringComparer.Ordinal)
                .ThenBy(static entry => entry.BlockIndex)
                .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public FeedSearchEntry? ResolveCurrentAnchor(FeedSearchEntry staleEntry, FeedSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(staleEntry);
        var matches = Search(query)
            .Where(entry => entry.Type == staleEntry.Type
                && string.Equals(entry.RelativePath, staleEntry.RelativePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.ContentHash, staleEntry.ContentHash, StringComparison.Ordinal))
            .ToArray();
        var sameIndex = matches.Where(entry => entry.BlockIndex == staleEntry.BlockIndex).ToArray();
        if (sameIndex.Length == 1)
        {
            return sameIndex[0];
        }

        return matches.Length == 1 ? matches[0] : null;
    }

    private void Add(FeedSearchEntry entry)
    {
        entries[entry.Key] = entry;
        if (!keysByPath.TryGetValue(entry.RelativePath, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            keysByPath.Add(entry.RelativePath, keys);
        }

        keys.Add(entry.Key);
    }

    private static string BuildContext(IReadOnlyList<MarkdownBlock> blocks, int blockIndex)
    {
        var current = blocks.First(block => block.Index == blockIndex);
        var position = blocks.IndexOf(current);
        return string.Join(" ", blocks.Skip(Math.Max(0, position - 1)).Take(3).Select(static block => block.Raw.Trim()));
    }

    private bool IsDaily(string relativePath, out DateOnly? date)
    {
        if (!dailyNaming.TryParseRelativePath(relativePath, out var parsed))
        {
            date = null;
            return false;
        }

        date = parsed;
        return true;
    }

    private static bool IsInDailyDirectory(string relativePath) =>
        relativePath.StartsWith(DailyNoteNaming.DailyDirectoryName + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsInternal(string relativePath) => relativePath.Replace('\\', '/').StartsWith(".unlimotion/", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset GetSortTimestamp(FeedSearchEntry entry)
    {
        if (entry.Type == FeedSearchDocumentType.Daily && entry.Date is { } dailyDate)
        {
            return new DateTimeOffset(dailyDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        if (entry.UpdatedAt is { } updatedAt)
        {
            return updatedAt;
        }

        return entry.Date is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    }

    private static IReadOnlyList<string> ExtractFrontMatterAreaIdentities(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length < 3 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            return [];
        }

        var areas = new List<string>();
        var readingAreas = false;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
            {
                break;
            }

            var keyMatch = FrontMatterAreasKeyRegex.Match(line);
            if (keyMatch.Success)
            {
                readingAreas = true;
                var inline = keyMatch.Groups["inline"].Value.Trim();
                if (inline.StartsWith('[') && inline.EndsWith(']'))
                {
                    foreach (var candidate in inline[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        AddArea(candidate.Trim('"', '\''));
                    }
                }

                continue;
            }

            if (!readingAreas)
            {
                continue;
            }

            var itemMatch = FrontMatterAreaItemRegex.Match(line);
            if (itemMatch.Success)
            {
                AddArea(itemMatch.Groups["id"].Value);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line) && !char.IsWhiteSpace(line[0]))
            {
                readingAreas = false;
            }
        }

        return areas;

        void AddArea(string candidate)
        {
            if (Regex.IsMatch(candidate, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)
                && !areas.Contains(candidate, StringComparer.Ordinal))
            {
                areas.Add(candidate);
            }
        }
    }

    private static string NormalizePath(string relativePath) => relativePath.Replace('\\', '/');

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
