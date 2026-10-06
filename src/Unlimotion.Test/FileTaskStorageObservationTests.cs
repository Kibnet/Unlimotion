using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Storage;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

[NotInParallel]
public sealed class FileTaskStorageObservationTests
{
    [Test]
    public async Task StableCapture_UsesRawManifestAndOneDomainParsePerTask()
    {
        using var fixture = new Fixture();
        var first = TaskJson("first");
        var second = TaskJson("second");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "a.json"), first);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "b"), second);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, ".hidden.json"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "ignored.tmp"), "ignored");
        Directory.CreateDirectory(Path.Combine(fixture.Path, "Archive"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "Archive", "old"), "ignored");
        var before = fixture.ReadSourceBytes();
        var storage = fixture.CreateStorage();

        var observation = await storage.ReadObservationAsync();
        var repeated = await fixture.CreateStorage().ReadObservationAsync();

        await Assert.That(observation.Graph.Tasks.Count).IsEqualTo(2);
        await Assert.That(observation.TaskParseCount).IsEqualTo(2);
        await Assert.That(observation.VerifiedFileCount).IsEqualTo(2);
        await Assert.That(observation.Attempts).IsEqualTo(1);
        await Assert.That(observation.SourceBytes).IsEqualTo((long)Encoding.UTF8.GetByteCount(first + second));
        var expectedManifest = new JArray(
            new JArray("a.json", Hash(Encoding.UTF8.GetBytes(first))),
            new JArray("b", Hash(Encoding.UTF8.GetBytes(second))));
        await Assert.That(observation.SourceManifestHash)
            .IsEqualTo(Hash(Encoding.UTF8.GetBytes(expectedManifest.ToString(Formatting.None))));
        await Assert.That(repeated.SourceManifestHash).IsEqualTo(observation.SourceManifestHash);
        await Assert.That(observation.StartedAt <= observation.EvaluatedAt &&
                         observation.EvaluatedAt <= observation.CompletedAt).IsTrue();
        await Assert.That(observation.UnstableTaskIds).IsEmpty();
        await Assert.That(fixture.SourceBytesMatch(before)).IsTrue();

        observation.Graph.TasksById["first"].Title = "Only detached memory";
        await Assert.That(repeated.Graph.TasksById["first"].Title).IsEqualTo("Title");
    }

    [Test]
    public async Task Capture_PreservesRepairUnknownFieldsAliasesAndLegacyDiagnostics()
    {
        using var fixture = new Fixture();
        var repairedJson = "{\"Id\":\"repaired\" \"Title\":\"Recovered\",\"CreatedDateTime\":\"2026-01-01T00:00:00.000+00:00\",\"Future\":{\"x\":7}}";
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "alias.json"), repairedJson);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "legacy"), "{\"Id\":\"legacy\",\"Title\":\"Old\"}");
        var before = fixture.ReadSourceBytes();
        var storage = fixture.CreateStorage();

        var observation = await storage.ReadObservationAsync();

        await Assert.That(observation.Graph.TasksById["repaired"].Title).IsEqualTo("Recovered");
        await Assert.That(observation.Graph.TasksById["repaired"].ExtensionData!["Future"]["x"]!.Value<int>()).IsEqualTo(7);
        await Assert.That(observation.UnstableTaskIds.SetEquals(new[] { "legacy" })).IsTrue();
        await Assert.That(fixture.SourceBytesMatch(before)).IsTrue();
        await Assert.That(Directory.GetFiles(fixture.Path, "*.repaired.json")).IsEmpty();

        var task = observation.Graph.TasksById["repaired"];
        task.Title = "Written to original alias";
        await storage.Save(task);
        await Assert.That(File.Exists(Path.Combine(fixture.Path, "repaired"))).IsFalse();
        await Assert.That((await storage.ReadObservationAsync()).Graph.TasksById["repaired"].Title)
            .IsEqualTo("Written to original alias");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PendingJournal_RefusesWithoutChangingBytes_ExistingRecoveryStillWorks(bool committed)
    {
        using var fixture = new Fixture();
        var taskPath = Path.Combine(fixture.Path, "task");
        await File.WriteAllTextAsync(taskPath, TaskJson("task", "Partial"));
        var journalDirectory = Path.Combine(fixture.Path, ".unlimotion.transactions");
        Directory.CreateDirectory(journalDirectory);
        var journalPath = Path.Combine(journalDirectory, "pending.json");
        await File.WriteAllTextAsync(journalPath, JsonConvert.SerializeObject(new
        {
            Id = "pending",
            Committed = committed,
            Entries = new[]
            {
                new
                {
                    TaskId = "task", FilePath = taskPath, BeforeExists = true, AfterExists = true,
                    BeforeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(TaskJson("task", "Before"))),
                    AfterBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(TaskJson("task", "After")))
                }
            }
        }));
        var receiptDirectory = Path.Combine(fixture.Path, ".unlimotion.applies", "v1");
        Directory.CreateDirectory(receiptDirectory);
        var receiptPath = Path.Combine(receiptDirectory, "unrelated-receipt.json");
        await File.WriteAllTextAsync(receiptPath, "{\"applicationId\":\"unrelated-existing-receipt\"}");
        var persistedPaths = new[] { taskPath, journalPath, receiptPath };
        foreach (var path in persistedPaths)
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var beforeTimes = persistedPaths.ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.Ordinal);
        var before = fixture.ReadSourceBytes();
        var storage = fixture.CreateStorage();

        var error = await ObservationFailure(() => storage.ReadObservationAsync());

        await Assert.That(error.Kind).IsEqualTo("recoveryRequired");
        await Assert.That(fixture.SourceBytesMatch(before)).IsTrue();
        foreach (var path in persistedPaths)
            await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(beforeTimes[path]);
        var recovered = await storage.ReadDirectoryAsync();
        await Assert.That(recovered.Tasks.Single().Title).IsEqualTo(committed ? "After" : "Before");
        await Assert.That(File.Exists(journalPath)).IsFalse();
    }

    [Test]
    public async Task Capture_ReportsZeroByteMalformedAndDuplicateFilesWithoutPartialSuccess()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "zero"), "");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "bad.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "alias-a"), TaskJson("same"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "alias-b.json"), TaskJson("same"));
        var before = fixture.ReadSourceBytes();

        var error = await ObservationFailure(() => fixture.CreateStorage().ReadObservationAsync());

        await Assert.That(error.Kind).IsEqualTo("observationFailed");
        await Assert.That(error.Graph!.LoadErrors.Count).IsEqualTo(2);
        await Assert.That(error.Graph.DuplicateIdIssues.Single().TaskId).IsEqualTo("same");
        await Assert.That(error.Attempts).IsEqualTo(1);
        await Assert.That(fixture.SourceBytesMatch(before)).IsTrue();
    }

    [Test]
    public async Task Capture_RetriesExplicitByteDriftAndCountsDiscardedWork()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "task");
        await File.WriteAllTextAsync(path, TaskJson("task", "Before"));
        var storage = new HookedStorage(fixture.Options, async (attempt, _) =>
        {
            if (attempt == 1) await File.WriteAllTextAsync(path, TaskJson("task", "After!"));
        });

        var observation = await storage.ReadObservationAsync();

        await Assert.That(observation.Attempts).IsEqualTo(2);
        await Assert.That(observation.TaskParseCount).IsEqualTo(2);
        await Assert.That(observation.VerifiedFileCount).IsEqualTo(2);
        await Assert.That(observation.Graph.Tasks.Single().Title).IsEqualTo("After!");
    }

    [Test]
    public async Task Capture_RejectsPersistentDriftAfterThreeAttempts()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "task");
        await File.WriteAllTextAsync(path, TaskJson("task"));
        var storage = new HookedStorage(fixture.Options,
            (attempt, _) => File.WriteAllTextAsync(path, TaskJson("task", "Change " + attempt)));

        var error = await ObservationFailure(() => storage.ReadObservationAsync());

        await Assert.That(error.Kind).IsEqualTo("snapshotUnstable");
        await Assert.That(error.Attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Capture_NameOnlyDriftRetriesAndManifestChanges()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "task");
        await File.WriteAllTextAsync(path, TaskJson("task"));
        var initial = await fixture.CreateStorage().ReadObservationAsync();
        var storage = new HookedStorage(fixture.Options, (attempt, _) =>
        {
            if (attempt == 1) File.Move(path, Path.Combine(fixture.Path, "alias.json"));
            return Task.CompletedTask;
        });

        var observation = await storage.ReadObservationAsync();

        await Assert.That(observation.Attempts).IsEqualTo(2);
        await Assert.That(observation.SourceManifestHash).IsNotEqualTo(initial.SourceManifestHash);
        await Assert.That(observation.Graph.Tasks.Single().Id).IsEqualTo("task");
    }

    [Test]
    public async Task Capture_RejectsOversizedFileBeforeParsing()
    {
        using var fixture = new Fixture();
        await using (var stream = File.Create(Path.Combine(fixture.Path, "too-large")))
        {
            stream.SetLength(8L * 1024 * 1024 + 1);
        }

        var error = await ObservationFailure(() => fixture.CreateStorage().ReadObservationAsync());

        await Assert.That(error.Kind).IsEqualTo("snapshotTooLarge");
        await Assert.That(error.Limit).IsEqualTo("sourceTaskFileBytes");
        await Assert.That(error.Observed).IsEqualTo(8L * 1024 * 1024 + 1);
    }

    [Test]
    public async Task MissingSource_IsNeverCreatedByConstructorOrCapture()
    {
        using var fixture = new Fixture();
        var missing = Path.Combine(fixture.Path, "missing");
        var storage = new FileTaskStorage(new FileTaskStorageOptions
        {
            Path = missing, CreateDirectoryIfMissing = false
        });

        var error = await ObservationFailure(() => storage.ReadObservationAsync());

        await Assert.That(error.Kind).IsEqualTo("observationFailed");
        await Assert.That(Directory.Exists(missing)).IsFalse();
    }

    [Test]
    public async Task Capture_CancellationPreservesSourceAndReleasesLock()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "task"), TaskJson("task"));
        using var cancellation = new CancellationTokenSource();
        var before = fixture.ReadSourceBytes();
        var storage = new HookedStorage(fixture.Options, (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        var cancelled = false;
        try
        {
            await storage.ReadObservationAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await Assert.That(cancelled).IsTrue();
        await Assert.That(fixture.SourceBytesMatch(before)).IsTrue();
        await Assert.That((await fixture.CreateStorage().ReadObservationAsync()).Graph.Tasks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Capture_ReusesOuterWriteLockWithoutRecoveryOfNewlyAppearedJournal()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "task"), TaskJson("task"));
        var storage = fixture.CreateStorage();
        await storage.WithWriteLockAsync(async () =>
        {
            var observed = await storage.ReadObservationAsync();
            await Assert.That(observed.Graph.Tasks.Count).IsEqualTo(1);
            var journalDirectory = Path.Combine(fixture.Path, ".unlimotion.transactions");
            Directory.CreateDirectory(journalDirectory);
            var journalPath = Path.Combine(journalDirectory, "external.json");
            await File.WriteAllTextAsync(journalPath, "malformed pending journal");

            var error = await ObservationFailure(() => storage.ReadObservationAsync());

            await Assert.That(error.Kind).IsEqualTo("recoveryRequired");
            await Assert.That(await File.ReadAllTextAsync(journalPath)).IsEqualTo("malformed pending journal");
            return true;
        });
    }

    [Test]
    public async Task Capture_AlwaysHoldsCooperativeLockUntilVerificationCompletes()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "task"), TaskJson("task"));
        var writer = fixture.CreateStorage();
        Task write = Task.CompletedTask;
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new HookedStorage(fixture.Options with { UseDirectoryLock = false }, async (_, _) =>
        {
            // A separate process does not inherit the AsyncLocal marker for the current lock.
            using (ExecutionContext.SuppressFlow())
            {
                write = Task.Run(async () =>
                {
                    var pending = writer.Save(new Domain.TaskItem { Id = "task", Title = "Updated" });
                    writeStarted.SetResult();
                    await pending;
                });
            }
            await writeStarted.Task;
            await Assert.That(write.IsCompleted).IsFalse();
        });

        var observation = await storage.ReadObservationAsync();
        await write;

        await Assert.That(observation.Graph.Tasks.Single().Title).IsEqualTo("Title");
        await Assert.That((await fixture.CreateStorage().ReadObservationAsync()).Graph.Tasks.Single().Title)
            .IsEqualTo("Updated");
    }

    [Test]
    public async Task GuardedSave_PreservesBypassEditBetweenSourceCheckAndReplace()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "alias.json");
        await File.WriteAllTextAsync(path, TaskJson("task", "Original"));
        var external = TaskJson("task", "External update");
        var storage = new GuardRaceStorage(fixture.Options)
        {
            BeforeWrite = file => File.WriteAllText(file, external)
        };

        var failed = await RunGuardedMutation(storage, async observation =>
        {
            var task = observation.Graph.TasksById["task"];
            task.Title = "Own update";
            await storage.Save(task);
        });

        await Assert.That(failed).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(external);
        await Assert.That(File.Exists(Path.Combine(fixture.Path, "task"))).IsFalse();
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    public async Task GuardedSave_RejectsChangedBaselineBeforeAnyWrite()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "task");
        await File.WriteAllTextAsync(path, TaskJson("task", "Original"));
        var external = TaskJson("task", "Changed after observation");
        var storage = fixture.CreateStorage();

        var failed = await RunGuardedMutation(storage, async observation =>
        {
            await File.WriteAllTextAsync(path, external);
            var task = observation.Graph.TasksById["task"];
            task.Title = "Own update";
            await storage.Save(task);
        });

        await Assert.That(failed).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(external);
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    public async Task GuardedCreate_PreservesFileCreatedBetweenCheckAndReplace()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "created");
        var external = TaskJson("created", "External creation");
        var storage = new GuardRaceStorage(fixture.Options)
        {
            BeforeWrite = file => File.WriteAllText(file, external)
        };

        var failed = await RunGuardedMutation(storage, _ =>
            storage.Save(new Domain.TaskItem { Id = "created", Title = "Own creation" }));

        await Assert.That(failed).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(external);
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    public async Task GuardedCreate_DetectsNewAliasCollisionAtCommitAndRemovesOnlyOwnFile()
    {
        using var fixture = new Fixture();
        var alias = Path.Combine(fixture.Path, "foreign-alias.json");
        var external = TaskJson("created", "External alias");
        var storage = new GuardRaceStorage(fixture.Options)
        {
            BeforeWrite = _ => File.WriteAllText(alias, external)
        };

        var failed = await RunGuardedMutation(storage, _ =>
            storage.Save(new Domain.TaskItem { Id = "created", Title = "Own creation" }));

        await Assert.That(failed).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(alias)).IsEqualTo(external);
        await Assert.That(File.Exists(Path.Combine(fixture.Path, "created"))).IsFalse();
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GuardedCommit_PreservesLateExternalEditAndRollsBackOwnOtherChanges(bool editTarget)
    {
        using var fixture = new Fixture();
        var firstPath = Path.Combine(fixture.Path, "first");
        var secondPath = Path.Combine(fixture.Path, "second");
        var original = TaskJson("first", "Original");
        await File.WriteAllTextAsync(firstPath, original);
        await File.WriteAllTextAsync(secondPath, TaskJson("second"));
        var external = TaskJson(editTarget ? "first" : "second", "External at commit");
        var storage = new GuardRaceStorage(fixture.Options)
        {
            BeforeJournalPersist = count =>
            {
                // First persistence prepares one task; second persistence is the commit marker.
                if (count == 2) File.WriteAllText(editTarget ? firstPath : secondPath, external);
            }
        };

        var failed = await RunGuardedMutation(storage, async observation =>
        {
            var task = observation.Graph.TasksById["first"];
            task.Title = "Own update";
            await storage.Save(task);
        });

        await Assert.That(failed).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(editTarget ? firstPath : secondPath)).IsEqualTo(external);
        if (!editTarget) await Assert.That(await File.ReadAllTextAsync(firstPath)).IsEqualTo(original);
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    public async Task GuardedCommit_PreservesExternalRenameWithoutRecreatingOriginalAlias()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "alias.json");
        var movedPath = Path.Combine(fixture.Path, "moved.json");
        await File.WriteAllTextAsync(path, TaskJson("task"));
        var storage = new GuardRaceStorage(fixture.Options)
        {
            BeforeJournalPersist = count =>
            {
                if (count == 2) File.Move(path, movedPath);
            }
        };

        var failed = await RunGuardedMutation(storage, async observation =>
        {
            var task = observation.Graph.TasksById["task"];
            task.Title = "Own update";
            await storage.Save(task);
        });

        await Assert.That(failed).IsTrue();
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(File.Exists(movedPath)).IsTrue();
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    [Test]
    public async Task GuardedApplication_CommitsStableSourceAndPreservesAlias()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Path, "alias.json");
        await File.WriteAllTextAsync(path, TaskJson("task"));
        var storage = fixture.CreateStorage();

        var failed = await RunGuardedMutation(storage, async observation =>
        {
            var task = observation.Graph.TasksById["task"];
            task.Title = "Own update";
            await storage.Save(task);
        });

        await Assert.That(failed).IsFalse();
        await Assert.That((await storage.ReadObservationAsync()).Graph.Tasks.Single().Title).IsEqualTo("Own update");
        await Assert.That(File.Exists(Path.Combine(fixture.Path, "task"))).IsFalse();
        await Assert.That(fixture.HasJournal()).IsFalse();
    }

    private static Task<bool> RunGuardedMutation(FileTaskStorage storage, Func<TaskGraphObservation, Task> mutation) =>
        storage.WithWriteLockAsync(async () =>
        {
            using var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
            var observation = await storage.ReadObservationAsync();
            using var guard = storage.BeginObservedWriteGuard(observation);
            try
            {
                await mutation(observation);
                await scope.CommitAsync();
                return false;
            }
            catch (IOException)
            {
                try { await scope.RollbackAsync(); }
                catch (IOException) { /* Preserved external state requires the caller's read-back. */ }
                return true;
            }
        });

    private static async Task<TaskGraphObservationException> ObservationFailure(Func<Task<TaskGraphObservation>> operation)
    {
        try
        {
            await operation();
        }
        catch (TaskGraphObservationException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected a typed observation refusal.");
    }

    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string TaskJson(string id, string title = "Title") =>
        JsonConvert.SerializeObject(new { Id = id, Title = title, CreatedDateTime = "2026-01-01T00:00:00.000+00:00" });

    private sealed class HookedStorage(FileTaskStorageOptions options, Func<int, CancellationToken, Task> hook)
        : FileTaskStorage(options)
    {
        protected override Task OnObservationFirstPassCompletedAsync(int attempt, CancellationToken cancellationToken) =>
            hook(attempt, cancellationToken);
    }

    private sealed class GuardRaceStorage(FileTaskStorageOptions options) : FileTaskStorage(options)
    {
        private int _journalPersists;
        public Action<string>? BeforeWrite { get; init; }
        public Action<int>? BeforeJournalPersist { get; init; }
        protected override void OnBeforeWrite(string taskId, string filePath) => BeforeWrite?.Invoke(filePath);
        protected override void OnBeforeTransactionJournalPersist(string journalPath) =>
            BeforeJournalPersist?.Invoke(++_journalPersists);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "unlimotion-observation-" + Guid.NewGuid().ToString("N"));
        public FileTaskStorageOptions Options => new() { Path = Path, CreateDirectoryIfMissing = false };
        public Fixture() => Directory.CreateDirectory(Path);
        public FileTaskStorage CreateStorage() => new(Options);
        public bool HasJournal() => Directory.GetFiles(Path, "*.json", SearchOption.AllDirectories)
            .Any(file => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file)) == ".unlimotion.transactions");
        public Dictionary<string, byte[]> ReadSourceBytes() => Directory.GetFiles(Path, "*", SearchOption.AllDirectories)
            .Where(path => System.IO.Path.GetFileName(path) != ".unlimotion.lock")
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        public bool SourceBytesMatch(IReadOnlyDictionary<string, byte[]> expected)
        {
            var current = ReadSourceBytes();
            return expected.Count == current.Count && expected.All(pair =>
                current.TryGetValue(pair.Key, out var bytes) && pair.Value.SequenceEqual(bytes));
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
