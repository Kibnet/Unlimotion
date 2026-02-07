using AutoMapper;
using Microsoft.Extensions.Configuration;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class TaskStorageFactory : ITaskStorageFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMapper _mapper;
    private readonly INotificationManagerWrapper? _notificationManager;
    
    public static string DefaultStoragePath { get; set; } = string.Empty;
    
    public ITaskStorage? CurrentStorage { get; private set; }
    public IDatabaseWatcher? CurrentWatcher { get; private set; }

    public TaskStorageFactory(IConfiguration configuration, IMapper mapper, INotificationManagerWrapper? notificationManager = null)
    {
        _configuration = configuration;
        _mapper = mapper;
        _notificationManager = notificationManager;
    }

    public ITaskStorage CreateFileStorage(string? path)
    {
        var storagePath = GetStoragePath(path);
        var fileStorage = new FileStorage(storagePath, watcher: true, _notificationManager);
        CurrentWatcher = fileStorage.Watcher;
        var taskTreeManager = new TaskTreeManager(fileStorage);
        var taskStorage = new UnifiedTaskStorage(taskTreeManager);
        CurrentStorage = taskStorage;
        return taskStorage;
    }

    public ITaskStorage CreateServerStorage(string? url)
    {
        CurrentWatcher = null; // Server storage doesn't have a file watcher
        var serverStorage = new ServerStorage(url ?? string.Empty, _configuration);
        var taskTreeManager = new TaskTreeManager(serverStorage);
        var taskStorage = new UnifiedTaskStorage(taskTreeManager);
        CurrentStorage = taskStorage;
        return taskStorage;
    }

    public void SwitchStorage(bool isServerMode, IConfiguration configuration)
    {
        var settings = configuration.Get<TaskStorageSettings>("TaskStorage");
        
        // Disconnect previous storage if exists
        if (CurrentStorage != null)
        {
            CurrentStorage.TaskTreeManager.Storage.Disconnect();
        }

        if (isServerMode)
        {
            CreateServerStorage(settings?.URL);
        }
        else
        {
            CreateFileStorage(settings?.Path);
        }
    }

    private static string GetStoragePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return DefaultStoragePath;
        return path;
    }
}
