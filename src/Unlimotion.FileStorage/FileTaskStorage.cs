using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Storage;

public class FileTaskStorage : IStorage, ITaskGraphDiagnosticStorage, ITaskGraphWriteLock, ITaskGraphWriteScopeStorage
{
    // Contract metadata is reusable; serializers and converters remain local to each read.
    private static readonly IContractResolver PreservingReadContractResolver = new DefaultContractResolver();
    private static readonly IContractResolver IgnoringReadContractResolver = new IgnoreExtensionDataContractResolver();
    private static readonly AsyncLocal<HashSet<string>?> HeldDirectoryLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectorySemaphores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _taskFilePaths = new(StringComparer.Ordinal);
    private readonly FileTaskStorageOptions _options;
    private readonly object _liveGraphSync = new();
    private TaskGraphReadResult? _liveGraph;
    private long _liveGraphRevision;
    private long _liveGraphInvalidationGeneration;
    private volatile bool _liveGraphNeedsReload;
    private readonly AsyncLocal<FileTaskGraphWriteScope?> _activeWriteScope = new();

    public FileTaskStorage(FileTaskStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedPath = string.IsNullOrWhiteSpace(options.Path)
            ? "Tasks"
            : options.Path;

        Path = System.IO.Path.GetFullPath(normalizedPath);
        Directory.CreateDirectory(Path);
        _options = options with { Path = Path };
    }

    public string Path { get; }

    public event EventHandler<TaskStorageUpdateEventArgs>? Updating;

    public event Action<Exception?>? OnConnectionError
    {
        add { }
        remove { }
    }

    public async Task<TaskItem> Save(TaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_options.UseDirectoryLock)
        {
            return await WithDirectoryLockAsync(() => SaveCore(item));
        }

        return await SaveCore(item);
    }

    public async Task<bool> Remove(string itemId)
    {
        if (_options.UseDirectoryLock)
        {
            return await WithDirectoryLockAsync(() => RemoveCore(itemId));
        }

        return await RemoveCore(itemId);
    }

    public async Task<TaskItem?> Load(string itemId) => await Load(itemId, forced: false);

    public async Task<TaskItem?> Load(string itemId, bool forced)
    {
        var liveGraphEnabled = false;
        if (!forced && TryGetLiveTask(itemId, out var liveTask, out liveGraphEnabled))
        {
            return liveTask;
        }

        if (!forced && liveGraphEnabled)
        {
            return null;
        }

        if (!forced && _tasks.TryGetValue(itemId, out var cached))
        {
            return TaskItemSnapshot.Clone(cached);
        }

        if (!TryResolveTaskFilePath(itemId, out var filePath))
        {
            return null;
        }

        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
        {
            RemoveCachedTaskMappedToFile(filePath, exceptTaskId: null);
            _tasks.TryRemove(itemId, out _);
            _taskFilePaths.TryRemove(itemId, out _);
            PublishLiveFileChange(filePath, task: null, error: null);
            return null;
        }

        TaskItem? task;
        try
        {
            task = await Task.Run(() => DeserializeTask(filePath));
        }
        catch (Exception ex)
        {
            RemoveCachedTaskMappedToFile(filePath, exceptTaskId: null);
            _tasks.TryRemove(itemId, out _);
            _taskFilePaths.TryRemove(itemId, out _);
            PublishLiveFileChange(filePath, task: null, error: ex.Message);
            return null;
        }

        if (task == null || !TryValidateTaskId(task.Id, out _))
        {
            RemoveCachedTaskMappedToFile(filePath, exceptTaskId: null);
            _tasks.TryRemove(itemId, out _);
            _taskFilePaths.TryRemove(itemId, out _);
            PublishLiveFileChange(filePath, task: null, error: "File does not contain a task with a valid non-empty Id.");
            return null;
        }

        var stored = TaskItemSnapshot.Clone(task);
        RemoveCachedTaskMappedToFile(filePath, exceptTaskId: stored.Id);
        _tasks.AddOrUpdate(stored.Id, stored, (_, _) => stored);
        _taskFilePaths.AddOrUpdate(task.Id, filePath, (_, _) => filePath);
        PublishLiveFileChange(filePath, stored, error: null);
        return TaskItemSnapshot.Clone(stored);
    }

    public async IAsyncEnumerable<TaskItem> GetAll()
    {
        if (TryGetLiveGraph(out var liveGraph))
        {
            foreach (var task in liveGraph.Tasks)
            {
                yield return TaskItemSnapshot.Clone(task);
            }

            yield break;
        }

        foreach (var file in EnumerateTaskFiles())
        {
            TaskItem? task = null;
            try
            {
                task = await Task.Run(() => DeserializeTask(file));
            }
            catch
            {
                // Directory validation APIs expose load failures; GetAll preserves the old tolerant enumeration contract.
            }

            if (task == null || !TryValidateTaskId(task.Id, out _))
            {
                continue;
            }

            var stored = TaskItemSnapshot.Clone(task);
            _tasks.AddOrUpdate(stored.Id, stored, (_, _) => stored);
            _taskFilePaths.AddOrUpdate(task.Id, file, (_, _) => file);
            yield return TaskItemSnapshot.Clone(stored);
        }
    }

    public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
    {
        foreach (var taskItem in taskItems)
        {
            await Save(taskItem);
        }
    }

    public Task<bool> Connect() => Task.FromResult(true);

    public Task Disconnect() => Task.CompletedTask;

    public async Task<FileTaskStorageDirectoryReadResult> ReadDirectoryAsync()
    {
        if (_options.UseDirectoryLock && !IsDirectoryLockHeld())
        {
            return await WithDirectoryLockAsync(ReadDirectoryCoreAsync);
        }

        return await ReadDirectoryCoreAsync();
    }

    private async Task<FileTaskStorageDirectoryReadResult> ReadDirectoryCoreAsync()
    {
        var tasks = new List<TaskItem>();
        var taskFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var loadErrors = new List<FileTaskStorageLoadError>();

        foreach (var file in EnumerateTaskFiles())
        {
            try
            {
                var task = await Task.Run(() => DeserializeTask(file));
                if (task == null || string.IsNullOrWhiteSpace(task.Id))
                {
                    loadErrors.Add(new FileTaskStorageLoadError(file, "File does not contain a task with non-empty Id."));
                    continue;
                }

                if (!TryValidateTaskId(task.Id, out var validationError))
                {
                    loadErrors.Add(new FileTaskStorageLoadError(file, validationError));
                    continue;
                }

                tasks.Add(task);
                if (!taskFiles.TryGetValue(task.Id, out var files))
                {
                    files = new List<string>();
                    taskFiles.Add(task.Id, files);
                }

                files.Add(file);
                var stored = TaskItemSnapshot.Clone(task);
                _tasks.AddOrUpdate(task.Id, stored, (_, _) => stored);
                _taskFilePaths.AddOrUpdate(task.Id, file, (_, _) => file);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                loadErrors.Add(new FileTaskStorageLoadError(file, ex.Message));
            }
        }

        var duplicates = taskFiles
            .Where(static pair => pair.Value.Count > 1)
            .Select(static pair => new FileTaskStorageDuplicateIdIssue(pair.Key, pair.Value.ToArray()))
            .ToArray();

        var filesByTaskId = taskFiles
            .Where(static pair => pair.Value.Count > 0)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value[^1], StringComparer.Ordinal);

        return new FileTaskStorageDirectoryReadResult(tasks, filesByTaskId, loadErrors, duplicates);
    }

    public virtual async Task<TaskGraphReadResult> ReadGraphAsync()
    {
        if (_options.UseDirectoryLock && !IsDirectoryLockHeld())
        {
            return await WithDirectoryLockAsync(ReadGraphCoreAsync);
        }

        return await ReadGraphCoreAsync();
    }

    private async Task<TaskGraphReadResult> ReadGraphCoreAsync()
    {
        if (TryGetLiveGraph(out var liveGraph))
        {
            return CloneGraph(liveGraph);
        }

        var result = await ReadDirectoryCoreAsync();
        return ToGraphResult(result);
    }

    public async Task EnableLiveGraphAsync()
    {
        await WithDirectoryLockAsync(async () =>
        {
            var invalidationGeneration = Interlocked.Read(ref _liveGraphInvalidationGeneration);
            _tasks.Clear();
            _taskFilePaths.Clear();
            var result = await ReadDirectoryCoreAsync();
            PublishLiveGraph(ToGraphResult(result));
            MarkLiveGraphReloaded(invalidationGeneration);
        });
    }

    public long LiveGraphRevision => Interlocked.Read(ref _liveGraphRevision);

    public void InvalidateLiveGraph()
    {
        lock (_liveGraphSync)
        {
            Interlocked.Increment(ref _liveGraphInvalidationGeneration);
            _liveGraphNeedsReload = true;
            _tasks.Clear();
        }
    }

    public TaskGraphReadResult? ReadLastPublishedGraph()
    {
        lock (_liveGraphSync)
        {
            return _liveGraph == null ? null : CloneGraph(_liveGraph);
        }
    }

    protected async Task EnsureLiveGraphReadyWithinWriteLockAsync()
    {
        if (!_liveGraphNeedsReload)
        {
            return;
        }

        var invalidationGeneration = Interlocked.Read(ref _liveGraphInvalidationGeneration);
        _tasks.Clear();
        _taskFilePaths.Clear();
        var result = await ReadDirectoryCoreAsync();
        PublishLiveGraph(ToGraphResult(result));
        MarkLiveGraphReloaded(invalidationGeneration);
    }

    private static TaskGraphReadResult ToGraphResult(FileTaskStorageDirectoryReadResult result) =>
        new(
            result.Tasks,
            result.FilesByTaskId,
            result.LoadErrors
                .Select(static error => new TaskGraphLoadError(error.File, error.Message))
                .ToArray(),
            result.DuplicateIdIssues
                .Select(static issue => new TaskGraphDuplicateIdIssue(issue.TaskId, issue.Files))
                .ToArray());

    public virtual Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation) =>
        WithDirectoryLockAsync(operation);

    public ITaskGraphWriteScope BeginWriteScope()
    {
        var scope = new FileTaskGraphWriteScope(this, _activeWriteScope.Value);
        _activeWriteScope.Value = scope;
        return scope;
    }

    public async Task<TaskGraphReadResult> RefreshAttemptedWritesAsync(ITaskGraphWriteScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var taskId in scope.AttemptedTaskIds.Distinct(StringComparer.Ordinal))
        {
            await Load(taskId, forced: true);
        }

        return await ReadGraphAsync();
    }

    public Task<T> WithDirectoryLockAsync<T>(Func<Task<T>> operation) =>
        WithDirectoryLockAsync(operation, CancellationToken.None);

    public async Task<T> WithDirectoryLockAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!_options.UseDirectoryLock)
        {
            return await operation();
        }

        var lockPath = System.IO.Path.Combine(Path, ".unlimotion.lock");
        var previousLocks = HeldDirectoryLocks.Value;
        if (previousLocks?.Contains(lockPath) == true)
        {
            return await operation();
        }

        var semaphore = DirectorySemaphores.GetOrAdd(Path, static _ => new SemaphoreSlim(1, 1));
        using var timeout = new CancellationTokenSource(_options.DirectoryLockTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await semaphore.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for task directory lock '{lockPath}'.");
        }

        FileStream? lockStream = null;
        try
        {
            lockStream = await AcquireDirectoryLockAsync(lockPath, linked.Token, cancellationToken);
            var currentLocks = previousLocks == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(previousLocks, StringComparer.OrdinalIgnoreCase);
            currentLocks.Add(lockPath);
            HeldDirectoryLocks.Value = currentLocks;

            await RecoverPendingTransactionsAsync();

            return await operation();
        }
        finally
        {
            HeldDirectoryLocks.Value = previousLocks;
            lockStream?.Dispose();
            TryDelete(lockPath);
            semaphore.Release();
        }
    }

    public Task WithDirectoryLockAsync(Func<Task> operation) =>
        WithDirectoryLockAsync(operation, CancellationToken.None);

    public async Task WithDirectoryLockAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await WithDirectoryLockAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
    }

    protected void RaiseUpdating(TaskStorageUpdateEventArgs e) => Updating?.Invoke(this, e);

    private async Task<TaskItem> SaveCore(TaskItem taskItem)
    {
        var item = TaskItemSnapshot.Clone(taskItem);
        var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id;
        ValidateTaskId(id);
        item.Id = id;
        item.EnsureStatusHistory(item.UserId ?? "local-user");
        _activeWriteScope.Value?.Record(item.Id);

        var filePath = ResolveTaskFilePath(item.Id);
        var json = JsonConvert.SerializeObject(item, Formatting.Indented, CreateSerializerSettings());
        var content = json + Environment.NewLine;
        if (_activeWriteScope.Value != null)
        {
            await _activeWriteScope.Value.PrepareWriteAsync(item.Id, filePath, content);
        }

        OnBeforeWrite(item.Id, filePath);
        await AtomicWriteAllTextAsync(filePath, content);
        OnAfterWritePersisted(item.Id, filePath);

        taskItem.Id = item.Id;
        var stored = TaskItemSnapshot.Clone(item);
        _tasks.AddOrUpdate(taskItem.Id, stored, (_, _) => stored);
        _taskFilePaths.AddOrUpdate(taskItem.Id, filePath, (_, _) => filePath);
        PublishLiveFileChange(filePath, stored, error: null);
        return TaskItemSnapshot.Clone(stored);
    }

    private Task<bool> RemoveCore(string itemId)
    {
        ValidateTaskId(itemId);
        _activeWriteScope.Value?.Record(itemId);
        var filePath = ResolveTaskFilePath(itemId);
        _activeWriteScope.Value?.PrepareRemove(itemId, filePath);
        OnBeforeRemove(itemId, filePath);
        _tasks.TryRemove(itemId, out _);
        _taskFilePaths.TryRemove(itemId, out _);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        PublishLiveFileChange(filePath, task: null, error: null);

        return Task.FromResult(true);
    }

    private TaskItem? DeserializeTask(string fullPath)
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = _options.PreserveUnknownJson
                ? PreservingReadContractResolver
                : IgnoringReadContractResolver,
            Converters = CreateConverters()
        });

        return JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, serializer, saveRepairedSidecar: false);
    }

    protected virtual IEnumerable<string> EnumerateTaskFiles()
    {
        var directoryInfo = new DirectoryInfo(Path);
        return directoryInfo
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(static file => file.Length > 0 && IsTaskFile(file.Name))
            .OrderBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(static file => file.FullName)
            .ToArray();
    }

    protected static bool IsTaskFile(string fileName)
    {
        if (fileName.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(fileName);
        return extension.Length == 0 || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonConverter[] CreateConverters() =>
    [
        new IsoDateTimeConverter
        {
            DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
            Culture = CultureInfo.InvariantCulture,
            DateTimeStyles = DateTimeStyles.None
        },
        new StringEnumConverter()
    ];

    private JsonSerializerSettings CreateSerializerSettings() => new()
    {
        ContractResolver = _options.PreserveUnknownJson
            ? new DefaultContractResolver()
            : new IgnoreExtensionDataContractResolver(),
        Converters = CreateConverters()
    };

    private async Task AtomicWriteAllTextAsync(string filePath, string content)
    {
        var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = filePath + "." + Guid.NewGuid().ToString("N") + ".bak";

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task AtomicWriteAllBytesAsync(string filePath, byte[] content)
    {
        var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = filePath + "." + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content);
                await stream.FlushAsync();
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    protected virtual void OnBeforeWrite(string taskId, string filePath)
    {
    }

    protected virtual void OnBeforeRemove(string taskId, string filePath)
    {
    }

    protected virtual void OnAfterWritePersisted(string taskId, string filePath)
    {
    }

    protected virtual void OnAfterTransactionCommitted(string journalPath)
    {
    }

    protected virtual void OnBeforeTransactionJournalPersist(string journalPath)
    {
    }

    protected virtual void OnAfterTransactionJournalPersist(string journalPath)
    {
    }

    protected virtual void OnBeforeTransactionTargetDelete(string filePath)
    {
    }

    protected virtual void OnBeforeTransactionJournalDelete(string journalPath)
    {
    }

    private void DeleteTransactionJournal(string journalPath)
    {
        OnBeforeTransactionJournalDelete(journalPath);
        File.Delete(journalPath);
        if (File.Exists(journalPath))
        {
            throw new IOException($"Transaction journal '{journalPath}' could not be deleted.");
        }
    }

    protected bool TryGetTaskIdBySourceFileName(string fileName, out string taskId)
    {
        foreach (var pair in _taskFilePaths)
        {
            if (string.Equals(
                    System.IO.Path.GetFileName(pair.Value),
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                taskId = pair.Key;
                return true;
            }
        }

        taskId = string.Empty;
        return false;
    }

    private bool TryGetLiveTask(string taskId, out TaskItem? task, out bool liveGraphEnabled)
    {
        lock (_liveGraphSync)
        {
            liveGraphEnabled = _liveGraph != null && !_liveGraphNeedsReload;
            if (liveGraphEnabled && _liveGraph!.TasksById.TryGetValue(taskId, out var cached))
            {
                task = TaskItemSnapshot.Clone(cached);
                return true;
            }
        }

        task = null;
        return false;
    }

    private bool TryGetLiveGraph(out TaskGraphReadResult graph)
    {
        lock (_liveGraphSync)
        {
            if (_liveGraph == null || _liveGraphNeedsReload)
            {
                graph = null!;
                return false;
            }

            graph = _liveGraph;
            return true;
        }
    }

    private void PublishLiveGraph(TaskGraphReadResult graph)
    {
        lock (_liveGraphSync)
        {
            var revision = Interlocked.Increment(ref _liveGraphRevision);
            _liveGraph = CloneGraph(graph) with { Revision = revision };
        }
    }

    private void MarkLiveGraphReloaded(long invalidationGeneration)
    {
        lock (_liveGraphSync)
        {
            if (Interlocked.Read(ref _liveGraphInvalidationGeneration) == invalidationGeneration)
            {
                _liveGraphNeedsReload = false;
            }
        }
    }

    private void PublishLiveFileChange(string filePath, TaskItem? task, string? error)
    {
        lock (_liveGraphSync)
        {
            if (_liveGraph == null)
            {
                return;
            }

            // A graph with duplicate IDs needs its complete per-file ordering to select the canonical source.
            // It is already write-unsafe, so keep its diagnostics intact until an explicit reload.
            if (_liveGraph.DuplicateIdIssues.Count > 0)
            {
                InvalidateLiveGraph();
                return;
            }

            var previousTask = _liveGraph.FilesByTaskId
                .Where(pair => string.Equals(pair.Value, filePath, StringComparison.OrdinalIgnoreCase))
                .Select(pair => _liveGraph.TasksById.GetValueOrDefault(pair.Key))
                .FirstOrDefault(existing => existing != null);
            var previousError = _liveGraph.LoadErrors.FirstOrDefault(existing =>
                string.Equals(existing.File, filePath, StringComparison.OrdinalIgnoreCase));
            if (task != null && previousTask != null && previousError == null &&
                JsonConvert.SerializeObject(previousTask, CreateSerializerSettings()) ==
                JsonConvert.SerializeObject(task, CreateSerializerSettings()))
            {
                return;
            }

            var previousIds = _liveGraph.FilesByTaskId
                .Where(pair => string.Equals(pair.Value, filePath, StringComparison.OrdinalIgnoreCase))
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            var tasks = _liveGraph.Tasks
                .Where(existing => !previousIds.Contains(existing.Id))
                .Select(TaskItemSnapshot.Clone)
                .ToList();
            var files = _liveGraph.FilesByTaskId
                .Where(pair => !previousIds.Contains(pair.Key))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            var errors = _liveGraph.LoadErrors
                .Where(existing => !string.Equals(existing.File, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var duplicates = new List<TaskGraphDuplicateIdIssue>();

            if (task != null)
            {
                var stored = TaskItemSnapshot.Clone(task);
                if (files.TryGetValue(stored.Id, out var otherFile) &&
                    !string.Equals(otherFile, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    duplicates.Add(new TaskGraphDuplicateIdIssue(stored.Id, [otherFile, filePath]));
                }

                tasks.Add(stored);
                files[stored.Id] = filePath;
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                errors.Add(new TaskGraphLoadError(filePath, error));
            }

            var revision = Interlocked.Increment(ref _liveGraphRevision);
            _liveGraph = new TaskGraphReadResult(tasks, files, errors, duplicates) { Revision = revision };
        }
    }

    private void RemoveCachedTaskMappedToFile(string filePath, string? exceptTaskId)
    {
        foreach (var pair in _taskFilePaths)
        {
            if (string.Equals(pair.Value, filePath, StringComparison.OrdinalIgnoreCase) &&
                (exceptTaskId == null || !string.Equals(pair.Key, exceptTaskId, StringComparison.Ordinal)))
            {
                _taskFilePaths.TryRemove(pair.Key, out _);
                _tasks.TryRemove(pair.Key, out _);
            }
        }
    }

    private static TaskGraphReadResult CloneGraph(TaskGraphReadResult graph) => new(
        graph.Tasks.Select(TaskItemSnapshot.Clone).ToArray(),
        new Dictionary<string, string>(graph.FilesByTaskId, StringComparer.Ordinal),
        graph.LoadErrors.Select(static error => new TaskGraphLoadError(error.File, error.Message)).ToArray(),
        graph.DuplicateIdIssues
            .Select(static issue => new TaskGraphDuplicateIdIssue(issue.TaskId, issue.Files.ToArray()))
            .ToArray())
    {
        Revision = graph.Revision
    };

    private bool IsDirectoryLockHeld()
    {
        var lockPath = System.IO.Path.Combine(Path, ".unlimotion.lock");
        return HeldDirectoryLocks.Value?.Contains(lockPath) == true;
    }

    private string TransactionDirectoryPath => System.IO.Path.Combine(Path, ".unlimotion.transactions");

    private async Task RecoverPendingTransactionsAsync()
    {
        if (!Directory.Exists(TransactionDirectoryPath))
        {
            return;
        }

        var recoveredAny = false;
        foreach (var journalPath in Directory
                     .EnumerateFiles(TransactionDirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            RecoverableMutationJournal journal;
            try
            {
                var json = await File.ReadAllTextAsync(journalPath);
                journal = JsonConvert.DeserializeObject<RecoverableMutationJournal>(json) ??
                          throw new InvalidDataException($"Transaction journal '{journalPath}' is empty.");
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"Cannot recover task transaction journal '{journalPath}'.", ex);
            }

            await ApplyJournalImagesAsync(journal, useAfterImages: journal.Committed);
            DeleteTransactionJournal(journalPath);
            recoveredAny = true;
        }

        if (recoveredAny)
        {
            InvalidateCachesAfterRecovery();
        }
    }

    private async Task ApplyJournalImagesAsync(RecoverableMutationJournal journal, bool useAfterImages)
    {
        foreach (var entry in journal.Entries.OrderBy(static entry => entry.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var filePath = ValidateJournalFilePath(entry.FilePath);
            var image = useAfterImages ? entry.AfterBase64 : entry.BeforeBase64;
            var exists = useAfterImages ? entry.AfterExists : entry.BeforeExists;
            if (!exists)
            {
                OnBeforeTransactionTargetDelete(filePath);
                File.Delete(filePath);
                if (File.Exists(filePath))
                {
                    throw new IOException($"Transaction recovery could not delete task file '{filePath}'.");
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(image))
            {
                throw new InvalidDataException($"Transaction image for '{filePath}' is missing.");
            }

            var bytes = Convert.FromBase64String(image);
            await AtomicWriteAllBytesAsync(filePath, bytes);
        }
    }

    private string ValidateJournalFilePath(string candidate)
    {
        var fullPath = System.IO.Path.GetFullPath(candidate);
        var directoryPrefix = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(System.IO.Path.GetDirectoryName(fullPath), Path, StringComparison.OrdinalIgnoreCase) ||
            !IsTaskFile(System.IO.Path.GetFileName(fullPath)))
        {
            throw new InvalidDataException($"Transaction journal contains an invalid task path '{candidate}'.");
        }

        return fullPath;
    }

    private void InvalidateCachesAfterRecovery()
    {
        lock (_liveGraphSync)
        {
            Interlocked.Increment(ref _liveGraphInvalidationGeneration);
            _liveGraphNeedsReload = true;
            _liveGraph = null;
            _tasks.Clear();
            _taskFilePaths.Clear();
            Interlocked.Increment(ref _liveGraphRevision);
        }
    }

    private sealed class FileTaskGraphWriteScope : IRecoverableTaskGraphWriteScope
    {
        private readonly FileTaskStorage _owner;
        private readonly FileTaskGraphWriteScope? _previous;
        private readonly HashSet<string> _attemptedTaskIds = new(StringComparer.Ordinal);
        private readonly RecoverableMutationJournal _journal = new();
        private string? _journalPath;
        private bool _disposed;
        private bool _completed;

        public FileTaskGraphWriteScope(FileTaskStorage owner, FileTaskGraphWriteScope? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public IReadOnlyList<string> AttemptedTaskIds
        {
            get
            {
                lock (_attemptedTaskIds)
                {
                    return _attemptedTaskIds.ToArray();
                }
            }
        }

        public void Record(string taskId)
        {
            lock (_attemptedTaskIds)
            {
                _attemptedTaskIds.Add(taskId);
            }
        }

        public async Task PrepareWriteAsync(string taskId, string filePath, string content)
        {
            Record(taskId);
            var entry = GetOrCreateEntry(taskId, filePath);
            entry.AfterExists = true;
            entry.AfterBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            await PersistJournalAsync();
        }

        public void PrepareRemove(string taskId, string filePath)
        {
            Record(taskId);
            var entry = GetOrCreateEntry(taskId, filePath);
            entry.AfterExists = false;
            entry.AfterBase64 = null;
            PersistJournalAsync().GetAwaiter().GetResult();
        }

        public async Task CommitAsync()
        {
            if (_completed || _journalPath == null)
            {
                _completed = true;
                return;
            }

            _journal.Committed = true;
            await PersistJournalAsync();
            _owner.OnAfterTransactionCommitted(_journalPath);
            _owner.DeleteTransactionJournal(_journalPath);
            _completed = true;
        }

        public async Task RollbackAsync()
        {
            if (_completed || _journalPath == null)
            {
                _completed = true;
                return;
            }

            await _owner.ApplyJournalImagesAsync(_journal, useAfterImages: _journal.Committed);
            _owner.DeleteTransactionJournal(_journalPath);
            _owner.InvalidateCachesAfterRecovery();
            _completed = true;
        }

        private RecoverableMutationEntry GetOrCreateEntry(string taskId, string filePath)
        {
            var existing = _journal.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            var beforeExists = File.Exists(filePath);
            var entry = new RecoverableMutationEntry
            {
                TaskId = taskId,
                FilePath = filePath,
                BeforeExists = beforeExists,
                BeforeBase64 = beforeExists ? Convert.ToBase64String(File.ReadAllBytes(filePath)) : null
            };
            _journal.Entries.Add(entry);
            return entry;
        }

        private async Task PersistJournalAsync()
        {
            Directory.CreateDirectory(_owner.TransactionDirectoryPath);
            _journalPath ??= System.IO.Path.Combine(
                _owner.TransactionDirectoryPath,
                $"{_journal.Id}.json");
            var json = JsonConvert.SerializeObject(_journal, Formatting.Indented) + Environment.NewLine;
            _owner.OnBeforeTransactionJournalPersist(_journalPath);
            await _owner.AtomicWriteAllTextAsync(_journalPath, json);
            _owner.OnAfterTransactionJournalPersist(_journalPath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(_owner._activeWriteScope.Value, this))
            {
                _owner._activeWriteScope.Value = _previous;
            }
        }
    }

    private sealed class RecoverableMutationJournal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool Committed { get; set; }
        public List<RecoverableMutationEntry> Entries { get; set; } = new();
    }

    private sealed class RecoverableMutationEntry
    {
        public string TaskId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool BeforeExists { get; set; }
        public string? BeforeBase64 { get; set; }
        public bool AfterExists { get; set; }
        public string? AfterBase64 { get; set; }
    }

    private async Task<FileStream> AcquireDirectoryLockAsync(
        string lockPath,
        CancellationToken linkedCancellationToken,
        CancellationToken callerCancellationToken)
    {
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    await Task.Delay(_options.DirectoryLockRetryDelay, linkedCancellationToken);
                }
                catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Timed out waiting for task directory lock '{lockPath}'.");
                }
            }
        }
    }

    private string ResolveTaskFilePath(string taskId)
    {
        ValidateTaskId(taskId);
        return _taskFilePaths.TryGetValue(taskId, out var sourcePath)
            ? sourcePath
            : System.IO.Path.Combine(Path, taskId);
    }

    private bool TryResolveTaskFilePath(string taskId, out string filePath)
    {
        if (!TryValidateTaskId(taskId, out _))
        {
            filePath = string.Empty;
            return false;
        }

        filePath = ResolveTaskFilePath(taskId);
        return true;
    }

    private static void ValidateTaskId(string taskId)
    {
        if (!TryValidateTaskId(taskId, out var error))
        {
            throw new InvalidDataException(error);
        }
    }

    private static bool TryValidateTaskId(string? taskId, out string error)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            error = "Task Id must not be empty.";
            return false;
        }

        if (System.IO.Path.IsPathRooted(taskId) ||
            !string.Equals(System.IO.Path.GetFileName(taskId), taskId, StringComparison.Ordinal) ||
            taskId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            taskId is "." or "..")
        {
            error = $"Task Id '{taskId}' must be a valid direct child file name.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed class IgnoreExtensionDataContractResolver : DefaultContractResolver
    {
        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            contract.ExtensionDataGetter = null;
            contract.ExtensionDataSetter = null;
            return contract;
        }
    }
}

public sealed record FileTaskStorageDirectoryReadResult(
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyDictionary<string, string> FilesByTaskId,
    IReadOnlyList<FileTaskStorageLoadError> LoadErrors,
    IReadOnlyList<FileTaskStorageDuplicateIdIssue> DuplicateIdIssues)
{
    public IReadOnlyDictionary<string, TaskItem> TasksById { get; } = Tasks
        .Where(static task => !string.IsNullOrWhiteSpace(task.Id))
        .GroupBy(static task => task.Id, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
}

public sealed record FileTaskStorageLoadError(string File, string Message);

public sealed record FileTaskStorageDuplicateIdIssue(string TaskId, IReadOnlyList<string> Files);
