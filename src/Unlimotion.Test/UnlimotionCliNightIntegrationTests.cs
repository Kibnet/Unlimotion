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
        var help = await RunCli("help", "apply", "--format", "json");
        await Assert.That(ParseJson(help.StdOut).GetProperty("optionalOptions").EnumerateArray().Select(p => p.GetString()).Contains("--expect-preview")).IsTrue();
    }
}
