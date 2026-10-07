using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed partial class UnlimotionCliIntegrationTests
{
    [Test]
    [Arguments("[\"area-b\",\"area-a\",\"area-b\"]", "IsGoal", true)]
    [Arguments("[]", "isGoal", false)]
    [Arguments("[]", "ISGOAL", true)]
    [Arguments("null", "iSgOaL", true)]
    public async Task AreaIds_TitleSave_PreservesAreasAndNestedMetadata_WhileReadsLeaveLegacyBytes(
        string areas, string retiredKey, bool retiredValue)
    {
        using var tasks = TempTaskDirectory.Create();
        using var request = TempRequestFile.Create();
        using var witness = TempRequestFile.Create();
        await SaveTasks(tasks.DirectoryPath, CreateTask("created", DomainTaskStatus.Prepared, true, "After"));
        var path = Path.Combine(tasks.DirectoryPath, "created");
        var raw = JObject.Parse(await File.ReadAllTextAsync(path));
        raw["AreaIds"] = JToken.Parse(areas); raw[retiredKey] = retiredValue;
        raw["Custom"] = new JObject { ["IsGoal"] = true, ["label"] = "keep" };
        raw["Extra"] = new JArray("opaque", 7);
        await File.WriteAllTextAsync(path, raw.ToString());
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = FixtureFiles(tasks.DirectoryPath);
        var task = await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "created", "--include", "details", "--format", "json");
        await Assert.That(task.ExitCode).IsEqualTo(0).Because(task.StdOut + task.StdErr);
        await File.WriteAllTextAsync(request.Path, AreaTitleRequest(ParseJson(task.StdOut).GetProperty("etag").GetString()!));
        var inspect = await RunCli("apply", "inspect", "--tasks", tasks.DirectoryPath, "--request", request.Path, "--format", "json");
        await Assert.That(inspect.ExitCode).IsEqualTo(0).Because(inspect.StdOut + inspect.StdErr);
        var preview = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", request.Path,
            "--dry-run", "--diff", "full", "--format", "json");
        await Assert.That(preview.ExitCode).IsEqualTo(0).Because(preview.StdOut + preview.StdErr);
        var body = ParseJson(preview.StdOut).GetProperty("preview");
        await Assert.That(body.GetProperty("contracts").GetProperty("projection").GetInt32()).IsEqualTo(2);
        var changes = body.GetProperty("changes").EnumerateArray().ToArray();
        await Assert.That(changes.Any(c => c.GetProperty("path").GetString() == "/details/areaIds")).IsFalse();
        await Assert.That(changes.Any(c => c.GetProperty("path").GetString() == "/details/unknownFieldsHash")).IsFalse();
        await Assert.That(changes.Any(c => c.GetProperty("path").GetString()!.Contains("isGoal", StringComparison.OrdinalIgnoreCase))).IsFalse();
        var title = changes.Single(c => c.GetProperty("path").GetString() == "/details/title");
        await Assert.That(title.GetProperty("before").GetString()).IsEqualTo("After");
        await Assert.That(title.GetProperty("after").GetString()).IsEqualTo("Reviewed title");
        await Assert.That(FixtureFiles(tasks.DirectoryPath)).IsEquivalentTo(before);
        await File.WriteAllTextAsync(witness.Path, preview.StdOut);
        var apply = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", request.Path,
            "--expect-preview", witness.Path, "--format", "json");
        await Assert.That(apply.ExitCode).IsEqualTo(0).Because(apply.StdOut + apply.StdErr);
        var applied = ParseJson(apply.StdOut);
        await Assert.That(applied.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(applied.GetProperty("didMutate").GetBoolean()).IsTrue();
        await Assert.That(applied.GetProperty("receiptWritten").GetBoolean()).IsTrue();
        var saved = JObject.Parse(await File.ReadAllTextAsync(path));
        await Assert.That((string?)saved["Title"]).IsEqualTo("Reviewed title");
        await Assert.That(saved["AreaIds"]!.ToString(Newtonsoft.Json.Formatting.None)).IsEqualTo(areas == "null" ? "[]" : areas);
        await Assert.That(saved.Properties().Any(p => p.Name.Equals("IsGoal", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(JToken.DeepEquals(saved["Custom"], raw["Custom"])).IsTrue();
        await Assert.That(JToken.DeepEquals(saved["Extra"], raw["Extra"])).IsTrue();
        await Assert.That((await LoadTask(tasks.DirectoryPath, "created")).Title).IsEqualTo("Reviewed title");
    }

    [Test]
    [Arguments("areas")]
    [Arguments("retiredValue")]
    [Arguments("retiredCase")]
    public async Task AreaIds_ByteOnlyClassificationDrift_RejectsGuardWithEqualSemanticEtag(string variant)
    {
        using var tasks = TempTaskDirectory.Create();
        using var request = TempRequestFile.Create();
        using var witness = TempRequestFile.Create();
        await SaveTasks(tasks.DirectoryPath, CreateTask("created", DomainTaskStatus.Prepared, true, "After"));
        var path = Path.Combine(tasks.DirectoryPath, "created");
        var raw = JObject.Parse(await File.ReadAllTextAsync(path));
        raw["AreaIds"] = variant == "areas" ? JValue.CreateNull() : new JArray();
        raw["IsGoal"] = true;
        await File.WriteAllTextAsync(path, raw.ToString());
        var initial = await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "created", "--include", "details", "--format", "json");
        await Assert.That(initial.ExitCode).IsEqualTo(0).Because(initial.StdOut + initial.StdErr);
        var etag = ParseJson(initial.StdOut).GetProperty("etag").GetString()!;
        await File.WriteAllTextAsync(request.Path, AreaTitleRequest(etag));
        var preview = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", request.Path,
            "--dry-run", "--diff", "full", "--format", "json");
        await Assert.That(preview.ExitCode).IsEqualTo(0).Because(preview.StdOut + preview.StdErr);
        await File.WriteAllTextAsync(witness.Path, preview.StdOut);
        if (variant == "areas") raw["AreaIds"] = new JArray();
        else if (variant == "retiredValue") raw["IsGoal"] = false;
        else { raw.Remove("IsGoal"); raw["ISGOAL"] = true; }
        await File.WriteAllTextAsync(path, raw.ToString());
        var before = FixtureFiles(tasks.DirectoryPath);
        var drifted = await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "created", "--include", "details", "--format", "json");
        await Assert.That(drifted.ExitCode).IsEqualTo(0).Because(drifted.StdOut + drifted.StdErr);
        await Assert.That(ParseJson(drifted.StdOut).GetProperty("etag").GetString()).IsEqualTo(etag);
        var apply = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", request.Path,
            "--expect-preview", witness.Path, "--format", "json");
        await Assert.That(apply.ExitCode).IsEqualTo(1).Because(apply.StdOut + apply.StdErr);
        await AssertJsonError(apply.StdOut, "previewStale");
        await Assert.That(FixtureFiles(tasks.DirectoryPath)).IsEquivalentTo(before);
    }

    [Test]
    public async Task AreaIds_NestedGoalMetadata_RemainsSemanticallySignificant()
    {
        using var tasks = TempTaskDirectory.Create();
        await SaveTasks(tasks.DirectoryPath, CreateTask("created", DomainTaskStatus.Prepared, true, "After"));
        var path = Path.Combine(tasks.DirectoryPath, "created");
        var raw = JObject.Parse(await File.ReadAllTextAsync(path));
        raw["Custom"] = new JObject { ["IsGoal"] = true };
        await File.WriteAllTextAsync(path, raw.ToString());
        var first = await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "created", "--include", "details", "--format", "json");
        await Assert.That(first.ExitCode).IsEqualTo(0).Because(first.StdOut + first.StdErr);
        raw["Custom"]!["IsGoal"] = false;
        await File.WriteAllTextAsync(path, raw.ToString());
        var second = await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "created", "--include", "details", "--format", "json");
        await Assert.That(second.ExitCode).IsEqualTo(0).Because(second.StdOut + second.StdErr);
        await Assert.That(ParseJson(first.StdOut).GetProperty("etag").GetString())
            .IsNotEqualTo(ParseJson(second.StdOut).GetProperty("etag").GetString());
    }

    private static string AreaTitleRequest(string etag) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, applicationId = "areaids-title-edit", proposalRefs = new[] { new { id = "P-areaids", revision = 1 } },
        author = "spec-agent", reason = "Isolated area preservation and retired key fixture",
        preconditions = new[] { new { taskId = "created", etag } },
        operations = new[] { new { operationId = "rename", kind = "setField", taskId = "created", field = "title", value = "Reviewed title" } }
    });

    [Test]
    [Arguments(true, "IsGoal")]
    [Arguments(false, "ISGOAL")]
    public async Task AreaIds_LegacyJournalImages_AreNotReplayedOrCleanedByInspectOrFullPreview(bool committed, string key)
    {
        using var tasks = TempTaskDirectory.Create();
        using var request = TempRequestFile.Create();
        await SaveTasks(tasks.DirectoryPath, CreateTask("created", DomainTaskStatus.Prepared, true, "Partial"));
        var path = Path.Combine(tasks.DirectoryPath, "created");
        var raw = JObject.Parse(await File.ReadAllTextAsync(path));
        raw["AreaIds"] = JValue.CreateNull(); raw[key] = true;
        await File.WriteAllTextAsync(path, raw.ToString());
        var beforeImage = (JObject)raw.DeepClone(); beforeImage["Title"] = "Before"; beforeImage[key] = false;
        var afterImage = (JObject)raw.DeepClone(); afterImage["Title"] = "After"; afterImage["AreaIds"] = new JArray("area-a");
        var journalDirectory = Path.Combine(tasks.DirectoryPath, ".unlimotion.transactions"); Directory.CreateDirectory(journalDirectory);
        await File.WriteAllTextAsync(Path.Combine(journalDirectory, "pending.json"), JsonSerializer.Serialize(new
        {
            Id = "legacy-areaids", Committed = committed,
            Entries = new[] { new
            {
                TaskId = "created", FilePath = path, BeforeExists = true, AfterExists = true,
                BeforeBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(beforeImage.ToString())),
                AfterBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(afterImage.ToString()))
            } }
        }));
        var receipts = Path.Combine(tasks.DirectoryPath, ".unlimotion.applies", "v1"); Directory.CreateDirectory(receipts);
        await File.WriteAllTextAsync(Path.Combine(receipts, "unrelated.json"), "{\"applicationId\":\"unrelated\"}");
        await File.WriteAllTextAsync(request.Path, AreaCreateRequest(composed: true));
        foreach (var file in Directory.GetFiles(tasks.DirectoryPath, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = FixtureFiles(tasks.DirectoryPath);
        foreach (var inspect in new[] { true, false })
        {
            var args = new System.Collections.Generic.List<string> { "apply" };
            if (inspect) args.Add("inspect");
            args.AddRange(["--tasks", tasks.DirectoryPath, "--request", request.Path, "--format", "json"]);
            if (!inspect) args.AddRange(["--dry-run", "--diff", "full"]);
            var result = await RunCli(args.ToArray());
            await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.StdOut + result.StdErr);
            await AssertJsonError(result.StdOut, "recoveryRequired");
            await Assert.That(FixtureFiles(tasks.DirectoryPath)).IsEquivalentTo(before);
            await Assert.That(File.Exists(Path.Combine(tasks.DirectoryPath, ".unlimotion.lock"))).IsFalse();
        }
    }

    [Test]
    public async Task FullPreview_CanBeSavedAppliedAndValidatedBeforeReceiptShortcut()
    {
        using var tasks = TempTaskDirectory.Create();
        using var requestFile = TempRequestFile.Create();
        using var witnessFile = TempRequestFile.Create();
        var target = CreateTask("target", DomainTaskStatus.Prepared, true, "До");
        await SaveTasks(tasks.DirectoryPath, target);
        var task = ParseJson((await RunCli("task", "--tasks", tasks.DirectoryPath, "--id", "target", "--include", "details", "--format", "json")).StdOut);
        await File.WriteAllTextAsync(requestFile.Path, DateApplicationRequest(
            """{"operationId":"title","kind":"setField","taskId":"target","field":"title","value":"После"}""",
            JsonSerializer.Serialize(new[] { new { taskId = "target", etag = task.GetProperty("etag").GetString() } })));
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(tasks.DirectoryPath, "target"));
        var plain = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--dry-run", "--format", "json");
        await Assert.That(ParseJson(plain.StdOut).TryGetProperty("preview", out _)).IsFalse();
        var preview = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--dry-run", "--diff", "full", "--format", "json");
        await Assert.That(preview.ExitCode).IsEqualTo(0).Because(preview.StdOut + preview.StdErr);
        var body = ParseJson(preview.StdOut);
        await Assert.That(body.GetProperty("preview").GetProperty("complete").GetBoolean()).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(tasks.DirectoryPath, "target"))).SequenceEqual(originalBytes)).IsTrue();
        await File.WriteAllTextAsync(witnessFile.Path, preview.StdOut);
        await File.AppendAllTextAsync(Path.Combine(tasks.DirectoryPath, "target"), "\n ");
        var driftedBytes = await File.ReadAllBytesAsync(Path.Combine(tasks.DirectoryPath, "target"));
        var stale = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--expect-preview", witnessFile.Path, "--format", "json");
        await Assert.That(stale.ExitCode).IsEqualTo(1).Because(stale.StdOut + stale.StdErr);
        await AssertJsonError(stale.StdOut, "previewStale");
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(tasks.DirectoryPath, "target"))).SequenceEqual(driftedBytes)).IsTrue();
        await File.WriteAllBytesAsync(Path.Combine(tasks.DirectoryPath, "target"), originalBytes);
        var applied = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--expect-preview", witnessFile.Path, "--format", "json");
        await Assert.That(applied.ExitCode).IsEqualTo(0).Because(applied.StdOut + applied.StdErr);
        await Assert.That((await LoadTask(tasks.DirectoryPath, "target")).Title).IsEqualTo("После");
        var repeated = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--expect-preview", witnessFile.Path, "--format", "json");
        await Assert.That(ParseJson(repeated.StdOut).GetProperty("mode").GetString()).IsEqualTo("alreadyApplied");
        var tampered = JObject.Parse(preview.StdOut);
        ((JArray)tampered["preview"]!["changes"]!).RemoveAt(0);
        await File.WriteAllTextAsync(witnessFile.Path, tampered.ToString());
        var rejected = await RunCli("apply", "--tasks", tasks.DirectoryPath, "--request", requestFile.Path, "--expect-preview", witnessFile.Path, "--format", "json");
        await Assert.That(rejected.ExitCode).IsEqualTo(1);
        await AssertJsonError(rejected.StdOut, "previewInvalid");
    }

    [Test]
    public async Task PreviewAndInspect_LeavePendingJournalAndSourceBytesUntouched()
    {
        using var tasks = TempTaskDirectory.Create();
        using var request = TempRequestFile.Create();
        await SaveTasks(tasks.DirectoryPath, CreateTask("target", DomainTaskStatus.Prepared, true));
        await File.WriteAllTextAsync(request.Path, DateApplicationRequest(
            """{"operationId":"new","kind":"createTask","newTaskId":"new","title":"New"}""", "[]"));
        var journalPath = Path.Combine(tasks.DirectoryPath, ".unlimotion.transactions");
        Directory.CreateDirectory(journalPath);
        var journal = Path.Combine(journalPath, "pending.json");
        await File.WriteAllTextAsync(journal, "unreadable pending journal must never be replayed by observation");
        var targetPath = Path.Combine(tasks.DirectoryPath, "target");
        var receiptDirectory = Path.Combine(tasks.DirectoryPath, ".unlimotion.applies", "v1");
        Directory.CreateDirectory(receiptDirectory);
        var receiptPath = Path.Combine(receiptDirectory, "unrelated-receipt.json");
        const string receiptText = "{\"applicationId\":\"unrelated-existing-receipt\"}";
        await File.WriteAllTextAsync(receiptPath, receiptText);
        var persistedPaths = new[] { targetPath, journal, receiptPath };
        foreach (var path in persistedPaths)
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var beforeTimes = persistedPaths.ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.Ordinal);
        var before = await File.ReadAllBytesAsync(targetPath);
        foreach (var variant in new[] { "plain", "full", "inspect" })
        {
            var args = new System.Collections.Generic.List<string> { "apply" };
            if (variant == "inspect") args.Add("inspect");
            args.AddRange(["--tasks", tasks.DirectoryPath, "--request", request.Path, "--format", "json"]);
            if (variant != "inspect") args.Add("--dry-run");
            if (variant == "full") args.AddRange(["--diff", "full"]);
            var result = await RunCli(args.ToArray());
            await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.StdOut + result.StdErr);
            await AssertJsonError(result.StdOut, "recoveryRequired");
            await Assert.That((await File.ReadAllBytesAsync(targetPath)).SequenceEqual(before)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(journal)).IsEqualTo("unreadable pending journal must never be replayed by observation");
            await Assert.That(await File.ReadAllTextAsync(receiptPath)).IsEqualTo(receiptText);
            await Assert.That(Directory.GetFiles(receiptDirectory, "*.json").Length).IsEqualTo(1);
            foreach (var path in persistedPaths)
                await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(beforeTimes[path]);
            await Assert.That(File.Exists(Path.Combine(tasks.DirectoryPath, "new"))).IsFalse();
        }
    }

    [Test]
    public async Task SnapshotSearch_RemainsOfflineAndPreservesLegacyShape()
    {
        using var tasks = TempTaskDirectory.Create();
        using var selection = TempRequestFile.Create();
        using var artifact = TempRequestFile.Create();
        var target = CreateTask("content", DomainTaskStatus.Archived, true, "Завершённая подготовка");
        target.Description = "Решение уже записано: café";
        await SaveTasks(tasks.DirectoryPath, target);
        var legacy = ParseJson((await RunCli("search", "--tasks", tasks.DirectoryPath, "--query", "content", "--format", "json")).StdOut);
        await Assert.That(legacy.EnumerateObject().Select(p => p.Name).Order().ToArray()).IsEquivalentTo(new[] { "items", "nextCursor", "totalCount" });
        await File.WriteAllTextAsync(selection.Path, """{"schemaVersion":1,"select":{"mode":"all"},"context":"none","include":["details","criteria"]}""");
        var capture = await RunCli("snapshot", "capture", "--selection", selection.Path, "--output", artifact.Path, "--tasks", tasks.DirectoryPath, "--format", "json");
        await Assert.That(capture.ExitCode).IsEqualTo(0).Because(capture.StdOut + capture.StdErr);
        File.Delete(Path.Combine(tasks.DirectoryPath, target.Id));
        var search = await RunCliWithEnvironment(Path.Combine(tasks.DirectoryPath, "missing-source"), null,
            "search", "--snapshot", artifact.Path, "--fields", "description", "--query", "cafe\u0301", "--format", "json");
        await Assert.That(search.ExitCode).IsEqualTo(0).Because(search.StdOut + search.StdErr);
        var result = ParseJson(search.StdOut);
        await Assert.That(result.GetProperty("searchVersion").GetInt32()).IsEqualTo(2);
        await Assert.That(result.GetProperty("items")[0].GetProperty("id").GetString()).IsEqualTo("content");
        await Assert.That(result.GetProperty("source").GetProperty("consistency").GetString()).IsEqualTo("immutableArtifact");
        await Assert.That(File.Exists(Path.Combine(tasks.DirectoryPath, target.Id))).IsFalse();
    }

    [Test]
    public async Task NightSchemasAndHelp_AreDiscoverableWithoutTaskSettings()
    {
        foreach (var kind in new[] { "selection", "artifact", "page", "delta" })
        {
            var schema = await RunCliWithEnvironment("not-an-existing-task-directory", null,
                "snapshot", "schema", "--kind", kind, "--format", "json");
            await Assert.That(schema.ExitCode).IsEqualTo(0).Because(schema.StdOut + schema.StdErr);
            await Assert.That(ParseJson(schema.StdOut).GetProperty("$schema").GetString()).Contains("json-schema.org");
        }
        var preview = await RunCliWithEnvironment("not-an-existing-task-directory", null,
            "apply", "schema", "--kind", "preview", "--format", "json");
        await Assert.That(preview.ExitCode).IsEqualTo(0).Because(preview.StdOut);
        var previewSchema = ParseJson(preview.StdOut);
        await Assert.That(previewSchema.GetProperty("properties").GetProperty("preview").GetProperty("properties")
            .GetProperty("contracts").GetProperty("properties").GetProperty("projection").GetProperty("const").GetInt32()).IsEqualTo(2);
        await Assert.That(previewSchema.GetProperty("$defs").GetProperty("areaIds").GetProperty("items").GetProperty("type").GetString()).IsEqualTo("string");
        var help = await RunCli("help", "apply", "--format", "json");
        await Assert.That(help.ExitCode).IsEqualTo(0).Because(help.StdOut + help.StdErr);
        await Assert.That(help.StdOut).Contains("Projection 2 includes areaIds");
        await Assert.That(ParseJson(help.StdOut).GetProperty("optionalOptions").EnumerateArray().Select(p => p.GetString()).Contains("--expect-preview")).IsTrue();
    }
}
