using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Cli;

/// <summary>Caller-owned, immutable observations. No method in this codec refreshes a live source.</summary>
public static partial class NightAgentSnapshotCodec
{
    public const int MaximumArtifactBytes = 128 * 1024 * 1024;
    public const int MaximumPageBytes = 16 * 1024 * 1024;
    public const string SourceNamespace = "topLevelTasks-v1";
    private static readonly string[] Sections = ["details", "criteria", "history", "execution"];
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Legacy task DTOs have non-null C# annotations but persisted null descriptions/titles.
        // Validate the envelope explicitly while preserving those domain values.
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Hash(object? value) => HashNode(JsonSerializer.SerializeToNode(value, JsonOptions));
    private static string HashNode(JsonNode? value) => "sha256:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(value)))).ToLowerInvariant();
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node?.ToJsonString(JsonOptions) ?? "null"
    };

    public static string ResolvePhysicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new NightAgentSnapshotException("sourceUnavailable", "Task source directory does not exist.");
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo || !target.Exists)
                    throw new NightAgentSnapshotException("sourceUnavailable", "Cannot resolve a physical source directory.");
                current = target.FullName;
            }
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    public static string CreateSourceKey(string path)
    {
        var resolved = ResolvePhysicalDirectory(path);
        if (OperatingSystem.IsWindows()) resolved = resolved.ToUpperInvariant();
        return Hash(new { root = resolved, sourceNamespace = SourceNamespace, platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos" });
    }

    public static NightAgentSnapshotSelection NormalizeSelection(NightAgentSnapshotSelection selection)
    {
        if (selection == null || selection.Select == null || selection.SchemaVersion != 1 ||
            selection.Select.Mode is not ("all" or "unlocked" or "ids") || selection.Context is not ("none" or "night-v1") ||
            selection.MissingSelection is not ("error" or "report")) InvalidSelection("Unsupported or missing selection contract.");
        var select = selection!.Select!;
        var roots = NormalizeIds(select.RootIds);
        var ids = NormalizeIds(select.TaskIds);
        var statuses = NormalizeIds(select.Statuses).Select(status =>
        {
            if (!Enum.TryParse<DomainTaskStatus>(status, true, out var parsed) || !Enum.GetNames<DomainTaskStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
                InvalidSelection($"Unknown status '{status}'.");
            return parsed.ToString();
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var include = NormalizeIds(selection.Include);
        if (include.Any(section => !Sections.Contains(section, StringComparer.Ordinal))) InvalidSelection("Unknown include section.");
        if (selection.Context == "night-v1" && (!include.Contains("details") || !include.Contains("criteria")))
            InvalidSelection("night-v1 requires details and criteria.");
        if (select.Mode == "ids" && (ids.Length == 0 || roots.Length != 0 || statuses.Length != 0))
            InvalidSelection("ids requires taskIds and forbids rootIds/statuses.");
        if (select.Mode != "ids" && ids.Length != 0) InvalidSelection("taskIds are valid only with mode ids.");
        return selection with { Select = select with { RootIds = roots, TaskIds = ids, Statuses = statuses }, Include = include };
    }

    private static string[] NormalizeIds(IReadOnlyList<string>? ids)
    {
        if (ids == null || ids.Any(string.IsNullOrWhiteSpace)) InvalidSelection("Lists cannot be null or contain empty identifiers.");
        return ids!.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private static void InvalidSelection(string message) => throw new NightAgentSnapshotException("invalidArguments", message, 2);

    public static NightAgentSnapshotArtifact CreateArtifact(TaskGraphObservation observation,
        NightAgentSnapshotSelection selection, string sourceKey, CancellationToken cancellationToken = default)
    {
        selection = NormalizeSelection(selection);
        var graph = observation.Graph;
        if (graph.Tasks.Count > 50_000) TooLarge("taskCount", graph.Tasks.Count);
        var analyzer = new TaskAvailabilityAnalyzer(graph.Tasks, observation.EvaluatedAt, cancellationToken);
        var validation = analyzer.Validate();
        if (graph.LoadErrors.Count != 0 || graph.DuplicateIdIssues.Count != 0 || validation.ReferenceIssues.Count != 0 || validation.DuplicateIdIssues.Count != 0)
            throw new NightAgentSnapshotException("snapshotInvalid", "Source graph has load, duplicate ID, missing/reverse reference, criterion or cycle errors. " +
                string.Join("; ", validation.ReferenceIssues.Take(8).Select(x => $"{x.Kind}:{x.SourceTaskId}:{x.TargetTaskId}")));
        var tasks = graph.TasksById;
        var analyses = new Dictionary<string, TaskAvailabilityAnalysis>(StringComparer.Ordinal);
        foreach (var task in graph.Tasks) { cancellationToken.ThrowIfCancellationRequested(); analyses.Add(task.Id, analyzer.Analyze(task)); }
        var edges = graph.Tasks.SelectMany(task => (task.ContainsTasks ?? []).Select(to => new NightAgentSnapshotEdge("contains", task.Id, to))
                .Concat((task.BlocksTasks ?? []).Select(to => new NightAgentSnapshotEdge("blocks", task.Id, to))))
            .OrderBy(EdgeKey, StringComparer.Ordinal).ToArray();
        if (edges.Length > 500_000) TooLarge("canonicalEdges", edges.Length);
        var edgeLookup = edges.SelectMany(edge => new[] { (Id: edge.From, Edge: edge), (Id: edge.To, Edge: edge) })
            .ToLookup(x => x.Id, x => x.Edge, StringComparer.Ordinal);
        var outgoingEdges = edges.ToLookup(x => x.From, StringComparer.Ordinal);
        var fullInclude = Sections.ToHashSet(StringComparer.Ordinal);
        var projected = new Dictionary<string, TaskSnapshotOutput>(StringComparer.Ordinal);
        var catalog = new List<NightAgentSnapshotCatalogEntry>(tasks.Count);
        foreach (var task in graph.Tasks.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projection = TaskSnapshotOutput.Create(task, analyses[task.Id], analyzer, fullInclude);
            projected.Add(task.Id, projection);
            var availability = Availability(analyses[task.Id]);
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["summary"] = Hash(new { task.Id, task.Title, task.Status }), ["details"] = Hash(projection.Details),
                ["criteria"] = Hash(projection.Criteria), ["history"] = Hash(projection.History), ["execution"] = Hash(projection.Execution),
                ["relations"] = Hash(new { task.ContainsTasks, task.ParentTasks, task.BlocksTasks, task.BlockedByTasks }),
                ["system"] = Hash(new { task.IsCanBeCompleted, task.UnlockedDateTime, task.UserId, task.Version }),
                ["unprojected"] = Hash(Newtonsoft.Json.JsonConvert.SerializeObject(task.ExtensionData))
            };
            catalog.Add(new(task.Id, projection.Etag, hashes, outgoingEdges[task.Id].ToArray(), availability, Hash(availability), task.Status));
        }
        var byId = catalog.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var missingRoots = selection.Select.RootIds.Where(id => !tasks.ContainsKey(id)).ToArray();
        var missingIds = selection.Select.TaskIds.Where(id => !tasks.ContainsKey(id)).ToArray();
        if (selection.MissingSelection == "error" && (missingRoots.Length != 0 || missingIds.Length != 0))
            throw new NightAgentSnapshotException("notFound", "Requested root/task IDs are absent: " + string.Join(", ", missingRoots.Concat(missingIds)));
        IEnumerable<string> selected;
        if (selection.Select.Mode == "ids") selected = selection.Select.TaskIds.Where(tasks.ContainsKey);
        else if (selection.Select.RootIds.Count == 0) selected = tasks.Keys;
        else selected = Traverse(selection.Select.RootIds.Where(tasks.ContainsKey), id => tasks[id].ContainsTasks ?? [], cancellationToken);
        var statuses = selection.Select.Statuses.ToHashSet(StringComparer.Ordinal);
        var targetIds = selected.Where(id => (statuses.Count == 0 || statuses.Contains(tasks[id].Status.ToString())) &&
                (selection.Select.Mode != "unlocked" || analyses[id].CanStart)).Order(StringComparer.Ordinal).ToArray();
        var contract = new NightAgentSnapshotContract(1, selection.Context, "task-etag-v1", "task-availability-v1", "NFC-OrdinalIgnoreCase-v1",
            TimeZoneInfo.Local.Id, TimeZoneInfo.Local.GetUtcOffset(observation.EvaluatedAt).ToString("c", CultureInfo.InvariantCulture));
        var targets = new List<NightAgentSnapshotTarget>(targetIds.Length);
        var payload = new HashSet<string>(StringComparer.Ordinal);
        long contextEntries = 0;
        foreach (var id in targetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = selection.Context == "none" ? new HashSet<string>([id], StringComparer.Ordinal) : ContextClosure(id, tasks, cancellationToken);
            contextEntries += context.Count;
            if (contextEntries > 5_000_000) TooLarge("contextIds", contextEntries);
            payload.UnionWith(context);
            var contextIds = context.Order(StringComparer.Ordinal).ToArray();
            var contextEdges = context.SelectMany(x => edgeLookup[x]).Distinct().OrderBy(EdgeKey, StringComparer.Ordinal)
                .Select(e => new { e.Kind, e.From, e.To, outsideContext = !context.Contains(e.From) || !context.Contains(e.To) }).ToArray();
            var contextHash = Hash(new { contract, selection.Include, targetId = id, contextIds,
                nodes = contextIds.Select(x => new { id = x, byId[x].Etag, byId[x].AvailabilityHash }), edges = contextEdges });
            targets.Add(new(id, contextIds, contextHash));
        }
        var sectionStates = Sections.ToDictionary(s => s, s => selection.Include.Contains(s) ? "captured" : "notRequested", StringComparer.Ordinal);
        var nodes = payload.Order(StringComparer.Ordinal).Select(id =>
        {
            var p = projected[id]; var task = tasks[id];
            return new NightAgentSnapshotNode { Id = id, Etag = p.Etag, Task = p.Task,
                Details = selection.Include.Contains("details") ? p.Details : null, Criteria = selection.Include.Contains("criteria") ? p.Criteria : null,
                History = selection.Include.Contains("history") ? p.History : null, Execution = selection.Include.Contains("execution") ? p.Execution : null,
                Sections = sectionStates, ContainsTasks = Sorted(task.ContainsTasks), ParentTasks = Sorted(task.ParentTasks),
                BlocksTasks = Sorted(task.BlocksTasks), BlockedByTasks = Sorted(task.BlockedByTasks), Availability = byId[id].Availability,
                Edges = edgeLookup[id].OrderBy(EdgeKey, StringComparer.Ordinal).Select(e => new NightAgentSnapshotBoundaryEdge(e.Kind, e.From, e.To,
                    !payload.Contains(e.From) || !payload.Contains(e.To))).ToArray() };
        }).ToArray();
        var artifact = new NightAgentSnapshotArtifact
        {
            SnapshotId = Guid.NewGuid().ToString("D"), Source = new("file", sourceKey, SourceNamespace), Contract = contract, Selection = selection,
            ScopeHash = Hash(new { selection, contract }),
            Acquisition = new(observation.StartedAt, observation.CompletedAt, observation.EvaluatedAt, "cooperativeLockWithVerifiedManifest", false,
                observation.Attempts, observation.TaskParseCount, observation.VerifiedFileCount, observation.SourceBytes),
            Completeness = new(true, nodes.Length, targets.Count, catalog.Count, sectionStates, missingRoots, missingIds,
                validation.AvailabilityMismatches.OrderBy(x => x.TaskId, StringComparer.Ordinal).Select(x => "storedAvailabilityMismatch:" + x.TaskId).ToArray()),
            Catalog = catalog, Targets = targets, Nodes = nodes,
            NextEvaluationAt = graph.Tasks.Select(t => t.PlannedBeginDateTime).Where(t => t > observation.EvaluatedAt).Min()
        };
        artifact = artifact with { ArtifactHash = Hash(artifact) };
        if (JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions).Length > MaximumArtifactBytes) TooLarge("artifactBytes", MaximumArtifactBytes + 1L);
        return artifact;
    }

    private static string[] Sorted(IEnumerable<string>? ids) => (ids ?? []).Order(StringComparer.Ordinal).ToArray();
    private static HashSet<string> ContextClosure(string id, IReadOnlyDictionary<string, TaskItem> tasks, CancellationToken cancellationToken)
    {
        var ancestors = Traverse([id], key => tasks[key].ParentTasks ?? [], cancellationToken);
        var seeds = ancestors.Concat(ancestors.SelectMany(key => (tasks[key].ContainsTasks ?? [])
            .Concat(tasks[key].BlocksTasks ?? []).Concat(tasks[key].BlockedByTasks ?? [])));
        return Traverse(seeds, key => (tasks[key].ParentTasks ?? []).Concat(tasks[key].BlockedByTasks ?? []), cancellationToken);
    }
    private static HashSet<string> Traverse(IEnumerable<string> seeds, Func<string, IEnumerable<string>> next, CancellationToken token)
    {
        var found = new HashSet<string>(StringComparer.Ordinal); var queue = new Queue<string>(seeds);
        while (queue.TryDequeue(out var id)) { token.ThrowIfCancellationRequested(); if (found.Add(id)) foreach (var neighbor in next(id)) queue.Enqueue(neighbor); }
        return found;
    }
    private static NightAgentSnapshotAvailability Availability(TaskAvailabilityAnalysis analysis) => new(analysis.CanStart, analysis.CanComplete,
        analysis.IsCanBeCompleted, analysis.CompletionCriteriaSatisfied, analysis.PlannedBeginIsFuture,
        analysis.Reasons.Select(r => new NightAgentSnapshotReason(r.Kind.ToString(), r.SubjectId, r.SourceTaskId, r.CriterionId))
            .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.SubjectId, StringComparer.Ordinal)
            .ThenBy(r => r.SourceTaskId, StringComparer.Ordinal).ThenBy(r => r.CriterionId, StringComparer.Ordinal).ToArray());
    internal static string EdgeKey(NightAgentSnapshotEdge edge) => JsonSerializer.Serialize(new[] { edge.Kind, edge.From, edge.To });
    private static void TooLarge(string limit, long value) => throw new NightAgentSnapshotException("snapshotTooLarge", $"Limit {limit} exceeded: {value}.");

    public static async Task<NightAgentSnapshotArtifact> LoadArtifactAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path)) throw new NightAgentSnapshotException("snapshotNotFound", "Snapshot artifact was not found.");
            var info = new FileInfo(path);
            if (info.Length > MaximumArtifactBytes) TooLarge("artifactBytes", info.Length);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            using var buffer = new MemoryStream((int)info.Length);
            var chunk = new byte[64 * 1024]; int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + count > MaximumArtifactBytes) TooLarge("artifactBytes", buffer.Length + count);
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateProperties(document.RootElement);
            var node = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException("Expected an object.");
            var digest = node["artifactHash"]?.GetValue<string>();
            node.Remove("artifactHash");
            if (!IsHash(digest) || HashNode(node) != digest) throw new JsonException("Artifact checksum mismatch.");
            var artifact = JsonSerializer.Deserialize<NightAgentSnapshotArtifact>(bytes, JsonOptions) ?? throw new JsonException("Missing artifact.");
            ValidateArtifact(artifact);
            return artifact;
        }
        catch (NightAgentSnapshotException error) when (error.Kind == "invalidArguments")
        { throw new NightAgentSnapshotException("snapshotInvalid", "Invalid artifact selection: " + error.Message); }
        catch (NightAgentSnapshotException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or NullReferenceException)
        { throw new NightAgentSnapshotException("snapshotInvalid", "Snapshot artifact is invalid: " + error.Message); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property."); RejectDuplicateProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private static bool IsHash(string? value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) && value[7..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateArtifact(NightAgentSnapshotArtifact artifact)
    {
        if (artifact.SnapshotFormatVersion != 1 || !Guid.TryParseExact(artifact.SnapshotId, "D", out _) ||
            artifact.Source.StorageKind != "file" || artifact.Source.SourceNamespace != SourceNamespace || !IsHash(artifact.Source.SourceKey) ||
            artifact.Contract.SchemaVersion != 1 || artifact.Contract.EtagVersion != "task-etag-v1" || artifact.Contract.AvailabilityVersion != "task-availability-v1" ||
            artifact.Contract.SearchNormalizationVersion != "NFC-OrdinalIgnoreCase-v1" || artifact.Contract.ClosureVersion != artifact.Selection.Context ||
            string.IsNullOrWhiteSpace(artifact.Contract.TimeZoneId) || !TimeSpan.TryParseExact(artifact.Contract.TimeZoneOffset, "c", CultureInfo.InvariantCulture, out _) ||
            artifact.Acquisition.Consistency != "cooperativeLockWithVerifiedManifest" || artifact.Acquisition.Atomic ||
            artifact.Acquisition.Attempts is < 1 or > 3 || artifact.Acquisition.CompletedAt < artifact.Acquisition.StartedAt ||
            artifact.Acquisition.EvaluatedAt < artifact.Acquisition.StartedAt || artifact.Acquisition.EvaluatedAt > artifact.Acquisition.CompletedAt ||
            artifact.Acquisition.TaskParseCount < 0 || artifact.Acquisition.VerifiedFileCount < 0 || artifact.Acquisition.SourceBytes is < 0 or > 256L * 1024 * 1024 ||
            !artifact.Completeness.Complete) throw new JsonException("Unsupported or incomplete snapshot contract.");
        var normalized = NormalizeSelection(artifact.Selection);
        if (Hash(normalized) != Hash(artifact.Selection) || artifact.ScopeHash != Hash(new { selection = normalized, contract = artifact.Contract }))
            throw new JsonException("Selection/scope hash mismatch.");
        if (artifact.Catalog.Count > 50_000 || artifact.Catalog.Sum(x => (long)x.Edges.Count) > 500_000 || artifact.Targets.Sum(x => (long)x.ContextIds.Count) > 5_000_000)
            throw new JsonException("Snapshot structural limits exceeded.");
        var catalog = artifact.Catalog.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var nodes = artifact.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var targets = artifact.Targets.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (!artifact.Catalog.Select(x => x.Id).SequenceEqual(catalog.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !artifact.Nodes.Select(x => x.Id).SequenceEqual(nodes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !artifact.Targets.Select(x => x.Id).SequenceEqual(targets.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new JsonException("Snapshot records are not in canonical ID order.");
        if (artifact.Completeness.CatalogCount != catalog.Count || artifact.Completeness.PayloadCount != nodes.Count || artifact.Completeness.TargetCount != targets.Count)
            throw new JsonException("Completeness counts disagree.");
        foreach (var entry in catalog.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !IsHash(entry.Etag) || !Enum.IsDefined(entry.Status) || Hash(entry.Availability) != entry.AvailabilityHash ||
                !entry.SectionHashes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["summary", "details", "criteria", "history", "execution", "relations", "system", "unprojected"]) ||
                entry.SectionHashes.Any(p => !IsHash(p.Value))) throw new JsonException("Invalid catalog record.");
            if (entry.Edges.Any(e => e.From != entry.Id || !catalog.ContainsKey(e.To) || e.Kind is not ("contains" or "blocks")) ||
                entry.Edges.Select(EdgeKey).Distinct(StringComparer.Ordinal).Count() != entry.Edges.Count) throw new JsonException("Invalid catalog edge.");
        }
        var expectedSections = Sections.ToDictionary(s => s, s => normalized.Include.Contains(s) ? "captured" : "notRequested", StringComparer.Ordinal);
        if (Hash(expectedSections) != Hash(artifact.Completeness.Sections)) throw new JsonException("Section completeness mismatch.");
        var allEdges = catalog.Values.SelectMany(entry => entry.Edges).ToArray();
        var incomingEdges = allEdges.ToLookup(edge => edge.To, StringComparer.Ordinal);
        var incidentEdges = allEdges.SelectMany(edge => new[] { (Id: edge.From, Edge: edge), (Id: edge.To, Edge: edge) }).ToLookup(pair => pair.Id, pair => pair.Edge, StringComparer.Ordinal);
        ValidateAcyclic(catalog, "contains"); ValidateAcyclic(catalog, "blocks");
        var expectedMissingRoots = normalized.Select.RootIds.Where(id => !catalog.ContainsKey(id)).ToArray();
        var expectedMissingTasks = normalized.Select.TaskIds.Where(id => !catalog.ContainsKey(id)).ToArray();
        if (!expectedMissingRoots.SequenceEqual(artifact.Completeness.MissingRootIds, StringComparer.Ordinal) ||
            !expectedMissingTasks.SequenceEqual(artifact.Completeness.MissingTaskIds, StringComparer.Ordinal) ||
            (normalized.MissingSelection == "error" && (expectedMissingRoots.Length != 0 || expectedMissingTasks.Length != 0)))
            throw new JsonException("Missing-selection report disagrees with the catalog.");
        var candidates = normalized.Select.Mode == "ids" ? normalized.Select.TaskIds.Where(catalog.ContainsKey) :
            normalized.Select.RootIds.Count == 0 ? catalog.Keys :
            Traverse(normalized.Select.RootIds.Where(catalog.ContainsKey), id => catalog[id].Edges.Where(edge => edge.Kind == "contains").Select(edge => edge.To), default);
        var statusFilter = normalized.Select.Statuses.ToHashSet(StringComparer.Ordinal);
        var expectedTargetIds = candidates.Where(id => (statusFilter.Count == 0 || statusFilter.Contains(catalog[id].Status.ToString())) &&
            (normalized.Select.Mode != "unlocked" || catalog[id].Availability.CanStart)).ToHashSet(StringComparer.Ordinal);
        if (!expectedTargetIds.SetEquals(targets.Keys)) throw new JsonException("Target membership disagrees with the selection/catalog.");
        foreach (var node in nodes.Values)
        {
            if (!catalog.TryGetValue(node.Id, out var entry) || entry.Etag != node.Etag || Hash(node.Availability) != entry.AvailabilityHash ||
                Hash(node.Sections) != Hash(expectedSections) || node.Task.Id != node.Id || node.Task.Status != entry.Status ||
                node.Task.CanStart != node.Availability.CanStart || node.Task.CanComplete != node.Availability.CanComplete ||
                node.Task.IsCanBeCompleted != node.Availability.IsCanBeCompleted ||
                Hash(new { node.Task.Id, node.Task.Title, node.Task.Status }) != entry.SectionHashes["summary"])
                throw new JsonException("Payload/catalog mismatch.");
            if ((node.Details != null) != normalized.Include.Contains("details") || (node.Criteria != null) != normalized.Include.Contains("criteria") ||
                (node.History != null) != normalized.Include.Contains("history") || (!normalized.Include.Contains("execution") && node.Execution != null))
                throw new JsonException("Captured section mismatch.");
            if (node.ContainsTasks.Concat(node.ParentTasks).Concat(node.BlocksTasks).Concat(node.BlockedByTasks).Any(id => !catalog.ContainsKey(id)))
                throw new JsonException("Missing payload relation reference.");
            if (!node.ContainsTasks.SequenceEqual(entry.Edges.Where(edge => edge.Kind == "contains").Select(edge => edge.To).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.BlocksTasks.SequenceEqual(entry.Edges.Where(edge => edge.Kind == "blocks").Select(edge => edge.To).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.ParentTasks.SequenceEqual(incomingEdges[node.Id].Where(edge => edge.Kind == "contains").Select(edge => edge.From).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.BlockedByTasks.SequenceEqual(incomingEdges[node.Id].Where(edge => edge.Kind == "blocks").Select(edge => edge.From).Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new JsonException("Payload relation arrays disagree with canonical edges.");
            var expectedEdges = incidentEdges[node.Id].OrderBy(EdgeKey, StringComparer.Ordinal).Select(edge => new NightAgentSnapshotBoundaryEdge(
                edge.Kind, edge.From, edge.To, !nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))).ToArray();
            if (!expectedEdges.SequenceEqual(node.Edges)) throw new JsonException("Payload edge boundary mismatch.");
            foreach (var section in normalized.Include)
            {
                object? value = section switch { "details" => node.Details, "criteria" => node.Criteria, "history" => node.History, "execution" => node.Execution, _ => null };
                if (Hash(value) != entry.SectionHashes[section]) throw new JsonException("Payload section hash mismatch.");
            }
        }
        foreach (var target in targets.Values)
        {
            if (!nodes.ContainsKey(target.Id) || !target.ContextIds.Contains(target.Id) || target.ContextIds.Distinct(StringComparer.Ordinal).Count() != target.ContextIds.Count ||
                target.ContextIds.Any(id => !nodes.ContainsKey(id)) || !IsHash(target.ContextHash)) throw new JsonException("Incomplete target context.");
            var ids = target.ContextIds.ToHashSet(StringComparer.Ordinal);
            var expectedContext = normalized.Context == "none" ? new HashSet<string>([target.Id], StringComparer.Ordinal) :
                CatalogContextClosure(target.Id, catalog, incomingEdges);
            if (!expectedContext.SetEquals(ids) || !target.ContextIds.SequenceEqual(expectedContext.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new JsonException("Target context is not the complete canonical closure.");
            var contextEdges = ids.SelectMany(id => incidentEdges[id]).Distinct().OrderBy(EdgeKey, StringComparer.Ordinal)
                .Select(edge => new { edge.Kind, edge.From, edge.To, outsideContext = !ids.Contains(edge.From) || !ids.Contains(edge.To) }).ToArray();
            if (target.ContextHash != Hash(new { contract = artifact.Contract, artifact.Selection.Include, targetId = target.Id, contextIds = target.ContextIds,
                nodes = target.ContextIds.Select(id => new { id, catalog[id].Etag, catalog[id].AvailabilityHash }), edges = contextEdges }))
                throw new JsonException("Context hash mismatch.");
        }
        if (!targets.Values.SelectMany(t => t.ContextIds).ToHashSet(StringComparer.Ordinal).SetEquals(nodes.Keys)) throw new JsonException("Payload/context mismatch.");
    }

    private static HashSet<string> CatalogContextClosure(string id, IReadOnlyDictionary<string, NightAgentSnapshotCatalogEntry> catalog,
        ILookup<string, NightAgentSnapshotEdge> incoming)
    {
        var ancestors = Traverse([id], key => incoming[key].Where(edge => edge.Kind == "contains").Select(edge => edge.From), default);
        var seeds = ancestors.Concat(ancestors.SelectMany(key => catalog[key].Edges.Select(edge => edge.To)
            .Concat(incoming[key].Where(edge => edge.Kind == "blocks").Select(edge => edge.From))));
        return Traverse(seeds, key => incoming[key].Select(edge => edge.From), default);
    }

    private static void ValidateAcyclic(IReadOnlyDictionary<string, NightAgentSnapshotCatalogEntry> catalog, string kind)
    {
        var indegree = catalog.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in catalog.Values.SelectMany(entry => entry.Edges).Where(edge => edge.Kind == kind)) indegree[edge.To]++;
        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var count = 0;
        while (queue.TryDequeue(out var id))
        {
            count++;
            foreach (var edge in catalog[id].Edges.Where(edge => edge.Kind == kind)) if (--indegree[edge.To] == 0) queue.Enqueue(edge.To);
        }
        if (count != catalog.Count) throw new JsonException("Catalog contains a " + kind + " cycle.");
    }
}
