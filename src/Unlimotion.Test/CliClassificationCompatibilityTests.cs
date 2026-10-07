using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

// RunApply writes to process-wide Console; no other test may share those writers.
[NotInParallel]
public sealed class CliClassificationCompatibilityTests
{
    [Test]
    public async Task InitialComposedCreate_PostcommitAreaRace_ReportsOutcomeUnknownAndWritesNoReceipt()
    {
        using var fixture = new Fixture();
        var requestPath = Path.Combine(fixture.Root, "request.json");
        // Keep request outside the task directory so it cannot be mistaken for a task.
        var tasks = Path.Combine(fixture.Root, "tasks"); Directory.CreateDirectory(tasks);
        await File.WriteAllTextAsync(requestPath, """
            {"schemaVersion":1,"applicationId":"areaids-race","proposalRefs":[{"id":"P-areaids","revision":1}],
            "author":"spec-agent","reason":"Deterministic initial composed create race","preconditions":[],"operations":[
            {"operationId":"create","kind":"createTask","newTaskId":"created","title":"Before"},
            {"operationId":"rename","kind":"setField","taskId":"created","field":"title","value":"After"}]}
            """);
        var storage = new RacingStorage(new FileTaskStorageOptions
            { Path = tasks, PreserveUnknownJson = true, UseDirectoryLock = true });
        await Assert.That(Directory.GetFiles(tasks)).IsEmpty();
        var options = CliOptions.Parse(["apply", "--tasks", tasks, "--request", requestPath, "--format", "json"]);
        var runApply = typeof(global::Unlimotion.Cli.Program).GetMethod("RunApply", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("CLI RunApply fixture entry point changed.");
        var (exitCode, output) = await CaptureRunApply(runApply, options, storage);
        await Assert.That(storage.CommitCount).IsEqualTo(1);
        await Assert.That(exitCode).IsEqualTo(1).Because(output.GetRawText());
        await Assert.That(output.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(output.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("outcomeUnknown");
        await Assert.That(output.GetProperty("receiptWritten").GetBoolean()).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(tasks, ".unlimotion.applies"))).IsFalse();
        var raw = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(tasks, "created")));
        await Assert.That((string?)raw["Title"]).IsEqualTo("After");
        await Assert.That(raw["AreaIds"]!.ToString(Newtonsoft.Json.Formatting.None)).IsEqualTo("[\"external-area\"]");
        var observed = await storage.ReadGraphAsync();
        await Assert.That(string.Join(",", observed.TasksById["created"].AreaIds)).IsEqualTo("external-area");
        await Assert.That(observed.TasksById["created"].Title).IsEqualTo("After");
        await Assert.That(output.GetProperty("authoritativeTasks").EnumerateArray().Any()).IsTrue();
    }

    [Test]
    public async Task SharedClone_ConsumedByCli_PreservesOriginalAreasAndUnknownMetadataWhileSanitizingRetiredKey()
    {
        var task = new TaskItem
        {
            Id = "programmatic", AreaIds = ["area-b", "area-a", "area-b"],
            ExtensionData = new Dictionary<string, JToken>
            {
                ["IsGoal"] = true, ["isGoal"] = false, ["ISGOAL"] = true,
                ["Custom"] = new JObject { ["IsGoal"] = true, ["label"] = "keep" }, ["Extra"] = new JArray(7, "opaque")
            }
        };
        var originalAreas = task.AreaIds;
        var originalMetadata = JObject.FromObject(task.ExtensionData).ToString();
        var clone = TaskItemSnapshot.Clone(task);
        await Assert.That(ReferenceEquals(clone.AreaIds, originalAreas)).IsFalse();
        await Assert.That(string.Join(",", clone.AreaIds)).IsEqualTo("area-b,area-a,area-b");
        await Assert.That(clone.ExtensionData!.Keys.Any(k => k.Equals("IsGoal", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(JToken.DeepEquals(clone.ExtensionData["Custom"], task.ExtensionData["Custom"])).IsTrue();
        await Assert.That(JToken.DeepEquals(clone.ExtensionData["Extra"], task.ExtensionData["Extra"])).IsTrue();
        clone.AreaIds.Add("clone-only");
        await Assert.That(string.Join(",", task.AreaIds)).IsEqualTo("area-b,area-a,area-b");
        await Assert.That(JObject.FromObject(task.ExtensionData).ToString()).IsEqualTo(originalMetadata);
        task.AreaIds = null!;
        var normalized = TaskItemSnapshot.Clone(task);
        await Assert.That(normalized.AreaIds).IsEmpty();
        await Assert.That(task.AreaIds).IsNull();
    }

#pragma warning disable TUnit0055
    private static async Task<(int ExitCode, JsonElement Output)> CaptureRunApply(MethodInfo method, CliOptions options, FileTaskStorage storage)
    {
        var oldOutput = Console.Out; var oldError = Console.Error;
        using var output = new StringWriter(); using var error = new StringWriter();
        try
        {
            Console.SetOut(output); Console.SetError(error);
            var invocation = (Task<int>?)method.Invoke(null, [options, storage])
                ?? throw new InvalidOperationException("RunApply did not return a task.");
            var exitCode = await invocation;
            using var json = JsonDocument.Parse(output.ToString());
            return (exitCode, json.RootElement.Clone());
        }
        finally { Console.SetOut(oldOutput); Console.SetError(oldError); }
    }
#pragma warning restore TUnit0055

    // Reimplement only the scope interface. Real saves and commit run through FileTaskStorage;
    // the external byte edit occurs deterministically after commit, before service read-back.
    private sealed class RacingStorage(FileTaskStorageOptions options) : FileTaskStorage(options), ITaskGraphWriteScopeStorage
    {
        public int CommitCount { get; private set; }
        ITaskGraphWriteScope ITaskGraphWriteScopeStorage.BeginWriteScope() => new RacingScope(this, (IRecoverableTaskGraphWriteScope)base.BeginWriteScope());
        Task<TaskGraphReadResult> ITaskGraphWriteScopeStorage.RefreshAttemptedWritesAsync(ITaskGraphWriteScope scope) =>
            base.RefreshAttemptedWritesAsync(scope is RacingScope racing ? racing.Inner : scope);
        private sealed class RacingScope(RacingStorage owner, IRecoverableTaskGraphWriteScope inner) : IRecoverableTaskGraphWriteScope
        {
            public IRecoverableTaskGraphWriteScope Inner => inner;
            public IReadOnlyList<string> AttemptedTaskIds => inner.AttemptedTaskIds;
            public async Task CommitAsync()
            {
                await inner.CommitAsync(); owner.CommitCount++;
                var path = Path.Combine(owner.Path, "created");
                var json = JObject.Parse(await File.ReadAllTextAsync(path));
                if ((string?)json["Title"] != "After" || ((JArray)json["AreaIds"]!).Count != 0)
                    throw new InvalidOperationException("R1 requires initial composed create with default areas before the race.");
                json["AreaIds"] = new JArray("external-area");
                await File.WriteAllTextAsync(path, json.ToString());
                owner.InvalidateLiveGraph();
            }
            public Task RollbackAsync() => inner.RollbackAsync();
            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unlimotion-classification-" + Guid.NewGuid().ToString("N")));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "unlimotion-classification-");
            if (!Root.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
