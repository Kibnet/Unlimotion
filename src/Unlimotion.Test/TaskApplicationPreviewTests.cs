using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskApplicationPreviewTests
{
    [Test]
    public async Task Guard_DetectsBypassWriterAfterEffectCheck_AndPreservesExternalBytes()
    {
        using var source = new Source();
        var task = Task("race"); await source.Save(task);
        var request = Request([task], Title("race", "Reviewed"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var path = Path.Combine(source.Path, "race");
        var external = JObject.Parse(await File.ReadAllTextAsync(path));
        external["Title"] = "External writer";
        var externalText = external.ToString();
        var applied = await source.Service.TryApplyAsync(request, plan =>
        {
            var error = TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey);
            File.WriteAllText(path, externalText);
            return error;
        });
        await Assert.That(applied.Success).IsFalse();
        await Assert.That(applied.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.OutcomeUnknown);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(externalText);
    }

    [Test]
    public async Task Guard_RejectsClockOnlyChangeInReviewedBusinessEffect()
    {
        using var source = new Source();
        var task = Task("clock"); await source.Save(task);
        var planned = DateTimeOffset.UtcNow.AddHours(1);
        var request = Request([task], new TaskApplicationOperation
        {
            OperationId = "schedule", Kind = TaskApplicationOperationKind.SetField, TaskId = "clock",
            Field = "plannedBeginDateTime", Value = planned.ToString("O")
        });
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var later = result.Plan! with { EvaluatedAt = planned.AddSeconds(1) };
        await Assert.That(TaskApplicationPreview.CheckGuard(preview, later, request, RequestHash, SourceKey)!.Kind)
            .IsEqualTo(TaskApplicationErrorKind.PreviewStale);
        await Assert.That(later.SourceManifestHash).IsEqualTo(result.Plan!.SourceManifestHash);
    }

    [Test]
    public async Task FullDiff_PreservesLongStringsNullsCriteriaAndBusinessDates()
    {
        using var source = new Source();
        var task = Task("full"); task.Description = "Before\n" + new string('x', 4096);
        task.PlannedDuration = TimeSpan.FromHours(1); await source.Save(task);
        var afterText = "After\n" + new string('y', 4096);
        var request = Request([task],
            new() { OperationId = "description", Kind = TaskApplicationOperationKind.SetField, TaskId = task.Id, Field = "descriptionUserText", Value = afterText },
            new() { OperationId = "duration", Kind = TaskApplicationOperationKind.ClearField, TaskId = task.Id, Field = "plannedDuration" },
            new() { OperationId = "criterion", Kind = TaskApplicationOperationKind.AddCriterion, TaskId = task.Id, CriterionId = "check", Text = "Проверить результат", IsSatisfied = false },
            new() { OperationId = "begin", Kind = TaskApplicationOperationKind.SetField, TaskId = task.Id, Field = "plannedBeginDateTime", Value = "2026-01-03T12:00:00+03:00" });
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var description = preview.Changes.Single(change => change.Path == "/details/descriptionUserText");
        await Assert.That(description.Before.GetString()).IsEqualTo(task.Description);
        await Assert.That(description.After.GetString()).IsEqualTo(afterText);
        await Assert.That(preview.Changes.Single(change => change.Path == "/details/plannedDuration").After.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(preview.Changes.Single(change => change.Path == "/criteria/check").After.GetProperty("text").GetString()).IsEqualTo("Проверить результат");
        await Assert.That(preview.Changes.Single(change => change.Path == "/details/plannedBeginDateTime").After.ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(TaskApplicationPreview.RenderText(preview)).Contains(new string('y', 4096));
        await Assert.That(TaskApplicationPreview.RenderText(preview)).Contains("+ After");
        var applied = await source.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey));
        await Assert.That(applied.Success).IsTrue();
        var persisted = (await source.Storage.ReadObservationAsync()).Graph.TasksById[task.Id];
        await Assert.That(persisted.Description).IsEqualTo(afterText);
        await Assert.That(persisted.PlannedDuration).IsNull();
        await Assert.That(persisted.CompletionCriteria.Single().Text).IsEqualTo("Проверить результат");
    }

    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private const string RequestHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceKey = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task FullPreview_IncludesGlobalDerivedChangesAndBothRelationSides_WithoutWriting()
    {
        using var source = new Source();
        var goal = Task("goal"); var work = Task("work"); var unrelated = Task("unrelated");
        unrelated.IsCanBeCompleted = false;
        await source.Save(goal, work, unrelated);
        var request = Request([goal, work], new TaskApplicationOperation
        { OperationId = "relation", Kind = TaskApplicationOperationKind.AddRelation, Relation = "contains", FromTaskId = "goal", ToTaskId = "work" });
        var bytes = source.Bytes();
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        await Assert.That(preview.Changes.Any(c => c.TaskId == "goal" && c.Path == "/relations/contains/work" && c.Origin == "explicit")).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == "work" && c.Path == "/storedRelations/parents/goal" && c.CanonicalEdge?.FromTaskId == "goal")).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == "goal" && c.Path == "/availability/canStart" && c.After.GetBoolean() == false)).IsTrue();
        await Assert.That(preview.Changes.Any(c => c.TaskId == "unrelated" && c.Path == "/details/isCanBeCompleted" && c.Origin == "derived" && c.OperationIds.Count == 0)).IsTrue();
        await Assert.That(source.Bytes()).IsEquivalentTo(bytes);
        await Assert.That(Directory.Exists(Path.Combine(source.Path, ".unlimotion.transactions"))).IsFalse();
        var witness = TaskApplicationPreview.ValidateWitness(Witness(request, result, preview), request.ApplicationId, RequestHash, SourceKey);
        await Assert.That(witness.Guard.EffectHash).IsEqualTo(preview.Guard.EffectHash);
    }

    [Test]
    public async Task Guard_AcceptsLaterGeneratedTimestamps_AndReconcilesRepeat()
    {
        using var source = new Source();
        var task = Task("task"); await source.Save(task);
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var applied = await source.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey));
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(applied.DidMutate).IsTrue();
        var bytes = source.Bytes();
        var repeat = await source.Service.TryApplyAsync(request, _ => throw new InvalidOperationException("Reconciliation must precede a new guard."));
        await Assert.That(repeat.Mode).IsEqualTo("alreadyApplied");
        await Assert.That(source.Bytes()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task Guard_RejectsIndependentByteOnlyDrift_BeforeTargetWrite()
    {
        using var source = new Source();
        var task = Task("task"); var unrelated = Task("unrelated"); await source.Save(task, unrelated);
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        await File.AppendAllTextAsync(Path.Combine(source.Path, "unrelated"), "\n ");
        var bytes = source.Bytes();
        var applied = await source.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey));
        await Assert.That(applied.Success).IsFalse();
        await Assert.That(applied.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.PreviewStale);
        await Assert.That(source.Bytes()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task Witness_RejectsRemovedDerivedChange_WithOriginalHash()
    {
        using var source = new Source();
        var task = Task("task"); task.IsCanBeCompleted = false; await source.Save(task);
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var witness = JObject.Parse(Witness(request, result, preview));
        var changes = (JArray)witness["preview"]!["changes"]!;
        changes.First(change => (string?)change["origin"] == "derived").Remove();
        await AssertInvalid(witness.ToString(), request.ApplicationId);
    }

    [Test]
    public async Task Witness_RejectsDuplicateChanges_UnknownMembers_AndBusinessDatePlaceholder()
    {
        using var source = new Source();
        var task = Task("task"); await source.Save(task);
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var valid = Witness(request, result, preview);
        var duplicate = JObject.Parse(valid);
        var changes = (JArray)duplicate["preview"]!["changes"]!;
        changes.Add(changes[0]!.DeepClone());
        await AssertInvalid(duplicate.ToString(), request.ApplicationId);
        var unknown = JObject.Parse(valid); unknown["preview"]!["unexpected"] = true;
        await AssertInvalid(unknown.ToString(), request.ApplicationId);
        var changed = preview.Changes.Select(change => change.Path == "/details/title"
            ? change with { Path = "/details/plannedBeginDateTime", After = JsonDocument.Parse("{\"generatedAtApply\":true,\"rule\":\"applicationClock\"}").RootElement.Clone() } : change).ToArray();
        var malformed = preview with { Changes = changed };
        malformed = malformed with { Guard = malformed.Guard with { EffectHash = TaskApplicationPreview.ComputeEffectHash(malformed) } };
        await AssertInvalid(Witness(request, result, malformed), request.ApplicationId);
    }

    [Test]
    public async Task VolatileUntouchedLegacy_DoesNotPoisonRawGuard()
    {
        using var source = new Source();
        var task = Task("task"); var legacy = Task("legacy"); await source.Save(task, legacy);
        source.RemoveCreatedDateTime("legacy");
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Plan!.UnstableTaskIds.Contains("legacy")).IsTrue();
        await Assert.That(result.ChangedTaskIds.Contains("legacy")).IsFalse();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var applied = await source.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey));
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(JObject.Parse(File.ReadAllText(Path.Combine(source.Path, "legacy"))).ContainsKey("CreatedDateTime")).IsFalse();
    }

    [Test]
    public async Task VolatileExplicitPrecondition_AndDerivedWrite_AreRejectedSeparately()
    {
        using var source = new Source();
        var task = Task("task"); var legacy = Task("legacy"); legacy.IsCanBeCompleted = false; await source.Save(task, legacy);
        source.RemoveCreatedDateTime("legacy");
        var derived = await source.Service.PreviewPlanAsync(Request([task], Title("task", "Reviewed title")));
        await Assert.That(derived.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.UnstableSource);
        await Assert.That(derived.Error.TaskId).IsEqualTo("legacy");
        var explicitResult = await source.Service.PreviewPlanAsync(Request([legacy], Title("legacy", "Reviewed title")));
        await Assert.That(explicitResult.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.UnstablePrecondition);
    }

    [Test]
    public async Task ProtectedDescription_IsNotDisclosed_AndUserTextRemovalCannotHideMutation()
    {
        using var source = new Source();
        var task = Task("task");
        task.Description = "User text" + AgentExecutionDescriptionRenderer.MarkerStart + "private execution body" + AgentExecutionDescriptionRenderer.MarkerEnd;
        await source.Save(task);
        var request = Request([task], Title("task", "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        await Assert.That(Witness(request, result, preview).Contains("private execution body", StringComparison.Ordinal)).IsFalse();
        var remove = Request([task], new TaskApplicationOperation { OperationId = "description", Kind = TaskApplicationOperationKind.SetField, TaskId = "task", Field = "descriptionUserText", Value = "Replacement" });
        var removed = await source.Service.PreviewPlanAsync(remove);
        var rejected = false;
        try { TaskApplicationPreview.Create(removed.Plan!, remove, RequestHash, SourceKey); }
        catch (CliException ex) { rejected = ex.Kind == "previewInvalid"; }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task CreatedNodePreview_ShowsFinalStateAndTypedTimes_WithoutIntermediateTitle()
    {
        using var source = new Source();
        var request = Request([], new TaskApplicationOperation { OperationId = "create", Kind = TaskApplicationOperationKind.CreateTask, NewTaskId = "new", Title = "Intermediate" }, Title("new", "Final"));
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var create = preview.Changes.Single(change => change.TaskId == "new" && change.Path == "/");
        await Assert.That(preview.Contracts.Projection).IsEqualTo(2);
        await Assert.That(create.Before.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(create.After.GetProperty("details").GetProperty("areaIds").GetArrayLength()).IsEqualTo(0);
        await Assert.That(create.After.GetProperty("details").EnumerateObject().Any(p => p.Name.Equals("isGoal", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(create.After.GetProperty("details").GetProperty("title").GetString()).IsEqualTo("Final");
        await Assert.That(create.After.GetProperty("details").GetProperty("createdDateTime").GetProperty("generatedAtApply").GetBoolean()).IsTrue();
        TaskApplicationPreview.ValidateWitness(Witness(request, result, preview), request.ApplicationId, RequestHash, SourceKey);
        var applied = await source.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, RequestHash, SourceKey));
        await Assert.That(applied.Success).IsTrue();
    }

    [Test]
    public async Task AreaProjection_PreservesOrderDuplicatesAndNormalizesNull_WithoutChangingPlanOrSource()
    {
        using var source = new Source();
        var task = Task("areas"); task.AreaIds = ["area-b", "area-a", "area-b"];
        await source.Save(task);
        var request = Request([task], Title(task.Id, "Reviewed title"));
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var plan = result.Plan!;
        var bytes = source.Bytes();
        var titleOnly = TaskApplicationPreview.Create(plan, request, RequestHash, SourceKey);
        await Assert.That(titleOnly.Changes.Any(c => c.Path == "/details/areaIds")).IsFalse();

        // Area assignment is not a CLI operation. Controlled staging endpoints exercise
        // projection and hashing without adding such an operation or mutating the real plan.
        foreach (var values in new System.Collections.Generic.List<string>?[] { ["area-a", "area-b", "area-a"], null })
        {
            var staged = TaskItemSnapshot.Clone(plan.After[task.Id]);
            staged.AreaIds = values!;
            var after = plan.After.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            after[task.Id] = staged;
            var controlled = plan with { After = after, AfterExplicit = after };
            var preview = TaskApplicationPreview.Create(controlled, request, RequestHash, SourceKey);
            var change = preview.Changes.Single(c => c.Path == "/details/areaIds");
            await Assert.That(change.Before.GetRawText()).IsEqualTo("[\"area-b\",\"area-a\",\"area-b\"]");
            await Assert.That(change.After.GetRawText()).IsEqualTo(values == null ? "[]" : "[\"area-a\",\"area-b\",\"area-a\"]");
            await Assert.That(preview.Guard.EffectHash).IsNotEqualTo(titleOnly.Guard.EffectHash);
            await Assert.That(TaskApplicationPreview.RenderText(preview)).Contains("/details/areaIds");
            TaskApplicationPreview.ValidateWitness(Witness(request, result, preview), request.ApplicationId, RequestHash, SourceKey);
            await Assert.That(ReferenceEquals(staged.AreaIds, values)).IsTrue();
        }
        await Assert.That(string.Join(",", plan.After[task.Id].AreaIds)).IsEqualTo("area-b,area-a,area-b");
        await Assert.That(string.Join(",", plan.Before[task.Id].AreaIds)).IsEqualTo("area-b,area-a,area-b");
        await Assert.That(source.Bytes()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task Witness_RejectsRehashedAreaShapesRetiredFieldsAndOldProjection()
    {
        using var source = new Source();
        var request = Request([], new TaskApplicationOperation
            { OperationId = "create", Kind = TaskApplicationOperationKind.CreateTask, NewTaskId = "new", Title = "Final" });
        var result = await source.Service.PreviewPlanAsync(request);
        await Assert.That(result.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(result.Plan!, request, RequestHash, SourceKey);
        var valid = JObject.Parse(Witness(request, result, preview));
        foreach (var value in new JToken?[] { null, JValue.CreateNull(), new JValue("area-a"), new JArray("area-a", 1), new JArray(JValue.CreateNull()) })
        {
            var invalid = (JObject)valid.DeepClone();
            var details = RootDetails(invalid);
            if (value == null) details.Remove("areaIds"); else details["areaIds"] = value.DeepClone();
            await AssertInvalid(Rehash(invalid), request.ApplicationId, value == null ? "include areaIds" : "array of strings");
        }
        foreach (var key in new[] { "isGoal", "IsGoal", "ISGOAL", "iSgOaL" })
        {
            var invalid = (JObject)valid.DeepClone(); RootDetails(invalid)[key] = false;
            await AssertInvalid(Rehash(invalid), request.ApplicationId, "retired goal field");
            invalid = (JObject)valid.DeepClone();
            ((JArray)invalid["preview"]!["changes"]!).Add(new JObject
            {
                ["taskId"] = "new", ["path"] = "/DETAILS/" + key + "/child", ["origin"] = "derived",
                ["before"] = false, ["after"] = true, ["operationIds"] = new JArray(), ["reasonTaskIds"] = new JArray()
            });
            await AssertInvalid(Rehash(invalid), request.ApplicationId, "retired goal field");
        }
        var old = (JObject)valid.DeepClone(); old["preview"]!["contracts"]!["projection"] = 1;
        await AssertInvalid(Rehash(old), request.ApplicationId, "preview contract");
        foreach (var side in new[] { "before", "after" })
        foreach (var shape in new JToken[] { JValue.CreateNull(), new JValue("area-a"), new JArray("area-a", 1), new JArray(JValue.CreateNull()) })
        {
            var invalid = (JObject)valid.DeepClone();
            var areaChange = new JObject
            {
                ["taskId"] = "new", ["path"] = "/details/areaIds", ["origin"] = "derived",
                ["before"] = new JArray(), ["after"] = new JArray("area-a"),
                ["operationIds"] = new JArray(), ["reasonTaskIds"] = new JArray()
            };
            areaChange[side] = shape.DeepClone(); ((JArray)invalid["preview"]!["changes"]!).Add(areaChange);
            await AssertInvalid(Rehash(invalid), request.ApplicationId, "array of strings");
        }
        var nested = (JObject)valid.DeepClone(); RootDetails(nested)["Custom"] = new JObject { ["IsGoal"] = true };
        TaskApplicationPreview.ValidateWitness(Rehash(nested), request.ApplicationId, RequestHash, SourceKey);
    }

    private static JObject RootDetails(JObject witness) => (JObject)((JArray)witness["preview"]!["changes"]!)
        .Single(c => (string?)c["path"] == "/")["after"]!["details"]!;
    private static string Rehash(JObject witness)
    {
        var payload = JsonSerializer.Deserialize<TaskApplicationPreviewPayload>(witness["preview"]!.ToString(), Json)!;
        witness["preview"]!["guard"]!["effectHash"] = TaskApplicationPreview.ComputeEffectHash(payload);
        return witness.ToString();
    }

    private static async Task AssertInvalid(string json, string applicationId, string? expectedMessage = null)
    {
        string? kind = null;
        string? message = null;
        try { TaskApplicationPreview.ValidateWitness(json, applicationId, RequestHash, SourceKey); }
        catch (CliException ex) { kind = ex.Kind; message = ex.Message; }
        await Assert.That(kind).IsEqualTo("previewInvalid");
        if (expectedMessage != null) await Assert.That(message).Contains(expectedMessage);
    }
    private static string Witness(TaskApplicationRequest request, TaskApplicationResult result, TaskApplicationPreviewPayload preview) =>
        JsonSerializer.Serialize(ApplicationCommandOutput.From(request.ApplicationId, RequestHash, result, false) with { Preview = preview }, Json);
    private static TaskApplicationOperation Title(string id, string title) => new() { OperationId = "title-" + id, Kind = TaskApplicationOperationKind.SetField, TaskId = id, Field = "title", Value = title };
    private static TaskApplicationRequest Request(TaskItem[] tasks, params TaskApplicationOperation[] operations) => new()
    {
        SchemaVersion = 1, ApplicationId = "test-application", Author = "reviewer", Reason = "Approved test proposal",
        ProposalRefs = [new("proposal", 1)], Preconditions = tasks.Select(task => new TaskApplicationPrecondition(task.Id, Etag(task), task.Status)).ToArray(), Operations = operations
    };
    private static TaskItem Task(string id)
    {
        var task = new TaskItem { Id = id, UserId = "owner", Title = id, Description = "", Status = DomainTaskStatus.Prepared, CreatedDateTime = Epoch,
            UpdatedDateTime = Epoch, UnlockedDateTime = Epoch, IsCanBeCompleted = true };
        task.EnsureStatusHistory("owner"); return task;
    }
    private static string Etag(TaskItem task)
    {
        static string Canonical(JToken token) => token switch
        {
            JObject obj => "{" + string.Join(",", obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
            JArray array => "[" + string.Join(",", array.Select(Canonical)) + "]", _ => token.ToString(Newtonsoft.Json.Formatting.None)
        };
        var canonical = Canonical(JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(TaskItemSnapshot.Clone(task))));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
    private sealed class Source : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-preview-" + Guid.NewGuid().ToString("N"));
        public FileTaskStorage Storage { get; }
        public TaskApplicationCommandService Service { get; }
        public Source() { Directory.CreateDirectory(Path); Storage = new(new FileTaskStorageOptions { Path = Path, UseDirectoryLock = true, PreserveUnknownJson = true }); Service = new(Storage, Etag); }
        public async Task Save(params TaskItem[] tasks) { foreach (var task in tasks) await Storage.Save(task); }
        public string[] Bytes() => Directory.GetFiles(Path).Where(file => !System.IO.Path.GetFileName(file).StartsWith('.')).Order(StringComparer.Ordinal).Select(file => System.IO.Path.GetFileName(file) + ":" + Convert.ToHexString(File.ReadAllBytes(file))).ToArray();
        public void RemoveCreatedDateTime(string id) { var file = System.IO.Path.Combine(Path, id); var json = JObject.Parse(File.ReadAllText(file)); json.Remove("CreatedDateTime"); File.WriteAllText(file, json.ToString()); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
