using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Cli;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskApplicationImportanceTests
{
    [Test]
    [Arguments("42", 42)]
    [Arguments("0", 0)]
    [Arguments("-2147483648", int.MinValue)]
    [Arguments("2147483647", int.MaxValue)]
    public async Task SetField_AppliesImportanceAndPreservesOtherBusinessFields(string value, int expected)
    {
        using var fixture = new Fixture();
        await fixture.Seed();
        var before = JObject.Parse(await File.ReadAllTextAsync(fixture.TaskPath));
        var request = await fixture.Request(Set(value));
        var result = await fixture.Service.TryApplyAsync(request);
        await Assert.That(result.Success).IsTrue().Because(result.Error?.Message ?? "Application must succeed.");
        await Assert.That((await fixture.Load()).Importance).IsEqualTo(expected);
        await Assert.That((await fixture.Service.InspectAsync(request)).PostconditionsMatch).IsEqualTo("all");
        var after = JObject.Parse(await File.ReadAllTextAsync(fixture.TaskPath));
        before.Remove("Importance"); after.Remove("Importance");
        before.Remove("UpdatedDateTime"); after.Remove("UpdatedDateTime");
        await Assert.That(JToken.DeepEquals(before, after)).IsTrue();
    }

    [Test]
    [Arguments("ru-RU")]
    [Arguments("en-US")]
    public async Task SetField_IsCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var fixture = new Fixture();
            await fixture.Seed();
            var applied = await fixture.Service.TryApplyAsync(await fixture.Request(Set("-42")));
            await Assert.That(applied.Success).IsTrue();
            await Assert.That((await fixture.Load()).Importance).IsEqualTo(-42);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments(" 42")]
    [Arguments("42 ")]
    [Arguments("42\n")]
    [Arguments("+42")]
    [Arguments("042")]
    [Arguments("-0")]
    [Arguments("-01")]
    [Arguments("42.0")]
    [Arguments("4e1")]
    [Arguments("42,0")]
    [Arguments("٤٢")]
    [Arguments("2147483648")]
    [Arguments("-2147483649")]
    public async Task SetField_RejectsInvalidImportanceWithoutWrites(string? value)
    {
        using var fixture = new Fixture();
        await fixture.Seed();
        var request = await fixture.Request(Set(value));
        var bytes = fixture.Files();
        var preview = await fixture.Service.PreviewPlanAsync(request);
        await Assert.That(preview.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.InvalidArguments);
        var apply = await fixture.Service.TryApplyAsync(request);
        await Assert.That(apply.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.InvalidArguments);
        await Assert.That(apply.Error.OperationId).IsEqualTo("importance");
        await Assert.That(apply.Error.TaskId).IsEqualTo("task");
        await Assert.That(fixture.Files()).IsEquivalentTo(bytes);
        await Assert.That(File.Exists(Path.Combine(fixture.Path, ".unlimotion.lock"))).IsFalse();
    }

    [Test]
    public async Task SetField_InvalidLaterOperationDoesNotCommitEarlierTitle()
    {
        using var fixture = new Fixture();
        await fixture.Seed();
        var request = await fixture.Request(
            new() { OperationId = "title", Kind = TaskApplicationOperationKind.SetField, TaskId = "task", Field = "title", Value = "Must not persist" },
            Set("2147483648"));
        var bytes = fixture.Files();
        var result = await fixture.Service.TryApplyAsync(request);
        await Assert.That(result.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.InvalidArguments);
        await Assert.That(fixture.Files()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task SetField_DuplicateImportancePreservesConflictingOperationsPolicy()
    {
        using var fixture = new Fixture();
        await fixture.Seed();
        var request = await fixture.Request(Set("42"), Set("43") with { OperationId = "second" });
        var bytes = fixture.Files();
        var result = await fixture.Service.TryApplyAsync(request);
        await Assert.That(result.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.ConflictingOperations);
        await Assert.That(fixture.Files()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task SetField_ClearIsUnsupportedAndSameValueDoesNotRewriteTask()
    {
        using var fixture = new Fixture();
        await fixture.Seed();
        var bytes = fixture.Files();
        var noOp = await fixture.Service.TryApplyAsync(await fixture.Request(Set("9")));
        await Assert.That(noOp.Success).IsTrue();
        await Assert.That(fixture.Files()).IsEquivalentTo(bytes);
        var clear = await fixture.Service.TryApplyAsync(await fixture.Request(
            new TaskApplicationOperation { OperationId = "clear", Kind = TaskApplicationOperationKind.ClearField, TaskId = "task", Field = "importance" }));
        await Assert.That(clear.Error!.Kind).IsEqualTo(TaskApplicationErrorKind.InvalidArguments);
        await Assert.That(fixture.Files()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task ComposedCreate_VerifiesFinalImportanceAndReconcilesWithoutReceipt()
    {
        using var fixture = new Fixture();
        var request = new TaskApplicationRequest
        {
            SchemaVersion = 1, ApplicationId = "created-importance", Author = "test-agent", Reason = "Approved fixture",
            ProposalRefs = [new("proposal", 1)], Preconditions = [],
            Operations =
            [
                new() { OperationId = "create", Kind = TaskApplicationOperationKind.CreateTask, NewTaskId = "task", Title = "Created" },
                Set("42")
            ]
        };
        var before = fixture.Files();
        var preview = await fixture.Service.PreviewPlanAsync(request);
        await Assert.That(preview.Success).IsTrue();
        await Assert.That(preview.Plan!.After["task"].Importance).IsEqualTo(42);
        await Assert.That(fixture.Files()).IsEquivalentTo(before);
        var applied = await fixture.Service.TryApplyAsync(request);
        await Assert.That(applied.Success).IsTrue().Because(applied.Error?.Message ?? "Composed creation must succeed.");
        await Assert.That((await fixture.Service.InspectAsync(request)).PostconditionsMatch).IsEqualTo("all");
        var committed = fixture.Files();
        var repeated = await fixture.Service.TryApplyAsync(request);
        await Assert.That(repeated.Success).IsTrue();
        await Assert.That(repeated.Mode).IsEqualTo("alreadyApplied");
        await Assert.That(fixture.Files()).IsEquivalentTo(committed);
        var external = JObject.Parse(await File.ReadAllTextAsync(fixture.TaskPath));
        external["Importance"] = 43;
        await File.WriteAllTextAsync(fixture.TaskPath, external.ToString());
        await Assert.That((await fixture.Service.InspectAsync(request)).PostconditionsMatch).IsEqualTo("none");
    }

    private static TaskApplicationOperation Set(string? value) => new()
    {
        OperationId = "importance", Kind = TaskApplicationOperationKind.SetField, TaskId = "task", Field = "importance", Value = value
    };

    private static string Etag(TaskItem task)
    {
        var analyzer = new TaskAvailabilityAnalyzer([task]);
        return TaskSnapshotOutput.Create(task, analyzer.Analyze(task), analyzer, new HashSet<string>()).Etag;
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-importance-" + Guid.NewGuid().ToString("N")));
        public string TaskPath => System.IO.Path.Combine(Path, "task");
        public FileTaskStorage Storage { get; }
        public TaskApplicationCommandService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Path);
            Storage = new(new FileTaskStorageOptions { Path = Path, PreserveUnknownJson = true, UseDirectoryLock = true });
            Service = new(Storage, Etag);
        }
        public async Task Seed()
        {
            var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);
            var task = new TaskItem
            {
                Id = "task", Title = "Original", UserId = "owner", Description = "Unchanged markdown\n**body**",
                Importance = 9, Wanted = true, Version = 1, Status = DomainTaskStatus.Prepared,
                CreatedDateTime = epoch, UpdatedDateTime = epoch, UnlockedDateTime = epoch, IsCanBeCompleted = true,
                PlannedDuration = TimeSpan.FromMinutes(15),
                ExtensionData = new Dictionary<string, JToken> { ["Custom"] = JObject.Parse("{\"Nested\":[1,\"preserve\"],\"IsGoal\":true}") }
            };
            task.EnsureStatusHistory("owner");
            await Storage.Save(task);
        }
        public async Task<TaskApplicationRequest> Request(params TaskApplicationOperation[] operations)
        {
            var task = await Load();
            return new()
            {
                SchemaVersion = 1, ApplicationId = "importance", Author = "test-agent", Reason = "Approved fixture",
                ProposalRefs = [new("proposal", 1)], Preconditions = [new("task", Etag(task), task.Status)], Operations = operations
            };
        }
        public async Task<TaskItem> Load() => await Storage.Load("task", forced: true) ?? throw new InvalidOperationException("Fixture task missing.");
        public string[] Files() => Directory.GetFiles(Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => System.IO.Path.GetRelativePath(Path, file) + ":" + Convert.ToHexString(File.ReadAllBytes(file)) + ":" + File.GetLastWriteTimeUtc(file).Ticks).ToArray();
        public void Dispose()
        {
            var prefix = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (!Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture cleanup escaped temporary root.");
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
