using System;
using System.IO;
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
        _dbWatcher.OnUpdated += (_, args) => OnUpdating(new TaskStorageUpdateEventArgs
        {
            Id = args.Id,
            Type = args.Type
        });
    }

    public IDatabaseWatcher? Watcher => _dbWatcher;

    protected virtual void OnUpdating(TaskStorageUpdateEventArgs e)
    {
        _ = Load(e.Id, forced: true);
        RaiseUpdating(e);
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
