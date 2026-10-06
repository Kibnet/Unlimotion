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

// Console.In/Out/Error are process-global. The junction and both task roots are disposable fixtures.
[NotInParallel]
public sealed class UnlimotionCliSourcePinTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GuardedRetry_PinsPhysicalRootBeforeRequestRead_ForReconciliationAndReceiptShortcut(bool existingReceipt)
    {
        using var space = new SourcePair();
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Id = "work", Title = "Original", Description = "", UserId = "synthetic",
            Status = DomainTaskStatus.Prepared, CreatedDateTime = epoch, UpdatedDateTime = epoch,
            UnlockedDateTime = epoch, IsCanBeCompleted = true
        };
        task.EnsureStatusHistory("synthetic");
        var storage = new FileTaskStorage(new() { Path = space.A, CreateDirectoryIfMissing = false, PreserveUnknownJson = true });
        await storage.Save(task);
        var observedTask = (await storage.ReadObservationAsync()).Graph.TasksById[task.Id];
        var request = new TaskApplicationRequest
        {
            SchemaVersion = 1, ApplicationId = "pin-source-retry", Author = "synthetic", Reason = "Accepted source-specific proposal",
            ProposalRefs = [new("proposal", 1)], Preconditions = [new(task.Id, Etag(observedTask), observedTask.Status)],
            Operations = [new() { OperationId = "title", Kind = TaskApplicationOperationKind.SetField, TaskId = task.Id, Field = "title", Value = "Approved" }]
        };
        var requestText = JsonSerializer.Serialize(new
        {
            schemaVersion = request.SchemaVersion, applicationId = request.ApplicationId, author = request.Author, reason = request.Reason,
            proposalRefs = new[] { new { id = "proposal", revision = 1 } },
            preconditions = new[] { new { taskId = task.Id, etag = Etag(observedTask), status = "Prepared" } },
            operations = new[] { new { operationId = "title", kind = "setField", taskId = task.Id, field = "title", value = "Approved" } }
        });
        var requestHash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestText))).ToLowerInvariant();
        var service = new TaskApplicationCommandService(storage, Etag);
        var previewResult = await service.PreviewPlanAsync(request);
        await Assert.That(previewResult.Success).IsTrue();
        var sourceKeyA = NightAgentSnapshotCodec.CreateSourceKey(space.A);
        var preview = TaskApplicationPreview.Create(previewResult.Plan!, request, requestHash, sourceKeyA);
        var witnessPath = Path.Combine(space.Root, "reviewed.preview.json");
        var witnessText = JsonSerializer.Serialize(ApplicationCommandOutput.From(request.ApplicationId, requestHash, previewResult, false)
            with { Preview = preview }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(witnessPath, witnessText);

        // The domain commit exists in A, but initially no CLI receipt does. B also has the desired
        // task state, so a retry accidentally redirected to B could reconcile there without task writes.
        var applied = await service.TryApplyAsync(request, plan => TaskApplicationPreview.CheckGuard(preview, plan, request, requestHash, sourceKeyA));
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(applied.DidMutate).IsTrue();
        var committedTaskBytes = await File.ReadAllBytesAsync(Path.Combine(space.A, task.Id));
        await File.WriteAllBytesAsync(Path.Combine(space.B, task.Id), committedTaskBytes);
        await Assert.That(NightAgentSnapshotCodec.CreateSourceKey(space.B) == sourceKeyA).IsFalse();
        if (existingReceipt)
        {
            using var initialInput = new StringReader(requestText);
            var initialRetry = await RunWithInput(initialInput,
                "apply", "--tasks", space.A, "--request", "-", "--expect-preview", witnessPath, "--format", "json");
            await Assert.That(initialRetry.ExitCode).IsEqualTo(0);
            await Assert.That(initialRetry.Json.GetProperty("mode").GetString()).IsEqualTo("alreadyApplied");
            await Assert.That(initialRetry.Json.GetProperty("receiptWritten").GetBoolean()).IsTrue();
        }
        var aBefore = TreeBytes(space.A);
        var bBefore = TreeBytes(space.B);
        space.PointAliasTo(space.A);
        using var switchedInput = new SwitchingReader(requestText, () => space.PointAliasTo(space.B));

        // Program pins A before ReadToEndAsync asks this reader for the request. The callback moves
        // the original CLI path to B before witness validation, receipt lookup or reconciliation.
        var retried = await RunWithInput(switchedInput,
            "apply", "--tasks", space.Alias, "--request", "-", "--expect-preview", witnessPath, "--format", "json");

        await Assert.That(switchedInput.SwitchCount).IsEqualTo(1);
        await Assert.That(NightAgentSnapshotCodec.ResolvePhysicalDirectory(space.Alias)).IsEqualTo(NightAgentSnapshotCodec.ResolvePhysicalDirectory(space.B));
        await Assert.That(retried.ExitCode).IsEqualTo(0);
        await Assert.That(retried.Json.GetProperty("mode").GetString()).IsEqualTo("alreadyApplied");
        await Assert.That(retried.Json.GetProperty("didMutate").GetBoolean()).IsFalse();
        await Assert.That(retried.Json.GetProperty("receiptWritten").GetBoolean()).IsEqualTo(!existingReceipt);
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(space.A, task.Id))).SequenceEqual(committedTaskBytes)).IsTrue();
        await Assert.That(TreeBytes(space.B).SequenceEqual(bBefore)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(space.B, ".unlimotion.applies"))).IsFalse();
        var receiptFiles = Directory.GetFiles(Path.Combine(space.A, ".unlimotion.applies", "v1"), "*.json");
        await Assert.That(receiptFiles.Length).IsEqualTo(1);
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(receiptFiles.Single()));
        await Assert.That(receipt.RootElement.GetProperty("ApplicationId").GetString()).IsEqualTo(request.ApplicationId);
        await Assert.That(receipt.RootElement.GetProperty("RequestHash").GetString()).IsEqualTo(requestHash);
        if (existingReceipt) await Assert.That(TreeBytes(space.A).SequenceEqual(aBefore)).IsTrue();
    }

    private static string Etag(TaskItem task)
    {
        var analyzer = new TaskAvailabilityAnalyzer([task]);
        return TaskSnapshotOutput.Create(task, analyzer.Analyze(task), analyzer, new HashSet<string>()).Etag;
    }

    private static string[] TreeBytes(string root) => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
        .Select(path => "directory:" + Path.GetRelativePath(root, path))
        .Concat(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => "file:" + Path.GetRelativePath(root, path) + ":" + File.GetLastWriteTimeUtc(path).Ticks + ":" + Convert.ToHexString(File.ReadAllBytes(path))))
        .Order(StringComparer.Ordinal).ToArray();

    // This class is globally NotInParallel; the original writers are always restored in finally.
#pragma warning disable TUnit0055
    private static async Task<CliResult> RunWithInput(TextReader input, params string[] args)
    {
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetIn(input); Console.SetOut(output); Console.SetError(error);
            var exitCode = await global::Unlimotion.Cli.Program.Main(args);
            try
            {
                using var document = JsonDocument.Parse(output.ToString());
                return new(exitCode, document.RootElement.Clone());
            }
            catch (JsonException exception)
            { throw new InvalidDataException($"CLI returned invalid JSON: {output}; stderr: {error}", exception); }
        }
        finally
        {
            Console.SetIn(originalInput); Console.SetOut(originalOutput); Console.SetError(originalError);
        }
    }

#pragma warning restore TUnit0055

    private sealed record CliResult(int ExitCode, JsonElement Json);

    private sealed class SwitchingReader(string text, Action changeSource) : StringReader(text)
    {
        private int _switchCount;
        public int SwitchCount => _switchCount;
        public override string ReadToEnd()
        {
            if (Interlocked.CompareExchange(ref _switchCount, 1, 0) == 0) changeSource();
            return base.ReadToEnd();
        }
        public override Task<string> ReadToEndAsync() => Task.FromResult(ReadToEnd());
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadToEndAsync();
        }
    }

    private sealed class SourcePair : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unlimotion-source-pin-" + Guid.NewGuid().ToString("N")));
        public string A => Path.Combine(Root, "source-a");
        public string B => Path.Combine(Root, "source-b");
        public string Alias => Path.Combine(Root, "selected-source");
        public SourcePair() { Directory.CreateDirectory(A); Directory.CreateDirectory(B); }

        public void PointAliasTo(string destination)
        {
            RemoveAlias();
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Alias, destination);
                return;
            }
            // Junction creation does not require the Windows symbolic-link privilege. No deletion
            // or move is delegated to cmd; teardown below removes only this verified reparse point.
            if (Alias.IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '(', ')', '"', '\r', '\n']) >= 0 ||
                destination.IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '(', ')', '"', '\r', '\n']) >= 0)
                throw new IOException("The temporary path contains shell metacharacters unsupported by this junction-creation fixture.");
            var start = new ProcessStartInfo("cmd.exe")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", Alias, destination }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start junction creation.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Junction creation timed out.");
            }
            if (process.ExitCode != 0)
                throw new IOException($"Cannot create test junction: {stdout.GetAwaiter().GetResult()} {stderr.GetAwaiter().GetResult()}");
        }

        private void RemoveAlias()
        {
            if (!Directory.Exists(Alias) && !File.Exists(Alias)) return;
            if ((File.GetAttributes(Alias) & FileAttributes.ReparsePoint) == 0)
                throw new IOException("Refusing to remove a test alias that is not a reparse point.");
            Directory.Delete(Alias, recursive: false);
        }

        public void Dispose()
        {
            RemoveAlias();
            var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Root.StartsWith(tempRoot, comparison) || !Path.GetFileName(Root).StartsWith("unlimotion-source-pin-", StringComparison.Ordinal))
                throw new IOException("Test cleanup target escaped its temporary workspace.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
