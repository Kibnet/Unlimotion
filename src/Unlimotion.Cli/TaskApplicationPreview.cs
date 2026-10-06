using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Cli;

public sealed record TaskApplicationPreviewChange
{
    public string TaskId { get; init; } = "";
    public string Path { get; init; } = "";
    public JsonElement Before { get; init; }
    public JsonElement After { get; init; }
    public string Origin { get; init; } = "";
    public IReadOnlyList<string> OperationIds { get; init; } = [];
    public IReadOnlyList<string> ReasonTaskIds { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cause { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TaskApplicationPreviewEdge? CanonicalEdge { get; init; }
}

public sealed record TaskApplicationPreviewEdge(string Kind, string FromTaskId, string ToTaskId);
public sealed record TaskApplicationPreviewEtag(string TaskId, string? BeforeEtag, string PredictedAfterEtag);
public sealed record TaskApplicationPreviewInvariant(string TaskId, string ExecutionHash, string DescriptionSegmentHash);
public sealed record TaskApplicationPreviewProtection(bool Changed, IReadOnlyList<TaskApplicationPreviewInvariant> Invariants);
public sealed record TaskApplicationPreviewGuard(int Version, string SourceManifestHash, string EffectHash);
public sealed record TaskApplicationPreviewContracts(int Application, int Projection, int Availability, string TimeZone);

public sealed record TaskApplicationPreviewPayload
{
    public int PreviewVersion { get; init; } = 1;
    public string RequestHash { get; init; } = "";
    public string SourceKey { get; init; } = "";
    public DateTimeOffset EvaluatedAt { get; init; }
    public bool Complete { get; init; } = true;
    public TaskApplicationPreviewContracts Contracts { get; init; } = new(1, 1, 1, TimeZoneInfo.Local.Id);
    public IReadOnlyList<TaskApplicationPreviewChange> Changes { get; init; } = [];
    public IReadOnlyList<string> AffectedTaskIds { get; init; } = [];
    public IReadOnlyList<string> StoredChangedTaskIds { get; init; } = [];
    public IReadOnlyList<string> CreatedTaskIds { get; init; } = [];
    public IReadOnlyList<TaskApplicationPreviewEtag> Etags { get; init; } = [];
    public TaskApplicationPreviewProtection ProtectedExecution { get; init; } = new(false, []);
    public TaskApplicationPreviewGuard Guard { get; init; } = new(1, "", "");
}

/// <summary>Full review artifact and its semantic commit witness. This is integrity, not a signature.</summary>
public static class TaskApplicationPreview
{
    public const int MaximumWitnessBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static TaskApplicationPreviewPayload Create(TaskApplicationPlan plan, TaskApplicationRequest request,
        string requestHash, string sourceKey)
    {
        var beforeRules = new TaskAvailabilityService(plan.Before.Values, plan.EvaluatedAt);
        var afterRules = new TaskAvailabilityService(plan.After.Values, plan.EvaluatedAt);
        var changes = new List<TaskApplicationPreviewChange>();
        var invariants = new List<TaskApplicationPreviewInvariant>();
        foreach (var (id, after) in plan.After.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var initialChangeCount = changes.Count;
            plan.Before.TryGetValue(id, out var before);
            var protection = Protection(after);
            if (before != null && Protection(before) != protection)
                throw Invalid("Protected execution or its description segment changed.");
            var afterProjection = Project(after, before, normalizeGenerated: true);
            if (before == null)
            {
                Add(id, "/", JValue.CreateNull(), afterProjection, "explicit",
                    request.Operations.Where(op => op.NewTaskId == id).Select(op => op.OperationId));
            }
            else
            {
                var beforeProjection = Project(before, before, normalizeGenerated: false);
                var explicitProjection = Project(plan.AfterExplicit[id], before, normalizeGenerated: true);
                foreach (var section in new[] { "details", "criteria" })
                {
                    var oldObject = (JObject)beforeProjection[section]!;
                    var newObject = (JObject)afterProjection[section]!;
                    foreach (var property in oldObject.Properties().Select(p => p.Name)
                                 .Union(newObject.Properties().Select(p => p.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    {
                        var oldValue = oldObject[property] ?? JValue.CreateNull();
                        var newValue = newObject[property] ?? JValue.CreateNull();
                        if (JToken.DeepEquals(oldValue, newValue)) continue;
                        var path = "/" + section + "/" + Escape(property);
                        var operationIds = ExplicitOperations(request, id, section, property).ToArray();
                        var system = property is "updatedDateTime" or "createdDateTime";
                        var explicitValue = explicitProjection[section]?[property] ?? JValue.CreateNull();
                        var origin = system ? "system" : operationIds.Length > 0 && JToken.DeepEquals(explicitValue, newValue) ? "explicit" : "derived";
                        Add(id, path, oldValue, newValue, origin, origin == "explicit" ? operationIds : [],
                            origin == "derived" ? ReasonIds(beforeRules.Analyze(before), afterRules.Analyze(after)) : []);
                    }
                }
                if (!JToken.DeepEquals(beforeProjection["history"], afterProjection["history"]))
                {
                    var explicitChange = JToken.DeepEquals(explicitProjection["history"], afterProjection["history"]);
                    Add(id, "/history", beforeProjection["history"]!, afterProjection["history"]!, explicitChange ? "explicit" : "derived",
                        explicitChange ? request.Operations.Where(op => op.TaskId == id && op.Kind == TaskApplicationOperationKind.SetStatus).Select(op => op.OperationId) : [],
                        explicitChange ? [] : ReasonIds(beforeRules.Analyze(before), afterRules.Analyze(after)));
                }
            }

            foreach (var (kind, reverse, oldIds, newIds) in Relations(before, after))
            {
                foreach (var other in oldIds.Union(newIds, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    var oldValue = oldIds.Contains(other, StringComparer.Ordinal);
                    var newValue = newIds.Contains(other, StringComparer.Ordinal);
                    if (oldValue == newValue) continue;
                    var edge = new TaskApplicationPreviewEdge(kind, reverse ? other : id, reverse ? id : other);
                    var operations = RelationOperations(request, edge).ToArray();
                    Add(id, "/storedRelations/" + (reverse ? kind == "contains" ? "parents" : "blockedBy" : kind) + "/" + Escape(other),
                        new JValue(oldValue), new JValue(newValue), "explicit", operations, edge: edge);
                    if (!reverse)
                        Add(id, "/relations/" + kind + "/" + Escape(other), new JValue(oldValue), new JValue(newValue), "explicit", operations, edge: edge);
                }
            }

            var oldAvailability = before == null ? null : Availability(beforeRules.Analyze(before));
            var newAvailability = Availability(afterRules.Analyze(after));
            foreach (var property in newAvailability.Properties())
            {
                var oldValue = oldAvailability?[property.Name] ?? JValue.CreateNull();
                if (!JToken.DeepEquals(oldValue, property.Value))
                    Add(id, "/availability/" + property.Name, oldValue, property.Value, "derived", [],
                        before == null ? ReasonIds(afterRules.Analyze(after)) : ReasonIds(beforeRules.Analyze(before), afterRules.Analyze(after)));
            }
            if (changes.Count != initialChangeCount)
                invariants.Add(new(id, protection.ExecutionHash, protection.DescriptionSegmentHash));
        }
        var affectedIds = changes.Select(change => change.TaskId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var payload = new TaskApplicationPreviewPayload
        {
            RequestHash = requestHash, SourceKey = sourceKey, EvaluatedAt = plan.EvaluatedAt,
            Changes = changes.OrderBy(change => change.TaskId, StringComparer.Ordinal).ThenBy(change => change.Path, StringComparer.Ordinal).ToArray(),
            AffectedTaskIds = affectedIds, StoredChangedTaskIds = plan.ChangedTaskIds.Order(StringComparer.Ordinal).ToArray(), CreatedTaskIds = plan.CreatedTaskIds.Order(StringComparer.Ordinal).ToArray(),
            Etags = affectedIds.Select(id => new TaskApplicationPreviewEtag(id,
                plan.Before.TryGetValue(id, out var old) ? TaskEtag.Create(old) : null, TaskEtag.Create(plan.After[id]))).ToArray(),
            ProtectedExecution = new(false, invariants), Guard = new(1, plan.SourceManifestHash, "")
        };
        payload = payload with { Guard = payload.Guard with { EffectHash = ComputeEffectHash(payload) } };
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload, Json)) > MaximumWitnessBytes)
            throw new CliException("Full preview exceeds 32 MiB; split independent intentions into separate applications.", kind: "previewTooLarge");
        return payload;

        void Add(string taskId, string path, JToken before, JToken after, string origin, IEnumerable<string> operations,
            IEnumerable<string>? reasons = null, TaskApplicationPreviewEdge? edge = null) => changes.Add(new()
        {
            TaskId = taskId, Path = path, Before = Element(before), After = Element(after), Origin = origin,
            OperationIds = operations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ReasonTaskIds = (reasons ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Cause = origin == "derived" ? "graphNormalization" : null, CanonicalEdge = edge
        });
    }

    public static TaskApplicationError? CheckGuard(TaskApplicationPreviewPayload saved, TaskApplicationPlan plan,
        TaskApplicationRequest request, string requestHash, string sourceKey)
    {
        try
        {
            if (saved.SourceKey != sourceKey || saved.RequestHash != requestHash || saved.Guard.SourceManifestHash != plan.SourceManifestHash)
                return Stale("The task source or request differs from the reviewed preview.");
            var fresh = Create(plan, request, requestHash, sourceKey);
            return saved.Contracts != fresh.Contracts || saved.Guard.EffectHash != fresh.Guard.EffectHash
                ? Stale("The staged semantic effect differs from the reviewed preview.") : null;
        }
        catch (CliException ex)
        {
            return new TaskApplicationError { Kind = ex.Kind == "previewTooLarge" ? TaskApplicationErrorKind.PreviewTooLarge : TaskApplicationErrorKind.PreviewStale, Message = ex.Message };
        }
    }

    public static TaskApplicationPreviewPayload ValidateWitness(string json, string applicationId, string requestHash, string sourceKey)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaximumWitnessBytes) throw Invalid("Preview witness exceeds 32 MiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            RejectDuplicateProperties(document.RootElement);
            var root = document.RootElement;
            RequireProperties(root, ["success", "mode", "applicationId", "requestHash", "didMutate", "changedTaskIds", "createdTaskIds", "operationResults", "receiptWritten", "validation", "authoritativeTasks", "preview"],
                ["success", "mode", "applicationId", "requestHash", "didMutate", "changedTaskIds", "createdTaskIds", "operationResults", "receiptWritten", "authoritativeTasks", "preview"]);
            if (!root.GetProperty("success").GetBoolean() || root.GetProperty("mode").GetString() != "preview" ||
                root.GetProperty("didMutate").GetBoolean() || root.GetProperty("receiptWritten").GetBoolean() ||
                root.GetProperty("applicationId").GetString() != applicationId || root.GetProperty("requestHash").GetString() != requestHash)
                throw Invalid("Witness must be the complete successful preview response for this application.");
            var payloadElement = root.GetProperty("preview");
            RequireProperties(payloadElement, ["previewVersion", "requestHash", "sourceKey", "evaluatedAt", "complete", "contracts", "changes", "affectedTaskIds", "storedChangedTaskIds", "createdTaskIds", "etags", "protectedExecution", "guard"]);
            RequireProperties(payloadElement.GetProperty("contracts"), ["application", "projection", "availability", "timeZone"]);
            RequireProperties(payloadElement.GetProperty("guard"), ["version", "sourceManifestHash", "effectHash"]);
            RequireProperties(payloadElement.GetProperty("protectedExecution"), ["changed", "invariants"]);
            foreach (var invariant in payloadElement.GetProperty("protectedExecution").GetProperty("invariants").EnumerateArray())
                RequireProperties(invariant, ["taskId", "executionHash", "descriptionSegmentHash"]);
            foreach (var etag in payloadElement.GetProperty("etags").EnumerateArray())
                RequireProperties(etag, ["taskId", "beforeEtag", "predictedAfterEtag"]);
            foreach (var change in payloadElement.GetProperty("changes").EnumerateArray())
            {
                RequireProperties(change, ["taskId", "path", "before", "after", "origin", "operationIds", "reasonTaskIds", "cause", "canonicalEdge"],
                    ["taskId", "path", "before", "after", "origin", "operationIds", "reasonTaskIds"]);
                if (change.TryGetProperty("canonicalEdge", out var edge)) RequireProperties(edge, ["kind", "fromTaskId", "toTaskId"]);
            }
            var payload = payloadElement.Deserialize<TaskApplicationPreviewPayload>(Json) ?? throw Invalid("Missing preview payload.");
            ValidatePayload(payload, requestHash, sourceKey);
            var changed = ReadIds(root.GetProperty("changedTaskIds"));
            var created = ReadIds(root.GetProperty("createdTaskIds"));
            if (!created.ToHashSet(StringComparer.Ordinal).SetEquals(payload.CreatedTaskIds) ||
                !changed.ToHashSet(StringComparer.Ordinal).SetEquals(payload.StoredChangedTaskIds))
                throw Invalid("Witness result IDs disagree with the semantic preview.");
            if (root.GetProperty("authoritativeTasks").GetArrayLength() != 0)
                throw Invalid("A preview witness cannot contain authoritative committed tasks.");
            if (!string.Equals(ComputeEffectHash(payload), payload.Guard.EffectHash, StringComparison.Ordinal))
                throw Invalid("Preview body does not match its effect hash.");
            return payload;
        }
        catch (CliException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NullReferenceException)
        { throw Invalid("Invalid preview witness: " + ex.Message); }
    }

    public static string ComputeEffectHash(TaskApplicationPreviewPayload payload) => Hash(JToken.Parse(JsonSerializer.Serialize(new
    {
        payload.PreviewVersion, payload.RequestHash, payload.SourceKey, payload.Contracts, payload.Complete,
        changes = payload.Changes.OrderBy(change => change.TaskId, StringComparer.Ordinal).ThenBy(change => change.Path, StringComparer.Ordinal),
        affectedTaskIds = payload.AffectedTaskIds.Order(StringComparer.Ordinal), storedChangedTaskIds = payload.StoredChangedTaskIds.Order(StringComparer.Ordinal), createdTaskIds = payload.CreatedTaskIds.Order(StringComparer.Ordinal),
        protectedExecution = new { payload.ProtectedExecution.Changed, invariants = payload.ProtectedExecution.Invariants.OrderBy(item => item.TaskId, StringComparer.Ordinal) }
    }, Json)));

    public static string RenderText(TaskApplicationPreviewPayload payload)
    {
        var text = new StringBuilder().Append("Preview: request ").Append(payload.RequestHash).AppendLine();
        foreach (var (origin, label) in new[] { ("explicit", "Явные изменения"), ("derived", "Производные изменения"), ("system", "Системные изменения") })
        {
            var changes = payload.Changes.Where(change => change.Origin == origin).ToArray();
            if (changes.Length == 0) continue;
            text.Append(label).AppendLine(":");
            foreach (var change in changes)
            {
                text.Append("  ").Append(ControlSafe(change.TaskId)).Append(' ').Append(ControlSafe(change.Path)).AppendLine();
                if (change.Before.ValueKind == JsonValueKind.String && change.After.ValueKind == JsonValueKind.String &&
                    (change.Before.GetString()!.Length > 160 || change.After.GetString()!.Length > 160 || change.Before.GetString()!.Contains('\n') || change.After.GetString()!.Contains('\n')))
                {
                    foreach (var line in change.Before.GetString()!.Split('\n')) text.Append("    - ").AppendLine(ControlSafe(line));
                    foreach (var line in change.After.GetString()!.Split('\n')) text.Append("    + ").AppendLine(ControlSafe(line));
                }
                else text.Append("    ").Append(ControlSafe(change.Before.GetRawText())).Append(" → ").AppendLine(ControlSafe(change.After.GetRawText()));
                if (change.ReasonTaskIds.Count > 0) text.Append("    Причины: ").AppendLine(string.Join(", ", change.ReasonTaskIds.Select(ControlSafe)));
            }
        }
        text.Append("Затронутые задачи: ").AppendLine(string.Join(", ", payload.AffectedTaskIds.Select(ControlSafe)));
        text.AppendLine("generatedAtApply: applicationClock — время применения; nextUpdated — прежнее время + 1 секунда, если оно не раньше времени применения, иначе время применения.");
        text.AppendLine("Execution state: сохранён. Полный diff: да. Задачи не записаны.");
        text.Append("Полный machine witness: сохраните вывод той же команды с --format json; этот текст не является guard-файлом.");
        return text.ToString();
    }

    private static string ControlSafe(string value) => NightAgentSearch.EscapeText(value);

    private static void ValidatePayload(TaskApplicationPreviewPayload payload, string requestHash, string sourceKey)
    {
        if (payload.PreviewVersion != 1 || !payload.Complete || payload.RequestHash != requestHash || payload.SourceKey != sourceKey ||
            payload.Contracts != new TaskApplicationPreviewContracts(1, 1, 1, TimeZoneInfo.Local.Id) ||
            payload.Guard.Version != 1 || !IsHash(payload.Guard.SourceManifestHash) || !IsHash(payload.Guard.EffectHash) ||
            !IsHash(payload.RequestHash) || !IsHash(payload.SourceKey) || payload.EvaluatedAt == default || payload.ProtectedExecution.Changed)
            throw Invalid("Unsupported, incomplete, or mismatched preview contract.");
        ValidateIds(payload.AffectedTaskIds); ValidateIds(payload.StoredChangedTaskIds); ValidateIds(payload.CreatedTaskIds);
        var keys = new HashSet<(string, string)>();
        foreach (var change in payload.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.TaskId) || !change.Path.StartsWith('/') || !keys.Add((change.TaskId, change.Path)) ||
                change.Origin is not ("explicit" or "derived" or "system") ||
                change.Before.ValueKind == JsonValueKind.Undefined || change.After.ValueKind == JsonValueKind.Undefined)
                throw Invalid("Duplicate or invalid preview change.");
            ValidateIds(change.OperationIds); ValidateIds(change.ReasonTaskIds);
            ValidatePlaceholders(change.Before, change.Path, after: false);
            ValidatePlaceholders(change.After, change.Path, after: true);
        }
        if (!payload.AffectedTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(payload.Changes.Select(change => change.TaskId)) ||
            payload.StoredChangedTaskIds.Any(id => !payload.AffectedTaskIds.Contains(id, StringComparer.Ordinal)) ||
            payload.CreatedTaskIds.Any(id => !payload.Changes.Any(change => change.TaskId == id && change.Path == "/" && change.Before.ValueKind == JsonValueKind.Null)))
            throw Invalid("Preview affected/created IDs disagree with its changes.");
        ValidateIds(payload.Etags.Select(item => item.TaskId).ToArray());
        ValidateIds(payload.ProtectedExecution.Invariants.Select(item => item.TaskId).ToArray());
        if (!payload.AffectedTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(payload.Etags.Select(item => item.TaskId)) ||
            !payload.AffectedTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(payload.ProtectedExecution.Invariants.Select(item => item.TaskId)) ||
            payload.Etags.Any(item => item.BeforeEtag != null && !IsHash(item.BeforeEtag) || !IsHash(item.PredictedAfterEtag)) ||
            payload.ProtectedExecution.Invariants.Any(item => !IsHash(item.ExecutionHash) || !IsHash(item.DescriptionSegmentHash)))
            throw Invalid("Invalid preview ETags or protected execution invariants.");
    }

    private static void ValidatePlaceholders(JsonElement element, string path, bool after)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("generatedAtApply", out var marker))
            {
                RequireProperties(element, ["generatedAtApply", "rule"]);
                var rule = element.GetProperty("rule").GetString();
                var allowedRule = path == "/details/updatedDateTime" ? "nextUpdated"
                    : path is "/details/createdDateTime" or "/details/unlockedDateTime" or "/history/changedAt" ? "applicationClock" : null;
                if (!after || marker.ValueKind != JsonValueKind.True || rule != allowedRule || allowedRule == null)
                    throw Invalid("Invalid generated timestamp placeholder.");
                return;
            }
            foreach (var property in element.EnumerateObject()) ValidatePlaceholders(property.Value, path.TrimEnd('/') + "/" + property.Name, after);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidatePlaceholders(item, path, after);
    }

    private static JObject Project(TaskItem task, TaskItem? before, bool normalizeGenerated)
    {
        if (!AgentExecutionDescriptionRenderer.TryRemove(task.Description, out var userText, out var error)) throw Invalid(error ?? "Description marker is malformed.");
        var details = new JObject
        {
            ["id"] = task.Id, ["userId"] = task.UserId, ["title"] = task.Title, ["descriptionUserText"] = task.Description == null ? null : userText,
            ["status"] = task.Status.ToString(), ["isCanBeCompleted"] = task.IsCanBeCompleted,
            ["createdDateTime"] = Date(task.CreatedDateTime), ["updatedDateTime"] = Date(task.UpdatedDateTime),
            ["unlockedDateTime"] = Date(task.UnlockedDateTime), ["plannedBeginDateTime"] = Date(task.PlannedBeginDateTime),
            ["plannedEndDateTime"] = Date(task.PlannedEndDateTime), ["plannedDuration"] = task.PlannedDuration.HasValue ? XmlConvert.ToString(task.PlannedDuration.Value) : null,
            ["importance"] = task.Importance, ["wanted"] = task.Wanted, ["version"] = task.Version,
            ["repeater"] = task.Repeater == null ? JValue.CreateNull() : JToken.FromObject(task.Repeater),
            ["unknownFieldsHash"] = Hash(task.ExtensionData == null ? new JObject() : JObject.FromObject(task.ExtensionData))
        };
        if (normalizeGenerated)
        {
            if (before == null) details["createdDateTime"] = Generated("applicationClock");
            if (task.UpdatedDateTime.HasValue && (before == null || !ExactDate(task.UpdatedDateTime, before.UpdatedDateTime))) details["updatedDateTime"] = Generated("nextUpdated");
            if (task.UnlockedDateTime.HasValue && (before == null || !ExactDate(task.UnlockedDateTime, before.UnlockedDateTime))) details["unlockedDateTime"] = Generated("applicationClock");
        }
        var history = new JArray(task.StatusHistory.Select((entry, index) => new JObject
        {
            ["status"] = entry.Status.ToString(), ["author"] = entry.Author,
            ["changedAt"] = normalizeGenerated && index >= (before?.StatusHistory.Count ?? 0) ? Generated("applicationClock") : Date(entry.ChangedAt)
        }));
        return new JObject
        {
            ["details"] = details,
            ["criteria"] = new JObject(task.CompletionCriteria.OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new JProperty(item.Id, new JObject { ["id"] = item.Id, ["text"] = item.Text, ["isSatisfied"] = item.IsSatisfied }))),
            ["history"] = history,
            ["storedRelations"] = new JObject
            {
                ["contains"] = new JArray(task.ContainsTasks.Order(StringComparer.Ordinal)), ["parents"] = new JArray(task.ParentTasks.Order(StringComparer.Ordinal)),
                ["blocks"] = new JArray(task.BlocksTasks.Order(StringComparer.Ordinal)), ["blockedBy"] = new JArray(task.BlockedByTasks.Order(StringComparer.Ordinal))
            }
        };
    }

    private static JObject Availability(TaskAvailabilityAnalysis analysis) => new()
    {
        ["isCanBeCompleted"] = analysis.IsCanBeCompleted, ["canStart"] = analysis.CanStart, ["canComplete"] = analysis.CanComplete,
        ["completionCriteriaSatisfied"] = analysis.CompletionCriteriaSatisfied, ["plannedBeginIsFuture"] = analysis.PlannedBeginIsFuture,
        ["reasons"] = new JArray(analysis.Reasons.OrderBy(reason => reason.Kind).ThenBy(reason => reason.SubjectId, StringComparer.Ordinal)
            .ThenBy(reason => reason.SourceTaskId, StringComparer.Ordinal).ThenBy(reason => reason.CriterionId, StringComparer.Ordinal)
            .Select(reason => new JObject { ["kind"] = reason.Kind.ToString(), ["subjectId"] = reason.SubjectId, ["subjectTitle"] = reason.SubjectTitle,
                ["subjectStatus"] = reason.SubjectStatus?.ToString(), ["sourceTaskId"] = reason.SourceTaskId, ["sourceTaskTitle"] = reason.SourceTaskTitle,
                ["criterionId"] = reason.CriterionId, ["details"] = reason.Details }))
    };

    private static (string ExecutionHash, string DescriptionSegmentHash) Protection(TaskItem task)
    {
        if (!AgentExecutionDescriptionRenderer.TryRemove(task.Description, out var userText, out var error)) throw Invalid(error ?? "Description marker is malformed.");
        // Rendered segments are protected by their exact text, while permitted user text remains diffable.
        var text = task.Description ?? "";
        var start = text.IndexOf(AgentExecutionDescriptionRenderer.MarkerStart, StringComparison.Ordinal);
        var end = text.IndexOf(AgentExecutionDescriptionRenderer.MarkerEnd, StringComparison.Ordinal);
        var segment = start < 0 ? "" : text[start..(end + AgentExecutionDescriptionRenderer.MarkerEnd.Length)];
        return (Hash(task.AgentExecution == null ? JValue.CreateNull() : JToken.FromObject(task.AgentExecution)), Hash(new JValue(segment)));
    }

    private static IEnumerable<string> ExplicitOperations(TaskApplicationRequest request, string id, string section, string property) =>
        request.Operations.Where(operation => operation.TaskId == id && (section == "criteria" ? operation.CriterionId == property
            : operation.Field == property || property == "status" && operation.Kind == TaskApplicationOperationKind.SetStatus)).Select(operation => operation.OperationId);
    private static IEnumerable<string> RelationOperations(TaskApplicationRequest request, TaskApplicationPreviewEdge edge) =>
        request.Operations.Where(operation => operation.Relation == edge.Kind && operation.FromTaskId == edge.FromTaskId && operation.ToTaskId == edge.ToTaskId ||
            edge.Kind == "contains" && operation.Kind == TaskApplicationOperationKind.CreateTask && operation.NewTaskId == edge.ToTaskId && operation.ParentIds?.Contains(edge.FromTaskId, StringComparer.Ordinal) == true)
            .Select(operation => operation.OperationId);
    private static IEnumerable<(string Kind, bool Reverse, IReadOnlyList<string> Before, IReadOnlyList<string> After)> Relations(TaskItem? before, TaskItem after)
    {
        yield return ("contains", false, before?.ContainsTasks ?? [], after.ContainsTasks);
        yield return ("contains", true, before?.ParentTasks ?? [], after.ParentTasks);
        yield return ("blocks", false, before?.BlocksTasks ?? [], after.BlocksTasks);
        yield return ("blocks", true, before?.BlockedByTasks ?? [], after.BlockedByTasks);
    }
    private static IEnumerable<string> ReasonIds(params TaskAvailabilityAnalysis[] analyses) => analyses.SelectMany(analysis => analysis.Reasons)
        .SelectMany(reason => new[] { reason.SubjectId, reason.SourceTaskId }).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!);
    private static bool ExactDate(DateTimeOffset? left, DateTimeOffset? right) => left.HasValue ? right.HasValue && left.Value.EqualsExact(right.Value) : !right.HasValue;
    private static JToken Date(DateTimeOffset? date) => date.HasValue ? new JValue(date.Value.ToString("O")) : JValue.CreateNull();
    private static JObject Generated(string rule) => new() { ["generatedAtApply"] = true, ["rule"] = rule };
    private static JsonElement Element(JToken token) { using var doc = JsonDocument.Parse(token.ToString(Newtonsoft.Json.Formatting.None)); return doc.RootElement.Clone(); }
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static bool IsHash(string value) => value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) && value[7..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(JToken token) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(token)))).ToLowerInvariant();
    private static string Canonical(JToken token) => token switch
    {
        JObject obj => "{" + string.Join(",", obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => JsonSerializer.Serialize(property.Name) + ":" + Canonical(property.Value))) + "}",
        JArray array => "[" + string.Join(",", array.Select(Canonical)) + "]", _ => token.ToString(Newtonsoft.Json.Formatting.None)
    };
    private static TaskApplicationError Stale(string message) => new() { Kind = TaskApplicationErrorKind.PreviewStale, Message = message };
    private static CliException Invalid(string message) => new(message, exitCode: 1, kind: "previewInvalid");
    private static void ValidateIds(IReadOnlyList<string> ids)
    { if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw Invalid("Duplicate or empty identifiers in preview."); }
    private static string[] ReadIds(JsonElement element) { var ids = element.EnumerateArray().Select(item => item.GetString()!).ToArray(); ValidateIds(ids); return ids; }
    private static void RequireProperties(JsonElement element, string[] allowed, string[]? required = null)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)) ||
            (required ?? allowed).Any(name => !element.TryGetProperty(name, out _))) throw Invalid("Missing or unknown witness properties.");
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw Invalid("Duplicate JSON property in witness."); RejectDuplicateProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
