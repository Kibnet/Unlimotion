using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Unlimotion.TaskTree;

namespace Unlimotion.Storage;

public partial class FileTaskStorage
{
    private readonly ConditionalWeakTable<TaskGraphObservation, ObservedSources> _observedSources = new();
    private readonly AsyncLocal<ObservedWriteGuard?> _activeObservedWriteGuard = new();

    public IDisposable BeginObservedWriteGuard(TaskGraphObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!IsDirectoryLockHeld())
            throw new InvalidOperationException("An observed write guard requires the storage write lock.");
        if (_activeWriteScope.Value == null)
            throw new InvalidOperationException("An observed write guard requires an active recoverable write scope.");
        if (!_observedSources.TryGetValue(observation, out var sources))
            throw new InvalidOperationException("An observed write guard requires an observation acquired by the same storage instance.");
        var guard = new ObservedWriteGuard(this, sources, _activeObservedWriteGuard.Value);
        _activeObservedWriteGuard.Value = guard;
        _activeWriteScope.Value?.UseObservedWriteGuard(guard);
        return guard;
    }

    private sealed record ObservedSources(IReadOnlyDictionary<string, byte[]> Hashes);

    private sealed class ObservedWriteGuard : IDisposable
    {
        private readonly FileTaskStorage _owner;
        private readonly ObservedWriteGuard? _previous;
        private readonly Dictionary<string, byte[]> _expectedHashes;
        private bool _disposed;

        public ObservedWriteGuard(FileTaskStorage owner, ObservedSources sources, ObservedWriteGuard? previous)
        {
            _owner = owner;
            _previous = previous;
            _expectedHashes = new Dictionary<string, byte[]>(sources.Hashes, FilePathComparer);
        }

        public async Task<GuardedSourceSnapshot> CheckBeforeWriteAsync(string path)
        {
            VerifyNames();
            if (!_expectedHashes.TryGetValue(path, out var expected))
                return new GuardedSourceSnapshot(false, null);
            var source = await ReadGuardedSourceAsync(path, 0);
            if (!Matches(source.Hash, expected)) Conflict();
            return new GuardedSourceSnapshot(true, expected);
        }

        public void RecordWritten(string path, byte[] hash) => _expectedHashes[path] = hash;

        public async Task VerifyBeforeCommitAsync()
        {
            VerifyNames();
            long bytes = 0;
            foreach (var pair in _expectedHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var source = await ReadGuardedSourceAsync(pair.Key, bytes);
                bytes += source.Length;
                if (!Matches(source.Hash, pair.Value)) Conflict();
            }
            VerifyNames();
        }

        private void VerifyNames()
        {
            string[] actual;
            try { actual = _owner.EnumerateObservationFiles(CancellationToken.None); }
            catch (TaskGraphObservationException exception)
            { throw new LiveGraphInvalidatedException("Task source changed during the guarded application: " + exception.Message); }
            var expected = _expectedHashes.Keys.Order(StringComparer.Ordinal);
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) Conflict();
        }

        private static async Task<ObservationSource> ReadGuardedSourceAsync(string path, long precedingBytes)
        {
            try { return await ReadObservationFileAsync(path, includeContent: false, precedingBytes, CancellationToken.None); }
            catch (TaskGraphObservationException exception)
            { throw new LiveGraphInvalidatedException("Task source changed during the guarded application: " + exception.Message); }
        }

        private static bool Matches(string actual, byte[] expected) =>
            CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual[7..]), expected);

        private static void Conflict() => throw new LiveGraphInvalidatedException(
            "Task source changed after the verified observation or during the guarded application.");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_owner._activeObservedWriteGuard.Value, this))
                _owner._activeObservedWriteGuard.Value = _previous;
        }
    }

    private async Task<bool> RollbackObservedJournalAsync(RecoverableMutationJournal journal, bool commitPersisted)
    {
        var externalChangesPreserved = false;
        foreach (var entry in journal.Entries)
        {
            var path = ValidateJournalFilePath(entry.FilePath);
            var before = entry.BeforeExists ? Convert.FromBase64String(entry.BeforeBase64!) : null;
            var after = entry.AfterExists ? Convert.FromBase64String(entry.AfterBase64!) : null;
            var current = await ReadRollbackSourceAsync(path);
            // A persisted commit already confirms all writes preceded it. Do not replay over a later writer.
            if (commitPersisted)
            {
                externalChangesPreserved |= !SameBytes(current, after);
                continue;
            }
            if (SameBytes(current, before)) continue;
            if (!SameBytes(current, after))
            {
                externalChangesPreserved = true;
                continue;
            }
            if (after == null)
                throw new IOException("Observed write rollback cannot safely restore a removed task.");

            string? rollbackImagePath = null;
            if (before != null)
            {
                rollbackImagePath = path + "." + Guid.NewGuid().ToString("N") + ".bak";
                await File.WriteAllBytesAsync(rollbackImagePath, before);
            }
            var rollback = new AtomicWriteLease(path, rollbackImagePath, SHA256.HashData(after));
            if (!rollback.RollbackIfOwnContent())
                throw new IOException("Observed write rollback could not safely restore its original source; the journal remains for recovery.");
            externalChangesPreserved |= !SameBytes(await ReadRollbackSourceAsync(path), before);
            if (rollbackImagePath != null) TryDelete(rollbackImagePath);
        }
        return externalChangesPreserved;
    }

    private static async Task<byte[]?> ReadRollbackSourceAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
            using var buffer = new MemoryStream();
            var block = new byte[64 * 1024];
            int count;
            while ((count = await stream.ReadAsync(block)) > 0)
            {
                if (buffer.Length + count > ObservationMaximumFileBytes)
                    throw new IOException("Task source exceeds the bounded conditional rollback read; source preserved and journal retained.");
                buffer.Write(block, 0, count);
            }
            return buffer.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool SameBytes(byte[]? left, byte[]? right) => left == null
        ? right == null
        : right != null && left.AsSpan().SequenceEqual(right);
}
