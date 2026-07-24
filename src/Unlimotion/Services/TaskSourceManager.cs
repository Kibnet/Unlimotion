using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private readonly ITaskSpaceOperationRunner? _operationRunner;
    private readonly List<TaskSourceRuntime> _sources = new();
    private TaskSourcesSettings _settings;

    public TaskSourceManager(
        IConfiguration configuration,
        ITaskStorageBuilder storageBuilder,
        INotificationManagerWrapper? notificationManager = null,
        Func<string?>? defaultStoragePathProvider = null,
        ITaskSpaceOperationRunner? operationRunner = null)
    {
        _configuration = configuration;
        _storageBuilder = storageBuilder;
        _notificationManager = notificationManager;
        _defaultStoragePathProvider = defaultStoragePathProvider;
        _operationRunner = operationRunner;
        _settings = TaskSourceSettingsAdapter.LoadOrCreate(_configuration, _defaultStoragePathProvider?.Invoke());
        ValidateConfiguredSourceOwnership(_settings);
    }

    public IReadOnlyList<TaskSourceRuntime> Sources => _sources;
    public IReadOnlyList<TaskSourceDescriptor> ConfiguredSources => _settings.Sources;
    public TaskSourceRuntime? ActiveSource { get; private set; }
    public ITaskStorage? ActiveStorage => ActiveSource?.Storage;
    public IDatabaseWatcher? ActiveWatcher => ActiveSource?.Watcher;
    public event EventHandler? ActiveSourceChanged;

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

    public async Task<TaskSourceRuntime> ActivateSourceByIdAsync(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A task space id is required.", nameof(sourceId));
        }

        TaskSourceSettingsAdapter.CaptureLegacyActiveSettings(_configuration, _settings);
        TaskSourceSettingsAdapter.Save(_configuration, _settings);

        var descriptor = _settings.Sources.FirstOrDefault(source =>
            string.Equals(source.Id, sourceId, StringComparison.Ordinal));
        if (descriptor == null)
        {
            throw new InvalidOperationException($"Task space '{sourceId}' is not configured.");
        }

        var serverSettings = descriptor.Kind == TaskSourceKind.Server
            ? TaskSourceSettingsAdapter.EnsureServerSettings(_settings, descriptor.Id)
            : null;
        return await ActivateSourceAsync(CloneDescriptor(descriptor), serverSettings).ConfigureAwait(false);
    }

    public TaskSourceDescriptor AddConfiguredLocalSource(string displayName, string? path = null)
    {
        var descriptor = CreateLocalDescriptor(displayName, path);
        CreateLocalSourceDirectory(descriptor);
        ValidateUniqueDescriptor(descriptor, _settings.Sources);
        var nextSettings = CloneSettings(_settings);
        nextSettings.Sources.Add(descriptor);
        TaskSourceSettingsAdapter.EnsureSyncSettings(
            nextSettings,
            descriptor.Id,
            new GitSettings { BackupEnabled = false });
        TaskSourceSettingsAdapter.ApplyCatalogMutation(
            _configuration,
            _settings,
            nextSettings,
            "Add");
        _settings = nextSettings;
        return CloneDescriptor(descriptor);
    }

    public void RenameConfiguredSource(string sourceId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A task space name is required.", nameof(displayName));
        }

        FindConfiguredSource(sourceId);
        var nextSettings = CloneSettings(_settings);
        var source = nextSettings.Sources.First(candidate =>
            string.Equals(candidate.Id, sourceId, StringComparison.Ordinal));
        source.DisplayName = displayName.Trim();
        TaskSourceSettingsAdapter.ApplyCatalogMutation(
            _configuration,
            _settings,
            nextSettings,
            "Rename");
        _settings = nextSettings;
        var activeSource = ActiveSource;
        if (activeSource != null &&
            string.Equals(activeSource.Descriptor.Id, sourceId, StringComparison.Ordinal))
        {
            activeSource.Descriptor.DisplayName = source.DisplayName;
        }

        ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveConfiguredSource(string sourceId)
    {
        if (string.Equals(sourceId, _settings.ActiveSourceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Activate another task space before removing the current space.");
        }

        if (_settings.Sources.Count <= 1)
        {
            throw new InvalidOperationException("At least one task space must remain configured.");
        }

        var nextSettings = CloneSettings(_settings);
        var source = nextSettings.Sources.First(candidate =>
            string.Equals(candidate.Id, sourceId, StringComparison.Ordinal));
        nextSettings.Sources.Remove(source);
        nextSettings.ServerSettings.RemoveAll(server =>
            string.Equals(server.SourceId, sourceId, StringComparison.Ordinal));
        nextSettings.SyncSettings.RemoveAll(sync =>
            string.Equals(sync.SourceId, sourceId, StringComparison.Ordinal));
        TaskSourceSettingsAdapter.ApplyCatalogMutation(
            _configuration,
            _settings,
            nextSettings,
            "Remove");
        _settings = nextSettings;
        ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PersistActiveSourceSettings()
    {
        TaskSourceSettingsAdapter.CaptureLegacyActiveSettings(_configuration, _settings);
        TaskSourceSettingsAdapter.Save(_configuration, _settings);
    }

    public TaskSpaceSettingsDraft GetSourceSettings(string sourceId)
    {
        var descriptor = FindConfiguredSource(sourceId);
        var server = _settings.ServerSettings.FirstOrDefault(candidate =>
            string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal));
        var sync = TaskSourceSettingsAdapter.EnsureSyncSettings(_settings, sourceId);
        return new TaskSpaceSettingsDraft
        {
            SourceId = sourceId,
            Storage = new TaskStorageSettings
            {
                Path = descriptor.Path,
                URL = descriptor.Url,
                Login = server?.Login ?? string.Empty,
                Password = server?.Password ?? string.Empty,
                IsServerMode = descriptor.Kind == TaskSourceKind.Server,
                IsFuzzySearch = _configuration.Get<TaskStorageSettings>("TaskStorage")?.IsFuzzySearch ?? false
            },
            Git = CloneGitSettings(sync.Git)
        };
    }

    public void PersistSourceSettings(TaskSpaceSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var current = FindConfiguredSource(draft.SourceId);
        var nextSettings = CloneSettings(_settings);
        var descriptor = CloneDescriptor(current);
        descriptor.Kind = draft.Storage.IsServerMode ? TaskSourceKind.Server : TaskSourceKind.File;
        descriptor.Path = draft.Storage.Path ?? string.Empty;
        descriptor.Url = draft.Storage.URL ?? string.Empty;
        ValidateUniqueDescriptor(
            descriptor,
            nextSettings.Sources.Where(source =>
                !string.Equals(source.Id, descriptor.Id, StringComparison.Ordinal)),
            nextSettings.ServerSettings,
            draft.Storage.Login);
        UpsertDescriptor(nextSettings, descriptor);

        if (descriptor.Kind == TaskSourceKind.Server)
        {
            var existing = nextSettings.ServerSettings.FirstOrDefault(candidate =>
                string.Equals(candidate.SourceId, descriptor.Id, StringComparison.Ordinal));
            var server = existing == null
                ? new TaskSourceServerSettings { SourceId = descriptor.Id }
                : CloneServerSettings(existing);
            if (!string.Equals(server.Login, draft.Storage.Login, StringComparison.Ordinal) ||
                !string.Equals(server.Password, draft.Storage.Password, StringComparison.Ordinal))
            {
                server.AccessToken = string.Empty;
                server.RefreshToken = string.Empty;
                server.ExpireTime = default;
                server.UserId = string.Empty;
            }

            server.Login = draft.Storage.Login ?? string.Empty;
            server.Password = draft.Storage.Password ?? string.Empty;
            UpsertServerSettings(nextSettings, server);
        }

        var sync = TaskSourceSettingsAdapter.EnsureSyncSettings(nextSettings, descriptor.Id);
        sync.Git = CloneGitSettings(draft.Git);
        TaskSourceSettingsAdapter.Save(_configuration, nextSettings);
        if (string.Equals(nextSettings.ActiveSourceId, descriptor.Id, StringComparison.Ordinal))
        {
            TaskSourceSettingsAdapter.SyncLegacy(_configuration, nextSettings, descriptor);
        }

        _settings = nextSettings;
        ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TaskSourceActivation> PrepareActivationCoreAsync(
        TaskSpaceOperationContext context,
        string sourceId)
    {
        ValidateOperationContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var previousSettings = CloneSettings(_settings);
        var preparedSettings = CloneSettings(_settings);
        var descriptor = preparedSettings.Sources.FirstOrDefault(source =>
            string.Equals(source.Id, sourceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Task space '{sourceId}' is not configured.");
        descriptor = CloneDescriptor(descriptor);
        var serverSettings = descriptor.Kind == TaskSourceKind.Server
            ? preparedSettings.ServerSettings
                .Where(server => string.Equals(server.SourceId, descriptor.Id, StringComparison.Ordinal))
                .Select(CloneServerSettings)
                .SingleOrDefault()
            : null;
        preparedSettings.ActiveSourceId = descriptor.Id;
        return await PrepareActivationCoreAsync(
                previousSettings,
                preparedSettings,
                descriptor,
                serverSettings,
                catalogMutationOperation: null)
            .ConfigureAwait(false);
    }

    public async Task<TaskSourceActivation> PrepareAddLocalActivationCoreAsync(
        TaskSpaceOperationContext context,
        string displayName,
        string? path = null)
    {
        ValidateOperationContext(context);
        var descriptor = CreateLocalDescriptor(displayName, path);
        CreateLocalSourceDirectory(descriptor);
        ValidateUniqueDescriptor(descriptor, _settings.Sources);
        var previousSettings = CloneSettings(_settings);
        var preparedSettings = CloneSettings(_settings);
        preparedSettings.Sources.Add(descriptor);
        TaskSourceSettingsAdapter.EnsureSyncSettings(
            preparedSettings,
            descriptor.Id,
            new GitSettings { BackupEnabled = false });
        preparedSettings.ActiveSourceId = descriptor.Id;
        return await PrepareActivationCoreAsync(
                previousSettings,
                preparedSettings,
                descriptor,
                serverSettings: null,
                catalogMutationOperation: "Add")
            .ConfigureAwait(false);
    }

    private async Task<TaskSourceActivation> PrepareActivationCoreAsync(
        TaskSourcesSettings previousSettings,
        TaskSourcesSettings preparedSettings,
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings,
        string? catalogMutationOperation)
    {
        var runtime = BuildRuntime(descriptor, serverSettings);
        var activation = new TaskSourceActivation(
            runtime,
            ActiveSource,
            previousSettings,
            preparedSettings,
            catalogMutationOperation);

        try
        {
            var connected = await runtime.Storage.TaskTreeManager.Storage.Connect().ConfigureAwait(false);
            if (!connected)
            {
                throw new InvalidOperationException(
                    $"Task space '{descriptor.Id}' rejected the storage connection.");
            }

            await runtime.Storage.Init().ConfigureAwait(false);
            runtime.Watcher?.SetEnable(false);
            return activation;
        }
        catch
        {
            await DisconnectRuntimeAsync(runtime).ConfigureAwait(false);
            throw;
        }
    }

    public async Task PublishActivationCoreAsync(
        TaskSpaceOperationContext context,
        TaskSourceActivation activation)
    {
        ValidateOperationContext(context);
        ArgumentNullException.ThrowIfNull(activation);
        if (activation.IsPublished)
        {
            throw new InvalidOperationException("The task-space activation is already published.");
        }

        if (!ReferenceEquals(ActiveSource, activation.Previous))
        {
            throw new InvalidOperationException("The published task-space runtime changed while activation was prepared.");
        }

        activation.HasPersistentChanges = true;
        if (activation.CatalogMutationOperation == null)
        {
            TaskSourceSettingsAdapter.Save(_configuration, activation.PreparedSettings);
        }
        else
        {
            TaskSourceSettingsAdapter.ApplyCatalogMutation(
                _configuration,
                activation.PreviousSettings,
                activation.PreparedSettings,
                activation.CatalogMutationOperation);
        }

        TaskSourceSettingsAdapter.SyncLegacy(
            _configuration,
            activation.PreparedSettings,
            activation.Candidate.Descriptor);

        _settings = activation.PreparedSettings;
        ActiveSource = activation.Candidate;
        _sources.Clear();
        _sources.Add(activation.Candidate);
        activation.IsPublished = true;
        activation.Candidate.Watcher?.SetEnable(true);

        if (activation.Previous != null)
        {
            await DisconnectRuntimeStrictAsync(activation.Previous).ConfigureAwait(false);
        }

        ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AbortActivationCoreAsync(
        TaskSpaceOperationContext context,
        TaskSourceActivation activation)
    {
        ValidateOperationContext(context);
        ArgumentNullException.ThrowIfNull(activation);

        if (activation.IsPublished || activation.HasPersistentChanges)
        {
            if (activation.CatalogMutationOperation == null)
            {
                TaskSourceSettingsAdapter.Save(_configuration, activation.PreviousSettings);
            }
            else
            {
                TaskSourceSettingsAdapter.ApplyCatalogMutation(
                    _configuration,
                    activation.PreparedSettings,
                    activation.PreviousSettings,
                    $"Abort{activation.CatalogMutationOperation}");
            }

            var previousDescriptor = activation.PreviousSettings.Sources.First(source =>
                string.Equals(source.Id, activation.PreviousSettings.ActiveSourceId, StringComparison.Ordinal));
            TaskSourceSettingsAdapter.SyncLegacy(_configuration, activation.PreviousSettings, previousDescriptor);
            _settings = activation.PreviousSettings;
            ActiveSource = activation.Previous;
            _sources.Clear();
            if (activation.Previous != null)
            {
                _sources.Add(activation.Previous);
                activation.Previous.Watcher?.SetEnable(true);
            }

            activation.IsPublished = false;
            activation.HasPersistentChanges = false;
            ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
        }

        await DisconnectRuntimeAsync(activation.Candidate).ConfigureAwait(false);
    }

    private void ValidateOperationContext(TaskSpaceOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _operationRunner?.Validate(context);
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
        TaskSourceSettingsAdapter.CaptureLegacyActiveSettings(_configuration, nextSettings);
        if (descriptor.Kind == TaskSourceKind.Server)
        {
            serverSettings ??= TaskSourceSettingsAdapter.EnsureServerSettings(
                nextSettings,
                descriptor.Id,
                _configuration.Get<TaskStorageSettings>("TaskStorage"),
                _configuration);
        }

        ValidateUniqueDescriptor(
            descriptor,
            nextSettings.Sources.Where(source =>
                !string.Equals(source.Id, descriptor.Id, StringComparison.Ordinal)),
            nextSettings.ServerSettings,
            serverSettings?.Login);
        UpsertDescriptor(nextSettings, descriptor);
        TaskSourceSettingsAdapter.EnsureSyncSettings(nextSettings, descriptor.Id);

        if (serverSettings != null)
        {
            UpsertServerSettings(nextSettings, serverSettings);
        }

        var runtime = BuildRuntime(descriptor, serverSettings);

        var previousActiveSource = ActiveSource;
        nextSettings.ActiveSourceId = descriptor.Id;
        TaskSourceSettingsAdapter.Save(_configuration, nextSettings);
        TaskSourceSettingsAdapter.SyncLegacy(_configuration, nextSettings, descriptor);

        ActiveSource = runtime;
        _settings = nextSettings;
        _sources.Clear();
        _sources.Add(runtime);

        if (previousActiveSource != null && !ReferenceEquals(previousActiveSource, runtime))
        {
            await DisconnectRuntimeAsync(previousActiveSource).ConfigureAwait(false);
        }

        ActiveSourceChanged?.Invoke(this, EventArgs.Empty);

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
            }).ToList(),
            SyncSettings = settings.SyncSettings.Select(sync => new TaskSourceSyncSettings
            {
                SourceId = sync.SourceId,
                Git = CloneGitSettings(sync.Git)
            }).ToList(),
            LegacyProjection = new TaskSourceLegacyProjectionState
            {
                ProfileSchemaVersion = settings.LegacyProjection.ProfileSchemaVersion,
                ProjectionState = settings.LegacyProjection.ProjectionState,
                TargetSourceId = settings.LegacyProjection.TargetSourceId,
                TargetTaskStorageFingerprint = settings.LegacyProjection.TargetTaskStorageFingerprint,
                TargetGitFingerprint = settings.LegacyProjection.TargetGitFingerprint,
                CommittedSourceId = settings.LegacyProjection.CommittedSourceId,
                CommittedTaskStorageFingerprint = settings.LegacyProjection.CommittedTaskStorageFingerprint,
                CommittedGitFingerprint = settings.LegacyProjection.CommittedGitFingerprint
            }
        };

    private TaskSourceRuntime BuildRuntime(
        TaskSourceDescriptor descriptor,
        TaskSourceServerSettings? serverSettings)
    {
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
        return new TaskSourceRuntime(
            descriptor,
            serverSettings,
            buildResult.Storage,
            buildResult.Watcher,
            taskContext);
    }

    private TaskSourceDescriptor FindConfiguredSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A task space id is required.", nameof(sourceId));
        }

        return _settings.Sources.FirstOrDefault(source =>
                   string.Equals(source.Id, sourceId, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Task space '{sourceId}' is not configured.");
    }

    private string ResolveNewSourcePath(string sourceId)
    {
        var configuredDefault = _defaultStoragePathProvider?.Invoke();
        var parent = string.IsNullOrWhiteSpace(configuredDefault)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Path.GetFullPath(configuredDefault)) ?? AppContext.BaseDirectory;
        return Path.Combine(parent, "Spaces", sourceId, "Tasks");
    }

    private TaskSourceDescriptor CreateLocalDescriptor(string displayName, string? path)
    {
        var id = $"space-{Guid.NewGuid():N}";
        var sourcePath = string.IsNullOrWhiteSpace(path) ? ResolveNewSourcePath(id) : path;
        return new TaskSourceDescriptor
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "New space" : displayName.Trim(),
            Kind = TaskSourceKind.File,
            Path = Path.GetFullPath(sourcePath),
            IsEnabled = true
        };
    }

    private static void CreateLocalSourceDirectory(TaskSourceDescriptor descriptor)
    {
        if (descriptor.Kind != TaskSourceKind.File || string.IsNullOrWhiteSpace(descriptor.Path))
        {
            throw new InvalidOperationException("A local task space requires a storage folder.");
        }

        Directory.CreateDirectory(descriptor.Path);
    }

    private static void ValidateUniqueDescriptor(
        TaskSourceDescriptor candidate,
        IEnumerable<TaskSourceDescriptor> existing,
        IEnumerable<TaskSourceServerSettings>? serverSettings = null,
        string? candidateServerLogin = null)
    {
        foreach (var source in existing)
        {
            if (string.Equals(source.Id, candidate.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Task space id '{candidate.Id}' already exists.");
            }

            if (candidate.Kind == TaskSourceKind.File && source.Kind == TaskSourceKind.File &&
                !string.IsNullOrWhiteSpace(candidate.Path) &&
                PathsReferToSameDirectory(candidate.Path, source.Path))
            {
                throw new InvalidOperationException("Two task spaces cannot use the same local folder.");
            }

            var candidateServerUrl = NormalizeServerUrl(candidate.Url);
            if (candidate.Kind == TaskSourceKind.Server && source.Kind == TaskSourceKind.Server &&
                !string.IsNullOrWhiteSpace(candidateServerUrl) &&
                string.Equals(
                    candidateServerUrl,
                    NormalizeServerUrl(source.Url),
                    StringComparison.OrdinalIgnoreCase))
            {
                var existingLogin = serverSettings?.FirstOrDefault(server =>
                    string.Equals(server.SourceId, source.Id, StringComparison.Ordinal))?.Login;
                if (string.Equals(
                        NormalizeServerLogin(candidateServerLogin),
                        NormalizeServerLogin(existingLogin),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Two task spaces cannot use the same server URL and login.");
                }
            }
        }
    }

    private static void ValidateConfiguredSourceOwnership(TaskSourcesSettings settings)
    {
        var validated = new List<TaskSourceDescriptor>();
        foreach (var source in settings.Sources)
        {
            var login = settings.ServerSettings.FirstOrDefault(server =>
                string.Equals(server.SourceId, source.Id, StringComparison.Ordinal))?.Login;
            ValidateUniqueDescriptor(source, validated, settings.ServerSettings, login);
            validated.Add(source);
        }
    }

    private static string NormalizeServerUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');

    private static string NormalizeServerLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? string.Empty : login.Trim();

    private static bool PathsReferToSameDirectory(string firstPath, string secondPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            ResolveComparableDirectoryPath(firstPath),
            ResolveComparableDirectoryPath(secondPath),
            comparison);
    }

    private static string ResolveComparableDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                continue;
            }

            try
            {
                var resolved = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    current = Path.GetFullPath(resolved.FullName);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                // Fall back to the normalized path when filesystem identity is unavailable.
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static TaskSourceDescriptor CloneDescriptor(TaskSourceDescriptor source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        Kind = source.Kind,
        Path = source.Path,
        Url = source.Url,
        IsEnabled = source.IsEnabled
    };

    private static TaskSourceServerSettings CloneServerSettings(TaskSourceServerSettings source) => new()
    {
        SourceId = source.SourceId,
        Login = source.Login,
        Password = source.Password,
        AccessToken = source.AccessToken,
        RefreshToken = source.RefreshToken,
        ExpireTime = source.ExpireTime,
        UserId = source.UserId
    };

    private static GitSettings CloneGitSettings(GitSettings source) => new()
    {
        BackupEnabled = source.BackupEnabled,
        ShowStatusToasts = source.ShowStatusToasts,
        RemoteUrl = source.RemoteUrl,
        Branch = source.Branch,
        UserName = source.UserName,
        Password = source.Password,
        SshPrivateKeyPath = source.SshPrivateKeyPath,
        SshPublicKeyPath = source.SshPublicKeyPath,
        SshKeyStoragePath = source.SshKeyStoragePath,
        PullIntervalSeconds = source.PullIntervalSeconds,
        PushIntervalSeconds = source.PushIntervalSeconds,
        RemoteName = source.RemoteName,
        PushRefSpec = source.PushRefSpec,
        CommitterName = source.CommitterName,
        CommitterEmail = source.CommitterEmail
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

    private static async Task DisconnectRuntimeStrictAsync(TaskSourceRuntime runtime)
    {
        await runtime.Storage.TaskTreeManager.Storage.Disconnect().ConfigureAwait(false);
        (runtime.Storage as IDisposable)?.Dispose();
    }
}
