using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskApplicationPreviewReplayTests
{
    [Test]
    public async Task MachineDiff_ReplaysWholeFinalProjection_IncludingCreationAndUnrelatedNormalization()
    {
        using var source = new Source();
        var edited = CreateTask("edited", DomainTaskStatus.InProgress);
        edited.Description = "Before\nwith exact whitespace ";
        edited.PlannedDuration = TimeSpan.FromHours(1);
        edited.CompletionCriteria =
        [
            new() { Id = "keep/~", Text = "Old criterion", IsSatisfied = true },
            new() { Id = "remove/~", Text = "Remove this", IsSatisfied = true }
        ];
        var parent = CreateTask("old-parent");
        var child = CreateTask("old-child");
        var blocker = CreateTask("blocker");
        parent.ContainsTasks = [child.Id]; child.ParentTasks = [parent.Id];
        blocker.BlocksTasks = [child.Id]; child.BlockedByTasks = [blocker.Id];
        parent.IsCanBeCompleted = child.IsCanBeCompleted = false;
        parent.UnlockedDateTime = child.UnlockedDateTime = null;
        var unrelated = CreateTask("unrelated");
        unrelated.IsCanBeCompleted = false;
        unrelated.UnlockedDateTime = null;
        var untouched = CreateTask("untouched");
        foreach (var task in new[] { edited, parent, child, blocker, unrelated, untouched })
            await source.Storage.Save(task);

        var observed = (await source.Storage.ReadObservationAsync()).Graph.TasksById;
        var request = new TaskApplicationRequest
        {
            SchemaVersion = 1, ApplicationId = "projection-replay", Author = "reviewer", Reason = "Replay approved changes",
            ProposalRefs = [new("proposal", 1)],
            Preconditions = new[] { edited.Id, parent.Id, child.Id, blocker.Id }
                .Select(id => new TaskApplicationPrecondition(id, Etag(observed[id]), observed[id].Status)).ToArray(),
            Operations =
            [
                new() { OperationId = "title", Kind = TaskApplicationOperationKind.SetField, TaskId = edited.Id, Field = "title", Value = "Final title" },
                new() { OperationId = "description", Kind = TaskApplicationOperationKind.SetField, TaskId = edited.Id, Field = "descriptionUserText", Value = "After\nexact text " },
                new() { OperationId = "duration", Kind = TaskApplicationOperationKind.ClearField, TaskId = edited.Id, Field = "plannedDuration" },
                new() { OperationId = "replace", Kind = TaskApplicationOperationKind.ReplaceCriterion, TaskId = edited.Id, CriterionId = "keep/~", Text = "Revised criterion" },
                new() { OperationId = "remove", Kind = TaskApplicationOperationKind.RemoveCriterion, TaskId = edited.Id, CriterionId = "remove/~" },
                new() { OperationId = "add", Kind = TaskApplicationOperationKind.AddCriterion, TaskId = edited.Id, CriterionId = "new/~", Text = "New criterion", IsSatisfied = false },
                new() { OperationId = "detach", Kind = TaskApplicationOperationKind.RemoveRelation, Relation = "contains", FromTaskId = parent.Id, ToTaskId = child.Id },
                new() { OperationId = "unblock", Kind = TaskApplicationOperationKind.RemoveRelation, Relation = "blocks", FromTaskId = blocker.Id, ToTaskId = child.Id },
                new() { OperationId = "block", Kind = TaskApplicationOperationKind.AddRelation, Relation = "blocks", FromTaskId = blocker.Id, ToTaskId = edited.Id },
                new() { OperationId = "status", Kind = TaskApplicationOperationKind.SetStatus, TaskId = parent.Id, Status = DomainTaskStatus.NotReady },
                new() { OperationId = "create", Kind = TaskApplicationOperationKind.CreateTask, NewTaskId = "new-child", Title = "Intermediate", ParentIds = [edited.Id], Criteria = [new("created/~", "Created criterion", false)] },
                new() { OperationId = "new-title", Kind = TaskApplicationOperationKind.SetField, TaskId = "new-child", Field = "title", Value = "Created final title" }
            ]
        };
        var result = await new TaskApplicationCommandService(source.Storage, Etag).PreviewPlanAsync(request);
        Require(result.Success, result.Error?.Message ?? "Preview did not succeed.");
        var plan = result.Plan!;
        var preview = TaskApplicationPreview.Create(plan, request, "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

        // These establish the intended coverage; the replay below checks the entire graph, not selected values.
        await Assert.That(preview.Changes.Any(c => c.TaskId == unrelated.Id && c.Path == "/details/isCanBeCompleted" && c.Origin == "derived" && c.OperationIds.Count == 0)).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == edited.Id && c.Path == "/history" && c.Origin == "derived")).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == parent.Id && c.Path == "/history" && c.Origin == "explicit")).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == "new-child" && c.Path == "/")).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == untouched.Id)).IsFalse();

        var before = ProjectGraph(plan.Before, plan.Before, plan.EvaluatedAt, normalizeGenerated: false);
        var expected = ProjectGraph(plan.After, plan.Before, plan.EvaluatedAt, normalizeGenerated: true);
        var replayed = Replay(before, preview.Changes, plan);
        Require(JToken.DeepEquals(replayed, expected), $"Replayed public projection differs.\nExpected: {expected}\nActual: {replayed}");

        // A missing global normalization must be observable even when all explicit operations are intact.
        var incomplete = Replay(before, preview.Changes.Where(c => c.TaskId != unrelated.Id), plan);
        await Assert.That(JToken.DeepEquals(incomplete, expected)).IsFalse();
    }

    private static JObject Replay(JObject before, IEnumerable<TaskApplicationPreviewChange> changes, TaskApplicationPlan plan)
    {
        var replayed = (JObject)before.DeepClone();
        var beforeEdges = Edges(plan.Before);
        var afterEdges = Edges(plan.After);
        var describedEdges = new HashSet<TaskApplicationPreviewEdge>();
        foreach (var change in changes)
        {
            var oldValue = Parse(change.Before);
            var newValue = Parse(change.After);
            var initial = (JObject?)before[change.TaskId];
            var parts = change.Path.Split('/').Skip(1).Select(Unescape).ToArray();
            if (change.Path == "/")
            {
                Require(initial == null && replayed[change.TaskId] == null && oldValue.Type == JTokenType.Null,
                    "Root creation must have an absent original task.");
                Require(newValue is JObject, "Created task must be a complete object.");
                replayed[change.TaskId] = newValue.DeepClone();
                continue;
            }
            if (parts is ["relations", var kind, var target])
            {
                // Canonical edges describe the same graph changes as reciprocal stored memberships.
                // Validate them separately; applying them again would duplicate the stored mutation.
                var edge = new TaskApplicationPreviewEdge(kind, change.TaskId, target);
                Require(change.CanonicalEdge == edge && describedEdges.Add(edge), "Canonical edge identity is invalid or duplicated.");
                Require(oldValue.Type == JTokenType.Boolean && newValue.Type == JTokenType.Boolean, "Edge values must be booleans.");
                Require(oldValue.Value<bool>() == beforeEdges.Contains(edge) && newValue.Value<bool>() == afterEdges.Contains(edge),
                    "Canonical edge does not describe its before/final membership.");
                continue;
            }

            var current = (JObject?)replayed[change.TaskId] ?? throw new InvalidOperationException("Change references an absent task.");
            JToken expectedBefore;
            switch (parts)
            {
                case ["storedRelations", var relation, var member]:
                    expectedBefore = new JValue(((JArray?)initial?["storedRelations"]?[relation])?.Values<string>().Contains(member, StringComparer.Ordinal) ?? false);
                    var values = ((JArray)current["storedRelations"]![relation]!).Values<string>().Select(id => id!).ToHashSet(StringComparer.Ordinal);
                    Require(newValue.Type == JTokenType.Boolean, "Stored membership must be a boolean.");
                    if (newValue.Value<bool>()) values.Add(member); else values.Remove(member);
                    current["storedRelations"]![relation] = new JArray(values.Order(StringComparer.Ordinal));
                    break;
                case ["details" or "availability", var property]:
                    expectedBefore = initial?[parts[0]]?[property] ?? JValue.CreateNull();
                    current[parts[0]] ??= new JObject();
                    current[parts[0]]![property] = newValue.DeepClone();
                    break;
                case ["criteria", var criterion]:
                    expectedBefore = initial?["criteria"]?[criterion] ?? JValue.CreateNull();
                    var criteria = (JObject)current["criteria"]!;
                    if (newValue.Type == JTokenType.Null) criteria.Remove(criterion);
                    else criteria[criterion] = newValue.DeepClone();
                    break;
                case ["history"]:
                    expectedBefore = initial?["history"] ?? JValue.CreateNull();
                    Require(newValue is JArray, "History must be an array.");
                    current["history"] = newValue.DeepClone();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported machine diff path: {change.Path}");
            }
            // Every entry is original-before -> final-after. A created root already carries its final
            // stored relations, so membership changes are idempotent and must not use progressive before.
            Require(JToken.DeepEquals(oldValue, expectedBefore),
                $"Incorrect before value at {change.TaskId}{change.Path}.\nDiff before: {oldValue}\nProjection before: {expectedBefore}");
        }
        var changedEdges = new HashSet<TaskApplicationPreviewEdge>(beforeEdges);
        changedEdges.SymmetricExceptWith(afterEdges);
        Require(describedEdges.SetEquals(changedEdges), "Canonical edge diff omits or adds a graph change.");
        return replayed;
    }

    private static HashSet<TaskApplicationPreviewEdge> Edges(IReadOnlyDictionary<string, TaskItem> graph) => graph.Values
        .SelectMany(task => task.ContainsTasks.Select(id => new TaskApplicationPreviewEdge("contains", task.Id, id))
            .Concat(task.BlocksTasks.Select(id => new TaskApplicationPreviewEdge("blocks", task.Id, id))))
        .ToHashSet();

    private static JObject ProjectGraph(IReadOnlyDictionary<string, TaskItem> graph,
        IReadOnlyDictionary<string, TaskItem> originals, DateTimeOffset evaluatedAt, bool normalizeGenerated)
    {
        // The public snapshot DTO is a different shape. Reuse only endpoint formatting, including
        // the contract's generated-time placeholders; the change interpreter above is independent.
        var project = typeof(TaskApplicationPreview).GetMethod("Project", BindingFlags.NonPublic | BindingFlags.Static)!;
        var availability = typeof(TaskApplicationPreview).GetMethod("Availability", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rules = new TaskAvailabilityService(graph.Values, evaluatedAt);
        var result = new JObject();
        foreach (var (id, task) in graph)
        {
            originals.TryGetValue(id, out var original);
            var node = (JObject)project.Invoke(null, [task, original, normalizeGenerated])!;
            node["availability"] = (JObject)availability.Invoke(null, [rules.Analyze(task)])!;
            result[id] = node;
        }
        // Compare public JSON values on both sides of the invariant. The private formatter can
        // retain JValue(string-null) in nullable reason/detail fields; JSON writes those as null,
        // and the machine diff's JsonElement has already crossed that serialization boundary.
        // Round-trip every endpoint without date inference, preserving all fields and values.
        return (JObject)Parse(result.ToString(Formatting.None));
    }

    private static JToken Parse(JsonElement value) => Parse(value.GetRawText());

    private static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }

    private static string Unescape(string value) => value.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static TaskItem CreateTask(string id, DomainTaskStatus status = DomainTaskStatus.Prepared)
    {
        var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var task = new TaskItem { Id = id, UserId = "owner", Title = id, Description = "", Status = status,
            CreatedDateTime = epoch, UpdatedDateTime = epoch, UnlockedDateTime = epoch, IsCanBeCompleted = true };
        task.EnsureStatusHistory("owner");
        return task;
    }

    private static string Etag(TaskItem task)
    {
        var analyzer = new TaskAvailabilityAnalyzer([task]);
        return TaskSnapshotOutput.Create(task, analyzer.Analyze(task), analyzer, new HashSet<string>()).Etag;
    }

    private sealed class Source : IDisposable
    {
        private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unlimotion-preview-replay-" + Guid.NewGuid().ToString("N")));
        public FileTaskStorage Storage { get; }
        public Source()
        {
            Directory.CreateDirectory(_root);
            Storage = new(new FileTaskStorageOptions { Path = _root, UseDirectoryLock = true, PreserveUnknownJson = true });
        }
        public void Dispose()
        {
            var allowedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "unlimotion-preview-replay-");
            Require(_root.StartsWith(allowedPrefix, StringComparison.Ordinal), "Cleanup path is outside the fixture directory.");
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
