using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Storage;

public partial class FileTaskStorage
{
    private const int ObservationMaximumFiles = 50_000;
    private const long ObservationMaximumFileBytes = 8L * 1024 * 1024;
    private const long ObservationMaximumSourceBytes = 256L * 1024 * 1024;
    private const int ObservationMaximumAttempts = 3;
    private static readonly TimeSpan ObservationDeadline = TimeSpan.FromSeconds(60);
    private static readonly ObservationContractResolver PreservingObservationResolver = new(true);
    private static readonly ObservationContractResolver IgnoringObservationResolver = new(false);

    public async Task<TaskGraphObservation> ReadObservationAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var counters = new ObservationCounters();
        using var deadline = new CancellationTokenSource(ObservationDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            for (var attempt = 1; attempt <= ObservationMaximumAttempts; attempt++)
            {
                counters.Attempts = attempt;
                linked.Token.ThrowIfCancellationRequested();
                EnsureObservationDirectoryExists();
                try
                {
                    // Observation never invokes recovery, including when reusing an outer write lock.
                    return await WithDirectoryLockCoreAsync(
                        () => ReadObservationAttemptAsync(startedAt, counters, linked.Token),
                        linked.Token,
                        recoverPendingTransactions: false,
                        requireDirectoryLock: true);
                }
                catch (ObservationDriftException) when (attempt < ObservationMaximumAttempts)
                {
                    // Each independent attempt relinquishes its lock. An outer caller retains its own lock.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TaskGraphObservationException("observationFailed",
                "The observation exceeded its 60 second deadline.", exception) { Attempts = counters.Attempts };
        }
        catch (ObservationDriftException exception)
        {
            throw new TaskGraphObservationException("snapshotUnstable",
                "The task source changed during all three observation attempts. No complete snapshot was acquired.",
                exception) { Attempts = counters.Attempts };
        }
        catch (TaskGraphObservationException exception) when (exception.Attempts == 0)
        {
            throw new TaskGraphObservationException(exception.Kind, exception.Message, exception)
            {
                Attempts = counters.Attempts, Limit = exception.Limit, Observed = exception.Observed,
                Maximum = exception.Maximum, Graph = exception.Graph
            };
        }
        catch (TaskGraphObservationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            throw new TaskGraphObservationException("observationFailed",
                "The task source could not be completely observed: " + exception.Message, exception)
            { Attempts = counters.Attempts };
        }

        throw new InvalidOperationException("An observation attempt must either return or fail.");
    }

    private async Task<TaskGraphObservation> ReadObservationAttemptAsync(
        DateTimeOffset startedAt,
        ObservationCounters counters,
        CancellationToken cancellationToken)
    {
        EnsureObservationDirectoryExists();
        EnsureNoPendingObservationRecovery(counters.Attempts);
        var paths = EnumerateObservationFiles(cancellationToken);
        var tasks = new List<TaskItem>(paths.Length);
        var filesById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        var unstableIds = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<TaskGraphLoadError>();
        var resolver = _options.PreserveUnknownJson ? PreservingObservationResolver : IgnoringObservationResolver;
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = resolver,
            Converters = CreateConverters()
        });
        long sourceBytes = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = await ReadObservationFileAsync(path, includeContent: true, sourceBytes, cancellationToken);
            sourceBytes += source.Length;
            manifest.Add(System.IO.Path.GetFileName(path), source.Hash);
            if (source.Length == 0)
            {
                errors.Add(new TaskGraphLoadError(path, "A zero-byte task file makes the observation incomplete."));
                continue;
            }

            try
            {
                counters.TaskParseCount++;
                var task = JsonRepairingReader.DeserializeWithRepair<TaskItem>(
                    source.Content!, path, serializer, saveRepairedSidecar: false);
                cancellationToken.ThrowIfCancellationRequested();
                if (task == null || !TryValidateTaskId(task.Id, out _))
                {
                    errors.Add(new TaskGraphLoadError(path, "File does not contain a task with a valid non-empty Id."));
                    continue;
                }

                tasks.Add(task);
                if (!resolver.HasCreatedDateTime(task))
                {
                    unstableIds.Add(task.Id);
                }

                if (!filesById.TryGetValue(task.Id, out var aliases))
                {
                    aliases = [];
                    filesById.Add(task.Id, aliases);
                }
                aliases.Add(path);
            }
            catch (JsonException exception)
            {
                errors.Add(new TaskGraphLoadError(path, exception.Message));
            }
        }

        await OnObservationFirstPassCompletedAsync(counters.Attempts, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoPendingObservationRecovery(counters.Attempts);
        var verificationPaths = EnumerateObservationFiles(cancellationToken);
        if (!paths.SequenceEqual(verificationPaths, StringComparer.Ordinal))
        {
            throw new ObservationDriftException("The task source file names changed between passes.");
        }

        long verifiedBytes = 0;
        foreach (var path in verificationPaths)
        {
            var source = await ReadObservationFileAsync(path, includeContent: false, verifiedBytes, cancellationToken);
            verifiedBytes += source.Length;
            counters.VerifiedFileCount++;
            if (!string.Equals(manifest[System.IO.Path.GetFileName(path)], source.Hash, StringComparison.Ordinal))
            {
                throw new ObservationDriftException("Task source bytes changed between passes.");
            }
        }

        // Also detect additions/removals that happened while the verification pass was in progress.
        if (!verificationPaths.SequenceEqual(EnumerateObservationFiles(cancellationToken), StringComparer.Ordinal))
        {
            throw new ObservationDriftException("The task source file names changed during verification.");
        }
        EnsureNoPendingObservationRecovery(counters.Attempts);
        var evaluatedAt = DateTimeOffset.UtcNow;
        var graph = new TaskGraphReadResult(
            tasks,
            filesById.ToDictionary(pair => pair.Key, pair => pair.Value[^1], StringComparer.Ordinal),
            errors,
            filesById.Where(pair => pair.Value.Count > 1)
                .Select(pair => new TaskGraphDuplicateIdIssue(pair.Key, pair.Value.ToArray())).ToArray());
        if (graph.LoadErrors.Count > 0 || graph.DuplicateIdIssues.Count > 0)
        {
            throw new TaskGraphObservationException("observationFailed",
                "The task source contains unreadable tasks or duplicate IDs; no complete observation is available.")
            { Graph = graph, Attempts = counters.Attempts };
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Keep reload's retained aliases in step with verified physical sources, including renames.
        // A later failed forced load clears the current mapping but must not turn corruption into Missing.
        lock (_liveGraphSync)
        {
            foreach (var pair in graph.FilesByTaskId)
            {
                _taskFilePaths[pair.Key] = pair.Value;
                _reloadTaskFilePaths[pair.Key] = pair.Value;
            }
        }
        var observation = new TaskGraphObservation(graph, HashObservationManifest(manifest), unstableIds,
            startedAt, evaluatedAt, DateTimeOffset.UtcNow, counters.Attempts, counters.TaskParseCount,
            counters.VerifiedFileCount, sourceBytes);
        _observedSources.Add(observation, new ObservedSources(manifest.ToDictionary(
            pair => System.IO.Path.Combine(Path, pair.Key), pair => Convert.FromHexString(pair.Value[7..]), FilePathComparer)));
        return observation;
    }

    protected virtual Task OnObservationFirstPassCompletedAsync(int attempt, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private void EnsureObservationDirectoryExists()
    {
        // Directory.Exists hides access errors; GetAttributes keeps those distinguishable from an empty store.
        if ((File.GetAttributes(Path) & FileAttributes.Directory) == 0)
        {
            throw new IOException("The task source is not a directory.");
        }
    }

    private void EnsureNoPendingObservationRecovery(int attempt)
    {
        try
        {
            if ((File.GetAttributes(TransactionDirectoryPath) & FileAttributes.Directory) == 0)
            {
                throw new IOException("The transaction journal location is not a directory.");
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }
        if (Directory.EnumerateFiles(TransactionDirectoryPath, "*.json", SearchOption.TopDirectoryOnly).Any())
        {
            throw new TaskGraphObservationException("recoveryRequired",
                "Pending task transaction journals require recovery through the authorized write path before observation.")
            { Attempts = attempt };
        }
    }

    private string[] EnumerateObservationFiles(CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTaskFile(System.IO.Path.GetFileName(path)))
            {
                continue;
            }
            paths.Add(path);
            EnsureObservationLimit("sourceTaskFiles", paths.Count, ObservationMaximumFiles);
        }
        paths.Sort(StringComparer.Ordinal);
        return paths.ToArray();
    }

    private static async Task<ObservationSource> ReadObservationFileAsync(
        string path,
        bool includeContent,
        long precedingBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            EnsureObservationLimit("sourceTaskFileBytes", stream.Length, ObservationMaximumFileBytes);
            EnsureObservationLimit("sourceBytes", precedingBytes + stream.Length, ObservationMaximumSourceBytes);
            using var content = includeContent ? new MemoryStream((int)stream.Length) : null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long length = 0;
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    length += read;
                    EnsureObservationLimit("sourceTaskFileBytes", length, ObservationMaximumFileBytes);
                    EnsureObservationLimit("sourceBytes", precedingBytes + length, ObservationMaximumSourceBytes);
                    hash.AppendData(buffer, 0, read);
                    content?.Write(buffer, 0, read);
                }
                return new ObservationSource(content?.ToArray(),
                    "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (FileNotFoundException exception)
        {
            throw new ObservationDriftException("A task source file disappeared while being read.", exception);
        }
    }

    private static void EnsureObservationLimit(string limit, long observed, long maximum)
    {
        if (observed > maximum)
        {
            throw new TaskGraphObservationException("snapshotTooLarge",
                $"Source limit '{limit}' exceeded: at least {observed}, maximum {maximum}. Narrowing selection does not reduce the source scan.")
            { Limit = limit, Observed = observed, Maximum = maximum };
        }
    }

    private static string HashObservationManifest(IReadOnlyDictionary<string, string> manifest)
    {
        // A compact JSON array of ordinally sorted [relative filename, original byte hash] pairs.
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(text))
        {
            writer.WriteStartArray();
            foreach (var pair in manifest.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray();
                writer.WriteValue(pair.Key);
                writer.WriteValue(pair.Value);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class ObservationCounters
    {
        public int Attempts;
        public int TaskParseCount;
        public int VerifiedFileCount;
    }

    private sealed record ObservationSource(byte[]? Content, string Hash, long Length);
    private sealed class ObservationDriftException(string message, Exception? innerException = null)
        : IOException(message, innerException);

    private sealed class ObservationContractResolver(bool preserveUnknownJson) : DefaultContractResolver
    {
        private readonly ConditionalWeakTable<object, object> _createdDateTimes = new();
        public bool HasCreatedDateTime(TaskItem task) => _createdDateTimes.TryGetValue(task, out _);

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (member.DeclaringType == typeof(TaskItem) && member.Name == nameof(TaskItem.CreatedDateTime))
            {
                property.ValueProvider = new ObservedDateValueProvider(property.ValueProvider!, _createdDateTimes);
            }
            return property;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            if (!preserveUnknownJson)
            {
                contract.ExtensionDataGetter = null;
                contract.ExtensionDataSetter = null;
            }
            return contract;
        }
    }

    private sealed class ObservedDateValueProvider(IValueProvider inner, ConditionalWeakTable<object, object> supplied)
        : IValueProvider
    {
        public object? GetValue(object target) => inner.GetValue(target);
        public void SetValue(object target, object? value)
        {
            inner.SetValue(target, value);
            supplied.GetValue(target, static _ => new object());
        }
    }
}
