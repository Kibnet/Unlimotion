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

public sealed class TaskSourceActivation
{
    internal TaskSourceActivation(
        TaskSourceRuntime candidate,
        TaskSourceRuntime? previous,
        TaskSourcesSettings previousSettings,
        TaskSourcesSettings preparedSettings,
        string? catalogMutationOperation = null)
    {
        Candidate = candidate;
        Previous = previous;
        PreviousSettings = previousSettings;
        PreparedSettings = preparedSettings;
        CatalogMutationOperation = catalogMutationOperation;
    }

    public TaskSourceRuntime Candidate { get; }
    public TaskSourceRuntime? Previous { get; }
    public bool IsPublished { get; internal set; }
    internal bool HasPersistentChanges { get; set; }
    internal TaskSourcesSettings PreviousSettings { get; }
    internal TaskSourcesSettings PreparedSettings { get; }
    internal string? CatalogMutationOperation { get; }
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

    Task<TaskSourceRuntime> ActivateSourceByIdAsync(string sourceId) =>
        Task.FromException<TaskSourceRuntime>(new NotSupportedException("Task space selection is not available."));

    TaskSourceDescriptor AddConfiguredLocalSource(string displayName, string? path = null) =>
        throw new NotSupportedException("Task space management is not available.");

    void RenameConfiguredSource(string sourceId, string displayName) =>
        throw new NotSupportedException("Task space management is not available.");

    void RemoveConfiguredSource(string sourceId) =>
        throw new NotSupportedException("Task space management is not available.");

    void PersistActiveSourceSettings() =>
        throw new NotSupportedException("Task space settings are not available.");

    TaskSpaceSettingsDraft GetSourceSettings(string sourceId) =>
        throw new NotSupportedException("Task space settings are not available.");

    void PersistSourceSettings(TaskSpaceSettingsDraft draft) =>
        throw new NotSupportedException("Task space settings are not available.");

    Task<TaskSourceActivation> PrepareActivationCoreAsync(
        TaskSpaceOperationContext context,
        string sourceId) =>
        Task.FromException<TaskSourceActivation>(
            new NotSupportedException("Transactional task-space activation is not available."));

    Task<TaskSourceActivation> PrepareAddLocalActivationCoreAsync(
        TaskSpaceOperationContext context,
        string displayName,
        string? path = null) =>
        Task.FromException<TaskSourceActivation>(
            new NotSupportedException("Transactional task-space creation is not available."));

    Task PublishActivationCoreAsync(
        TaskSpaceOperationContext context,
        TaskSourceActivation activation) =>
        Task.FromException(new NotSupportedException("Transactional task-space activation is not available."));

    Task AbortActivationCoreAsync(
        TaskSpaceOperationContext context,
        TaskSourceActivation activation) =>
        Task.FromException(new NotSupportedException("Transactional task-space activation is not available."));

    void SwitchStorage(bool isServerMode, Microsoft.Extensions.Configuration.IConfiguration configuration);

    Task SwitchStorageAsync(bool isServerMode, Microsoft.Extensions.Configuration.IConfiguration configuration);
}
