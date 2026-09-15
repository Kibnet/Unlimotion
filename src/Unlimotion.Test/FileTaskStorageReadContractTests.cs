using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using FileTaskStorage = Unlimotion.Storage.FileTaskStorage;
using FileTaskStorageOptions = Unlimotion.Storage.FileTaskStorageOptions;

namespace Unlimotion.Test;

[NotInParallel]
public sealed class FileTaskStorageReadContractTests
{
    [Test]
    public async Task ReadDirectory_RepeatedModelsStayWithinAllocationBudget()
    {
        using var fixture = new Fixture();
        for (var index = 0; index < 128; index++)
            await File.WriteAllTextAsync(Path.Combine(fixture.Path, $"task-{index}"), RichJson($"task-{index}"));

        var storage = fixture.CreateStorage(preserveUnknown: true);
        await storage.ReadDirectoryAsync();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = await storage.ReadDirectoryAsync();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        await Assert.That(result.Tasks.Count).IsEqualTo(128);
        await Assert.That(result.LoadErrors).IsEmpty();
        // A repeated type must not rebuild its reflection contracts for every file.
        // This deliberately allows much more than the task payload and cloning allocations.
        await Assert.That(allocated).IsLessThan(16L * 1024 * 1024);
    }

    [Test]
    [Arguments(true, "+03:00")]
    [Arguments(false, "+03:00")]
    [Arguments(true, "-05:30")]
    [Arguments(false, "-05:30")]
    [Arguments(true, "+00:00")]
    [Arguments(false, "+00:00")]
    public async Task ReadAndSave_PreserveKnownFieldsAndRespectUnknownJsonPolicy(bool preserveUnknown, string offset)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "task.json");
        var json = RichJson("task", offset);
        var expectedBegin = DateTimeOffset.Parse("2026-02-01T12:00:00.000" + offset, CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(path, json);
        var storage = fixture.CreateStorage(preserveUnknown);
        var loaded = (await storage.ReadDirectoryAsync()).Tasks.Single();

        await Assert.That(loaded.Title).IsEqualTo("Задача 🧭");
        await Assert.That(loaded.Status).IsEqualTo(Domain.TaskStatus.Prepared);
        // The existing JsonTextReader/IsoDateTimeConverter path normalizes explicit offsets
        // to the host zone. Preserve the instant, not the fixture's original offset.
        await Assert.That(loaded.PlannedBeginDateTime!.Value.UtcDateTime).IsEqualTo(expectedBegin.UtcDateTime);
        await Assert.That(loaded.PlannedBeginDateTime.Value.Offset).IsEqualTo(expectedBegin.ToLocalTime().Offset);
        await Assert.That(loaded.PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(90));
        await Assert.That(loaded.CompletionCriteria.Single().Text).IsEqualTo("Готово");
        await Assert.That(loaded.StatusHistory.Single().Author).IsEqualTo("author");
        await Assert.That(loaded.Repeater!.Type).IsEqualTo(RepeaterType.Weekly);
        await Assert.That(loaded.ContainsTasks.SequenceEqual(new[] { "child-b", "child-a" })).IsTrue();
        await Assert.That(loaded.ExtensionData?.ContainsKey("FutureTask") == true).IsEqualTo(preserveUnknown);
        await Assert.That(loaded.Repeater.ExtensionData?.ContainsKey("FutureRepeater") == true).IsEqualTo(preserveUnknown);
        await Assert.That(loaded.StatusHistory.Single().ExtensionData?.ContainsKey("FutureHistory") == true).IsEqualTo(preserveUnknown);
        await Assert.That(loaded.CompletionCriteria.Single().ExtensionData?.ContainsKey("FutureCriterion") == true).IsEqualTo(preserveUnknown);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(json);

        await storage.Save(loaded);
        var reloaded = (await fixture.CreateStorage(preserveUnknown).ReadDirectoryAsync()).Tasks.Single();
        await Assert.That(reloaded.PlannedBeginDateTime!.Value.UtcDateTime).IsEqualTo(expectedBegin.UtcDateTime);
        await Assert.That(reloaded.PlannedBeginDateTime.Value.Offset).IsEqualTo(expectedBegin.ToLocalTime().Offset);
        await Assert.That(JToken.DeepEquals(JToken.FromObject(loaded), JToken.FromObject(reloaded))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.Path, "task"))).IsFalse();
    }

    [Test]
    public async Task ConcurrentReads_KeepPoliciesAndReturnedModelsIsolated()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "task"), RichJson("task"));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
        {
            var preserve = index % 2 == 0;
            var task = await fixture.CreateStorage(preserve).Load("task", forced: true);
            return (preserve, task: task!);
        }));

        foreach (var result in results)
        {
            await Assert.That(result.task.ExtensionData?.ContainsKey("FutureTask") == true).IsEqualTo(result.preserve);
            await Assert.That(result.task.CompletionCriteria.Single().ExtensionData?.ContainsKey("FutureCriterion") == true)
                .IsEqualTo(result.preserve);
        }
        results[0].task.ContainsTasks.Clear();
        results[0].task.CompletionCriteria[0].Text = "Changed locally";
        results[0].task.ExtensionData!["FutureTask"]["value"] = "changed";
        await Assert.That(results[2].task.ContainsTasks.Count).IsEqualTo(2);
        await Assert.That(results[2].task.CompletionCriteria[0].Text).IsEqualTo("Готово");
        await Assert.That(results[2].task.ExtensionData!["FutureTask"]["value"]!.Value<string>()).IsEqualTo("kept");
    }

    [Test]
    public async Task ReadDirectory_RepairsMissingCommaWithoutWritingAndKeepsDiagnostics()
    {
        using var fixture = new Fixture();
        var repairPath = Path.Combine(fixture.Path, "repair");
        var repairJson = "{\"Id\":\"repair\" \"Title\":\"Recovered\",\"CreatedDateTime\":\"2026-01-01T00:00:00.000+00:00\"}";
        await File.WriteAllTextAsync(repairPath, repairJson);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "bad"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "duplicate-a"), RichJson("duplicate"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "duplicate-b.json"), RichJson("duplicate"));
        var read = await fixture.CreateStorage(true).ReadDirectoryAsync();

        await Assert.That(read.Tasks.Single(task => task.Id == "repair").Title).IsEqualTo("Recovered");
        await Assert.That(read.LoadErrors.Count).IsEqualTo(1);
        await Assert.That(read.DuplicateIdIssues.Single().TaskId).IsEqualTo("duplicate");
        await Assert.That(await File.ReadAllTextAsync(repairPath)).IsEqualTo(repairJson);
        await Assert.That(Directory.GetFiles(fixture.Path, "*.repaired.json")).IsEmpty();
    }

    private static string RichJson(string id, string offset = "+03:00") => $$$"""
        {
          "Id":"{{{id}}}", "UserId":"owner", "Title":"Задача 🧭", "Description":"Строка\nс деталями",
          "Status":"Prepared", "CreatedDateTime":"2026-01-01T12:00:00.000{{{offset}}}",
          "PlannedBeginDateTime":"2026-02-01T12:00:00.000{{{offset}}}", "PlannedDuration":"01:30:00",
          "ContainsTasks":["child-b","child-a"], "ParentTasks":["parent"], "BlocksTasks":[], "BlockedByTasks":[],
          "StatusHistory":[{"Status":"Prepared","ChangedAt":"2026-01-01T12:00:00.000{{{offset}}}","Author":"author","FutureHistory":7}],
          "CompletionCriteria":[{"Id":"criterion","Text":"Готово","IsSatisfied":true,"FutureCriterion":{"x":1}}],
          "Repeater":{"Type":"Weekly","Period":2,"Pattern":[1,3],"AfterComplete":false,"FutureRepeater":[1,2]},
          "FutureTask":{"value":"kept"}
        }
        """;

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-read-contract-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public FileTaskStorage CreateStorage(bool preserveUnknown) => new(new FileTaskStorageOptions
        {
            Path = Path,
            PreserveUnknownJson = preserveUnknown
        });
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
