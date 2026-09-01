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
    private static readonly AsyncLocal<HashSet<string>?> HeldDirectoryLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectorySemaphores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _taskFilePaths = new(StringComparer.Ordinal);
    private readonly FileTaskStorageOptions _options;
    private readonly object _liveGraphSync = new();
    private TaskGraphReadResult? _liveGraph;
    private long _liveGraphRevision;
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
        if (TryGetLiveGraph(out var liveGraph))
        {
            return CloneGraph(liveGraph);
        }

        var result = await ReadDirectoryAsync();
        return ToGraphResult(result);
    }

    public async Task EnableLiveGraphAsync()
    {
        await WithDirectoryLockAsync(async () =>
        {
            _tasks.Clear();
            _taskFilePaths.Clear();
            var result = await ReadDirectoryAsync();
            PublishLiveGraph(ToGraphResult(result));
            _liveGraphNeedsReload = false;
        });
    }

    public long LiveGraphRevision => Interlocked.Read(ref _liveGraphRevision);

    public void InvalidateLiveGraph() => _liveGraphNeedsReload = true;

    protected async Task EnsureLiveGraphReadyWithinWriteLockAsync()
    {
        if (!_liveGraphNeedsReload)
        {
            return;
        }

        _tasks.Clear();
        _taskFilePaths.Clear();
        var result = await ReadDirectoryAsync();
        PublishLiveGraph(ToGraphResult(result));
        _liveGraphNeedsReload = false;
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
        OnBeforeWrite(item.Id, filePath);
        var json = JsonConvert.SerializeObject(item, Formatting.Indented, CreateSerializerSettings());
        await AtomicWriteAllTextAsync(filePath, json + Environment.NewLine);
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
        var serializer = JsonSerializer.Create(CreateSerializerSettings());

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

    protected virtual void OnBeforeWrite(string taskId, string filePath)
    {
    }

    protected virtual void OnBeforeRemove(string taskId, string filePath)
    {
    }

    protected virtual void OnAfterWritePersisted(string taskId, string filePath)
    {
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
            liveGraphEnabled = _liveGraph != null;
            if (_liveGraph?.TasksById.TryGetValue(taskId, out var cached) == true)
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
            if (_liveGraph == null)
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
                _liveGraphNeedsReload = true;
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

    private sealed class FileTaskGraphWriteScope : ITaskGraphWriteScope
    {
        private readonly FileTaskStorage _owner;
        private readonly FileTaskGraphWriteScope? _previous;
        private readonly HashSet<string> _attemptedTaskIds = new(StringComparer.Ordinal);
        private bool _disposed;

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
