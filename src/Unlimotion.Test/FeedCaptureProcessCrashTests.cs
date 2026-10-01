using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

/// <summary>Real child-process termination, not an exception followed by recovery in the same process.</summary>
public sealed class FeedCaptureProcessCrashTests
{
    private const string WorkerRoot = "UNLIMOTION_CAPTURE_CRASH_TEST_ROOT";
    private const string WorkerStage = "UNLIMOTION_CAPTURE_CRASH_TEST_STAGE";
    private const string Capture = "Задача после перезапуска\n\nВажное описание\n\n- [ ] Подпункт";

    [Test]
    [Arguments("intent")]
    [Arguments("append")]
    [Arguments("task")]
    [Arguments("link")]
    [Arguments("retained-intent")]
    [Arguments("retained-append")]
    public async Task AbruptProcessExit_RecoversExactlyOneWholeCapture(string stage)
    {
        using var root = new TempNotesDirectory();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root.Path
        };
        start.ArgumentList.Add(typeof(FeedCaptureProcessCrashTests).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/FeedCaptureProcessCrashTests/IsolatedCrashWorker");
        start.Environment[WorkerRoot] = root.Path;
        start.Environment[WorkerStage] = stage;
        using var worker = Process.Start(start)!;
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { if (!worker.HasExited) { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(); } }
        var output = await stdout + await stderr;
        if (worker.ExitCode != 73) throw new InvalidOperationException($"Crash worker exited {worker.ExitCode}: {output}");

        var vault = new FileNoteVault(Path.Combine(root.Path, "vault"));
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        var journal = new FileFeedTaskConversionJournal(Path.Combine(root.Path, "journal"));
        if (stage.StartsWith("retained-", StringComparison.Ordinal))
        {
            var pending = (await journal.ListPendingAsync("crash-vault")).Single();
            var recoveryService = new FeedCaptureDraftRecoveryService(vault, journal);
            await recoveryService.ResumeAsync(pending);
            await recoveryService.ResumeAsync((await journal.LoadAsync("crash-vault", pending.OperationId))!);
            var recovered = await vault.ReadAsync("Ежедневные/2026.09.05.md");
            await Assert.That(recovered!.Text.Split("Задача после перезапуска").Length - 1).IsEqualTo(1);
            await Assert.That(recovered.Text).Contains("Важное описание");
            await Assert.That(recovered.Text).Contains("Подпункт");
            await Assert.That(await vault.ReadAsync("Ежедневные/2026-09-04.md")).IsNull();
            await Assert.That(await journal.ListPendingAsync("crash-vault")).IsEmpty();
            await Assert.That(File.Exists(Path.Combine(root.Path, "task.json"))).IsFalse();
            return;
        }
        var target = new DurableTarget(root.Path, null);
        var request = Request();
        var service = new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations),
            parser, mutations, target, journal);
        var result = await service.CaptureAsync(request);
        await service.CaptureAsync(request); // Repeating the restart must also be idempotent.
        var source = (await vault.ReadAsync(result.SourcePath))!.Text;
        var stored = JsonSerializer.Deserialize<FeedTaskDraft>(await File.ReadAllTextAsync(Path.Combine(root.Path, "task.json")))!;
        await Assert.That(source.Split("unlimotion://task/feed-process-crash").Length - 1).IsEqualTo(1);
        await Assert.That(source).DoesNotContain("Важное описание");
        await Assert.That(stored.Description).Contains("Важное описание");
        await Assert.That(stored.Description).Contains("Подпункт");
        await Assert.That((await File.ReadAllLinesAsync(Path.Combine(root.Path, "creates.log"))).Length).IsEqualTo(1);
        await Assert.That(await journal.ListPendingAsync("crash-vault")).IsEmpty();
    }

    // Invoked only in the isolated child via an exact filter and private environment variables.
    // The normal suite returns immediately and never terminates its own process.
    [Test]
    public async Task IsolatedCrashWorker()
    {
        var root = Environment.GetEnvironmentVariable(WorkerRoot);
        var stage = Environment.GetEnvironmentVariable(WorkerStage);
        if (string.IsNullOrEmpty(root) || stage is not ("intent" or "append" or "task" or "link" or "retained-intent" or "retained-append")) return;
        var vault = new CrashVault(new FileNoteVault(Path.Combine(root, "vault")), stage);
        if (stage.StartsWith("retained-", StringComparison.Ordinal))
        {
            var journal = new CrashJournal(new FileFeedTaskConversionJournal(Path.Combine(root, "journal")), stage);
            var retained = new FeedTaskConversionRecord(2, "crash-vault", "retained", FeedTaskConversionState.Completed,
                "Ежедневные/2026-09-04.md", "original", "feed-retained", null, DateTimeOffset.UtcNow,
                RecoveryResolution: FeedOperationRecoveryResolution.KeptBoth,
                CaptureIntent: new FeedTaskCaptureIntent(Capture, null, null, "original-append", false));
            await journal.SaveAsync(retained);
            await new FeedCaptureDraftRecoveryService(vault, journal).SaveAsync(retained,
                DailyNoteNaming.Create("yyyy.MM.dd"), new DateOnly(2026, 9, 5), Capture, null);
            throw new InvalidOperationException("Retained capture checkpoint was not reached.");
        }
        var parser = new MarkdownDocumentParser();
        var mutations = new MarkdownMutationService(parser);
        await new FeedTaskCaptureService(vault, new DailyNoteService(vault, parser, mutations), parser,
            mutations, new DurableTarget(root, stage),
            new CrashJournal(new FileFeedTaskConversionJournal(Path.Combine(root, "journal")), stage))
            .CaptureAsync(Request());
        throw new InvalidOperationException("The requested process-exit checkpoint was not reached.");
    }

    private static FeedTaskCaptureRequest Request() => new("crash-vault", "process-crash",
        new DateOnly(2026, 9, 4), Capture, null, null, []);

    private sealed class DurableTarget(string root, string? stage) : IFeedTaskCreationTarget
    {
        public async Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, "task.json");
            if (!File.Exists(path))
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(draft), cancellationToken);
                await File.AppendAllTextAsync(Path.Combine(root, "creates.log"), draft.TaskId + "\n", cancellationToken);
                if (stage == "task") Environment.Exit(73);
            }
            var saved = JsonSerializer.Deserialize<FeedTaskDraft>(await File.ReadAllTextAsync(path, cancellationToken))!;
            if (saved.OperationId != draft.OperationId) throw new InvalidDataException("Task ownership mismatch.");
            return new FeedCreatedTask(saved.TaskId, saved.Title);
        }
    }

    private sealed class CrashJournal(IFeedTaskConversionJournal inner, string stage) : IFeedTaskConversionJournal
    {
        public Task<FeedTaskConversionRecord?> LoadAsync(string vaultId, string operationId, CancellationToken cancellationToken = default)
            => inner.LoadAsync(vaultId, operationId, cancellationToken);
        public async Task SaveAsync(FeedTaskConversionRecord record, CancellationToken cancellationToken = default)
        {
            await inner.SaveAsync(record, cancellationToken);
            if (stage == "intent" && record.State == FeedTaskConversionState.Pending) Environment.Exit(73);
            if (stage == "retained-intent" && record.RecoveredCaptureWrite is not null) Environment.Exit(73);
        }
    }

    private sealed class CrashVault(INoteVault inner, string stage) : INoteVault
    {
        public string RootPath => inner.RootPath;
        public string ResolveSafePath(string path) => inner.ResolveSafePath(path);
        public Task<VaultDocument?> ReadAsync(string path, CancellationToken cancellationToken = default) => inner.ReadAsync(path, cancellationToken);
        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default) => inner.ListMarkdownFilesAsync(cancellationToken);
        public async Task<VaultWriteResult> CreateAsync(string path, string text, bool hasUtf8Bom = false, CancellationToken cancellationToken = default)
        {
            var result = await inner.CreateAsync(path, text, hasUtf8Bom, cancellationToken);
            ExitIfCheckpoint(text);
            return result;
        }
        public async Task<VaultWriteResult> WriteAsync(string path, string text, string? expectedRevision, bool hasUtf8Bom = false, CancellationToken cancellationToken = default)
        {
            var result = await inner.WriteAsync(path, text, expectedRevision, hasUtf8Bom, cancellationToken);
            ExitIfCheckpoint(text);
            return result;
        }
        private void ExitIfCheckpoint(string text)
        {
            var hasLink = text.Contains("unlimotion://task/", StringComparison.Ordinal);
            if (stage is "append" or "retained-append" && !hasLink || stage == "link" && hasLink) Environment.Exit(73);
        }
    }
}
