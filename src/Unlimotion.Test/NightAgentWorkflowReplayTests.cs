using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

// The external ledger below is a synthetic consumer fixture. It does not install or migrate a workflow.
[NotInParallel]
public sealed class NightAgentWorkflowReplayTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ThreeNightsAndMorningReplay_GuardedCommitLostReceiptReconciliationRevisionAndCheckpoint()
    {
        using var space = new Workspace();
        var goalA = Item("goal-a"); var goalB = Item("goal-b"); var work = Item("shared-work");
        var rejected = Item("rejected-work"); var deferred = Item("deferred-work");
        Contains(goalA, work); Contains(goalB, work); Contains(goalA, rejected); Contains(goalB, deferred);
        goalA.IsCanBeCompleted = goalB.IsCanBeCompleted = false;
        goalA.UnlockedDateTime = goalB.UnlockedDateTime = null;
        foreach (var task in new[] { goalA, goalB, work, rejected, deferred }) await space.Storage.Save(task);
        var selection = new NightAgentSnapshotSelection
        {
            Select = new() { Mode = "unlocked", RootIds = ["goal-a", "goal-b"] }, MissingSelection = "report", Include = ["details", "criteria", "history"]
        };

        // N1: one authoritative graph, both goal branches, one actual bounded local experiment.
        var night1 = await space.Capture("night-1", selection);
        await Assert.That(night1.Nodes.Single(node => node.Id == work.Id).ParentTasks.SequenceEqual(new[] { goalA.Id, goalB.Id })).IsTrue();
        var experimentInput = Encoding.UTF8.GetBytes("synthetic controlled experiment input v1");
        var materialPath = Path.Combine(space.External, "material-M1.txt");
        var material = "Observed fixture SHA-256: " + Convert.ToHexString(SHA256.HashData(experimentInput));
        await File.WriteAllTextAsync(materialPath, material);
        var materialHash = Hash(await File.ReadAllTextAsync(materialPath));
        var ledger = new Ledger
        {
            BaselineHash = night1.ArtifactHash!, Materials = new() { [work.Id] = materialPath }, ExperimentRuns = 1,
            PreparationQueue = [work.Id, rejected.Id, deferred.Id], ExecutionQueue = ["separately-authorized-followup"],
            Decisions = new() { ["P-approved/r1"] = "superseded", ["P-approved/r2"] = "accepted", ["P-rejected/r1"] = "rejected: unsuitable alternative", ["P-deferred/r1"] = "deferred: await evidence" }
        };
        await space.SaveLedger(ledger);
        var request = new TaskApplicationRequest
        {
            SchemaVersion = 1, ApplicationId = "A-approved-r2", Author = "synthetic-reviewer", Reason = "Accepted exact proposal P-approved/r2",
            ProposalRefs = [new("P-approved", 2)],
            Preconditions = [new(work.Id, night1.Catalog.Single(node => node.Id == work.Id).Etag, work.Status)],
            Operations =
            [
                new() { OperationId = "estimate", Kind = TaskApplicationOperationKind.SetField, TaskId = work.Id, Field = "plannedDuration", Value = "PT45M" },
                new() { OperationId = "proof-part", Kind = TaskApplicationOperationKind.CreateTask, NewTaskId = "proof-part", Title = "Verify accepted material", DescriptionUserText = "Use saved M1; do not rerun the completed experiment.", ParentIds = [work.Id], Status = DomainTaskStatus.Prepared }
            ]
        };
        var requestText = JsonSerializer.Serialize(request, NightAgentSnapshotCodec.JsonOptions);
        var requestPath = Path.Combine(space.External, "A-approved-r2.request.json");
        await File.WriteAllTextAsync(requestPath, requestText);
        var requestHash = Hash(requestText);
        await File.WriteAllTextAsync(Path.Combine(space.External, "application-intent.json"), JsonSerializer.Serialize(new
        { request.ApplicationId, requestHash, proposal = "P-approved/r2", decision = ledger.Decisions["P-approved/r2"], requestPath, beforeArtifactHash = night1.ArtifactHash }));

        // Morning: fresh exact full preview, then commit under the witness guard.
        var previewResult = await space.Service.PreviewPlanAsync(request);
        await Assert.That(previewResult.Success).IsTrue();
        var preview = TaskApplicationPreview.Create(previewResult.Plan!, request, requestHash, space.SourceKey);
        var witnessText = JsonSerializer.Serialize(ApplicationCommandOutput.From(request.ApplicationId, requestHash, previewResult, false) with { Preview = preview }, NightAgentSnapshotCodec.JsonOptions);
        var witnessPath = Path.Combine(space.External, "A-approved-r2.preview.json");
        await File.WriteAllTextAsync(witnessPath, witnessText);
        var witness = TaskApplicationPreview.ValidateWitness(witnessText, request.ApplicationId, requestHash, space.SourceKey);
        await Assert.That(witness.Changes.Any(change => change.TaskId == work.Id && change.Origin == "derived")).IsTrue();
        var applied = await space.Service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(witness, plan, request, requestHash, space.SourceKey));
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(applied.DidMutate).IsTrue();
        await Assert.That(applied.CreatedTaskIds.Single()).IsEqualTo("proof-part");

        // Crash boundary: the domain commit completed, but the CLI receipt and external result/checkpoint have not been written.
        await Assert.That(Directory.Exists(Path.Combine(space.Tasks, ".unlimotion.applies"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(space.External, "queue-checkpoint.json"))).IsFalse();
        var committedBytes = space.TaskBytes();
        var inspectMissingReceipt = await RunCli("apply", "inspect", "--tasks", space.Tasks, "--request", requestPath, "--format", "json");
        await Assert.That(inspectMissingReceipt.ExitCode).IsEqualTo(0);
        await Assert.That(inspectMissingReceipt.Json.GetProperty("assessment").GetString()).IsEqualTo("desiredStatePresent");
        await Assert.That(inspectMissingReceipt.Json.GetProperty("receiptState").GetString()).IsEqualTo("missing");
        await Assert.That(inspectMissingReceipt.Json.GetProperty("didMutate").GetBoolean()).IsFalse();
        await Assert.That(space.TaskBytes().SequenceEqual(committedBytes)).IsTrue();

        // Restart uses durable original bytes and witness, not a reconstructed/new application.
        ledger = await space.ReadLedger();
        await Assert.That(Hash(await File.ReadAllTextAsync(requestPath))).IsEqualTo(requestHash);
        var resumed = await RunCli("apply", "--tasks", space.Tasks, "--request", requestPath, "--expect-preview", witnessPath, "--format", "json");
        await Assert.That(resumed.ExitCode).IsEqualTo(0);
        await Assert.That(resumed.Json.GetProperty("mode").GetString()).IsEqualTo("alreadyApplied");
        await Assert.That(resumed.Json.GetProperty("didMutate").GetBoolean()).IsFalse();
        await Assert.That(resumed.Json.GetProperty("receiptWritten").GetBoolean()).IsTrue();
        await Assert.That(space.TaskBytes().SequenceEqual(committedBytes)).IsTrue();
        var inspectedReceipt = await RunCli("apply", "inspect", "--tasks", space.Tasks, "--request", requestPath, "--format", "json");
        await Assert.That(inspectedReceipt.Json.GetProperty("assessment").GetString()).IsEqualTo("receiptMatched");
        await Assert.That(inspectedReceipt.Json.GetProperty("postconditionsMatch").GetString()).IsEqualTo("all");
        var readBack = await new FileTaskStorage(new() { Path = space.Tasks, CreateDirectoryIfMissing = false }).ReadObservationAsync();
        await Assert.That(readBack.Graph.TasksById[work.Id].PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(45));
        await Assert.That(readBack.Graph.TasksById[work.Id].ContainsTasks.Contains("proof-part")).IsTrue();
        await Assert.That(readBack.Graph.TasksById["proof-part"].ParentTasks.SequenceEqual(new[] { work.Id })).IsTrue();
        await Assert.That(readBack.Graph.Tasks.Count(task => task.Id == "proof-part")).IsEqualTo(1);
        await Assert.That(witness.AffectedTaskIds.All(readBack.Graph.TasksById.ContainsKey)).IsTrue();
        var resultPath = Path.Combine(space.External, "application-result.json");
        var resultText = JsonSerializer.Serialize(new { request.ApplicationId, requestHash, outcome = "reconciledState", affectedIds = witness.AffectedTaskIds, readBackAt = readBack.EvaluatedAt });
        await Assert.That(await SaveOnce(resultPath, resultText)).IsTrue();
        await Assert.That(await SaveOnce(resultPath, resultText)).IsFalse();

        // Crash after result but before queue checkpoint: recover checkpoint from the same application result.
        var resultAgain = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath)).RootElement;
        await Assert.That(resultAgain.GetProperty("requestHash").GetString()).IsEqualTo(requestHash);
        await space.SaveCheckpoint(new { ledger.BaselineHash, applicationResult = resultPath, ledger.PreparationQueue, ledger.ExecutionQueue });
        var night2 = await space.Capture("night-2", selection);
        var delta2 = NightAgentSnapshotCodec.Diff(night1, night2);
        await Assert.That(delta2.Any(change => change.Kind == "created" && change.TaskId == "proof-part")).IsTrue();
        await Assert.That(delta2.Any(change => change.Kind == "membershipChanged" && change.TaskId == work.Id)).IsTrue();
        await Assert.That(delta2.Any(change => change.Kind == "targetInvalidated" && change.TaskId == work.Id)).IsTrue();
        ledger.PreparationQueue = ledger.PreparationQueue.Union(delta2.Where(change => change.Kind == "targetInvalidated").Select(change => change.TaskId), StringComparer.Ordinal)
            .Union(night2.Targets.Select(target => target.Id), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        ledger.BaselineHash = night2.ArtifactHash!;
        await space.SaveLedger(ledger);
        await space.SaveCheckpoint(new { ledger.BaselineHash, applicationResult = resultPath, ledger.PreparationQueue, ledger.ExecutionQueue });
        await Assert.That(ledger.ExecutionQueue.Contains("proof-part")).IsFalse(); // Unlocked is not execution authorization.

        // Changed proposal r3 cannot inherit the r2 decision or reuse its witness.
        var revision3 = request with
        {
            ApplicationId = "A-approved-r3", ProposalRefs = [new("P-approved", 3)],
            Preconditions = [new(work.Id, Etag(readBack.Graph.TasksById[work.Id]), readBack.Graph.TasksById[work.Id].Status)],
            Operations = [new() { OperationId = "estimate-r3", Kind = TaskApplicationOperationKind.SetField, TaskId = work.Id, Field = "plannedDuration", Value = "PT90M" }]
        };
        await Assert.That(ledger.Decisions.ContainsKey("P-approved/r3")).IsFalse();
        ledger.Decisions["P-approved/r3"] = "pending: changed estimate requires its own decision";
        var revision3Path = Path.Combine(space.External, "A-approved-r3.request.json");
        await File.WriteAllTextAsync(revision3Path, JsonSerializer.Serialize(revision3, NightAgentSnapshotCodec.JsonOptions));
        var wrongApproval = await RunCli("apply", "--tasks", space.Tasks, "--request", revision3Path, "--expect-preview", witnessPath, "--format", "json");
        await Assert.That(wrongApproval.ExitCode).IsEqualTo(1);
        await Assert.That(wrongApproval.Json.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("previewInvalid");
        await Assert.That(space.TaskBytes().SequenceEqual(committedBytes)).IsTrue();
        await space.SaveLedger(ledger);

        // N3: no-op reuses the real material, does not rerun the experiment or reprompt rejected/deferred revisions.
        var night3 = await space.Capture("night-3", selection);
        var delta3 = NightAgentSnapshotCodec.Diff(night2, night3);
        await Assert.That(delta3.Count).IsEqualTo(0);
        var persisted = await space.ReadLedger();
        await Assert.That(persisted.ExperimentRuns).IsEqualTo(1);
        await Assert.That(Hash(await File.ReadAllTextAsync(persisted.Materials[work.Id]))).IsEqualTo(materialHash);
        await Assert.That(persisted.Decisions["P-rejected/r1"]).IsEqualTo("rejected: unsuitable alternative");
        await Assert.That(persisted.Decisions["P-deferred/r1"]).IsEqualTo("deferred: await evidence");
        await Assert.That(persisted.ExecutionQueue.SequenceEqual(new[] { "separately-authorized-followup" })).IsTrue();
        await Assert.That(persisted.PreparationQueue.SequenceEqual(ledger.PreparationQueue)).IsTrue();

        // Missing baseline has an explicit error. Full fallback preserves all durable consumer state.
        var missingDelta = await RunCli("snapshot", "diff", "--before", Path.Combine(space.External, "missing-baseline.json"), "--after", space.SnapshotPath("night-3"), "--format", "json");
        await Assert.That(missingDelta.ExitCode).IsEqualTo(1);
        await Assert.That(missingDelta.Json.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("baselineUnavailable");
        var reset = await space.Capture("baseline-reset", selection);
        persisted.BaselineReset = true;
        persisted.BaselineHash = reset.ArtifactHash!;
        persisted.RevalidationQueue = reset.Targets.Select(target => target.Id).Order(StringComparer.Ordinal).ToList();
        await space.SaveLedger(persisted);
        await space.SaveCheckpoint(new { persisted.BaselineHash, persisted.BaselineReset, persisted.RevalidationQueue, applicationResult = resultPath });
        var final = await space.ReadLedger();
        await Assert.That(final.RevalidationQueue.Count).IsEqualTo(reset.Targets.Count);
        await Assert.That(final.Decisions.Count).IsEqualTo(5);
        await Assert.That(final.Materials.Count).IsEqualTo(1);
        await Assert.That(final.ExperimentRuns).IsEqualTo(1);
        await Assert.That(space.TaskBytes().SequenceEqual(committedBytes)).IsTrue();
        var replayEvidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "night-agent-replay-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(replayEvidence);
        foreach (var file in Directory.GetFiles(space.External)) File.Copy(file, Path.Combine(replayEvidence, Path.GetFileName(file)));
        var taskEvidence = Path.Combine(replayEvidence, "final-synthetic-tasks"); Directory.CreateDirectory(taskEvidence);
        foreach (var file in Directory.GetFiles(space.Tasks).Where(file => !Path.GetFileName(file).StartsWith('.')))
            File.Copy(file, Path.Combine(taskEvidence, Path.GetFileName(file)));
        Console.WriteLine("NIGHT_REPLAY_EVIDENCE " + replayEvidence);
        Console.WriteLine("NIGHT_REPLAY nights=3 goalParents=2 exactApproval=r2 guardedCommit=1 lostReceipt=desiredStatePresent resume=alreadyApplied taskRewritesOnResume=0 resultRecords=1 checkpointRecovered=true rejectedPreserved=true deferredPreserved=true r3OldWitness=previewInvalid experiments=1 noOpInvalidations=0 baselineReset=true");
    }

    [Test]
    public async Task MeasuredBenchmark_3000Tasks600ReadsVsCaptureAndOfflinePages()
    {
        using var space = new Workspace();
        const int nodeCount = 3000; const int targetCount = 600;
        var tasks = Enumerable.Range(0, nodeCount).Select(index => Item("bench-" + index.ToString("D4"))).ToArray();
        for (var index = targetCount; index < nodeCount; index++)
        { tasks[index].Status = DomainTaskStatus.Completed; tasks[index].SetStatus(DomainTaskStatus.Completed, Epoch, "synthetic"); }
        for (var index = 0; index < targetCount; index++)
        {
            Contains(tasks[600 + index / 10], tasks[index]);
            if (index < 60) Contains(tasks[700 + index / 10], tasks[index]);
            tasks[2999].BlocksTasks.Add(tasks[index].Id); tasks[index].BlockedByTasks.Add(tasks[2999].Id);
        }
        foreach (var task in tasks) await File.WriteAllTextAsync(Path.Combine(space.Tasks, task.Id), Newtonsoft.Json.JsonConvert.SerializeObject(task));
        var selection = new NightAgentSnapshotSelection { Select = new() { Mode = "ids", TaskIds = tasks.Take(targetCount).Select(task => task.Id).ToArray() }, Include = ["details", "criteria", "history"] };
        var sourceKey = space.SourceKey;

        // One real verified capture plus publication, with process-wide allocation measurement while this test is isolated.
        var allocationBeforeCapture = GC.GetTotalAllocatedBytes(precise: true);
        var captureWatch = Stopwatch.StartNew();
        var observation = await space.Storage.ReadObservationAsync();
        var artifact = NightAgentSnapshotCodec.CreateArtifact(observation, selection, sourceKey);
        var artifactPath = space.SnapshotPath("benchmark");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(artifact, NightAgentSnapshotCodec.JsonOptions));
        captureWatch.Stop();
        var captureAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationBeforeCapture;
        await Assert.That(observation.TaskParseCount).IsEqualTo(nodeCount);
        await Assert.That(observation.VerifiedFileCount).IsEqualTo(nodeCount);
        await Assert.That(artifact.Targets.Count).IsEqualTo(targetCount);

        // An independent fixture oracle: each target belongs to its primary group of ten siblings;
        // the first sixty also belong to a second parent of that same group, and every target has
        // the shared blocker. Parents and the blocker have no parents/upstream blockers in this fixture.
        // This checks graph fidelity, not merely the number of records loaded by the benchmark.
        var sourceById = tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var payloadById = artifact.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var targetById = artifact.Targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        var expectedEdges = tasks.SelectMany(task => task.ContainsTasks.Select(child => (Kind: "contains", From: task.Id, To: child))
            .Concat(task.BlocksTasks.Select(blocked => (Kind: "blocks", From: task.Id, To: blocked)))).ToHashSet();
        var observedEdgeRows = artifact.Catalog.SelectMany(node => node.Edges).Select(edge => (edge.Kind, edge.From, edge.To)).ToArray();
        var observedEdges = observedEdgeRows.ToHashSet();
        var missingEdges = expectedEdges.Except(observedEdges).Count();
        var unexpectedEdges = observedEdges.Except(expectedEdges).Count();
        await Assert.That(expectedEdges.Count(edge => edge.Kind == "contains")).IsEqualTo(660);
        await Assert.That(expectedEdges.Count(edge => edge.Kind == "blocks")).IsEqualTo(600);
        await Assert.That(expectedEdges.Count).IsEqualTo(1260);
        await Assert.That(observedEdgeRows.Length).IsEqualTo(1260);
        await Assert.That(observedEdges.Count).IsEqualTo(1260);
        await Assert.That(missingEdges).IsEqualTo(0);
        await Assert.That(unexpectedEdges).IsEqualTo(0);
        var checkedTargets = 0; var checkedMultiParentTargets = 0;
        long expectedContextIdCount = 0; long observedContextIdCount = 0; long missingContextIds = 0; long unexpectedContextIds = 0;
        long expectedContextEdgeCount = 0; long observedContextEdgeCount = 0; long missingContextEdges = 0; long unexpectedContextEdges = 0;
        for (var index = 0; index < targetCount; index++)
        {
            var sourceTask = tasks[index]; var target = targetById[sourceTask.Id]; var payloadNode = payloadById[sourceTask.Id];
            var groupStart = index / 10 * 10;
            var expectedContext = Enumerable.Range(groupStart, 10).Select(sibling => tasks[sibling].Id).ToHashSet(StringComparer.Ordinal);
            expectedContext.Add(tasks[600 + index / 10].Id);
            expectedContext.Add(tasks[2999].Id);
            if (index < 60) expectedContext.Add(tasks[700 + index / 10].Id);
            var observedContext = target.ContextIds.ToHashSet(StringComparer.Ordinal);
            expectedContextIdCount += expectedContext.Count; observedContextIdCount += target.ContextIds.Count;
            missingContextIds += expectedContext.Except(observedContext, StringComparer.Ordinal).Count();
            unexpectedContextIds += observedContext.Except(expectedContext, StringComparer.Ordinal).Count();
            if (observedContext.Count != target.ContextIds.Count || !expectedContext.SetEquals(observedContext))
                throw new InvalidDataException($"Benchmark target '{sourceTask.Id}' does not contain its exact expected sibling/parent/blocker context.");
            if (sourceTask.ParentTasks.Any(parent => !observedContext.Contains(parent)) ||
                sourceTask.BlockedByTasks.Any(blocker => !observedContext.Contains(blocker)) ||
                !payloadNode.ParentTasks.SequenceEqual(sourceTask.ParentTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !payloadNode.BlockedByTasks.SequenceEqual(sourceTask.BlockedByTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException($"Benchmark target '{sourceTask.Id}' lost a source parent or blocker relationship.");

            // Incident edges include the shared blocker's boundary edges to other targets. They must
            // survive the bounded closure even though those outgoing neighbors do not expand C(t).
            var expectedIncident = expectedEdges.Where(edge => expectedContext.Contains(edge.From) || expectedContext.Contains(edge.To)).ToHashSet();
            var observedIncident = target.ContextIds.SelectMany(id => payloadById[id].Edges).Select(edge => (edge.Kind, edge.From, edge.To)).ToHashSet();
            expectedContextEdgeCount += expectedIncident.Count; observedContextEdgeCount += observedIncident.Count;
            missingContextEdges += expectedIncident.Except(observedIncident).Count();
            unexpectedContextEdges += observedIncident.Except(expectedIncident).Count();
            if (!expectedIncident.SetEquals(observedIncident))
                throw new InvalidDataException($"Benchmark target '{sourceTask.Id}' lost or invented an incident context edge.");
            checkedTargets++;
            if (sourceTask.ParentTasks.Count > 1) checkedMultiParentTargets++;
        }
        foreach (var node in artifact.Nodes)
        {
            var sourceNode = sourceById[node.Id];
            if (!node.ContainsTasks.SequenceEqual(sourceNode.ContainsTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.ParentTasks.SequenceEqual(sourceNode.ParentTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.BlocksTasks.SequenceEqual(sourceNode.BlocksTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !node.BlockedByTasks.SequenceEqual(sourceNode.BlockedByTasks.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException($"Benchmark payload node '{node.Id}' lost a source relation array.");
        }
        await Assert.That(checkedTargets).IsEqualTo(targetCount);
        await Assert.That(checkedMultiParentTargets).IsEqualTo(60);
        await Assert.That(missingContextIds).IsEqualTo(0L);
        await Assert.That(unexpectedContextIds).IsEqualTo(0L);
        await Assert.That(missingContextEdges).IsEqualTo(0L);
        await Assert.That(unexpectedContextEdges).IsEqualTo(0L);

        // The old task command performs a complete ReadDirectoryAsync for every target. This is that actual
        // loader + analyzer + projection path in one process, deliberately excluding process startup overhead.
        var oldStorage = new FileTaskStorage(new() { Path = space.Tasks, CreateDirectoryIfMissing = false });
        var allocationBeforeOld = GC.GetTotalAllocatedBytes(precise: true);
        var oldWatch = Stopwatch.StartNew();
        long successfulDomainParses = 0; long projectedTargets = 0;
        var oldInclude = new HashSet<string>(["details", "criteria", "history"], StringComparer.Ordinal);
        for (var index = 0; index < targetCount; index++)
        {
            var loaded = await oldStorage.ReadDirectoryAsync();
            if (loaded.Tasks.Count != nodeCount || loaded.LoadErrors.Count != 0) throw new InvalidDataException("Benchmark source changed during the old reader run.");
            successfulDomainParses += loaded.Tasks.Count;
            var analyzer = new TaskAvailabilityAnalyzer(loaded.Tasks);
            var task = loaded.Tasks.Single(item => item.Id == tasks[index].Id);
            var projection = TaskSnapshotOutput.Create(task, analyzer.Analyze(task), analyzer, oldInclude);
            if (projection.Task.Id != task.Id) throw new InvalidDataException("Old reader projected the wrong target.");
            projectedTargets++;
            if ((index + 1) % 100 == 0) Console.WriteLine($"SNAPSHOT_BENCHMARK_PROGRESS realLegacyReads={index + 1}/600 successfulDomainParses={successfulDomainParses} elapsedMs={oldWatch.Elapsed.TotalMilliseconds:F1}");
        }
        oldWatch.Stop();
        var oldAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationBeforeOld;
        await Assert.That(successfulDomainParses).IsEqualTo(1_800_000L);
        await Assert.That(projectedTargets).IsEqualTo(600L);

        var nextObservation = await space.Storage.ReadObservationAsync();
        var noOp = NightAgentSnapshotCodec.CreateArtifact(nextObservation, selection, sourceKey);
        var noOpInvalidations = NightAgentSnapshotCodec.Diff(artifact, noOp).Count(change => change.Kind == "targetInvalidated");
        await Assert.That(noOpInvalidations).IsEqualTo(0);
        var beforeSourceBytes = space.TaskBytes();

        // Make every source path unavailable before offline load/pages: a hidden live fallback would fail.
        Directory.Move(space.Tasks, Path.Combine(space.Root, "source-unavailable-during-offline-read"));
        var allocationBeforeOffline = GC.GetTotalAllocatedBytes(precise: true);
        var offlineWatch = Stopwatch.StartNew();
        var reloaded = await NightAgentSnapshotCodec.LoadArtifactAsync(artifactPath);
        var offlineCount = 0; string? cursor = null;
        do
        {
            var page = NightAgentSnapshotCodec.ReadPage(reloaded, "targets", 100, cursor);
            offlineCount += page.Items.Count; cursor = page.NextCursor;
        } while (cursor != null);
        offlineWatch.Stop();
        var offlineAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationBeforeOffline;
        await Assert.That(offlineCount).IsEqualTo(targetCount);
        var afterSourceBytes = Directory.GetFiles(Path.Combine(space.Root, "source-unavailable-during-offline-read"))
            .Where(path => !Path.GetFileName(path).StartsWith('.')).Order(StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path) + ":" + Convert.ToHexString(File.ReadAllBytes(path))).ToArray();
        await Assert.That(beforeSourceBytes.SequenceEqual(afterSourceBytes)).IsTrue();
        var evidence = new
        {
            fixture = new { nodes = nodeCount, targets = targetCount, multiParentTargets = 60, sharedBlockerId = tasks[2999].Id },
            observation = new { observation.TaskParseCount, observation.VerifiedFileCount, observation.SourceBytes, observation.Attempts,
                elapsedMs = captureWatch.Elapsed.TotalMilliseconds, processAllocatedBytes = captureAllocatedBytes },
            graphCoverage = new
            {
                expectedCanonicalEdges = expectedEdges.Count, observedCanonicalEdges = observedEdges.Count,
                observedCanonicalEdgeRows = observedEdgeRows.Length, missingCanonicalEdges = missingEdges, unexpectedCanonicalEdges = unexpectedEdges,
                checkedTargets, checkedMultiParentTargets, checkedPayloadRelationArrays = artifact.Nodes.Count * 4,
                expectedContextIds = expectedContextIdCount, observedContextIds = observedContextIdCount, missingContextIds, unexpectedContextIds,
                expectedContextEdges = expectedContextEdgeCount, observedContextEdges = observedContextEdgeCount, missingContextEdges, unexpectedContextEdges,
                oracle = "fixture groups of ten siblings, every source parent, shared blocker, all incident canonical edges"
            },
            legacy = new { reader = "ReadDirectoryAsync+Analyze+TaskSnapshotOutput, in-process, no startup", completeReads = targetCount,
                successfulDomainParses, projectedTargets, elapsedMs = oldWatch.Elapsed.TotalMilliseconds, processAllocatedBytes = oldAllocatedBytes },
            offline = new { records = offlineCount, sourcePathAbsent = !Directory.Exists(space.Tasks), sourceReads = 0,
                elapsedMs = offlineWatch.Elapsed.TotalMilliseconds, processAllocatedBytes = offlineAllocatedBytes },
            noOp = new { targetInvalidations = noOpInvalidations, taskWrites = 0, taskBytesUnchanged = true,
                nextObservation.TaskParseCount, nextObservation.VerifiedFileCount },
            measurement = new { order = "capture, legacy repeated reads, second capture, offline", isolatedTest = true,
                includesOsCache = true, allocationScope = "process-wide GC.GetTotalAllocatedBytes delta", evaluatedAt = DateTimeOffset.UtcNow }
        };
        var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults"); Directory.CreateDirectory(evidenceDirectory);
        var evidencePath = Path.Combine(evidenceDirectory, "night-agent-benchmark-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        var evidenceJson = JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(evidencePath, evidenceJson);
        Console.WriteLine("SNAPSHOT_BENCHMARK_EVIDENCE " + evidencePath);
        Console.WriteLine(evidenceJson);
    }

    private static string Etag(TaskItem task) => TaskSnapshotOutput.Create(task, new TaskAvailabilityAnalyzer([task]).Analyze(task),
        new TaskAvailabilityAnalyzer([task]), new HashSet<string>()).Etag;
    private static string Hash(string text) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static TaskItem Item(string id)
    {
        var task = new TaskItem { Id = id, UserId = "synthetic", Title = id, Description = "", Status = DomainTaskStatus.Prepared,
            CreatedDateTime = Epoch, UpdatedDateTime = Epoch, UnlockedDateTime = Epoch, IsCanBeCompleted = true };
        task.EnsureStatusHistory("synthetic"); return task;
    }
    private static void Contains(TaskItem parent, TaskItem child) { parent.ContainsTasks.Add(child.Id); child.ParentTasks.Add(parent.Id); }
    private static async Task<bool> SaveOnce(string path, string text)
    {
        if (File.Exists(path))
        {
            if (await File.ReadAllTextAsync(path) != text) throw new InvalidDataException("Application result identity conflict.");
            return false;
        }
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text)); return true;
    }
    private static async Task<CliResult> RunCli(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        start.Environment.Remove("UNLIMOTION_TASKS");
        start.ArgumentList.Add(typeof(global::Unlimotion.Cli.Program).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start CLI.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var stdout = await output; var stderr = await error;
        try { return new(process.ExitCode, JsonDocument.Parse(stdout).RootElement.Clone()); }
        catch (JsonException) { throw new InvalidDataException($"CLI returned invalid JSON: {stdout}; stderr: {stderr}"); }
    }
    private sealed record CliResult(int ExitCode, JsonElement Json);
    private sealed class Ledger
    {
        public string BaselineHash { get; set; } = "";
        public bool BaselineReset { get; set; }
        public Dictionary<string, string> Materials { get; set; } = new();
        public Dictionary<string, string> Decisions { get; set; } = new();
        public List<string> PreparationQueue { get; set; } = new();
        public List<string> ExecutionQueue { get; set; } = new();
        public List<string> RevalidationQueue { get; set; } = new();
        public int ExperimentRuns { get; set; }
    }
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unlimotion-night-replay-" + Guid.NewGuid().ToString("N"));
        public string Tasks => Path.Combine(Root, "tasks");
        public string External => Path.Combine(Root, "external-workflow");
        public FileTaskStorage Storage { get; }
        public TaskApplicationCommandService Service { get; }
        public string SourceKey => NightAgentSnapshotCodec.CreateSourceKey(Tasks);
        public Workspace()
        {
            Directory.CreateDirectory(Tasks); Directory.CreateDirectory(External);
            Storage = new(new() { Path = Tasks, CreateDirectoryIfMissing = false }); Service = new(Storage, Etag);
        }
        public string SnapshotPath(string name) => Path.Combine(External, name + ".snapshot.json");
        public async Task<NightAgentSnapshotArtifact> Capture(string name, NightAgentSnapshotSelection selection)
        {
            var artifact = NightAgentSnapshotCodec.CreateArtifact(await Storage.ReadObservationAsync(), selection, SourceKey);
            await File.WriteAllTextAsync(SnapshotPath(name), JsonSerializer.Serialize(artifact, NightAgentSnapshotCodec.JsonOptions));
            return await NightAgentSnapshotCodec.LoadArtifactAsync(SnapshotPath(name));
        }
        public Task SaveLedger(Ledger ledger) => AtomicWrite(Path.Combine(External, "ledger.json"), ledger);
        public async Task<Ledger> ReadLedger() => JsonSerializer.Deserialize<Ledger>(await File.ReadAllTextAsync(Path.Combine(External, "ledger.json")))!;
        public Task SaveCheckpoint(object checkpoint) => AtomicWrite(Path.Combine(External, "queue-checkpoint.json"), checkpoint);
        public string[] TaskBytes() => Directory.GetFiles(Tasks).Where(path => !Path.GetFileName(path).StartsWith('.')).Order(StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path) + ":" + Convert.ToHexString(File.ReadAllBytes(path))).ToArray();
        private static async Task AtomicWrite(string path, object value)
        {
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, path, true);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
