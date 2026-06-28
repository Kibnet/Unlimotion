using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Storage;

public class FileTaskStorage : IStorage
{
    private static readonly AsyncLocal<HashSet<string>?> HeldDirectoryLocks = new();
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
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

        var filePath = System.IO.Path.Combine(Path, itemId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var task = await Task.Run(() => DeserializeTask(filePath));
        if (task == null || string.IsNullOrWhiteSpace(task.Id))
        {
            return null;
        }

        _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
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

            if (task == null || string.IsNullOrWhiteSpace(task.Id))
            {
                continue;
            }

            _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
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

                tasks.Add(task);
                if (!taskFiles.TryGetValue(task.Id, out var files))
                {
                    files = new List<string>();
                    taskFiles.Add(task.Id, files);
                }

                files.Add(file);
                _tasks.AddOrUpdate(task.Id, task, (_, _) => task);
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

    public async Task<T> WithDirectoryLockAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var directoryLock = AcquireDirectoryLock();
        return await operation();
    }

    public async Task WithDirectoryLockAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var directoryLock = AcquireDirectoryLock();
        await operation();
    }

    protected void RaiseUpdating(TaskStorageUpdateEventArgs e) => Updating?.Invoke(this, e);

    private async Task<TaskItem> SaveCore(TaskItem taskItem)
    {
        taskItem.EnsureStatusHistory(taskItem.UserId ?? "local-user");

        var item = taskItem with { };
        var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id;
        item.Id = id;

        var filePath = System.IO.Path.Combine(Path, item.Id);
        var json = JsonConvert.SerializeObject(item, Formatting.Indented, CreateConverters());
        await AtomicWriteAllTextAsync(filePath, json + Environment.NewLine);

        taskItem.Id = item.Id;
        _tasks.AddOrUpdate(taskItem.Id, item, (_, _) => item);
        return item;
    }

    private Task<bool> RemoveCore(string itemId)
    {
        var filePath = System.IO.Path.Combine(Path, itemId);
        _tasks.TryRemove(itemId, out _);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.FromResult(true);
    }

    private TaskItem? DeserializeTask(string fullPath)
    {
        var serializer = new JsonSerializer();
        foreach (var converter in CreateConverters())
        {
            serializer.Converters.Add(converter);
        }

        return JsonRepairingReader.DeserializeWithRepair<TaskItem>(fullPath, serializer, saveRepairedSidecar: false);
    }

    private IEnumerable<string> EnumerateTaskFiles()
    {
        var directoryInfo = new DirectoryInfo(Path);
        return directoryInfo
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(static file => file.Length > 0 && !ShouldSkip(file.Name))
            .OrderBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(static file => file.FullName)
            .ToArray();
    }

    private static bool ShouldSkip(string fileName) =>
        fileName.StartsWith(".", StringComparison.Ordinal) ||
        fileName.EndsWith(".report", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".migration.report", StringComparison.OrdinalIgnoreCase);

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
                stream.Flush(flushToDisk: true);
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

    private IDisposable AcquireDirectoryLock()
    {
        if (!_options.UseDirectoryLock)
        {
            return NoopDisposable.Instance;
        }

        var lockPath = System.IO.Path.Combine(Path, ".unlimotion.lock");
        var heldLocks = HeldDirectoryLocks.Value ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (heldLocks.Contains(lockPath))
        {
            return NoopDisposable.Instance;
        }

        var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        heldLocks.Add(lockPath);
        return new DirectoryLock(stream, lockPath, heldLocks);
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

    private sealed class DirectoryLock : IDisposable
    {
        private readonly FileStream _stream;
        private readonly string _lockPath;
        private readonly ISet<string> _heldLocks;

        public DirectoryLock(FileStream stream, string lockPath, ISet<string> heldLocks)
        {
            _stream = stream;
            _lockPath = lockPath;
            _heldLocks = heldLocks;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _heldLocks.Remove(_lockPath);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
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
