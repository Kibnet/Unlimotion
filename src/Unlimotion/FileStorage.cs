using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion;

public class FileStorage : global::Unlimotion.Storage.FileTaskStorage
{
    private readonly IDatabaseWatcher? _dbWatcher;

    public FileStorage(string path, bool watcher = false, INotificationManagerWrapper? notificationManager = null)
        : base(new global::Unlimotion.Storage.FileTaskStorageOptions { Path = PreparePath(path) })
    {
        if (!watcher)
        {
            return;
        }

        _dbWatcher = new FileDbWatcher(Path, notificationManager);
        SubscribeToWatcher(_dbWatcher);
    }

    protected FileStorage(string path, IDatabaseWatcher watcher)
        : base(new global::Unlimotion.Storage.FileTaskStorageOptions { Path = PreparePath(path) })
    {
        _dbWatcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        SubscribeToWatcher(_dbWatcher);
    }

    public IDatabaseWatcher? Watcher => _dbWatcher;

    protected virtual async Task OnUpdatingAsync(TaskStorageUpdateEventArgs e)
    {
        var taskId = TryGetTaskIdBySourceFileName(e.Id, out var mappedTaskId)
            ? mappedTaskId
            : e.Id;
        var loaded = await Load(taskId, forced: true);
        RaiseUpdating(new TaskStorageUpdateEventArgs
        {
            Id = loaded?.Id ?? taskId,
            Type = e.Type
        });
    }

    protected override void OnBeforeWrite(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    protected override void OnBeforeRemove(string taskId, string filePath) =>
        _dbWatcher?.AddIgnoredTask(System.IO.Path.GetFileName(filePath));

    private void SubscribeToWatcher(IDatabaseWatcher watcher)
    {
        watcher.OnUpdated += async (_, args) =>
        {
            try
            {
                await OnUpdatingAsync(new TaskStorageUpdateEventArgs
                {
                    Id = args.Id,
                    Type = args.Type
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to process file storage update for '{args.Id}': {ex}");
            }
        };
    }

    private static string PreparePath(string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "Tasks"
            : path;

        try
        {
            Directory.CreateDirectory(normalizedPath);
            return normalizedPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(L10n.Format("FileStorageNoAccess", normalizedPath), ex);
        }
    }
}
