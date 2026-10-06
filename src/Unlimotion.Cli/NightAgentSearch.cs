using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Cli;

/// <summary>Opt-in content search. Legacy search remains in Program, with its original contract.</summary>
public static class NightAgentSearch
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "search" ||
            !(args.Contains("--fields") || args.Contains("--snapshot"))) return null;
        var options = Parse(args);
        var source = options.Snapshot != null
            ? FromArtifact(await NightAgentSnapshotCodec.LoadArtifactAsync(options.Snapshot))
            : await FromLiveAsync(TaskDirectoryResolver.ResolveWithSource(options.Tasks).TasksPath);
        var output = Search(source, options.Query, options.Fields, options.Roots, options.Status, options.Limit, options.Cursor);
        if (options.Format == "json") Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
        else
        {
            foreach (var item in output.Items)
            {
                Console.WriteLine($"{EscapeText(item.Id)}\t{item.Status}\t{EscapeText(item.Title)}");
                foreach (var match in item.Matches)
                    Console.WriteLine($"  {match.Field}{(match.CriterionId == null ? "" : ":" + EscapeText(match.CriterionId))}: {EscapeText(match.Snippet)}");
            }
            foreach (var warning in output.Warnings) Console.Error.WriteLine($"{warning.Kind}: {EscapeText(warning.TaskId)}");
            Console.WriteLine($"Total: {output.TotalCount}; coverage: {output.Coverage} ({output.SearchedNodeCount}/{output.CatalogCount}); consistency: {output.Source.Consistency}; nextCursor: {output.NextCursor ?? "none"}");
        }
        return 0;
    }

    public static NightSearchSource FromArtifact(NightAgentSnapshotArtifact artifact) => new(
        new NightSearchSourceMetadata("snapshot", "immutableArtifact", artifact.ArtifactHash!, artifact.SnapshotId),
        artifact.Catalog.ToDictionary(entry => entry.Id,
            entry => (IReadOnlyList<string>)entry.Edges.Where(edge => edge.Kind == "contains" && edge.From == entry.Id)
                .Select(edge => edge.To).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal),
        artifact.Nodes.Select(node => new NightSearchRecord(node.Id, node.Etag, node.Task.Title ?? "", node.Task.Status,
            node.Details?.Importance ?? 0, node.Availability.CanStart, node.Availability.CanComplete,
            node.Details?.Description, node.Criteria,
            node.Sections.TryGetValue("details", out var details) && details == "captured",
            node.Sections.TryGetValue("criteria", out var criteria) && criteria == "captured")).ToArray(),
        artifact.Selection.Include.Contains("details"), artifact.Selection.Include.Contains("criteria"));

    private static async Task<NightSearchSource> FromLiveAsync(string path)
    {
        var storage = new FileTaskStorage(new FileTaskStorageOptions
        {
            Path = path, CreateDirectoryIfMissing = false, UseDirectoryLock = true, PreserveUnknownJson = true
        });
        var observation = await storage.ReadObservationAsync();
        var validation = TaskGraphValidationReport.From(observation.Graph);
        if (!validation.IsWriteSafe) throw new CliException(validation.BuildWriteSafetyMessage(), 1, "observationFailed");
        var analyzer = new TaskAvailabilityAnalyzer(observation.Graph.TasksById.Values, observation.EvaluatedAt);
        return new NightSearchSource(new NightSearchSourceMetadata("live", "liveUnpinned",
                NightAgentSnapshotCodec.CreateSourceKey(path), null),
            observation.Graph.TasksById.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ContainsTasks.ToArray(), StringComparer.Ordinal),
            observation.Graph.TasksById.Values.Select(task =>
            {
                var analysis = analyzer.Analyze(task);
                return new NightSearchRecord(task.Id, TaskEtag.Create(task), task.Title ?? "", task.Status,
                    task.Importance, analysis.CanStart, analysis.CanComplete, task.Description,
                    task.CompletionCriteria.Select(criterion => new TaskCriterionOutput
                    { Id = criterion.Id, Text = criterion.Text, IsSatisfied = criterion.IsSatisfied }).ToArray(), true, true);
            }).ToArray());
    }

    public static NightSearchOutput Search(NightSearchSource source, string query, IReadOnlyList<string> fields,
        IReadOnlyList<string>? roots = null, DomainTaskStatus? status = null, int limit = 20, string? cursor = null)
    {
        ValidateFields(fields);
        if (limit is < 1 or > 100) throw new CliException("--limit must be between 1 and 100.");
        var normalizedQuery = Normalize(query);
        var normalizedFields = fields.Order(StringComparer.Ordinal).ToArray();
        var normalizedRoots = (roots ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var scope = Scope(source, normalizedRoots);
        var candidates = source.Records.Where(record => (scope == null || scope.Contains(record.Id)) &&
            (status == null || record.Status == status)).ToArray();
        if (fields.Contains("description") && (!source.DetailsCaptured || candidates.Any(record => !record.DetailsCaptured)) ||
            fields.Contains("criteria") && (!source.CriteriaCaptured || candidates.Any(record => !record.CriteriaCaptured)))
            throw new CliException("Requested search section was not captured.", 1, "sectionNotCaptured");
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            source = source.Metadata.Fingerprint, query = normalizedQuery, fields = normalizedFields,
            roots = normalizedRoots, status, limit, normalization = "NFC-OrdinalIgnoreCase-v1"
        }, JsonOptions));
        NightSearchCursor? after = null;
        if (cursor != null)
        {
            try { after = JsonSerializer.Deserialize<NightSearchCursor>(Convert.FromBase64String(cursor), JsonOptions); }
            catch (Exception ex) when (ex is FormatException or JsonException)
            { throw new CliException("Search cursor is malformed.", 2, "invalidCursor"); }
            if (after is not { Version: 2, Title: not null, Id: not null } || after.Fingerprint != fingerprint)
                throw new CliException("Search cursor does not match the artifact or filters.", 2, "invalidCursor");
        }
        var warnings = new List<NightSearchWarning>();
        var items = new List<NightSearchItem>();
        foreach (var record in candidates)
        {
            var matches = new List<NightSearchMatch>();
            var truncated = false;
            var found = normalizedQuery.Length == 0;
            foreach (var field in normalizedFields)
            {
                if (field == "criteria")
                {
                    foreach (var criterion in (record.Criteria ?? []).OrderBy(item => item.Id, StringComparer.Ordinal))
                        Add(criterion.Text, criterion.Id);
                }
                else if (field == "description")
                {
                    if (AgentExecutionDescriptionRenderer.TryRemove(record.Description, out var userText, out _)) Add(userText, null);
                    else warnings.Add(new NightSearchWarning("descriptionUnavailable", record.Id));
                }
                else Add(field == "id" ? record.Id : record.Title, null);

                void Add(string? text, string? criterionId)
                {
                    if (normalizedQuery.Length == 0) return;
                    var value = Normalize(text ?? "");
                    var offsets = new List<int>();
                    var from = 0;
                    while (from <= value.Length - normalizedQuery.Length)
                    {
                        var offset = value.IndexOf(normalizedQuery, from, StringComparison.OrdinalIgnoreCase);
                        if (offset < 0) break;
                        found = true;
                        offsets.Add(offset);
                        if (offsets.Count == 4) break;
                        from = offset + normalizedQuery.Length;
                    }
                    if (offsets.Count == 0) return;
                    var more = offsets.Count > 3;
                    truncated |= more;
                    foreach (var offset in offsets.Take(3))
                    {
                        var snippet = Snippet(value, offset, normalizedQuery.Length, out var range, out var clipped);
                        truncated |= clipped;
                        matches.Add(new NightSearchMatch(field, criterionId, snippet, "NFC", [range], more));
                    }
                }
            }
            if (found) items.Add(new NightSearchItem(record.Id, record.Title, record.Status, record.Importance,
                record.CanStart, record.CanComplete, record.Etag, matches, truncated));
        }
        var ordered = items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (after != null && source.Metadata.Kind == "snapshot" &&
            !ordered.Any(item => item.Id == after.Id && item.Title == after.Title))
            throw new CliException("Cursor's last record does not belong to this result.", 2, "invalidCursor");
        var remaining = after == null ? ordered : ordered.Where(item => Compare(item.Title, item.Id, after.Title, after.Id) > 0).ToArray();
        var page = remaining.Take(limit).ToArray();
        var next = remaining.Length > page.Length ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new NightSearchCursor(2, fingerprint, page[^1].Title, page[^1].Id), JsonOptions)) : null;
        return new NightSearchOutput(2, page, items.Count, next, source.Metadata, "payload", candidates.Length,
            source.Children.Count, warnings.Count == 0, warnings);
    }

    private static HashSet<string>? Scope(NightSearchSource source, string[] roots)
    {
        if (roots.Length == 0) return null;
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        while (queue.TryDequeue(out var id))
        {
            if (!result.Add(id)) continue;
            if (!source.Children.TryGetValue(id, out var children))
                throw new CliException($"Root or descendant '{id}' was not found in catalog.", 1, "notFound");
            foreach (var child in children) queue.Enqueue(child);
        }
        var captured = source.Records.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Any(id => !captured.Contains(id)))
            throw new CliException("The complete requested root scope was not captured in payload.", 1, "scopeNotCaptured");
        return result;
    }

    private static string Snippet(string value, int offset, int matchLength, out NightSearchRange range, out bool clipped)
    {
        var boundaries = new List<int> { 0 };
        var position = 0;
        foreach (var rune in value.EnumerateRunes()) { position += rune.Utf16SequenceLength; boundaries.Add(position); }
        var startIndex = boundaries.BinarySearch(offset);
        if (startIndex < 0) startIndex = ~startIndex - 1;
        var endIndex = boundaries.BinarySearch(offset + matchLength);
        if (endIndex < 0) endIndex = ~endIndex;
        var first = Math.Max(0, startIndex - Math.Min(40, Math.Max(0, 160 - (endIndex - startIndex))));
        var last = Math.Min(boundaries.Count - 1, first + 160);
        var start = boundaries[first];
        var end = boundaries[last];
        range = new NightSearchRange(Math.Max(0, offset - start), Math.Min(matchLength, end - offset));
        clipped = start > 0 || end < value.Length;
        return value[start..end];
    }

    public static string EscapeText(string value)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
            if (Rune.IsControl(rune) || rune.Value is 0x2028 or 0x2029 or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069)
                result.Append("\\u").Append(rune.Value.ToString("x4"));
            else result.Append(rune);
        return result.ToString();
    }

    private static string Normalize(string value)
    {
        try { return value.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { throw new CliException("Search text contains invalid Unicode."); }
    }
    private static string Hash(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static int Compare(string title, string id, string otherTitle, string otherId)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(title, otherTitle);
        if (result == 0) result = StringComparer.Ordinal.Compare(title, otherTitle);
        return result != 0 ? result : StringComparer.Ordinal.Compare(id, otherId);
    }
    private static void ValidateFields(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Count ||
            fields.Any(field => field is not ("id" or "title" or "description" or "criteria")))
            throw new CliException("--fields requires unique values from id,title,description,criteria.");
    }
    private static SearchOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var roots = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            var option = args[i];
            if (option is not ("--snapshot" or "--tasks" or "-t" or "--query" or "--fields" or "--root" or "--status" or "--limit" or "--cursor" or "--format") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new CliException($"Invalid search option or missing value '{option}'.");
            var value = args[++i];
            if (option == "--root") { roots.Add(value); continue; }
            if (!values.TryAdd(option == "-t" ? "--tasks" : option, value)) throw new CliException($"Duplicate option '{option}'.");
        }
        if (values.ContainsKey("--snapshot") && values.ContainsKey("--tasks"))
            throw new CliException("--snapshot and --tasks are mutually exclusive.");
        var fields = values.TryGetValue("--fields", out var fieldText) ? fieldText.Split(',') : ["id", "title"];
        ValidateFields(fields);
        DomainTaskStatus? status = null;
        if (values.TryGetValue("--status", out var statusText))
        {
            if (!Enum.TryParse<DomainTaskStatus>(statusText, true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(statusText, out _))
                throw new CliException("Invalid --status value.");
            status = parsed;
        }
        var limit = 20;
        if (values.TryGetValue("--limit", out var limitText) && (!int.TryParse(limitText, out limit) || limit is < 1 or > 100))
            throw new CliException("--limit must be between 1 and 100.");
        var format = values.GetValueOrDefault("--format", "text");
        if (format is not ("text" or "json")) throw new CliException("--format must be text or json.");
        if (roots.Any(string.IsNullOrWhiteSpace) || values.TryGetValue("--snapshot", out var snapshot) && string.IsNullOrWhiteSpace(snapshot) ||
            values.TryGetValue("--tasks", out var tasks) && string.IsNullOrWhiteSpace(tasks)) throw new CliException("Paths and roots cannot be empty.");
        return new SearchOptions(values.GetValueOrDefault("--snapshot"), values.GetValueOrDefault("--tasks"),
            values.GetValueOrDefault("--query", ""), fields, roots, status, limit, values.GetValueOrDefault("--cursor"), format);
    }
    private sealed record SearchOptions(string? Snapshot, string? Tasks, string Query, string[] Fields,
        IReadOnlyList<string> Roots, DomainTaskStatus? Status, int Limit, string? Cursor, string Format);
}

public sealed record NightSearchSource(NightSearchSourceMetadata Metadata, IReadOnlyDictionary<string, IReadOnlyList<string>> Children,
    IReadOnlyList<NightSearchRecord> Records, bool DetailsCaptured = true, bool CriteriaCaptured = true);
public sealed record NightSearchSourceMetadata(string Kind, string Consistency, string Fingerprint, string? SnapshotId);
public sealed record NightSearchRecord(string Id, string Etag, string Title, DomainTaskStatus Status, int Importance,
    bool CanStart, bool CanComplete, string? Description, IReadOnlyList<TaskCriterionOutput>? Criteria, bool DetailsCaptured, bool CriteriaCaptured);
public sealed record NightSearchOutput(int SearchVersion, IReadOnlyList<NightSearchItem> Items, int TotalCount, string? NextCursor,
    NightSearchSourceMetadata Source, string Coverage, int SearchedNodeCount, int CatalogCount, bool SearchComplete, IReadOnlyList<NightSearchWarning> Warnings);
public sealed record NightSearchWarning(string Kind, string TaskId);
public sealed record NightSearchItem(string Id, string Title, DomainTaskStatus Status, int Importance, bool CanStart, bool CanComplete,
    string Etag, IReadOnlyList<NightSearchMatch> Matches, bool MatchesTruncated);
public sealed record NightSearchMatch(string Field, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CriterionId,
    string Snippet, string Normalization, IReadOnlyList<NightSearchRange> Ranges, bool MoreOccurrences);
public sealed record NightSearchRange(int Start, int Length);
internal sealed record NightSearchCursor(int Version, string Fingerprint, string Title, string Id);
