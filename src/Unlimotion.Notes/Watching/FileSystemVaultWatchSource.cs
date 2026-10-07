namespace Unlimotion.Notes.Watching;

public sealed class FileSystemVaultWatchSource : IVaultWatchSource
{
    private readonly FileSystemWatcher watcher;
    private int started;
    private int disposed;

    public FileSystemVaultWatchSource(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(fullPath);
        watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            Filter = "*",
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            InternalBufferSize = 32 * 1024
        };
        watcher.Created += OnCreated;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
    }

    public event Action<VaultRawChange>? Change;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
        {
            throw new InvalidOperationException("The vault watcher has already been started.");
        }

        watcher.EnableRaisingEvents = true;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnCreated;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnDeleted;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
        Change = null;
        return ValueTask.CompletedTask;
    }

    private void OnCreated(object sender, FileSystemEventArgs args) =>
        Publish(VaultRawChangeKind.Created, args.FullPath);

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        Publish(VaultRawChangeKind.Changed, args.FullPath);

    private void OnDeleted(object sender, FileSystemEventArgs args) =>
        Publish(VaultRawChangeKind.Deleted, args.FullPath, isDirectory: Path.GetExtension(args.FullPath).Length == 0);

    private void OnRenamed(object sender, RenamedEventArgs args) =>
        Publish(VaultRawChangeKind.Renamed, args.FullPath, args.OldFullPath);

    private void OnError(object sender, ErrorEventArgs args) =>
        Change?.Invoke(new VaultRawChange(VaultRawChangeKind.RescanRequired, watcher.Path));

    private void Publish(
        VaultRawChangeKind kind,
        string fullPath,
        string? oldFullPath = null,
        bool? isDirectory = null)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var directory = isDirectory ?? Directory.Exists(fullPath);
        Change?.Invoke(new VaultRawChange(kind, fullPath, oldFullPath, directory));
    }
}
