using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class TaskSourceManager : ITaskSourceManager
{
    private readonly IConfiguration _configuration;
    private readonly ITaskStorageBuilder _storageBuilder;
    private readonly INotificationManagerWrapper? _notificationManager;
    private readonly Func<string?>? _defaultStoragePathProvider;
    private readonly List<TaskSourceRuntime> _sources = new();
    private TaskSourcesSettings _settings;

    public TaskSourceManager(
        IConfiguration configuration,
        ITaskStorageBuilder storageBuilder,
        INotificationManagerWrapper? notificationManager = null,
        Func<string?>? defaultStoragePathProvider = null)
    {
        _configuration = configuration;
        _storageBuilder = storageBuilder;
        _notificationManager = notificationManager;
        _defaultStoragePathProvider = defaultStoragePathProvider;
        _settings = TaskSourceSettingsAdapter.LoadOrCreate(_configuration, _defaultStoragePathProvider?.Invoke());
    }

    public IReadOnlyList<TaskSourceRuntime> Sources => _sources;
    public IReadOnlyList<TaskSourceDescriptor> ConfiguredSources => _settings.Sources;
    public TaskSourceRuntime? ActiveSource { get; private set; }
    public ITaskStorage? ActiveStorage => ActiveSource?.Storage;
    public IDatabaseWatcher? ActiveWatcher => ActiveSource?.Watcher;

    public TaskSourceRuntime ActivateConfiguredSource() =>
        ActivateConfiguredSourceAsync().GetAwaiter().GetResult();

    public async Task<TaskSourceRuntime> ActivateConfiguredSourceAsync()
    {
        var descriptor = _settings.Sources.First(source =>
            string.Equals(source.Id, _settings.ActiveSourceId, StringComparison.Ordinal));
        var serverSettings = descriptor.Kind == TaskSourceKind.Server
            ? TaskSourceSettingsAdapter.EnsureServerSettings(
                _settings,
                descriptor.Id,
                _configuration.Get<TaskStorageSettings>("TaskStorage"),
                _configuration)
            : null;

        return await ActivateSourceAsync(descriptor, serverSettings).ConfigureAwait(false);
    }

    public TaskSourceRuntime ActivateSource(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null) =>
        ActivateSourceAsync(descriptor, serverSettings).GetAwaiter().GetResult();

    public async Task<TaskSourceRuntime> ActivateSourceAsync(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings = null)
    {
        descriptor = NormalizeDescriptor(descriptor);
        var nextSettings = CloneSettings(_settings);
        UpsertDescriptor(nextSettings, descriptor);

        if (descriptor.Kind == TaskSourceKind.Server)
        {
            serverSettings ??= TaskSourceSettingsAdapter.EnsureServerSettings(
                nextSettings,
                descriptor.Id,
                _configuration.Get<TaskStorageSettings>("TaskStorage"),
                _configuration);
            UpsertServerSettings(nextSettings, serverSettings);
        }

        var taskContext = new TaskItemViewModelContext
        {
            SourceId = descriptor.Id,
            NotificationManager = _notificationManager
        };
        var buildResult = _storageBuilder.Build(new TaskStorageBuildRequest
        {
            Descriptor = descriptor,
            ServerSettings = serverSettings,
            PersistServerSettings = serverSettings == null ? null : PersistServerSettings,
            TaskContext = taskContext
        });
        var runtime = new TaskSourceRuntime(
            descriptor,
            serverSettings,
            buildResult.Storage,
            buildResult.Watcher,
            taskContext);

        var existingIndex = _sources.FindIndex(source =>
            string.Equals(source.Descriptor.Id, descriptor.Id, StringComparison.Ordinal));
        var replacedSource = existingIndex >= 0 ? _sources[existingIndex] : null;
        var previousActiveSource = ActiveSource;

        if (existingIndex >= 0)
        {
            _sources[existingIndex] = runtime;
        }
        else
        {
            _sources.Add(runtime);
        }

        ActiveSource = runtime;
        nextSettings.ActiveSourceId = descriptor.Id;
        _settings = nextSettings;
        TaskSourceSettingsAdapter.Save(_configuration, _settings);
        TaskSourceSettingsAdapter.SyncLegacy(_configuration, _settings, descriptor);

        if (replacedSource != null)
        {
            await DisconnectRuntimeAsync(replacedSource).ConfigureAwait(false);
        }

        if (previousActiveSource != null && !ReferenceEquals(previousActiveSource, replacedSource))
        {
            await DisconnectRuntimeAsync(previousActiveSource).ConfigureAwait(false);
        }

        return runtime;
    }

    public void SwitchStorage(bool isServerMode, IConfiguration configuration) =>
        SwitchStorageAsync(isServerMode, configuration).GetAwaiter().GetResult();

    public async Task SwitchStorageAsync(bool isServerMode, IConfiguration configuration)
    {
        var legacySettings = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        legacySettings.IsServerMode = isServerMode;
        var descriptor = CreateDescriptorForSettingsSwitch(legacySettings);
        var serverSettings = descriptor.Kind == TaskSourceKind.Server
            ? PrepareServerSettingsForSwitch(descriptor, legacySettings, configuration)
            : null;

        await ActivateSourceAsync(descriptor, serverSettings).ConfigureAwait(false);
    }

    private TaskSourceDescriptor CreateDescriptorForSettingsSwitch(TaskStorageSettings legacySettings)
    {
        if (ActiveSource == null ||
            string.Equals(ActiveSource.Descriptor.Id, TaskSourceDescriptor.DefaultSourceId, StringComparison.Ordinal))
        {
            return TaskSourceSettingsAdapter.CreateLegacyDescriptor(
                legacySettings,
                _defaultStoragePathProvider?.Invoke());
        }

        var activeDescriptor = ActiveSource.Descriptor;
        return new TaskSourceDescriptor
        {
            Id = activeDescriptor.Id,
            DisplayName = activeDescriptor.DisplayName,
            Kind = legacySettings.IsServerMode ? TaskSourceKind.Server : TaskSourceKind.File,
            Path = string.IsNullOrWhiteSpace(legacySettings.Path)
                ? activeDescriptor.Path
                : legacySettings.Path,
            Url = string.IsNullOrWhiteSpace(legacySettings.URL)
                ? activeDescriptor.Url
                : legacySettings.URL,
            IsEnabled = true
        };
    }

    private TaskSourceServerSettings PrepareServerSettingsForSwitch(
        TaskSourceDescriptor descriptor,
        TaskStorageSettings legacySettings,
        IConfiguration configuration)
    {
        var serverSettings = TaskSourceSettingsAdapter.EnsureServerSettings(
            _settings,
            descriptor.Id,
            legacySettings,
            configuration);
        if (!string.Equals(serverSettings.Login, legacySettings.Login, StringComparison.Ordinal) ||
            !string.Equals(serverSettings.Password, legacySettings.Password, StringComparison.Ordinal))
        {
            serverSettings.AccessToken = string.Empty;
            serverSettings.RefreshToken = string.Empty;
            serverSettings.ExpireTime = default;
            serverSettings.UserId = string.Empty;
        }

        serverSettings.Login = legacySettings.Login;
        serverSettings.Password = legacySettings.Password;
        return serverSettings;
    }

    private TaskSourceDescriptor NormalizeDescriptor(TaskSourceDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
        {
            descriptor.Id = TaskSourceDescriptor.DefaultSourceId;
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            descriptor.DisplayName = descriptor.Kind == TaskSourceKind.Server
                ? "Server tasks"
                : "Local tasks";
        }

        if (descriptor.Kind == TaskSourceKind.File &&
            string.IsNullOrWhiteSpace(descriptor.Path))
        {
            descriptor.Path = _defaultStoragePathProvider?.Invoke() ?? string.Empty;
        }

        descriptor.IsEnabled = true;
        return descriptor;
    }

    private void UpsertDescriptor(TaskSourceDescriptor descriptor) =>
        UpsertDescriptor(_settings, descriptor);

    private static void UpsertDescriptor(TaskSourcesSettings settings, TaskSourceDescriptor descriptor)
    {
        var existingIndex = settings.Sources.FindIndex(source =>
            string.Equals(source.Id, descriptor.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            settings.Sources[existingIndex] = descriptor;
            return;
        }

        settings.Sources.Add(descriptor);
    }

    private void PersistServerSettings(TaskSourceServerSettings serverSettings)
    {
        TaskSourceSettingsAdapter.PersistServerSettings(_configuration, _settings, serverSettings);
    }

    private void UpsertServerSettings(TaskSourceServerSettings serverSettings) =>
        UpsertServerSettings(_settings, serverSettings);

    private static void UpsertServerSettings(TaskSourcesSettings settings, TaskSourceServerSettings serverSettings)
    {
        var existingIndex = settings.ServerSettings.FindIndex(candidate =>
            string.Equals(candidate.SourceId, serverSettings.SourceId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            settings.ServerSettings[existingIndex] = serverSettings;
            return;
        }

        settings.ServerSettings.Add(serverSettings);
    }

    private static TaskSourcesSettings CloneSettings(TaskSourcesSettings settings) =>
        new()
        {
            ActiveSourceId = settings.ActiveSourceId,
            Sources = settings.Sources.Select(source => new TaskSourceDescriptor
            {
                Id = source.Id,
                DisplayName = source.DisplayName,
                Kind = source.Kind,
                Path = source.Path,
                Url = source.Url,
                IsEnabled = source.IsEnabled
            }).ToList(),
            ServerSettings = settings.ServerSettings.Select(server => new TaskSourceServerSettings
            {
                SourceId = server.SourceId,
                Login = server.Login,
                Password = server.Password,
                AccessToken = server.AccessToken,
                RefreshToken = server.RefreshToken,
                ExpireTime = server.ExpireTime,
                UserId = server.UserId
            }).ToList()
        };

    private static async Task DisconnectRuntimeAsync(TaskSourceRuntime runtime)
    {
        try
        {
            await runtime.Storage.TaskTreeManager.Storage.Disconnect().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            (runtime.Storage as IDisposable)?.Dispose();
        }
    }
}
