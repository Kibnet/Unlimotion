using System.Text;
using System.Text.Json;

namespace Unlimotion.Cli;

public static partial class NightAgentSnapshotCodec
{
    private sealed record PageCursor(int Version, string ArtifactHash, string? BeforeArtifactHash,
        string SnapshotId, string ScopeHash, string View, string FilterHash, int PageSize, string LastId);

    public static NightAgentSnapshotPage ReadPage(NightAgentSnapshotArtifact artifact, string view = "targets", int pageSize = 100, string? cursor = null)
    {
        if (view is not ("targets" or "context" or "manifest")) InvalidSelection("Unknown snapshot view.");
        CheckPageSize(pageSize);
        var targetIds = artifact.Targets.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var targets = artifact.Targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var records = view == "manifest" ? artifact.Catalog.Select(x => (x.Id, Value: (object)x)).ToArray() :
            artifact.Nodes.Where(n => view == "targets" ? targetIds.Contains(n.Id) : !targetIds.Contains(n.Id))
                .Select(x => (x.Id, Value: view == "targets" ? (object)NightAgentSnapshotTargetNode.From(x, targets[x.Id]) : x)).ToArray();
        records = records.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var binding = new PageCursor(1, artifact.ArtifactHash!, null, artifact.SnapshotId, artifact.ScopeHash, view, Hash(new { view }), pageSize, "");
        var last = DecodeCursor(cursor, binding);
        if (last != null && !records.Any(x => x.Id == last)) CursorError();
        var eligible = records.Where(x => last == null || StringComparer.Ordinal.Compare(x.Id, last) > 0).ToArray();
        var items = new List<object>(); string? next = null; long recordBytes = 0;
        foreach (var record in eligible.Take(pageSize))
        {
            var itemBytes = JsonSerializer.SerializeToUtf8Bytes(record.Value, JsonOptions).Length;
            items.Add(record.Value);
            var remaining = items.Count < eligible.Length;
            next = remaining ? EncodeCursor(binding with { LastId = record.Id }) : null;
            var envelope = new NightAgentSnapshotPage(1, artifact.SnapshotId, artifact.ArtifactHash!, view, records.Length, [], next, true);
            if (JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions).Length + recordBytes + itemBytes + items.Count - 1 <= MaximumPageBytes)
            { recordBytes += itemBytes; continue; }
            items.RemoveAt(items.Count - 1);
            if (items.Count == 0) throw new NightAgentSnapshotException("recordTooLarge", "One snapshot record exceeds the 16 MiB page limit.");
            next = EncodeCursor(binding with { LastId = eligible[items.Count - 1].Id });
            break;
        }
        return new(1, artifact.SnapshotId, artifact.ArtifactHash!, view, records.Length, items.ToArray(), next, true);
    }

    public static IReadOnlyList<NightAgentSnapshotDelta> Diff(NightAgentSnapshotArtifact before, NightAgentSnapshotArtifact after)
    {
        if (before.Source != after.Source || before.ScopeHash != after.ScopeHash || before.Contract != after.Contract ||
            !before.Completeness.Complete || !after.Completeness.Complete || after.Acquisition.EvaluatedAt < before.Acquisition.EvaluatedAt)
            throw new NightAgentSnapshotException("deltaIncompatible", "Snapshots have incompatible sources, selections, contracts, or evaluation times. Capture a new baseline and preserve workflow decisions/materials.");
        var a = before.Catalog.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var b = after.Catalog.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var beforeTargets = before.Targets.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var afterTargets = after.Targets.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var beforeNodes = before.Nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var afterNodes = after.Nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var changedDataIds = a.Keys.Union(b.Keys, StringComparer.Ordinal)
            .Where(id => !a.TryGetValue(id, out var old) || !b.TryGetValue(id, out var current) || old.Etag != current.Etag).ToHashSet(StringComparer.Ordinal);
        string[] Roles(string id, bool old) => (old ? beforeTargets : afterTargets).ContainsKey(id) ? ["target"] :
            (old ? beforeNodes : afterNodes).Contains(id) ? ["context"] : [];
        var result = new List<NightAgentSnapshotDelta>();
        foreach (var id in a.Keys.Union(b.Keys, StringComparer.Ordinal))
        {
            a.TryGetValue(id, out var old); b.TryGetValue(id, out var current);
            var beforeRoles = Roles(id, true); var afterRoles = Roles(id, false);
            if (old == null) result.Add(new() { Kind = "created", TaskId = id, AfterEtag = current!.Etag, AfterRoles = afterRoles });
            else if (current == null) result.Add(new() { Kind = "deleted", TaskId = id, BeforeEtag = old.Etag, BeforeRoles = beforeRoles, Evidence = "absentFromObservedNamespace" });
            else
            {
                if (old.Etag != current.Etag)
                {
                    var sections = old.SectionHashes.Keys.Union(current.SectionHashes.Keys, StringComparer.Ordinal)
                        .Where(s => old.SectionHashes.GetValueOrDefault(s) != current.SectionHashes.GetValueOrDefault(s)).Order(StringComparer.Ordinal).ToArray();
                    result.Add(new() { Kind = "updated", TaskId = id, BeforeEtag = old.Etag, AfterEtag = current.Etag,
                        ChangedSections = sections.Where(s => s != "unprojected").ToArray(),
                        UnprojectedDataChanged = sections.Length == 0 || sections.Contains("unprojected") });
                }
                if (old.AvailabilityHash != current.AvailabilityHash)
                {
                    var causes = new List<string>();
                    if (old.Etag != current.Etag) causes.Add("taskData");
                    if (old.Availability.Reasons.Concat(current.Availability.Reasons).SelectMany(reason => new[] { reason.SubjectId, reason.SourceTaskId })
                        .Any(subject => subject != null && subject != id && changedDataIds.Contains(subject))) causes.Add("context");
                    if (old.Etag == current.Etag && after.Acquisition.EvaluatedAt > before.Acquisition.EvaluatedAt &&
                        old.Availability.PlannedBeginIsFuture != current.Availability.PlannedBeginIsFuture) causes.Add("clock");
                    result.Add(new() { Kind = "availabilityChanged", TaskId = id, BeforeAvailability = old.Availability,
                        AfterAvailability = current.Availability, Reasons = causes });
                }
            }
            if (!beforeRoles.SequenceEqual(afterRoles, StringComparer.Ordinal)) result.Add(new() { Kind = "membershipChanged", TaskId = id, BeforeRoles = beforeRoles, AfterRoles = afterRoles });
        }
        var oldEdges = before.Catalog.SelectMany(x => x.Edges).ToHashSet();
        var newEdges = after.Catalog.SelectMany(x => x.Edges).ToHashSet();
        foreach (var edge in oldEdges.Except(newEdges)) result.Add(new() { Kind = "relationRemoved", TaskId = edge.From, Relation = edge });
        foreach (var edge in newEdges.Except(oldEdges)) result.Add(new() { Kind = "relationAdded", TaskId = edge.From, Relation = edge });
        foreach (var id in beforeTargets.Keys.Union(afterTargets.Keys, StringComparer.Ordinal))
        {
            beforeTargets.TryGetValue(id, out var old); afterTargets.TryGetValue(id, out var current);
            var beforeRoles = Roles(id, true); var afterRoles = Roles(id, false);
            if (old?.ContextHash == current?.ContextHash && beforeRoles.SequenceEqual(afterRoles, StringComparer.Ordinal)) continue;
            var oldIds = (old?.ContextIds ?? []).ToHashSet(StringComparer.Ordinal);
            var newIds = (current?.ContextIds ?? []).ToHashSet(StringComparer.Ordinal);
            var changedContextIds = oldIds.Union(newIds, StringComparer.Ordinal).Where(key =>
                !oldIds.Contains(key) || !newIds.Contains(key) || !a.TryGetValue(key, out var x) || !b.TryGetValue(key, out var y) ||
                x.Etag != y.Etag || x.AvailabilityHash != y.AvailabilityHash).Order(StringComparer.Ordinal).ToArray();
            var reasons = new List<string>();
            if (!beforeRoles.SequenceEqual(afterRoles, StringComparer.Ordinal)) reasons.Add("membership");
            if (!oldIds.SetEquals(newIds)) reasons.Add("contextMembership");
            if (a.TryGetValue(id, out var previous) && b.TryGetValue(id, out var next))
            {
                if (previous.Etag != next.Etag) reasons.Add("taskData");
                if (previous.AvailabilityHash != next.AvailabilityHash) reasons.Add("availability");
            }
            if (changedContextIds.Any(key => key != id)) reasons.Add("context");
            if (old?.ContextHash != current?.ContextHash) reasons.Add("contextHash");
            result.Add(new() { Kind = "targetInvalidated", TaskId = id, BeforeRoles = beforeRoles, AfterRoles = afterRoles,
                Reasons = reasons, ChangedContextIds = changedContextIds });
        }
        return result.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.TaskId, StringComparer.Ordinal)
            .ThenBy(x => x.Relation == null ? "" : EdgeKey(x.Relation), StringComparer.Ordinal).ToArray();
    }

    public static NightAgentSnapshotDeltaPage DiffPage(NightAgentSnapshotArtifact before, NightAgentSnapshotArtifact after, int pageSize = 100, string? cursor = null)
    {
        CheckPageSize(pageSize);
        var records = Diff(before, after);
        var binding = new PageCursor(1, after.ArtifactHash!, before.ArtifactHash, after.SnapshotId, after.ScopeHash, "delta", Hash(new { view = "delta" }), pageSize, "");
        var last = DecodeCursor(cursor, binding);
        var start = 0;
        if (last != null)
        {
            start = records.ToList().FindIndex(r => DeltaKey(r) == last) + 1;
            if (start == 0) CursorError();
        }
        var items = new List<NightAgentSnapshotDelta>(); string? next = null; long recordBytes = 0;
        foreach (var record in records.Skip(start).Take(pageSize))
        {
            var itemBytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions).Length;
            items.Add(record);
            next = start + items.Count < records.Count ? EncodeCursor(binding with { LastId = DeltaKey(record) }) : null;
            var envelope = new NightAgentSnapshotDeltaPage(1, before.SnapshotId, after.SnapshotId, after.ScopeHash, "stateDifference", [], records.Count, next, true);
            if (JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions).Length + recordBytes + itemBytes + items.Count - 1 <= MaximumPageBytes)
            { recordBytes += itemBytes; continue; }
            items.RemoveAt(items.Count - 1);
            if (items.Count == 0) throw new NightAgentSnapshotException("recordTooLarge", "One delta record exceeds the 16 MiB page limit.");
            next = EncodeCursor(binding with { LastId = DeltaKey(items[^1]) });
            break;
        }
        return new(1, before.SnapshotId, after.SnapshotId, after.ScopeHash, "stateDifference", items.ToArray(), records.Count, next, true);
    }

    private static string DeltaKey(NightAgentSnapshotDelta item) => JsonSerializer.Serialize(new[] { item.Kind, item.TaskId, item.Relation == null ? "" : EdgeKey(item.Relation) });
    private static void CheckPageSize(int size) { if (size is < 1 or > 500) InvalidSelection("page-size must be between 1 and 500."); }
    private static string EncodeCursor(PageCursor value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string? DecodeCursor(string? cursor, PageCursor expected)
    {
        if (cursor == null) return null;
        try
        {
            if (cursor.Length is 0 or > 64 * 1024) CursorError();
            var base64 = cursor.Replace('-', '+').Replace('_', '/'); base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var actual = JsonSerializer.Deserialize<PageCursor>(Convert.FromBase64String(base64), JsonOptions);
            if (actual == null || actual with { LastId = "" } != expected || string.IsNullOrEmpty(actual.LastId)) CursorError();
            return actual!.LastId;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException) { CursorError(); return null; }
    }
    private static void CursorError() => throw new NightAgentSnapshotException("cursorMismatch", "Cursor does not match this artifact, view, filter, or page size.", 2);
}
