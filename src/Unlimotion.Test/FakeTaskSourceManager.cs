using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Services;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

internal sealed class FakeTaskSourceManager(
    Func<ITaskStorage?> activeStorageProvider,
    Func<IDatabaseWatcher?>? activeWatcherProvider = null,
    Func<bool, IConfiguration, Task>? switchStorageAsync = null)
    : ITaskSourceManager
{
    public IReadOnlyList<TaskSourceRuntime> Sources => [];
    public IReadOnlyList<TaskSourceDescriptor> ConfiguredSources => [];
    public TaskSourceRuntime? ActiveSource => null;
    public ITaskStorage? ActiveStorage => activeStorageProvider();
    public IDatabaseWatcher? ActiveWatcher => activeWatcherProvider?.Invoke();

    public TaskSourceRuntime ActivateConfiguredSource() =>
        throw new NotSupportedException();

    public TaskSourceRuntime ActivateSource(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null) =>
        throw new NotSupportedException();

    public Task<TaskSourceRuntime> ActivateConfiguredSourceAsync() =>
        throw new NotSupportedException();

    public Task<TaskSourceRuntime> ActivateSourceAsync(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null) =>
        throw new NotSupportedException();

    public void SwitchStorage(bool isServerMode, IConfiguration configuration)
    {
        SwitchStorageAsync(isServerMode, configuration).GetAwaiter().GetResult();
    }

    public Task SwitchStorageAsync(bool isServerMode, IConfiguration configuration) =>
        switchStorageAsync?.Invoke(isServerMode, configuration) ?? throw new NotSupportedException();
}
