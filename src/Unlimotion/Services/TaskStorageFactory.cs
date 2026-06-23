using System;
using AutoMapper;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class TaskStorageFactory : ITaskStorageFactory
{
    private readonly IConfiguration _configuration;
    private readonly Func<string?> _defaultStoragePathProvider;
    private readonly ITaskStorageBuilder _storageBuilder;

    public TaskStorageFactory(
        IConfiguration configuration,
        IMapper mapper,
        INotificationManagerWrapper? notificationManager = null,
        Func<string?>? defaultStoragePathProvider = null)
    {
        _configuration = configuration;
        _defaultStoragePathProvider = defaultStoragePathProvider ?? (() => string.Empty);
        _storageBuilder = new TaskStorageBuilder(
            configuration,
            mapper,
            notificationManager,
            _defaultStoragePathProvider);
        SourceManager = new TaskSourceManager(
            configuration,
            _storageBuilder,
            notificationManager,
            _defaultStoragePathProvider);
    }

    public ITaskSourceManager SourceManager { get; }

    public ITaskStorage CreateConfiguredStorage() =>
        SourceManager.ActivateConfiguredSource().Storage;

    public ITaskStorage CreateFileStorage(string? path)
    {
        var descriptor = TaskSourceSettingsAdapter.CreateLegacyDescriptor(
            new TaskStorageSettings
            {
                Path = string.IsNullOrWhiteSpace(path) ? _defaultStoragePathProvider() ?? string.Empty : path,
                IsServerMode = false
            },
            _defaultStoragePathProvider());
        return SourceManager.ActivateSource(descriptor).Storage;
    }

    public ITaskStorage CreateDetachedFileStorage(string? path)
    {
        var storagePath = string.IsNullOrWhiteSpace(path) ? _defaultStoragePathProvider() ?? string.Empty : path;
        var buildResult = _storageBuilder.Build(new TaskStorageBuildRequest
        {
            Descriptor = new TaskSourceDescriptor
            {
                Id = $"detached-file-{Guid.NewGuid():N}",
                DisplayName = "Detached file storage",
                Kind = TaskSourceKind.File,
                Path = storagePath,
                IsEnabled = true
            },
            TaskContext = new TaskItemViewModelContext
            {
                SourceId = "detached-file"
            },
            EnableWatcher = false
        });

        return buildResult.Storage;
    }

    public ITaskStorage CreateServerStorage(string? url)
    {
        var legacySettings = _configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        legacySettings.IsServerMode = true;
        legacySettings.URL = url ?? string.Empty;

        var descriptor = TaskSourceSettingsAdapter.CreateLegacyDescriptor(legacySettings, _defaultStoragePathProvider());
        var serverSettings = TaskSourceSettingsAdapter.EnsureServerSettings(
            TaskSourceSettingsAdapter.LoadOrCreate(_configuration, _defaultStoragePathProvider()),
            descriptor.Id,
            legacySettings,
            _configuration);
        return SourceManager.ActivateSource(descriptor, serverSettings).Storage;
    }

    public void SwitchStorage(bool isServerMode, IConfiguration configuration)
    {
        SourceManager.SwitchStorage(isServerMode, configuration);
    }
}
