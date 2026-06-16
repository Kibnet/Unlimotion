using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class TaskStorageBuildRequest
{
    public TaskSourceDescriptor Descriptor { get; init; } = new();
    public TaskSourceServerSettings? ServerSettings { get; init; }
    public Action<TaskSourceServerSettings>? PersistServerSettings { get; init; }
    public TaskItemViewModelContext? TaskContext { get; init; }
    public bool EnableWatcher { get; init; } = true;
}

public sealed class TaskStorageBuildResult(ITaskStorage storage, IDatabaseWatcher? watcher)
{
    public ITaskStorage Storage { get; } = storage;
    public IDatabaseWatcher? Watcher { get; } = watcher;
}

public sealed class TaskSourceRuntime(
    TaskSourceDescriptor descriptor,
    TaskSourceServerSettings? serverSettings,
    ITaskStorage storage,
    IDatabaseWatcher? watcher,
    TaskItemViewModelContext taskContext)
{
    public TaskSourceDescriptor Descriptor { get; } = descriptor;
    public TaskSourceServerSettings? ServerSettings { get; } = serverSettings;
    public ITaskStorage Storage { get; } = storage;
    public IDatabaseWatcher? Watcher { get; } = watcher;
    public TaskItemViewModelContext TaskContext { get; } = taskContext;
}

public interface ITaskStorageBuilder
{
    TaskStorageBuildResult Build(TaskStorageBuildRequest request);
}

public interface ITaskSourceManager
{
    IReadOnlyList<TaskSourceRuntime> Sources { get; }
    IReadOnlyList<TaskSourceDescriptor> ConfiguredSources { get; }
    TaskSourceRuntime? ActiveSource { get; }
    ITaskStorage? ActiveStorage { get; }
    IDatabaseWatcher? ActiveWatcher { get; }

    TaskSourceRuntime ActivateConfiguredSource();

    TaskSourceRuntime ActivateSource(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null);

    Task<TaskSourceRuntime> ActivateConfiguredSourceAsync();

    Task<TaskSourceRuntime> ActivateSourceAsync(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null);

    void SwitchStorage(bool isServerMode, Microsoft.Extensions.Configuration.IConfiguration configuration);

    Task SwitchStorageAsync(bool isServerMode, Microsoft.Extensions.Configuration.IConfiguration configuration);
}
