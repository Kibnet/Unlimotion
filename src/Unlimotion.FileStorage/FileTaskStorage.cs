using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Storage;

public class FileTaskStorage : IStorage, ITaskGraphDiagnosticStorage, ITaskGraphWriteLock
{
    private static readonly AsyncLocal<HashSet<string>?> HeldDirectoryLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectorySemaphores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _taskFilePaths = new(StringComparer.Ordinal);
    private readonly FileTaskStorageOptions _options;

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
        if (!forced && _tasks.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        if (!TryResolveTaskFilePath(itemId, out var filePath))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        TaskItem? task;
        try
        {
            task = await Task.Run(() => DeserializeTask(filePath));
        }
        catch
        {
            return null;
        }

        if (task == null || !TryValidateTaskId(task.Id, out _))
        {
            return null;
        }

        _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
        _taskFilePaths.AddOrUpdate(task.Id, filePath, (_, _) => filePath);
        return task;
    }

    public async IAsyncEnumerable<TaskItem> GetAll()
    {
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

            _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
            _taskFilePaths.AddOrUpdate(task.Id, file, (_, _) => file);
            yield return task;
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
                _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
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

    public async Task<TaskGraphReadResult> ReadGraphAsync()
    {
        var result = await ReadDirectoryAsync();
        return new TaskGraphReadResult(
            result.Tasks,
            result.FilesByTaskId,
            result.LoadErrors
                .Select(static error => new TaskGraphLoadError(error.File, error.Message))
                .ToArray(),
            result.DuplicateIdIssues
                .Select(static issue => new TaskGraphDuplicateIdIssue(issue.TaskId, issue.Files))
                .ToArray());
    }

    public Task<T> WithWriteLockAsync<T>(Func<Task<T>> operation) =>
        WithDirectoryLockAsync(operation);

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
        var item = taskItem with { };
        var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id;
        ValidateTaskId(id);
        item.Id = id;
        item.EnsureStatusHistory(item.UserId ?? "local-user");

        var filePath = ResolveTaskFilePath(item.Id);
        OnBeforeWrite(item.Id, filePath);
        var json = JsonConvert.SerializeObject(item, Formatting.Indented, CreateSerializerSettings());
        await AtomicWriteAllTextAsync(filePath, json + Environment.NewLine);

        taskItem.Id = item.Id;
        _tasks.AddOrUpdate(taskItem.Id, item, (_, _) => item);
        _taskFilePaths.AddOrUpdate(taskItem.Id, filePath, (_, _) => filePath);
        return item;
    }

    private Task<bool> RemoveCore(string itemId)
    {
        ValidateTaskId(itemId);
        var filePath = ResolveTaskFilePath(itemId);
        OnBeforeRemove(itemId, filePath);
        _tasks.TryRemove(itemId, out _);
        _taskFilePaths.TryRemove(itemId, out _);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.FromResult(true);
    }

    private TaskItem? DeserializeTask(string fullPath)
    {
        var serializer = JsonSerializer.Create(CreateSerializerSettings());

        return JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, serializer, saveRepairedSidecar: false);
    }

    private IEnumerable<string> EnumerateTaskFiles()
    {
        var directoryInfo = new DirectoryInfo(Path);
        return directoryInfo
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(static file => file.Length > 0 && IsTaskFile(file.Name))
            .OrderBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(static file => file.FullName)
            .ToArray();
    }

    private static bool IsTaskFile(string fileName)
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
