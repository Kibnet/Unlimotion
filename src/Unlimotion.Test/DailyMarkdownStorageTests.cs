using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class DailyMarkdownStorageTests
{
    [Test]
    public async Task WriteAsync_UsesRevisionAndDoesNotOverwriteExternalChange()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var created = await vault.CreateAsync("Ежедневные/2026-08-24.md", "Первая версия\n");
        var fullPath = vault.ResolveSafePath("Ежедневные/2026-08-24.md");
        await File.WriteAllTextAsync(fullPath, "Изменено в Obsidian\n", new UTF8Encoding(false));

        var conflict = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() =>
            vault.WriteAsync("Ежедневные/2026-08-24.md", "Перезапись\n", created.Revision));

        await Assert.That(conflict.ExpectedRevision).IsEqualTo(created.Revision);
        await Assert.That(await File.ReadAllTextAsync(fullPath)).IsEqualTo("Изменено в Obsidian\n");
    }

    [Test]
    public async Task DeleteAsync_PreservesExternalWriteThatStartsAfterRevisionVerification()
    {
        using var directory = new TempNotesDirectory();
        using var writerCancellation = new CancellationTokenSource();
        const string path = "Ежедневные/2026-08-24.md";
        const string externalText = "external replacement\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base\n");
        var writerBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? externalWriter = null;
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            afterRevisionVerified: null,
            afterDeleteRevisionVerified: async (fullPath, _) =>
            {
                externalWriter = Task.Run(async () =>
                {
                    while (true)
                    {
                        writerCancellation.Token.ThrowIfCancellationRequested();
                        try
                        {
                            await File.WriteAllTextAsync(
                                fullPath,
                                externalText,
                                new UTF8Encoding(false),
                                writerCancellation.Token);
                            return;
                        }
                        catch (IOException)
                        {
                            writerBlocked.TrySetResult();
                            await Task.Delay(TimeSpan.FromMilliseconds(10), writerCancellation.Token);
                        }
                    }
                });
                await writerBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });

        try
        {
            var deleted = await vault.DeleteAsync(path, seed.Revision);
            await externalWriter!.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(deleted).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(vault.ResolveSafePath(path))).IsEqualTo(externalText);
        }
        finally
        {
            writerCancellation.Cancel();
            if (externalWriter is not null)
            {
                try
                {
                    await externalWriter;
                }
                catch (OperationCanceledException) when (writerCancellation.IsCancellationRequested)
                {
                }
            }
        }
    }

    [Test]
    public async Task DeleteAsync_DoesNotDeleteExternalAtomicReplacementAfterRevisionVerification()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string externalText = "external atomic replacement\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base\n");
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            afterRevisionVerified: null,
            afterDeleteRevisionVerified: async (fullPath, cancellationToken) =>
            {
                var directoryPath = Path.GetDirectoryName(fullPath)!;
                var replacementPath = Path.Combine(
                    directoryPath,
                    $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.replacement.tmp");
                var backupPath = Path.Combine(
                    directoryPath,
                    $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.external.bak");
                await File.WriteAllTextAsync(replacementPath, externalText, new UTF8Encoding(false), cancellationToken);
                File.Replace(replacementPath, fullPath, backupPath, ignoreMetadataErrors: true);
            });

        var conflict = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() =>
            vault.DeleteAsync(path, seed.Revision));

        await Assert.That(conflict.ExpectedRevision).IsEqualTo(seed.Revision);
        await Assert.That(await File.ReadAllTextAsync(vault.ResolveSafePath(path))).IsEqualTo(externalText);
    }

    [Test]
    public async Task DeleteAsync_RetainsTombstoneWrittenAfterFinalRevisionCheck()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string lateWriterText = "late writer content\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base\n");
        string? tombstonePath = null;
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            afterRevisionVerified: null,
            afterDeleteTombstoneVerified: async (fullPath, cancellationToken) =>
            {
                tombstonePath = fullPath;
                await File.WriteAllTextAsync(fullPath, lateWriterText, new UTF8Encoding(false), cancellationToken);
            });

        var deleted = await vault.DeleteAsync(path, seed.Revision);
        var markdownFiles = await vault.ListMarkdownFilesAsync();

        await Assert.That(deleted).IsTrue();
        await Assert.That(File.Exists(vault.ResolveSafePath(path))).IsFalse();
        await Assert.That(tombstonePath).IsNotNull();
        var tombstoneRelativePath = Path.GetRelativePath(vault.RootPath, tombstonePath!).Replace('\\', '/');
        var tombstonePathParts = tombstoneRelativePath.Split('/');
        await Assert.That(tombstonePathParts[0]).IsEqualTo(".unlimotion");
        await Assert.That(tombstonePathParts[1]).IsEqualTo("deleted");
        await Assert.That(Guid.TryParseExact(tombstonePathParts[2], "N", out _)).IsTrue();
        await Assert.That(string.Join("/", tombstonePathParts.Skip(3))).IsEqualTo(path);
        await Assert.That(await File.ReadAllTextAsync(tombstonePath!)).IsEqualTo(lateWriterText);
        await Assert.That(markdownFiles).IsEmpty();
    }

    [Test]
    public async Task DeleteAsync_RetainsLateWriteFromHandleOpenedBeforeDelete()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string lateWriterText = "late writer from retained handle\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base\n");
        var originalPath = seedVault.ResolveSafePath(path);
        var lateWriterBytes = new UTF8Encoding(false).GetBytes(lateWriterText);
        var externalWriter = new FileStream(
            originalPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        async Task WriteLateTextAsync(CancellationToken cancellationToken)
        {
            externalWriter.Position = 0;
            externalWriter.SetLength(0);
            await externalWriter.WriteAsync(lateWriterBytes, cancellationToken);
            await externalWriter.FlushAsync(cancellationToken);
            externalWriter.Flush(flushToDisk: true);
        }

        string? tombstonePath = null;
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            afterRevisionVerified: null,
            afterDeleteTombstoneVerified: async (fullPath, cancellationToken) =>
            {
                tombstonePath = fullPath;
                await WriteLateTextAsync(cancellationToken);
            });

        try
        {
            bool deleted = false;
            Exception? deleteFailure = null;
            try
            {
                deleted = await vault.DeleteAsync(path, seed.Revision);
            }
            catch (Exception exception) when (exception is IOException or VaultRevisionConflictException)
            {
                deleteFailure = exception;
            }

            if (deleteFailure is null)
            {
                await externalWriter.DisposeAsync();

                await Assert.That(deleted).IsTrue();
                await Assert.That(tombstonePath).IsNotNull();
                await Assert.That(File.Exists(originalPath)).IsFalse();
                await Assert.That(await File.ReadAllTextAsync(tombstonePath!)).IsEqualTo(lateWriterText);
            }
            else
            {
                await WriteLateTextAsync(CancellationToken.None);
                await externalWriter.DisposeAsync();

                await Assert.That(File.Exists(originalPath)).IsTrue();
                await Assert.That(await File.ReadAllTextAsync(originalPath)).IsEqualTo(lateWriterText);
            }
        }
        finally
        {
            await externalWriter.DisposeAsync();
        }
    }

    [Test]
    public async Task ReadWrite_PreservesUtf8BomAndCrLf()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var created = await vault.CreateAsync("Ежедневные/2026-08-24.md", "# День\r\n\r\nТекст\r\n", hasUtf8Bom: true);
        var read = await vault.ReadAsync("Ежедневные/2026-08-24.md");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.HasUtf8Bom).IsTrue();
        await Assert.That(read.NewLine).IsEqualTo("\r\n");
        await Assert.That(read.Revision).IsEqualTo(created.Revision);
        await Assert.That(read.Text).IsEqualTo("# День\r\n\r\nТекст\r\n");
    }

    [Test]
    public async Task ResolveSafePath_RejectsAbsoluteAndTraversalPaths()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);

        var traversal = await NotesTestSupport.Capture<UnauthorizedAccessException>(() => vault.ResolveSafePath("../outside.md"));
        var absolute = await NotesTestSupport.Capture<UnauthorizedAccessException>(() => vault.ResolveSafePath(Path.GetFullPath("outside.md")));

        await Assert.That(traversal.Message.Length > 0).IsTrue();
        await Assert.That(absolute.Message.Length > 0).IsTrue();
    }

    [Test]
    public async Task BoundedRevisions_StayOutsideVaultAndRespectRetention()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var store = new BoundedRevisionStore(recoveryDirectory.Path, retention: 2);
        for (var index = 0; index < 3; index++)
        {
            await store.SaveAsync("vault1", new VaultDocument("Ежедневные/2026-08-24.md", $"v{index}", $"r{index}", false, "\n"));
            await Task.Delay(2);
        }

        var revisions = await store.ListAsync("vault1", "Ежедневные/2026-08-24.md");

        await Assert.That(revisions.Count).IsEqualTo(2);
        await Assert.That(Directory.EnumerateFiles(vaultDirectory.Path, "*", SearchOption.AllDirectories).Any()).IsFalse();
        await Assert.That(revisions.All(path => path.StartsWith(recoveryDirectory.Path, StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task RecursiveEnumerationDoesNotFollowDirectorySymlinkOutsideVault()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var outsideDirectory = new TempNotesDirectory();
        await File.WriteAllTextAsync(System.IO.Path.Combine(outsideDirectory.Path, "outside.md"), "secret");
        var link = System.IO.Path.Combine(vaultDirectory.Path, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory.Path);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var files = await new FileNoteVault(vaultDirectory.Path).ListMarkdownFilesAsync();

        await Assert.That(files.Any(path => path.Contains("outside.md", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task WriteThroughDirectorySymlink_IsRejectedBeforeCreatingOutsideDirectories()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var outsideDirectory = new TempNotesDirectory();
        var link = System.IO.Path.Combine(vaultDirectory.Path, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory.Path);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var vault = new FileNoteVault(vaultDirectory.Path);
        var exception = await NotesTestSupport.CaptureAsync<UnauthorizedAccessException>(() =>
            vault.CreateAsync("linked/new-folder/note.md", "must stay inside\n"));

        await Assert.That(exception.Message).Contains("Symbolic links");
        await Assert.That(Directory.Exists(System.IO.Path.Combine(outsideDirectory.Path, "new-folder"))).IsFalse();
    }

    [Test]
    public async Task ConcurrentCreateNeverOverwritesTheWinningFile()
    {
        using var directory = new TempNotesDirectory();
        var firstVault = new FileNoteVault(directory.Path);
        var secondVault = new FileNoteVault(directory.Path);
        var first = firstVault.CreateAsync("Ежедневные/2026-08-24.md", "first\n");
        var second = secondVault.CreateAsync("Ежедневные/2026-08-24.md", "second\n");

        var results = await Task.WhenAll(CaptureCreate(first), CaptureCreate(second));
        var text = (await firstVault.ReadAsync("Ежедневные/2026-08-24.md"))!.Text;

        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(result => result is VaultRevisionConflictException)).IsEqualTo(1);
        await Assert.That(text is "first\n" or "second\n").IsTrue();
    }

    [Test]
    public async Task ConcurrentWritesFromDifferentVaultInstancesAllowOnlyOneMatchingRevision()
    {
        using var directory = new TempNotesDirectory();
        var seedVault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-24.md";
        var seed = await seedVault.CreateAsync(path, "base\n");
        var firstVault = new FileNoteVault(directory.Path);
        var secondVault = new FileNoteVault(directory.Path);
        using var start = new Barrier(3);

        var first = Task.Run(async () =>
        {
            start.SignalAndWait();
            return await firstVault.WriteAsync(path, "first\n", seed.Revision);
        });
        var second = Task.Run(async () =>
        {
            start.SignalAndWait();
            return await secondVault.WriteAsync(path, "second\n", seed.Revision);
        });
        start.SignalAndWait();

        var results = await Task.WhenAll(CaptureCreate(first), CaptureCreate(second));
        var document = await seedVault.ReadAsync(path);

        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(result => result is VaultRevisionConflictException)).IsEqualTo(1);
        await Assert.That(document!.Text is "first\n" or "second\n").IsTrue();
    }

    [Test]
    public async Task WriteAsync_StagesSiblingTempAndCleansItWhenCommitPreparationFails()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string originalText = "base\n";
        const string replacementText = "replacement that must be complete before commit\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, originalText);
        string? stagedText = null;
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            async (fullPath, cancellationToken) =>
            {
                var directoryPath = Path.GetDirectoryName(fullPath)!;
                var tempPaths = Directory.GetFiles(
                    directoryPath,
                    $".{Path.GetFileName(fullPath)}.*.tmp",
                    SearchOption.TopDirectoryOnly);
                if (tempPaths.Length == 1)
                {
                    stagedText = await File.ReadAllTextAsync(tempPaths[0], cancellationToken);
                }

                throw new IOException("Simulated failure after revision verification.");
            });

        var failure = await NotesTestSupport.CaptureAsync<IOException>(() =>
            vault.WriteAsync(path, replacementText, seed.Revision));
        var finalText = await File.ReadAllTextAsync(vault.ResolveSafePath(path));
        var remainingTemps = Directory.GetFiles(
            Path.GetDirectoryName(vault.ResolveSafePath(path))!,
            $".{Path.GetFileName(path)}.*.tmp",
            SearchOption.TopDirectoryOnly);

        await Assert.That(failure.Message).IsEqualTo("Simulated failure after revision verification.");
        await Assert.That(stagedText).IsEqualTo(replacementText);
        await Assert.That(finalText).IsEqualTo(originalText);
        await Assert.That(remainingTemps).IsEmpty();
    }

    [Test]
    public async Task WriteAsync_DoesNotSilentlyOverwriteExternalChangeAfterRevisionWasVerified()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base\n");
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    "external replacement\n",
                    new UTF8Encoding(false),
                    cancellationToken);
            });

        var error = await CaptureCreate(vault.WriteAsync(path, "vault write\n", seed.Revision));
        var finalText = await File.ReadAllTextAsync(vault.ResolveSafePath(path));

        await Assert.That(error).IsTypeOf<VaultRevisionConflictException>();
        await Assert.That(finalText).IsEqualTo("external replacement\n");
    }

    [Test]
    public async Task WriteAsync_PreservesSecondExternalWriterRacingWithConflictRollback()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string firstExternalText = "external B\n";
        const string secondExternalText = "external C\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base A\n");
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    firstExternalText,
                    new UTF8Encoding(false),
                    cancellationToken);
            },
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    secondExternalText,
                    new UTF8Encoding(false),
                    cancellationToken);
            });

        var error = await CaptureCreate(vault.WriteAsync(path, "vault write\n", seed.Revision));
        var fullPath = vault.ResolveSafePath(path);
        var backupTexts = Directory
            .EnumerateFiles(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.*.bak")
            .Select(File.ReadAllText)
            .ToArray();
        var remainingRollbackTemps = Directory.GetFiles(
            Path.GetDirectoryName(fullPath)!,
            $".{Path.GetFileName(fullPath)}.*.rollback.tmp",
            SearchOption.TopDirectoryOnly);

        await Assert.That(error).IsTypeOf<VaultRevisionConflictException>();
        await Assert.That(await File.ReadAllTextAsync(fullPath)).IsEqualTo(secondExternalText);
        await Assert.That(backupTexts).Contains(firstExternalText);
        await Assert.That(backupTexts).Contains(secondExternalText);
        await Assert.That(remainingRollbackTemps).IsEmpty();
    }

    [Test]
    public async Task WriteAsync_RestoresThirdExternalWriterRacingWithConcurrentVersionRestore()
    {
        using var directory = new TempNotesDirectory();
        const string path = "Ежедневные/2026-08-24.md";
        const string firstExternalText = "external B\n";
        const string secondExternalText = "external C\n";
        const string thirdExternalText = "external D\n";
        var seedVault = new FileNoteVault(directory.Path);
        var seed = await seedVault.CreateAsync(path, "base A\n");
        var vault = new FileNoteVault(
            directory.Path,
            ownWrites: null,
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    firstExternalText,
                    new UTF8Encoding(false),
                    cancellationToken);
            },
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    secondExternalText,
                    new UTF8Encoding(false),
                    cancellationToken);
            },
            async (fullPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    thirdExternalText,
                    new UTF8Encoding(false),
                    cancellationToken);
            });

        var error = await CaptureCreate(vault.WriteAsync(path, "vault write\n", seed.Revision));
        var fullPath = vault.ResolveSafePath(path);
        var backupTexts = Directory
            .EnumerateFiles(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.*.bak")
            .Select(File.ReadAllText)
            .ToArray();
        var remainingRollbackTemps = Directory.GetFiles(
            Path.GetDirectoryName(fullPath)!,
            $".{Path.GetFileName(fullPath)}.*.rollback.tmp",
            SearchOption.TopDirectoryOnly);

        await Assert.That(error).IsTypeOf<VaultRevisionConflictException>();
        await Assert.That(await File.ReadAllTextAsync(fullPath)).IsEqualTo(thirdExternalText);
        await Assert.That(backupTexts).Contains(firstExternalText);
        await Assert.That(backupTexts).Contains(secondExternalText);
        await Assert.That(backupTexts).Contains(thirdExternalText);
        await Assert.That(remainingRollbackTemps).IsEmpty();
    }

    private static async Task<Exception?> CaptureCreate(Task<VaultWriteResult> operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}

internal sealed class TempNotesDirectory : IDisposable
{
    public TempNotesDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-notes-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class NotesTestSupport
{
    public static async Task<TException> CaptureAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static Task<TException> Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return Task.FromResult(exception);
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
