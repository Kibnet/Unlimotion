using System;
using AutoMapper;
using Microsoft.Extensions.Configuration;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class TaskStorageBuilder(
    IConfiguration configuration,
    IMapper mapper,
    INotificationManagerWrapper? notificationManager = null,
    Func<string?>? defaultStoragePathProvider = null)
    : ITaskStorageBuilder
{
    public TaskStorageBuildResult Build(TaskStorageBuildRequest request)
    {
        var descriptor = request.Descriptor;
        return descriptor.Kind == TaskSourceKind.Server
            ? BuildServerStorage(request)
            : BuildFileStorage(request);
    }

    private TaskStorageBuildResult BuildFileStorage(TaskStorageBuildRequest request)
    {
        var storagePath = ResolveStoragePath(request.Descriptor.Path);
        var fileStorage = new FileStorage(storagePath, watcher: request.EnableWatcher, notificationManager);
        fileStorage.Watcher?.SetEnable(false);
        var taskTreeManager = new TaskTreeManager(fileStorage);
        var taskStorage = new UnifiedTaskStorage(taskTreeManager, request.TaskContext);
        return new TaskStorageBuildResult(taskStorage, fileStorage.Watcher);
    }

    private TaskStorageBuildResult BuildServerStorage(TaskStorageBuildRequest request)
    {
        var serverStorage = new ServerStorage(
            request.Descriptor.Url,
            configuration,
            request.ServerSettings,
            request.PersistServerSettings);
        var taskTreeManager = new TaskTreeManager(serverStorage);
        var taskStorage = new UnifiedTaskStorage(taskTreeManager, request.TaskContext);
        return new TaskStorageBuildResult(taskStorage, watcher: null);
    }

    private string ResolveStoragePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var defaultPath = defaultStoragePathProvider?.Invoke();
        return string.IsNullOrWhiteSpace(defaultPath) ? "Tasks" : defaultPath;
    }
}
