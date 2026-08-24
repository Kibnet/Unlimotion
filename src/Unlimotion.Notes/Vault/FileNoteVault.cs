using System.Collections.Concurrent;
using System.Text;
using Unlimotion.Notes.Watching;

namespace Unlimotion.Notes.Vault;

public sealed class FileNoteVault : INoteVault
{
    private const int MaximumConflictRestoreAttempts = 8;
    private const string DeletedTombstonesRelativeDirectory = ".unlimotion/deleted";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SharedFileLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string canonicalRootWithSeparator;
    private readonly OwnWriteRegistry? ownWrites;
    private readonly Func<string, CancellationToken, ValueTask>? afterRevisionVerified;
    private readonly Func<string, CancellationToken, ValueTask>? afterDeleteRevisionVerified;
    private readonly Func<string, CancellationToken, ValueTask>? afterDeleteTombstoneVerified;
    private readonly Func<string, CancellationToken, ValueTask>? beforeConflictRollback;
    private readonly Func<string, CancellationToken, ValueTask>? beforeConcurrentVersionRestore;

    public FileNoteVault(string rootPath, OwnWriteRegistry? ownWrites = null)
        : this(
            rootPath,
            ownWrites,
            afterRevisionVerified: null,
            beforeConflictRollback: null,
            beforeConcurrentVersionRestore: null,
            afterDeleteRevisionVerified: null,
            afterDeleteTombstoneVerified: null)
    {
    }

    internal FileNoteVault(
        string rootPath,
        OwnWriteRegistry? ownWrites,
        Func<string, CancellationToken, ValueTask>? afterRevisionVerified,
        Func<string, CancellationToken, ValueTask>? beforeConflictRollback = null,
        Func<string, CancellationToken, ValueTask>? beforeConcurrentVersionRestore = null,
        Func<string, CancellationToken, ValueTask>? afterDeleteRevisionVerified = null,
        Func<string, CancellationToken, ValueTask>? afterDeleteTombstoneVerified = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        EnsureNotReparsePoint(RootPath);
        canonicalRootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        this.ownWrites = ownWrites;
        this.afterRevisionVerified = afterRevisionVerified;
        this.afterDeleteRevisionVerified = afterDeleteRevisionVerified;
        this.afterDeleteTombstoneVerified = afterDeleteTombstoneVerified;
        this.beforeConflictRollback = beforeConflictRollback;
        this.beforeConcurrentVersionRestore = beforeConcurrentVersionRestore;
    }

    public string RootPath { get; }

    public async Task<VaultDocument?> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        EnsurePathComponentsSafe(fullPath);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        var body = hasBom ? bytes.AsSpan(Encoding.UTF8.GetPreamble().Length) : bytes.AsSpan();
        var text = Encoding.UTF8.GetString(body);
        return new VaultDocument(NormalizeRelative(relativePath), text, VaultRevision.Compute(bytes), hasBom, DetectNewLine(text));
    }

    public async Task<VaultWriteResult> WriteAsync(
        string relativePath,
        string text,
        string? expectedRevision,
        bool hasUtf8Bom = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = NormalizeRelative(relativePath);
        var fullPath = ResolveSafePath(normalized);
        var gate = SharedFileLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsurePathComponentsSafe(fullPath, includeLeaf: false);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            EnsurePathComponentsSafe(fullPath, includeLeaf: false);
            if (expectedRevision is null)
            {
                var actual = await ReadRevisionAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (actual is not null)
                {
                    throw new VaultRevisionConflictException(normalized, expectedRevision, actual);
                }
            }

            var bytes = VaultRevision.Encode(text, hasUtf8Bom);
            var revision = VaultRevision.Compute(bytes);
            using var ownWrite = ownWrites?.RegisterRevision(Guid.NewGuid().ToString("N"), normalized, revision);
            if (expectedRevision is null)
            {
                await AtomicCreateAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteExistingAtomicAsync(
                    fullPath,
                    normalized,
                    bytes,
                    revision,
                    expectedRevision,
                    cancellationToken).ConfigureAwait(false);
            }

            ownWrite?.Commit();
            return new VaultWriteResult(normalized, revision);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<VaultWriteResult> CreateAsync(
        string relativePath,
        string text,
        bool hasUtf8Bom = false,
        CancellationToken cancellationToken = default)
    {
        return await WriteAsync(relativePath, text, expectedRevision: null, hasUtf8Bom, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => EnumerateSafeFiles(RootPath, "*.md", cancellationToken)
                .Where(path => !Path.GetRelativePath(RootPath, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains(".unlimotion", StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(RootPath, path).Replace('\\', '/'))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray() as IReadOnlyList<string>,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        string relativePath,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRelative(relativePath);
        var fullPath = ResolveSafePath(normalized);
        var gate = SharedFileLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        OwnWriteRegistration? ownWrite = null;
        string? tombstonePath = null;
        try
        {
            EnsurePathComponentsSafe(fullPath);
            FileStream sourceStream;
            try
            {
                // Block ordinary writers while allowing this verified handle to be atomically relocated.
                sourceStream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                var actual = await ReadRevisionAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, expectedRevision, StringComparison.Ordinal))
                {
                    throw new VaultRevisionConflictException(normalized, expectedRevision, actual);
                }

                return false;
            }

            await using (sourceStream.ConfigureAwait(false))
            {
                var bytes = await ReadAllBytesAsync(sourceStream, cancellationToken).ConfigureAwait(false);
                var actual = VaultRevision.Compute(bytes);
                if (!string.Equals(actual, expectedRevision, StringComparison.Ordinal))
                {
                    throw new VaultRevisionConflictException(normalized, expectedRevision, actual);
                }

                if (afterDeleteRevisionVerified is not null)
                {
                    await afterDeleteRevisionVerified(fullPath, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                EnsurePathComponentsSafe(fullPath);
                var movedTombstonePath = CreateDeleteTombstonePath(normalized);
                tombstonePath = movedTombstonePath;
                ownWrite = ownWrites?.RegisterDeletion(Guid.NewGuid().ToString("N"), normalized);
                try
                {
                    File.Move(fullPath, movedTombstonePath, overwrite: false);
                }
                catch (FileNotFoundException)
                {
                    var current = await ReadRevisionAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
                    throw new VaultRevisionConflictException(normalized, expectedRevision, current);
                }

                var movedRevision = await ReadRevisionAsync(movedTombstonePath, CancellationToken.None).ConfigureAwait(false);
                // A replacement can move a different file into the path, so delete only the revision we verified.
                if (!string.Equals(movedRevision, expectedRevision, StringComparison.Ordinal))
                {
                    try
                    {
                        File.Move(movedTombstonePath, fullPath, overwrite: false);
                    }
                    catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                    {
                        throw CreateRollbackException(
                            normalized,
                            expectedRevision!,
                            movedRevision,
                            [movedTombstonePath],
                            restoreException);
                    }

                    throw new VaultRevisionConflictException(normalized, expectedRevision, movedRevision);
                }
            }

            if (afterDeleteTombstoneVerified is not null)
            {
                await afterDeleteTombstoneVerified(tombstonePath!, CancellationToken.None).ConfigureAwait(false);
            }

            // Preserve the detached inode: a POSIX writer can retain a handle past the final revision read.
            ownWrite?.Commit();
            return true;
        }
        finally
        {
            ownWrite?.Dispose();
            gate.Release();
        }
    }

    private string EnsureDeleteTombstoneDirectory()
    {
        var directory = ResolveSafePath(DeletedTombstonesRelativeDirectory);
        EnsurePathComponentsSafe(directory, includeLeaf: false);
        Directory.CreateDirectory(directory);
        EnsurePathComponentsSafe(directory);
        return directory;
    }

    private string CreateDeleteTombstonePath(string normalizedRelativePath)
    {
        var tombstoneDirectory = Path.Combine(
            EnsureDeleteTombstoneDirectory(),
            Guid.NewGuid().ToString("N"));
        var tombstonePath = Path.Combine(tombstoneDirectory, normalizedRelativePath);
        EnsurePathComponentsSafe(tombstonePath, includeLeaf: false);
        Directory.CreateDirectory(Path.GetDirectoryName(tombstonePath)!);
        EnsurePathComponentsSafe(tombstonePath, includeLeaf: false);
        return tombstonePath;
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(
        string relativeDirectory,
        string searchPattern,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        var directory = ResolveSafePath(relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        EnsurePathComponentsSafe(directory);
        return await Task.Run(
            () => EnumerateSafeFiles(directory, searchPattern, cancellationToken)
                .Select(path => Path.GetRelativePath(RootPath, path).Replace('\\', '/'))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray() as IReadOnlyList<string>,
            cancellationToken).ConfigureAwait(false);
    }

    public string ResolveSafePath(string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        var combined = Path.GetFullPath(Path.Combine(RootPath, normalized));
        if (!combined.StartsWith(canonicalRootWithSeparator, PathComparison)
            && !string.Equals(combined, RootPath, PathComparison))
        {
            throw new UnauthorizedAccessException("The note path escapes the selected vault root.");
        }

        return combined;
    }

    private async Task AtomicCreateAsync(
        string fullPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteFlushedTempAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

            var actualBeforeCommit = await ReadRevisionAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (actualBeforeCommit is not null)
            {
                throw new VaultRevisionConflictException(Path.GetRelativePath(RootPath, fullPath), null, actualBeforeCommit);
            }

            try
            {
                File.Move(tempPath, fullPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                var winningRevision = await ReadRevisionAsync(fullPath, cancellationToken).ConfigureAwait(false);
                throw new VaultRevisionConflictException(Path.GetRelativePath(RootPath, fullPath), null, winningRevision);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task WriteExistingAtomicAsync(
        string fullPath,
        string normalizedPath,
        byte[] bytes,
        string revision,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        EnsurePathComponentsSafe(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        var tempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.bak");
        var deleteBackup = false;
        try
        {
            await WriteFlushedTempAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

            FileStream sourceStream;
            try
            {
                sourceStream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                var actual = await ReadRevisionAsync(fullPath, cancellationToken).ConfigureAwait(false);
                throw new VaultRevisionConflictException(normalizedPath, expectedRevision, actual);
            }

            await using (sourceStream.ConfigureAwait(false))
            {
                var originalBytes = await ReadAllBytesAsync(sourceStream, cancellationToken).ConfigureAwait(false);
                var actual = VaultRevision.Compute(originalBytes);
                if (!string.Equals(actual, expectedRevision, StringComparison.Ordinal))
                {
                    throw new VaultRevisionConflictException(normalizedPath, expectedRevision, actual);
                }
            }

            if (afterRevisionVerified is not null)
            {
                await afterRevisionVerified(fullPath, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsurePathComponentsSafe(fullPath);
            try
            {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            catch (FileNotFoundException)
            {
                var actual = await ReadRevisionAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
                throw new VaultRevisionConflictException(normalizedPath, expectedRevision, actual);
            }

            var displacedRevision = await ReadRevisionAsync(backupPath, CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(displacedRevision, expectedRevision, StringComparison.Ordinal))
            {
                var currentRevision = await ReadRevisionAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
                if (string.Equals(currentRevision, revision, StringComparison.Ordinal))
                {
                    if (beforeConflictRollback is not null)
                    {
                        await beforeConflictRollback(fullPath, CancellationToken.None).ConfigureAwait(false);
                    }

                    await RestoreDisplacedVersionAsync(
                        fullPath,
                        normalizedPath,
                        backupPath,
                        revision,
                        expectedRevision,
                        displacedRevision).ConfigureAwait(false);
                }

                throw new VaultRevisionConflictException(normalizedPath, expectedRevision, displacedRevision);
            }

            deleteBackup = true;
            var actualAfterCommit = await ReadRevisionAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(actualAfterCommit, revision, StringComparison.Ordinal))
            {
                throw new VaultRevisionConflictException(normalizedPath, expectedRevision, actualAfterCommit);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (deleteBackup && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
    }

    private async Task RestoreDisplacedVersionAsync(
        string fullPath,
        string normalizedPath,
        string displacedBackupPath,
        string attemptedRevision,
        string expectedRevision,
        string? displacedRevision)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        var rollbackTempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.rollback.tmp");
        var preservedCurrentPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.conflict.bak");
        try
        {
            var displacedBytes = await File.ReadAllBytesAsync(displacedBackupPath, CancellationToken.None)
                .ConfigureAwait(false);
            await WriteFlushedTempAsync(rollbackTempPath, displacedBytes, CancellationToken.None).ConfigureAwait(false);

            try
            {
                File.Replace(
                    rollbackTempPath,
                    fullPath,
                    preservedCurrentPath,
                    ignoreMetadataErrors: true);
            }
            catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
            {
                throw CreateRollbackException(
                    normalizedPath,
                    expectedRevision,
                    displacedRevision,
                    [displacedBackupPath, preservedCurrentPath],
                    restoreException);
            }

            var preservedCurrentRevision = await ReadRevisionAsync(preservedCurrentPath, CancellationToken.None)
                .ConfigureAwait(false);
            if (string.Equals(preservedCurrentRevision, attemptedRevision, StringComparison.Ordinal))
            {
                File.Delete(preservedCurrentPath);
                return;
            }

            if (beforeConcurrentVersionRestore is not null)
            {
                await beforeConcurrentVersionRestore(fullPath, CancellationToken.None).ConfigureAwait(false);
            }

            await RestoreLatestConcurrentVersionAsync(
                fullPath,
                normalizedPath,
                displacedBackupPath,
                preservedCurrentPath,
                preservedCurrentRevision,
                expectedRevision,
                displacedRevision).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(rollbackTempPath))
            {
                File.Delete(rollbackTempPath);
            }
        }
    }

    private static async Task RestoreLatestConcurrentVersionAsync(
        string fullPath,
        string normalizedPath,
        string displacedBackupPath,
        string preservedCurrentPath,
        string? preservedCurrentRevision,
        string expectedRevision,
        string? displacedRevision)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        var versionToRestorePath = preservedCurrentPath;
        var versionToRestoreRevision = preservedCurrentRevision;
        var expectedDestinationRevision = displacedRevision;
        var preservedPaths = new List<string> { displacedBackupPath, preservedCurrentPath };
        for (var attempt = 0; attempt < MaximumConflictRestoreAttempts; attempt++)
        {
            var concurrentRestoreTempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.rollback.tmp");
            var additionallyDisplacedPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.conflict.bak");
            preservedPaths.Add(additionallyDisplacedPath);
            try
            {
                var concurrentBytes = await File.ReadAllBytesAsync(versionToRestorePath, CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteFlushedTempAsync(concurrentRestoreTempPath, concurrentBytes, CancellationToken.None)
                    .ConfigureAwait(false);

                try
                {
                    File.Replace(
                        concurrentRestoreTempPath,
                        fullPath,
                        additionallyDisplacedPath,
                        ignoreMetadataErrors: true);
                }
                catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                {
                    throw CreateRollbackException(
                        normalizedPath,
                        expectedRevision,
                        displacedRevision,
                        preservedPaths,
                        restoreException);
                }

                var additionallyDisplacedRevision = await ReadRevisionAsync(
                        additionallyDisplacedPath,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.Equals(additionallyDisplacedRevision, expectedDestinationRevision, StringComparison.Ordinal))
                {
                    return;
                }

                versionToRestorePath = additionallyDisplacedPath;
                expectedDestinationRevision = versionToRestoreRevision;
                versionToRestoreRevision = additionallyDisplacedRevision;
            }
            finally
            {
                if (File.Exists(concurrentRestoreTempPath))
                {
                    File.Delete(concurrentRestoreTempPath);
                }
            }
        }

        throw CreateRollbackException(
            normalizedPath,
            expectedRevision,
            displacedRevision,
            preservedPaths,
            new IOException("The note kept changing while competing versions were being restored."));
    }

    private static AggregateException CreateRollbackException(
        string normalizedPath,
        string expectedRevision,
        string? displacedRevision,
        IReadOnlyCollection<string> preservedPaths,
        Exception restoreException) =>
        new(
            $"The note revision changed during atomic replacement and competing content was preserved at "
                + $"'{string.Join("', '", preservedPaths)}'.",
            new VaultRevisionConflictException(normalizedPath, expectedRevision, displacedRevision),
            restoreException);

    private static async Task<byte[]> ReadAllBytesAsync(FileStream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return buffer.ToArray();
    }

    private static async Task WriteFlushedTempAsync(
        string tempPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private void EnsurePathComponentsSafe(string path, bool includeLeaf = true)
    {
        var relative = Path.GetRelativePath(RootPath, path);
        var current = RootPath;
        var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var count = includeLeaf ? parts.Length : Math.Max(0, parts.Length - 1);
        for (var index = 0; index < count; index++)
        {
            current = Path.Combine(current, parts[index]);
            if (File.Exists(current) || Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException($"Symbolic links and junctions are not writable note paths: '{path}'.");
        }
    }

    private static async Task<string?> ReadRevisionAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return VaultRevision.Compute(bytes);
    }

    private static IEnumerable<string> EnumerateSafeFiles(
        string directory,
        string searchPattern,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(directory, searchPattern, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
        }
    }

    private static string NormalizeRelative(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.IndexOf('\0') >= 0)
        {
            throw new UnauthorizedAccessException("Only a relative vault path is allowed.");
        }

        var parts = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(static part => part is "." or ".."))
        {
            throw new UnauthorizedAccessException("Relative traversal is not allowed in note paths.");
        }

        return Path.Combine(parts);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string DetectNewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
