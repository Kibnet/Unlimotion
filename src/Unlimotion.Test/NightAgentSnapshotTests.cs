using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class NightAgentSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly string SourceKey = "sha256:" + new string('a', 64);

    [Test]
    public async Task NightContextKeepsEveryParentTerminalAncestorAndUpstreamBlockerWithoutRecursiveSiblingExpansion()
    {
        var root = Item("root", DomainTaskStatus.Archived);
        var a = Item("goal-a", DomainTaskStatus.Completed); var b = Item("goal-b", DomainTaskStatus.Archived);
        var work = Item("work"); var sibling = Item("sibling"); var deep = Item("deep-child");
        var blocker = Item("blocker", DomainTaskStatus.Completed); var upstream = Item("upstream", DomainTaskStatus.Completed);
        Contains(root, a); Contains(root, b); Contains(a, work); Contains(b, work); Contains(a, sibling); Contains(sibling, deep);
        Blocks(blocker, sibling); Blocks(upstream, blocker);
        var artifact = Capture([root, a, b, work, sibling, deep, blocker, upstream], Ids("work"));
        await Assert.That(artifact.Targets.Single().ContextIds.SequenceEqual(new[] { "blocker", "goal-a", "goal-b", "root", "sibling", "upstream", "work" })).IsTrue();
        await Assert.That(artifact.Nodes.Single(n => n.Id == "work").ParentTasks.SequenceEqual(new[] { "goal-a", "goal-b" })).IsTrue();
        await Assert.That(artifact.Nodes.Single(n => n.Id == "root").Task.Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(artifact.Nodes.Single(n => n.Id == "sibling").Edges.Single(e => e.To == "deep-child").OutsidePayload).IsTrue();
        await Assert.That(artifact.Nodes.Single(n => n.Id == "root").History!.Count).IsEqualTo(1);
        await Assert.That(artifact.Completeness.Complete).IsTrue();
    }

    [Test]
    public async Task MissingSelectionReportsAbsenceWithoutBroadeningAndTerminalStatusIsNotDeletion()
    {
        var tasks = new[] { Item("present", DomainTaskStatus.Completed) };
        var selection = new NightAgentSnapshotSelection { Select = new() { Mode = "all", RootIds = ["missing"] }, MissingSelection = "report" };
        var artifact = Capture(tasks, selection);
        await Assert.That(artifact.Targets.Count).IsEqualTo(0);
        await Assert.That(artifact.Catalog.Count).IsEqualTo(1);
        await Assert.That(artifact.Completeness.MissingRootIds.Single()).IsEqualTo("missing");
        await Assert.That(Failure(() => Capture(tasks, selection with { MissingSelection = "error" }))).IsEqualTo("notFound");
        await Assert.That(Failure(() => Capture(tasks, Ids("present") with { Select = new() { Mode = "ids", TaskIds = ["present"], Statuses = ["Completed"] } }))).IsEqualTo("invalidArguments");
        var selected = Capture(tasks, Ids("present"));
        await Assert.That(selected.Targets.Single().Id).IsEqualTo("present");
    }

    [Test]
    public async Task NoOpPreservesContextHashesAndCatalogNeverLeaksUnselectedBodies()
    {
        var selected = Item("selected"); var privateTask = Item("outside"); privateTask.Title = "private title sentinel"; privateTask.Description = "private description sentinel";
        var a = Capture([selected, privateTask], Ids("selected") with { Context = "none" });
        var b = Capture([selected, privateTask], Ids("selected") with { Context = "none" }, Now.AddHours(1));
        await Assert.That(a.SnapshotId == b.SnapshotId).IsFalse();
        await Assert.That(a.Targets.Single().ContextHash).IsEqualTo(b.Targets.Single().ContextHash);
        await Assert.That(NightAgentSnapshotCodec.Diff(a, b).Count).IsEqualTo(0);
        var text = JsonSerializer.Serialize(a, NightAgentSnapshotCodec.JsonOptions);
        await Assert.That(text.Contains("private title sentinel", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("private description sentinel", StringComparison.Ordinal)).IsFalse();
        await Assert.That(a.Nodes.Single().Sections["execution"]).IsEqualTo("notRequested");
    }

    [Test]
    public async Task ClockOnlyAvailabilityAndMembershipChangeInvalidatesTargetWithIdenticalEtag()
    {
        var work = Item("work"); work.PlannedBeginDateTime = Now.AddMinutes(5);
        var selection = new NightAgentSnapshotSelection();
        var before = Capture([work], selection, Now);
        var after = Capture([work], selection, Now.AddMinutes(6));
        await Assert.That(before.NextEvaluationAt).IsEqualTo(work.PlannedBeginDateTime);
        await Assert.That(after.NextEvaluationAt).IsNull();
        await Assert.That(before.Catalog.Single().Etag).IsEqualTo(after.Catalog.Single().Etag);
        var changes = NightAgentSnapshotCodec.Diff(before, after);
        await Assert.That(changes.Any(c => c.Kind == "availabilityChanged" && c.Reasons!.Contains("clock"))).IsTrue();
        await Assert.That(changes.Any(c => c.Kind == "membershipChanged" && c.TaskId == "work")).IsTrue();
        await Assert.That(changes.Any(c => c.Kind == "targetInvalidated" && c.TaskId == "work")).IsTrue();
        await Assert.That(changes.Any(c => c.Kind == "updated")).IsFalse();
        await Assert.That(before.Catalog.Single().Availability.CanStart).IsFalse();
    }

    [Test]
    public async Task AvailabilityCausesDoNotBlameUnrelatedNamespaceEditsOrCallChangedDatesClockOnly()
    {
        var work = Item("work"); work.PlannedBeginDateTime = Now.AddMinutes(5); var unrelated = Item("unrelated");
        var selection = Ids("work");
        var before = Capture([work, unrelated], selection);
        unrelated.Description = "An unrelated edit";
        var after = Capture([work, unrelated], selection, Now.AddMinutes(6));
        var clockChange = NightAgentSnapshotCodec.Diff(before, after).Single(change => change.Kind == "availabilityChanged" && change.TaskId == "work");
        await Assert.That(clockChange.Reasons!.SequenceEqual(new[] { "clock" })).IsTrue();
        work.PlannedBeginDateTime = Now.AddMinutes(60);
        var dateEdit = Capture([work, unrelated], selection, Now.AddMinutes(7));
        var editedChange = NightAgentSnapshotCodec.Diff(after, dateEdit).Single(change => change.Kind == "availabilityChanged" && change.TaskId == "work");
        await Assert.That(editedChange.Reasons!.SequenceEqual(new[] { "taskData" })).IsTrue();
    }

    [Test]
    public async Task ChangedSharedParentInvalidatesBothChildrenButIndependentBranchDoesNot()
    {
        var parent = Item("parent"); var first = Item("first"); var second = Item("second"); var independent = Item("independent");
        Contains(parent, first); Contains(parent, second);
        var selection = Ids("first", "second");
        var before = Capture([parent, first, second, independent], selection);
        independent.Description = "A fact outside the requested context";
        var unrelated = Capture([parent, first, second, independent], selection, Now.AddMinutes(1));
        await Assert.That(NightAgentSnapshotCodec.Diff(before, unrelated).Count(c => c.Kind == "targetInvalidated")).IsEqualTo(0);
        parent.Description = "Changed parent acceptance context";
        var after = Capture([parent, first, second, independent], selection, Now.AddMinutes(2));
        var invalidated = NightAgentSnapshotCodec.Diff(unrelated, after).Where(c => c.Kind == "targetInvalidated").ToArray();
        await Assert.That(invalidated.Select(c => c.TaskId).SequenceEqual(new[] { "first", "second" })).IsTrue();
        await Assert.That(invalidated.All(c => c.ChangedContextIds!.Contains("parent"))).IsTrue();
    }

    [Test]
    public async Task DeltaReportsCanonicalEdgesOnceAndDistinguishesCreationDeletionAndMembership()
    {
        var root = Item("root"); var work = Item("work");
        var selection = new NightAgentSnapshotSelection { Select = new() { Mode = "all", RootIds = ["root"] }, MissingSelection = "report" };
        var before = Capture([root, work], selection);
        Contains(root, work);
        var linked = Capture([root, work], selection, Now.AddMinutes(1));
        var linkDelta = NightAgentSnapshotCodec.Diff(before, linked);
        await Assert.That(linkDelta.Count(c => c.Kind == "relationAdded")).IsEqualTo(1);
        await Assert.That(linkDelta.Count(c => c.Kind == "created")).IsEqualTo(0);
        await Assert.That(linkDelta.Any(c => c.Kind == "membershipChanged" && c.TaskId == "work")).IsTrue();
        work.ParentTasks.Clear();
        var removed = Capture([work], selection, Now.AddMinutes(2));
        var deletion = NightAgentSnapshotCodec.Diff(linked, removed).Single(c => c.Kind == "deleted");
        await Assert.That(deletion.TaskId).IsEqualTo("root");
        await Assert.That(deletion.Evidence).IsEqualTo("absentFromObservedNamespace");
        await Assert.That(removed.Targets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ArtifactRoundTripIsOfflinePagedAndRejectsTamperUnknownPropertiesAndMismatchedCursor()
    {
        using var temp = new Sandbox();
        var artifact = Capture([Item("a"), Item("b"), Item("c")], new() { Select = new() { Mode = "all" } });
        var path = Path.Combine(temp.Root, "snapshot.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, NightAgentSnapshotCodec.JsonOptions));
        var loaded = await NightAgentSnapshotCodec.LoadArtifactAsync(path);
        var one = NightAgentSnapshotCodec.ReadPage(loaded, "targets", 1);
        var two = NightAgentSnapshotCodec.ReadPage(loaded, "targets", 1, one.NextCursor);
        await Assert.That(((NightAgentSnapshotNode)one.Items.Single()).Id).IsEqualTo("a");
        await Assert.That(((NightAgentSnapshotTargetNode)one.Items.Single()).ContextHash).IsEqualTo(loaded.Targets.Single(t => t.Id == "a").ContextHash);
        await Assert.That(((NightAgentSnapshotTargetNode)one.Items.Single()).ContextIds.SequenceEqual(loaded.Targets.Single(t => t.Id == "a").ContextIds)).IsTrue();
        await Assert.That(((NightAgentSnapshotNode)two.Items.Single()).Id).IsEqualTo("b");
        await Assert.That(Failure(() => NightAgentSnapshotCodec.ReadPage(loaded, "context", 1, one.NextCursor))).IsEqualTo("cursorMismatch");
        await Assert.That(Failure(() => NightAgentSnapshotCodec.ReadPage(loaded, "targets", 2, one.NextCursor))).IsEqualTo("cursorMismatch");
        var modified = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        modified["nodes"]![0]!["task"]!["title"] = "Tampered";
        await File.WriteAllTextAsync(path, modified.ToJsonString());
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCodec.LoadArtifactAsync(path))).IsEqualTo("snapshotInvalid");
        modified = JsonSerializer.SerializeToNode(artifact, NightAgentSnapshotCodec.JsonOptions)!;
        modified["unexpected"] = true;
        modified.AsObject().Remove("artifactHash");
        modified["artifactHash"] = NightAgentSnapshotCodec.Hash(modified);
        await File.WriteAllTextAsync(path, modified.ToJsonString());
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCodec.LoadArtifactAsync(path))).IsEqualTo("snapshotInvalid");
    }

    [Test]
    public async Task CapturePublishesOutsideRootAtomicallyAndNeverOverwritesOrCreatesSource()
    {
        using var temp = new Sandbox();
        var tasks = Path.Combine(temp.Root, "tasks"); Directory.CreateDirectory(tasks);
        await File.WriteAllTextAsync(Path.Combine(tasks, "work.json"), Newtonsoft.Json.JsonConvert.SerializeObject(Item("work")));
        var selection = Path.Combine(temp.Root, "selection.json");
        await File.WriteAllTextAsync(selection, JsonSerializer.Serialize(new NightAgentSnapshotSelection { Select = new() { Mode = "all" } }, NightAgentSnapshotCodec.JsonOptions));
        var path = Path.Combine(temp.Root, "snapshot.json");
        await NightAgentSnapshotCommands.CaptureAsync(selection, path, tasks);
        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCommands.CaptureAsync(selection, path, tasks))).IsEqualTo("invalidArguments");
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes)).IsTrue();
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCommands.CaptureAsync(selection, Path.Combine(tasks, "bad.json"), tasks))).IsEqualTo("invalidArguments");
        var missing = Path.Combine(temp.Root, "missing-source");
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCommands.CaptureAsync(selection, Path.Combine(temp.Root, "bad.json"), missing))).IsEqualTo("sourceUnavailable");
        await Assert.That(Directory.Exists(missing)).IsFalse();
        await Assert.That(Directory.EnumerateFiles(temp.Root, "*.tmp").Any()).IsFalse();
        File.Delete(Path.Combine(tasks, "work.json"));
        Directory.Delete(tasks, true);
        await Assert.That((await NightAgentSnapshotCodec.LoadArtifactAsync(path)).Nodes.Single().Id).IsEqualTo("work");
    }

    [Test]
    public async Task ByteBoundedPagesContinueWithoutTruncationAndRefuseAnOversizedRecord()
    {
        var first = Item("a"); var second = Item("b");
        first.Description = new string('x', 5 * 1024 * 1024); second.Description = first.Description;
        var artifact = Capture([first, second], new() { Select = new() { Mode = "all" }, Context = "none" });
        var page = NightAgentSnapshotCodec.ReadPage(artifact, "targets", 500);
        await Assert.That(page.Items.Count).IsEqualTo(1);
        await Assert.That(page.NextCursor != null).IsTrue();
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(page, NightAgentSnapshotCodec.JsonOptions).Length <= NightAgentSnapshotCodec.MaximumPageBytes).IsTrue();
        var next = NightAgentSnapshotCodec.ReadPage(artifact, "targets", 500, page.NextCursor);
        await Assert.That(((NightAgentSnapshotNode)next.Items.Single()).Id).IsEqualTo("b");
        await Assert.That(next.NextCursor).IsNull();
        var oversized = artifact with { Nodes = artifact.Nodes.Select(node => node.Id == "a" ? node with
        { Details = node.Details! with { Description = new string('y', 17 * 1024 * 1024) } } : node).ToArray() };
        await Assert.That(Failure(() => NightAgentSnapshotCodec.ReadPage(oversized, "targets", 1))).IsEqualTo("recordTooLarge");
    }

    [Test]
    public async Task IncompatibleBaselineAndStructuralGraphErrorsFailExplicitly()
    {
        var task = Item("task");
        var before = Capture([task], Ids("task"));
        var after = Capture([task], Ids("task") with { Context = "none" }, Now.AddMinutes(1));
        await Assert.That(Failure(() => NightAgentSnapshotCodec.Diff(before, after))).IsEqualTo("deltaIncompatible");
        await Assert.That(Failure(() => NightAgentSnapshotCodec.Diff(before, Capture([task], Ids("task"), Now.AddMinutes(-1))))).IsEqualTo("deltaIncompatible");
        task.ParentTasks = ["missing"];
        await Assert.That(Failure(() => Capture([task], Ids("task")))).IsEqualTo("snapshotInvalid");
        task.ParentTasks.Clear(); task.ContainsTasks = ["task"]; task.ParentTasks = ["task"];
        await Assert.That(Failure(() => Capture([task], Ids("task")))).IsEqualTo("snapshotInvalid");
    }

    [Test]
    public async Task ThreeNightReplayReusesMaterialPreservesDecisionsAndInvalidatesChangedContext()
    {
        var goal = Item("goal"); var work = Item("work"); Contains(goal, work);
        var selection = Ids("work");
        var first = Capture([goal, work], selection);
        var materials = new Dictionary<string, string> { ["work"] = "M1: verified experiment artifact" };
        var decisions = new Dictionary<string, string> { ["P1/r1"] = "deferred until new evidence" };
        var preparationQueue = new HashSet<string> { "work" }; var executionQueue = new HashSet<string> { "authorized-followup" };
        var second = Capture([goal, work], selection, Now.AddHours(1));
        var secondDelta = NightAgentSnapshotCodec.Diff(first, second);
        var rewrites = secondDelta.Count(c => c.Kind == "targetInvalidated");
        await Assert.That(rewrites).IsEqualTo(0);
        goal.Description = "New criterion requires checking the previous experiment";
        var third = Capture([goal, work], selection, Now.AddHours(2));
        foreach (var change in NightAgentSnapshotCodec.Diff(second, third).Where(c => c.Kind == "targetInvalidated")) preparationQueue.Add(change.TaskId);
        await Assert.That(materials["work"]).IsEqualTo("M1: verified experiment artifact");
        await Assert.That(decisions["P1/r1"]).IsEqualTo("deferred until new evidence");
        await Assert.That(executionQueue.SetEquals(["authorized-followup"])).IsTrue();
        await Assert.That(preparationQueue.SetEquals(["work"])).IsTrue();
        // Baseline loss resets validation, not external decisions or previously prepared material.
        var baselineReset = Failure(() => NightAgentSnapshotCodec.Diff(second, third with { Source = third.Source with { SourceKey = "sha256:" + new string('b', 64) } })) == "deltaIncompatible";
        await Assert.That(baselineReset).IsTrue();
        await Assert.That(materials.Count + decisions.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Synthetic3000Task600TargetCaptureUsesOneParseAndTwoBytePassesThenZeroOfflineSourceReads()
    {
        using var temp = new Sandbox();
        var tasks = Path.Combine(temp.Root, "tasks"); Directory.CreateDirectory(tasks);
        var graph = Enumerable.Range(0, 3000).Select(i => Item("task-" + i.ToString("D4"), i < 600 ? DomainTaskStatus.Prepared : DomainTaskStatus.Completed)).ToArray();
        for (var i = 0; i < 600; i++)
        {
            Contains(graph[600 + i / 10], graph[i]);
            if (i < 60) Contains(graph[700 + i / 10], graph[i]);
            Blocks(graph[2999], graph[i]);
        }
        foreach (var task in graph) await File.WriteAllTextAsync(Path.Combine(tasks, task.Id + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(task));
        var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tasks, CreateDirectoryIfMissing = false });
        var timer = Stopwatch.StartNew();
        var observed = await storage.ReadObservationAsync();
        var selection = Ids(graph.Take(600).Select(t => t.Id).ToArray());
        var artifact = NightAgentSnapshotCodec.CreateArtifact(observed, selection, NightAgentSnapshotCodec.CreateSourceKey(tasks));
        timer.Stop();
        await Assert.That(observed.TaskParseCount).IsEqualTo(3000);
        await Assert.That(observed.VerifiedFileCount).IsEqualTo(3000);
        await Assert.That(artifact.Targets.Count).IsEqualTo(600);
        await Assert.That(artifact.Targets.All(t => t.ContextIds.Contains("task-2999"))).IsTrue();
        var noOp = NightAgentSnapshotCodec.CreateArtifact(observed with { StartedAt = observed.StartedAt.AddSeconds(1), EvaluatedAt = observed.EvaluatedAt.AddSeconds(1), CompletedAt = observed.CompletedAt.AddSeconds(1) }, selection, artifact.Source.SourceKey);
        await Assert.That(NightAgentSnapshotCodec.Diff(artifact, noOp).Count).IsEqualTo(0);
        var path = Path.Combine(temp.Root, "benchmark.snapshot.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, NightAgentSnapshotCodec.JsonOptions));
        Directory.Move(tasks, Path.Combine(temp.Root, "source-unavailable"));
        var offline = await NightAgentSnapshotCodec.LoadArtifactAsync(path);
        var count = 0; string? cursor = null;
        do { var page = NightAgentSnapshotCodec.ReadPage(offline, "targets", 100, cursor); count += page.Items.Count; cursor = page.NextCursor; } while (cursor != null);
        await Assert.That(count).IsEqualTo(600);
        Console.WriteLine($"SNAPSHOT_BENCHMARK tasks=3000 targets=600 multiparent=60 parses={observed.TaskParseCount} verified={observed.VerifiedFileCount} sourceBytes={observed.SourceBytes} elapsedMs={timer.Elapsed.TotalMilliseconds:F1} legacyRepeatedReadParseCount={3000L * 600} offlineSourceReads=0 noOpInvalidations=0");
    }

    [Test]
    public async Task ResignedArtifactCannotClaimTruncatedContextOrIncorrectTargetMembership()
    {
        using var temp = new Sandbox();
        var parent = Item("parent"); var work = Item("work"); Contains(parent, work);
        var artifact = Capture([parent, work], Ids("work"));
        var path = Path.Combine(temp.Root, "invalid.snapshot.json");
        var truncated = artifact with { Targets = [artifact.Targets.Single() with { ContextIds = ["work"] }] };
        await SaveResigned(path, truncated);
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCodec.LoadArtifactAsync(path))).IsEqualTo("snapshotInvalid");
        var wrongMembership = artifact with { Targets = [], Nodes = [], Completeness = artifact.Completeness with { TargetCount = 0, PayloadCount = 0 } };
        await SaveResigned(path, wrongMembership);
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCodec.LoadArtifactAsync(path))).IsEqualTo("snapshotInvalid");
        var wrongRelations = artifact with { Nodes = artifact.Nodes.Select(n => n.Id == "work" ? n with { ParentTasks = [] } : n).ToArray() };
        await SaveResigned(path, wrongRelations);
        await Assert.That(await FailureAsync(() => NightAgentSnapshotCodec.LoadArtifactAsync(path))).IsEqualTo("snapshotInvalid");
        static Task SaveResigned(string file, NightAgentSnapshotArtifact value)
        {
            value = value with { ArtifactHash = null };
            value = value with { ArtifactHash = NightAgentSnapshotCodec.Hash(value) };
            return File.WriteAllTextAsync(file, JsonSerializer.Serialize(value, NightAgentSnapshotCodec.JsonOptions));
        }
    }

    [Test]
    public async Task LegacyNullAndMissingDescriptionsRoundTripForSelectedAndCatalogOnlyTasks()
    {
        using var temp = new Sandbox();
        var tasks = Path.Combine(temp.Root, "tasks"); Directory.CreateDirectory(tasks);
        foreach (var id in new[] { "selected-null", "selected-missing", "outside-null", "outside-missing" })
        {
            var json = JsonNode.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(Item(id)))!.AsObject();
            if (id.EndsWith("missing", StringComparison.Ordinal)) json.Remove("Description"); else json["Description"] = null;
            await File.WriteAllTextAsync(Path.Combine(tasks, id + ".json"), json.ToJsonString());
        }
        var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = tasks, CreateDirectoryIfMissing = false });
        var observation = await storage.ReadObservationAsync();
        var artifact = NightAgentSnapshotCodec.CreateArtifact(observation, Ids("selected-null", "selected-missing") with { Context = "none" }, SourceKey);
        var path = Path.Combine(temp.Root, "legacy.snapshot.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, NightAgentSnapshotCodec.JsonOptions));
        var loaded = await NightAgentSnapshotCodec.LoadArtifactAsync(path);
        await Assert.That(loaded.Nodes.Count).IsEqualTo(2);
        await Assert.That(loaded.Catalog.Count).IsEqualTo(4);
        await Assert.That(loaded.Nodes.All(node => node.Details!.Description == null)).IsTrue();
    }

    [Test]
    public async Task FilesystemRootAndDirectoryBoundaryContainmentAreHandledWithoutDoubleSeparators()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        await Assert.That(NightAgentSnapshotCommands.IsOutputWithinSourceRoot(Path.Combine(root, "snapshot.json"), root)).IsTrue();
        var source = Path.Combine(root, "tasks");
        await Assert.That(NightAgentSnapshotCommands.IsOutputWithinSourceRoot(Path.Combine(source, "snapshot.json"), source)).IsTrue();
        await Assert.That(NightAgentSnapshotCommands.IsOutputWithinSourceRoot(Path.Combine(root, "tasks-other", "snapshot.json"), source)).IsFalse();
        if (OperatingSystem.IsWindows())
            await Assert.That(NightAgentSnapshotCommands.IsOutputWithinSourceRoot(@"\\server\share\snapshot.json", @"\\server\share\")).IsTrue();
    }

    [Test]
    public async Task SchemasAreAvailableWithoutStorageAndDescribeStrictSelection()
    {
        foreach (var kind in new[] { "selection", "artifact", "page", "delta" })
        {
            var schema = NightAgentSnapshotCommands.Schema(kind);
            await Assert.That(schema["$schema"]!.GetValue<string>()).IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        }
        await Assert.That(NightAgentSnapshotCommands.Schema("selection")["additionalProperties"]!.GetValue<bool>()).IsFalse();
        await Assert.That(NightAgentSnapshotCommands.Schema("page")["$defs"]!["target"]!["properties"]!["contextHash"] != null).IsTrue();
    }

    private static TaskItem Item(string id, DomainTaskStatus status = DomainTaskStatus.Prepared) => new()
    {
        Id = id, Title = id, Description = "Description for " + id, UserId = "test", Status = status, CreatedDateTime = Now.AddDays(-1),
        StatusHistory = [new TaskStatusHistoryEntry { Status = status, ChangedAt = Now.AddDays(-1), Author = "test" }]
    };
    private static void Contains(TaskItem parent, TaskItem child) { parent.ContainsTasks.Add(child.Id); child.ParentTasks.Add(parent.Id); }
    private static void Blocks(TaskItem blocker, TaskItem target) { blocker.BlocksTasks.Add(target.Id); target.BlockedByTasks.Add(blocker.Id); }
    private static NightAgentSnapshotSelection Ids(params string[] ids) => new() { Select = new() { Mode = "ids", TaskIds = ids }, Include = ["details", "criteria", "history"] };
    private static NightAgentSnapshotArtifact Capture(IReadOnlyList<TaskItem> tasks, NightAgentSnapshotSelection selection, DateTimeOffset? now = null)
    {
        var time = now ?? Now;
        var graph = new TaskGraphReadResult(tasks, tasks.ToDictionary(t => t.Id, t => t.Id + ".json"), [], []);
        return NightAgentSnapshotCodec.CreateArtifact(new(graph, SourceKey, new HashSet<string>(), time, time, time, 1, tasks.Count, tasks.Count, 0), selection, SourceKey);
    }
    private static string Failure(Action operation) { try { operation(); return "noFailure"; } catch (NightAgentSnapshotException error) { return error.Kind; } }
    private static async Task<string> FailureAsync(Func<Task> operation) { try { await operation(); return "noFailure"; } catch (NightAgentSnapshotException error) { return error.Kind; } }
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unlimotion-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        public Sandbox() => Directory.CreateDirectory(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
