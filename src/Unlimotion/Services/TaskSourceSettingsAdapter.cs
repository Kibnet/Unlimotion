using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public static class TaskSourceSettingsAdapter
{
    private const string SyncProfilesSectionName = "TaskSourceSyncProfiles";
    private const string ProjectionSectionName = "TaskSourceLegacyProjection";
    private const string MutationJournalSectionName = "TaskSourceMutationJournal";
    private const int ProfileSchemaVersion = 1;

    public static TaskSourcesSettings LoadOrCreate(
        IConfiguration configuration,
        string? defaultStoragePath,
        Action<TaskSourcesSettings>? validateBeforePersistence = null)
    {
        RecoverMutationJournal(configuration, validateBeforePersistence);
        var settings = Read(configuration);
        RecoverInterruptedFirstMigrationCatalog(configuration, settings);
        ValidateCatalog(settings);
        var hasConfiguredSources = settings.Sources.Count > 0;
        var legacyStorage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();

        if (settings.Sources.Count == 0)
        {
            var descriptor = CreateLegacyDescriptor(legacyStorage, defaultStoragePath);
            settings.ActiveSourceId = descriptor.Id;
            settings.Sources.Add(descriptor);
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveSourceId) ||
            settings.Sources.All(source => !string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal)))
        {
            settings.ActiveSourceId = settings.Sources.First().Id;
        }

        if (string.Equals(settings.LegacyProjection.ProjectionState, "Prepared", StringComparison.Ordinal) &&
            settings.Sources.Any(source => string.Equals(
                source.Id,
                settings.LegacyProjection.TargetSourceId,
                StringComparison.Ordinal)))
        {
            settings.ActiveSourceId = settings.LegacyProjection.TargetSourceId;
        }

        var activeSource = settings.Sources.First(source =>
            string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal));
        var projection = settings.LegacyProjection;
        if (hasConfiguredSources &&
            string.Equals(projection.ProjectionState, "Committed", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(projection.CommittedSourceId) &&
            LegacyProjectionDiverged(configuration, projection))
        {
            ImportLegacyProjection(configuration, settings, projection.CommittedSourceId);
            activeSource = settings.Sources.First(source =>
                string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal));
        }
        else if (hasConfiguredSources &&
                 string.IsNullOrWhiteSpace(projection.CommittedSourceId) &&
                 ShouldCaptureUnprojectedLegacyStorage(settings, legacyStorage))
        {
            // Preserve a legacy edit that is not already another configured space.
            // Git remains profile-owned and must never be inferred from a stale
            // global projection during startup.
            CaptureLegacyActiveSettings(configuration, settings, captureGitSettings: false);
            activeSource = settings.Sources.First(source =>
                string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal));
        }
        if (activeSource.Kind == TaskSourceKind.Server)
        {
            EnsureServerSettings(settings, activeSource.Id, legacyStorage, configuration);
        }

        EnsureSyncSettings(settings, activeSource.Id, configuration.Get<GitSettings>("Git"));
        foreach (var source in settings.Sources)
        {
            EnsureSyncSettings(settings, source.Id);
        }

        validateBeforePersistence?.Invoke(settings);
        Save(configuration, settings);
        SyncLegacy(configuration, settings, activeSource);
        ValidateCatalog(settings);
        return settings;
    }

    public static TaskSourceDescriptor CreateLegacyDescriptor(
        TaskStorageSettings legacyStorage,
        string? defaultStoragePath)
    {
        var isServerMode = legacyStorage.IsServerMode;
        return new TaskSourceDescriptor
        {
            Id = TaskSourceDescriptor.DefaultSourceId,
            DisplayName = isServerMode ? "Default server" : "Local tasks",
            Kind = isServerMode ? TaskSourceKind.Server : TaskSourceKind.File,
            Path = string.IsNullOrWhiteSpace(legacyStorage.Path) ? defaultStoragePath ?? string.Empty : legacyStorage.Path,
            Url = legacyStorage.URL,
            IsEnabled = true
        };
    }

    public static TaskSourceServerSettings EnsureServerSettings(
        TaskSourcesSettings settings,
        string sourceId,
        TaskStorageSettings? legacyStorage = null,
        IConfiguration? configuration = null)
    {
        var serverSettings = settings.ServerSettings.FirstOrDefault(server =>
            string.Equals(server.SourceId, sourceId, StringComparison.Ordinal));
        if (serverSettings != null)
        {
            return serverSettings;
        }

        var isDefaultSource = string.Equals(sourceId, TaskSourceDescriptor.DefaultSourceId, StringComparison.Ordinal);
        var legacyClient = isDefaultSource && configuration != null ? ReadClientSettings(configuration) : null;
        serverSettings = new TaskSourceServerSettings
        {
            SourceId = sourceId,
            Login = legacyStorage?.Login ?? legacyClient?.Login ?? string.Empty,
            Password = legacyStorage?.Password ?? string.Empty,
            AccessToken = legacyClient?.AccessToken ?? string.Empty,
            RefreshToken = legacyClient?.RefreshToken ?? string.Empty,
            ExpireTime = legacyClient?.ExpireTime ?? default,
            UserId = legacyClient?.UserId ?? string.Empty
        };
        settings.ServerSettings.Add(serverSettings);
        return serverSettings;
    }

    public static TaskSourceSyncSettings EnsureSyncSettings(
        TaskSourcesSettings settings,
        string sourceId,
        GitSettings? legacyGitSettings = null)
    {
        var existing = settings.SyncSettings.FirstOrDefault(sync =>
            string.Equals(sync.SourceId, sourceId, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        var created = new TaskSourceSyncSettings
        {
            SourceId = sourceId,
            Git = CloneGitSettings(legacyGitSettings ?? new GitSettings())
        };
        settings.SyncSettings.Add(created);
        return created;
    }

    public static GitSettings GetActiveGitSettings(TaskSourcesSettings settings)
    {
        return EnsureSyncSettings(settings, settings.ActiveSourceId).Git;
    }

    public static void CaptureLegacyActiveSettings(
        IConfiguration configuration,
        TaskSourcesSettings settings,
        bool captureGitSettings = true)
    {
        var active = settings.Sources.FirstOrDefault(source =>
            string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal));
        if (active == null)
        {
            return;
        }

        var legacyStorage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        active.Kind = legacyStorage.IsServerMode ? TaskSourceKind.Server : TaskSourceKind.File;
        active.Path = legacyStorage.Path ?? string.Empty;
        active.Url = legacyStorage.URL ?? string.Empty;

        if (active.Kind == TaskSourceKind.Server)
        {
            var server = EnsureServerSettings(settings, active.Id, legacyStorage, configuration);
            server.Login = legacyStorage.Login ?? string.Empty;
            server.Password = legacyStorage.Password ?? string.Empty;
        }

        if (captureGitSettings)
        {
            var profile = EnsureSyncSettings(settings, active.Id);
            profile.Git = CloneGitSettings(configuration.Get<GitSettings>("Git") ?? new GitSettings());
        }
    }

    public static void Save(IConfiguration configuration, TaskSourcesSettings settings)
    {
        var section = configuration.GetSection(TaskSourcesSettings.SectionName);
        var previousSourceCount = Math.Max(
            section.GetSection("SourcesCount").Get<int?>() ?? 0,
            GetPhysicalSlotCount(section, "SourceEntry", "SourceEntries"));
        var previousServerCount = Math.Max(
            section.GetSection("ServerSettingsCount").Get<int?>() ?? 0,
            GetPhysicalSlotCount(section, "ServerSettingEntry", "ServerSettingEntries"));

        for (var i = 0; i < settings.Sources.Count; i++)
        {
            var source = settings.Sources[i];
            var index = i.ToString(CultureInfo.InvariantCulture);
            section.GetSection($"SourceKey{index}").Set(source.Id);
            section.GetSection($"SourceEntry{index}").Set(JsonSerializer.Serialize(source));
            ClearLegacySourceFields(section, index);
        }

        for (var i = 0; i < settings.ServerSettings.Count; i++)
        {
            var server = settings.ServerSettings[i];
            var index = i.ToString(CultureInfo.InvariantCulture);
            section.GetSection($"ServerSettingsKey{index}").Set(server.SourceId);
            section.GetSection($"ServerSettingEntry{index}").Set(JsonSerializer.Serialize(server));
            ClearLegacyServerFields(section, index);
        }

        for (var i = settings.Sources.Count; i < previousSourceCount; i++)
        {
            ClearSourceSlot(section, i);
        }

        for (var i = settings.ServerSettings.Count; i < previousServerCount; i++)
        {
            ClearServerSlot(section, i);
        }

        SaveSyncSettings(configuration, settings.SyncSettings);
        section.GetSection("SourcesCount").Set(settings.Sources.Count);
        section.GetSection("ServerSettingsCount").Set(settings.ServerSettings.Count);
        section.GetSection(nameof(TaskSourcesSettings.ActiveSourceId)).Set(settings.ActiveSourceId);
    }

    public static void PersistServerSettings(
        IConfiguration configuration,
        TaskSourcesSettings settings,
        TaskSourceServerSettings serverSettings)
    {
        var existingIndex = settings.ServerSettings.FindIndex(candidate =>
            string.Equals(candidate.SourceId, serverSettings.SourceId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            settings.ServerSettings[existingIndex] = serverSettings;
        }
        else
        {
            settings.ServerSettings.Add(serverSettings);
        }

        Save(configuration, settings);
        if (string.Equals(serverSettings.SourceId, settings.ActiveSourceId, StringComparison.Ordinal))
        {
            WriteClientSettings(configuration, ToClientSettings(serverSettings));
        }
    }

    public static void SyncLegacy(
        IConfiguration configuration,
        TaskSourcesSettings settings,
        TaskSourceDescriptor activeSource)
    {
        var currentStorage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        var serverSettings = settings.ServerSettings.FirstOrDefault(server =>
            string.Equals(server.SourceId, activeSource.Id, StringComparison.Ordinal));
        var targetStorage = new TaskStorageSettings
        {
            IsServerMode = activeSource.Kind == TaskSourceKind.Server,
            Path = activeSource.Path,
            URL = activeSource.Url,
            Login = serverSettings?.Login ?? string.Empty,
            Password = serverSettings?.Password ?? string.Empty,
            IsFuzzySearch = currentStorage.IsFuzzySearch
        };
        var targetGit = CloneGitSettings(EnsureSyncSettings(settings, activeSource.Id).Git);
        var storageFingerprint = ProjectionFingerprint(targetStorage);
        var gitFingerprint = ProjectionFingerprint(targetGit);
        var projection = settings.LegacyProjection;
        projection.ProfileSchemaVersion = ProfileSchemaVersion;
        projection.TargetSourceId = activeSource.Id;
        projection.TargetTaskStorageFingerprint = storageFingerprint;
        projection.TargetGitFingerprint = gitFingerprint;
        projection.ProjectionState = "Prepared";
        WritePreparedProjection(configuration, projection);

        configuration.Set("TaskStorage", targetStorage);
        configuration.Set("Git", targetGit);
        if (serverSettings != null)
        {
            WriteClientSettings(configuration, ToClientSettings(serverSettings));
        }
        else
        {
            WriteClientSettings(configuration, new ClientSettings());
        }

        var persistedStorage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        var persistedGit = configuration.Get<GitSettings>("Git") ?? new GitSettings();
        if (!string.Equals(ProjectionFingerprint(persistedStorage), storageFingerprint, StringComparison.Ordinal) ||
            !string.Equals(ProjectionFingerprint(persistedGit), gitFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The active task-space compatibility projection could not be verified ({DescribeProjectionDifference(targetStorage, persistedStorage, targetGit, persistedGit)}).");
        }

        projection.CommittedSourceId = activeSource.Id;
        projection.CommittedTaskStorageFingerprint = storageFingerprint;
        projection.CommittedGitFingerprint = gitFingerprint;
        projection.ProjectionState = "Committed";
        WriteCommittedProjection(configuration, projection);
    }

    public static void ApplyCatalogMutation(
        IConfiguration configuration,
        TaskSourcesSettings before,
        TaskSourcesSettings after,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ValidateCatalog(before);
        ValidateCatalog(after);

        var journal = configuration.GetSection(MutationJournalSectionName);
        var mutationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var beforeJson = JsonSerializer.Serialize(before);
        var afterJson = JsonSerializer.Serialize(after);

        try
        {
            journal.GetSection("MutationId").Set(mutationId);
            journal.GetSection("Operation").Set(operation);
            journal.GetSection("BeforeSnapshot").Set(beforeJson);
            journal.GetSection("AfterSnapshot").Set(afterJson);
            journal.GetSection("State").Set("Prepared");
            if (!string.Equals(journal.GetSection("MutationId").Get<string>(), mutationId, StringComparison.Ordinal) ||
                !string.Equals(journal.GetSection("BeforeSnapshot").Get<string>(), beforeJson, StringComparison.Ordinal) ||
                !string.Equals(journal.GetSection("AfterSnapshot").Get<string>(), afterJson, StringComparison.Ordinal) ||
                !string.Equals(journal.GetSection("State").Get<string>(), "Prepared", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The task-space mutation journal could not be verified.");
            }

            Save(configuration, after);
            var persisted = Read(configuration);
            ValidateCatalog(persisted);
            if (!string.Equals(CatalogFingerprint(persisted), CatalogFingerprint(after), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The task-space catalog mutation could not be verified.");
            }

            journal.GetSection("State").Set("Committed");
            ClearMutationJournal(journal);
        }
        catch (Exception mutationError)
        {
            try
            {
                Save(configuration, before);
                var restored = Read(configuration);
                if (!string.Equals(CatalogFingerprint(restored), CatalogFingerprint(before), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The task-space catalog rollback could not be verified.");
                }

                ClearMutationJournal(journal);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "The task-space catalog mutation and its rollback both failed. Restart is required.",
                    mutationError,
                    rollbackError);
            }

            throw;
        }
    }

    private static void SaveSyncSettings(
        IConfiguration configuration,
        IReadOnlyList<TaskSourceSyncSettings> syncSettings)
    {
        // WritableJsonConfiguration only replaces top-level scalar values reliably:
        // object updates leave obsolete children behind and list updates fail. One JSON
        // value gives us an atomic profile catalog, including deletion of a space.
        var profiles = syncSettings
            .Where(profile => !string.IsNullOrWhiteSpace(profile.SourceId))
            .ToDictionary(
                profile => profile.SourceId,
                profile => CloneGitSettings(profile.Git),
                StringComparer.Ordinal);
        configuration.Set(SyncProfilesSectionName, JsonSerializer.Serialize(profiles));
    }

    private static void RecoverMutationJournal(
        IConfiguration configuration,
        Action<TaskSourcesSettings>? validateBeforePersistence)
    {
        var journal = configuration.GetSection(MutationJournalSectionName);
        var state = journal.GetSection("State").Get<string>();
        if (!string.Equals(state, "Prepared", StringComparison.Ordinal) &&
            !string.Equals(state, "Committed", StringComparison.Ordinal))
        {
            return;
        }

        var snapshotKey = string.Equals(state, "Committed", StringComparison.Ordinal)
            ? "AfterSnapshot"
            : "BeforeSnapshot";
        var snapshotJson = journal.GetSection(snapshotKey).Get<string>();
        TaskSourcesSettings? snapshot;
        try
        {
            snapshot = string.IsNullOrWhiteSpace(snapshotJson)
                ? null
                : JsonSerializer.Deserialize<TaskSourcesSettings>(snapshotJson);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new TaskSpaceCatalogException(
                TaskSpaceCatalogIssue.InvalidSourceConfiguration,
                [MutationJournalSectionName],
                "The task-space mutation journal is corrupt and requires manual recovery.",
                error);
        }

        if (snapshot == null)
        {
            throw new TaskSpaceCatalogException(
                TaskSpaceCatalogIssue.InvalidSourceConfiguration,
                [MutationJournalSectionName],
                "The task-space mutation journal is corrupt and requires manual recovery.");
        }

        ValidateCatalog(snapshot);
        validateBeforePersistence?.Invoke(snapshot);
        Save(configuration, snapshot);
        var persisted = Read(configuration);
        if (!string.Equals(CatalogFingerprint(persisted), CatalogFingerprint(snapshot), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The task-space mutation journal recovery could not be verified.");
        }

        ClearMutationJournal(journal);
    }

    private static void RecoverInterruptedFirstMigrationCatalog(
        IConfiguration configuration,
        TaskSourcesSettings settings)
    {
        if (settings.Sources.Count != 0 ||
            settings.ServerSettings.Count == 0 && settings.SyncSettings.Count == 0)
        {
            return;
        }

        var section = configuration.GetSection(TaskSourcesSettings.SectionName);
        var declaredSourceCount = section.GetSection("SourcesCount").Get<int?>();
        int? sourceCount = declaredSourceCount.HasValue
            ? Math.Max(
                declaredSourceCount.Value,
                GetPhysicalSlotCount(section, "SourceEntry", "SourceEntries"))
            : null;
        var hasPreparedSourceSlot = !string.IsNullOrWhiteSpace(
            section.GetSection("SourceEntry0").Get<string>());
        if (sourceCount.HasValue || !hasPreparedSourceSlot)
        {
            return;
        }

        // A first migration writes provider-safe source/profile slots before their
        // count marker. If that marker write fails, the profile can be visible
        // without its source on restart. Discard only this recognizable uncounted
        // partial state and rebuild it from untouched legacy sections below.
        settings.ActiveSourceId = TaskSourceDescriptor.DefaultSourceId;
        settings.ServerSettings.Clear();
        settings.SyncSettings.Clear();
        settings.LegacyProjection = new TaskSourceLegacyProjectionState();
    }

    private static void ClearMutationJournal(IConfiguration journal)
    {
        journal.GetSection("BeforeSnapshot").Set(string.Empty);
        journal.GetSection("AfterSnapshot").Set(string.Empty);
        journal.GetSection("Operation").Set(string.Empty);
        journal.GetSection("MutationId").Set(string.Empty);
        journal.GetSection("State").Set(string.Empty);
    }

    private static void ValidateCatalog(TaskSourcesSettings settings)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in settings.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
            {
                throw new TaskSpaceCatalogException(
                    TaskSpaceCatalogIssue.DuplicateOrEmptySourceId,
                    [source.Id],
                    $"Duplicate or empty task-space id '{source.Id}'.");
            }
        }

        ValidateScopedIds(
            settings.ServerSettings.Select(server => server.SourceId),
            sourceIds,
            "server settings");
        ValidateScopedIds(
            settings.SyncSettings.Select(sync => sync.SourceId),
            sourceIds,
            "sync settings");

        if (settings.Sources.Count > 0 &&
            (string.IsNullOrWhiteSpace(settings.ActiveSourceId) || !sourceIds.Contains(settings.ActiveSourceId)))
        {
            throw new TaskSpaceCatalogException(
                TaskSpaceCatalogIssue.MissingActiveSource,
                [settings.ActiveSourceId],
                $"Active task-space id '{settings.ActiveSourceId}' is not present in the catalog.");
        }
    }

    private static void ValidateScopedIds(
        IEnumerable<string> scopedIds,
        IReadOnlySet<string> sourceIds,
        string scope)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceId in scopedIds)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || !unique.Add(sourceId) || !sourceIds.Contains(sourceId))
            {
                throw new TaskSpaceCatalogException(
                    TaskSpaceCatalogIssue.OrphanScopedSettings,
                    [sourceId],
                    $"Duplicate, empty, or orphan task-space id '{sourceId}' in {scope}.");
            }
        }
    }

    private static void ImportLegacyProjection(
        IConfiguration configuration,
        TaskSourcesSettings settings,
        string sourceId)
    {
        var source = settings.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, sourceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Committed legacy projection refers to unknown task space '{sourceId}'.");
        var storage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        source.Kind = storage.IsServerMode ? TaskSourceKind.Server : TaskSourceKind.File;
        source.Path = storage.Path ?? string.Empty;
        source.Url = storage.URL ?? string.Empty;
        if (source.Kind == TaskSourceKind.Server)
        {
            var server = EnsureServerSettings(settings, sourceId, storage, configuration);
            server.Login = storage.Login ?? string.Empty;
            server.Password = storage.Password ?? string.Empty;
        }

        EnsureSyncSettings(settings, sourceId).Git =
            CloneGitSettings(configuration.Get<GitSettings>("Git") ?? new GitSettings());
    }

    private static bool LegacyProjectionDiverged(
        IConfiguration configuration,
        TaskSourceLegacyProjectionState projection)
    {
        var storage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        var git = configuration.Get<GitSettings>("Git") ?? new GitSettings();
        return !string.Equals(
                   ProjectionFingerprint(storage),
                   projection.CommittedTaskStorageFingerprint,
                   StringComparison.Ordinal) ||
               !string.Equals(
                   ProjectionFingerprint(git),
                   projection.CommittedGitFingerprint,
                   StringComparison.Ordinal);
    }

    private static TaskSourceLegacyProjectionState ReadProjection(IConfiguration configuration)
    {
        var section = configuration.GetSection(ProjectionSectionName);
        return new TaskSourceLegacyProjectionState
        {
            ProfileSchemaVersion = section.GetSection(nameof(TaskSourceLegacyProjectionState.ProfileSchemaVersion))
                .Get<int?>() ?? ProfileSchemaVersion,
            ProjectionState = section.GetSection(nameof(TaskSourceLegacyProjectionState.ProjectionState))
                .Get<string>() ?? "Committed",
            TargetSourceId = section.GetSection(nameof(TaskSourceLegacyProjectionState.TargetSourceId))
                .Get<string>() ?? string.Empty,
            TargetTaskStorageFingerprint = section
                .GetSection(nameof(TaskSourceLegacyProjectionState.TargetTaskStorageFingerprint))
                .Get<string>() ?? string.Empty,
            TargetGitFingerprint = section.GetSection(nameof(TaskSourceLegacyProjectionState.TargetGitFingerprint))
                .Get<string>() ?? string.Empty,
            CommittedSourceId = section.GetSection(nameof(TaskSourceLegacyProjectionState.CommittedSourceId))
                .Get<string>() ?? string.Empty,
            CommittedTaskStorageFingerprint = section
                .GetSection(nameof(TaskSourceLegacyProjectionState.CommittedTaskStorageFingerprint))
                .Get<string>() ?? string.Empty,
            CommittedGitFingerprint = section
                .GetSection(nameof(TaskSourceLegacyProjectionState.CommittedGitFingerprint))
                .Get<string>() ?? string.Empty
        };
    }

    private static void WritePreparedProjection(
        IConfiguration configuration,
        TaskSourceLegacyProjectionState projection)
    {
        var section = configuration.GetSection(ProjectionSectionName);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.ProfileSchemaVersion))
            .Set(projection.ProfileSchemaVersion);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.TargetSourceId))
            .Set(projection.TargetSourceId);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.TargetTaskStorageFingerprint))
            .Set(projection.TargetTaskStorageFingerprint);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.TargetGitFingerprint))
            .Set(projection.TargetGitFingerprint);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.ProjectionState)).Set("Prepared");
    }

    private static void WriteCommittedProjection(
        IConfiguration configuration,
        TaskSourceLegacyProjectionState projection)
    {
        var section = configuration.GetSection(ProjectionSectionName);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.CommittedSourceId))
            .Set(projection.CommittedSourceId);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.CommittedTaskStorageFingerprint))
            .Set(projection.CommittedTaskStorageFingerprint);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.CommittedGitFingerprint))
            .Set(projection.CommittedGitFingerprint);
        section.GetSection(nameof(TaskSourceLegacyProjectionState.ProjectionState)).Set("Committed");
    }

    private static string CatalogFingerprint(TaskSourcesSettings settings) =>
        Fingerprint(new
        {
            settings.ActiveSourceId,
            Sources = settings.Sources.Select(source => new
            {
                source.Id,
                source.DisplayName,
                source.Kind,
                source.Path,
                source.Url,
                source.IsEnabled
            }).ToArray(),
            Servers = settings.ServerSettings.Select(server => new
            {
                server.SourceId,
                server.Login,
                server.Password,
                server.AccessToken,
                server.RefreshToken,
                server.ExpireTime,
                server.UserId
            }).ToArray(),
            Sync = settings.SyncSettings.Select(sync => new
            {
                sync.SourceId,
                sync.Git
            }).ToArray()
        });

    private static string Fingerprint<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string ProjectionFingerprint(TaskStorageSettings storage) =>
        Fingerprint(new
        {
            Path = storage.Path ?? string.Empty,
            URL = storage.URL ?? string.Empty,
            Login = storage.Login ?? string.Empty,
            Password = storage.Password ?? string.Empty,
            storage.IsServerMode,
            storage.IsFuzzySearch
        });

    private static string ProjectionFingerprint(GitSettings git) =>
        Fingerprint(new
        {
            git.BackupEnabled,
            git.ShowStatusToasts,
            RemoteUrl = git.RemoteUrl ?? string.Empty,
            Branch = git.Branch ?? string.Empty,
            UserName = git.UserName ?? string.Empty,
            Password = git.Password ?? string.Empty,
            SshPrivateKeyPath = git.SshPrivateKeyPath ?? string.Empty,
            SshPublicKeyPath = git.SshPublicKeyPath ?? string.Empty,
            SshKeyStoragePath = git.SshKeyStoragePath ?? string.Empty,
            git.PullIntervalSeconds,
            git.PushIntervalSeconds,
            RemoteName = git.RemoteName ?? string.Empty,
            PushRefSpec = git.PushRefSpec ?? string.Empty,
            CommitterName = git.CommitterName ?? string.Empty,
            CommitterEmail = git.CommitterEmail ?? string.Empty
        });

    private static string DescribeProjectionDifference(
        TaskStorageSettings expectedStorage,
        TaskStorageSettings actualStorage,
        GitSettings expectedGit,
        GitSettings actualGit)
    {
        var fields = new List<string>();
        if (!string.Equals(expectedStorage.Path, actualStorage.Path, StringComparison.Ordinal)) fields.Add("TaskStorage.Path");
        if (!string.Equals(expectedStorage.URL, actualStorage.URL, StringComparison.Ordinal)) fields.Add("TaskStorage.URL");
        if (!string.Equals(expectedStorage.Login, actualStorage.Login, StringComparison.Ordinal)) fields.Add("TaskStorage.Login");
        if (!string.Equals(expectedStorage.Password, actualStorage.Password, StringComparison.Ordinal)) fields.Add("TaskStorage.Password");
        if (expectedStorage.IsServerMode != actualStorage.IsServerMode) fields.Add("TaskStorage.IsServerMode");
        if (expectedStorage.IsFuzzySearch != actualStorage.IsFuzzySearch) fields.Add("TaskStorage.IsFuzzySearch");
        if (expectedGit.BackupEnabled != actualGit.BackupEnabled) fields.Add("Git.BackupEnabled");
        if (expectedGit.ShowStatusToasts != actualGit.ShowStatusToasts) fields.Add("Git.ShowStatusToasts");
        if (!string.Equals(expectedGit.RemoteUrl, actualGit.RemoteUrl, StringComparison.Ordinal)) fields.Add("Git.RemoteUrl");
        if (!string.Equals(expectedGit.Branch, actualGit.Branch, StringComparison.Ordinal)) fields.Add("Git.Branch");
        if (!string.Equals(expectedGit.UserName, actualGit.UserName, StringComparison.Ordinal)) fields.Add("Git.UserName");
        if (!string.Equals(expectedGit.Password, actualGit.Password, StringComparison.Ordinal)) fields.Add("Git.Password");
        if (!string.Equals(expectedGit.SshPrivateKeyPath, actualGit.SshPrivateKeyPath, StringComparison.Ordinal)) fields.Add("Git.SshPrivateKeyPath");
        if (!string.Equals(expectedGit.SshPublicKeyPath, actualGit.SshPublicKeyPath, StringComparison.Ordinal)) fields.Add("Git.SshPublicKeyPath");
        if (!string.Equals(expectedGit.SshKeyStoragePath, actualGit.SshKeyStoragePath, StringComparison.Ordinal)) fields.Add("Git.SshKeyStoragePath");
        if (expectedGit.PullIntervalSeconds != actualGit.PullIntervalSeconds) fields.Add("Git.PullIntervalSeconds");
        if (expectedGit.PushIntervalSeconds != actualGit.PushIntervalSeconds) fields.Add("Git.PushIntervalSeconds");
        if (!string.Equals(expectedGit.RemoteName, actualGit.RemoteName, StringComparison.Ordinal)) fields.Add("Git.RemoteName");
        if (!string.Equals(expectedGit.PushRefSpec, actualGit.PushRefSpec, StringComparison.Ordinal)) fields.Add("Git.PushRefSpec");
        if (!string.Equals(expectedGit.CommitterName, actualGit.CommitterName, StringComparison.Ordinal)) fields.Add("Git.CommitterName");
        if (!string.Equals(expectedGit.CommitterEmail, actualGit.CommitterEmail, StringComparison.Ordinal)) fields.Add("Git.CommitterEmail");
        return fields.Count == 0 ? "serialization mismatch" : string.Join(", ", fields);
    }

    private static bool ShouldCaptureUnprojectedLegacyStorage(
        TaskSourcesSettings settings,
        TaskStorageSettings legacyStorage)
    {
        if (!legacyStorage.IsServerMode && string.IsNullOrWhiteSpace(legacyStorage.Path) &&
            string.IsNullOrWhiteSpace(legacyStorage.URL))
        {
            return false;
        }

        var legacyDescriptor = CreateLegacyDescriptor(legacyStorage, defaultStoragePath: null);
        return settings.Sources.All(source => !DescribesSameSource(source, legacyDescriptor));
    }

    private static bool DescribesSameSource(TaskSourceDescriptor left, TaskSourceDescriptor right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind == TaskSourceKind.Server
            ? string.Equals(left.Url, right.Url, StringComparison.Ordinal)
            : string.Equals(left.Path, right.Path, StringComparison.Ordinal);
    }

    private static void ClearSourceSlot(IConfiguration section, int index)
    {
        var value = index.ToString(CultureInfo.InvariantCulture);
        section.GetSection($"SourceKey{value}").Set(string.Empty);
        section.GetSection($"SourceEntry{value}").Set(string.Empty);
        ClearLegacySourceFields(section, value);
    }

    private static void ClearLegacySourceFields(IConfiguration section, string value)
    {
        var source = section.GetSection("SourceEntries").GetSection($"Source{value}");
        source.GetSection(nameof(TaskSourceDescriptor.Id)).Set(string.Empty);
        source.GetSection(nameof(TaskSourceDescriptor.DisplayName)).Set(string.Empty);
        source.GetSection(nameof(TaskSourceDescriptor.Kind)).Set(string.Empty);
        source.GetSection(nameof(TaskSourceDescriptor.Path)).Set(string.Empty);
        source.GetSection(nameof(TaskSourceDescriptor.Url)).Set(string.Empty);
        source.GetSection(nameof(TaskSourceDescriptor.IsEnabled)).Set(false);
    }

    private static void ClearServerSlot(IConfiguration section, int index)
    {
        var value = index.ToString(CultureInfo.InvariantCulture);
        section.GetSection($"ServerSettingsKey{value}").Set(string.Empty);
        section.GetSection($"ServerSettingEntry{value}").Set(string.Empty);
        ClearLegacyServerFields(section, value);
    }

    private static void ClearLegacyServerFields(IConfiguration section, string value)
    {
        var server = section.GetSection("ServerSettingEntries").GetSection($"Server{value}");
        server.GetSection(nameof(TaskSourceServerSettings.SourceId)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.Login)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.Password)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.AccessToken)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.RefreshToken)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.ExpireTime)).Set(string.Empty);
        server.GetSection(nameof(TaskSourceServerSettings.UserId)).Set(string.Empty);
    }

    private static int GetPhysicalSlotCount(
        IConfiguration section,
        string scalarPrefix,
        string legacyEntriesSectionName)
    {
        var maxScalarIndex = section.GetChildren()
            .Select(child => child.Key)
            .Where(key => key.StartsWith(scalarPrefix, StringComparison.Ordinal))
            .Select(key => int.TryParse(
                key.AsSpan(scalarPrefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var index)
                ? index
                : -1)
            .DefaultIfEmpty(-1)
            .Max();
        return Math.Max(
            maxScalarIndex + 1,
            section.GetSection(legacyEntriesSectionName).GetChildren().Count());
    }

    public static ClientSettings ToClientSettings(TaskSourceServerSettings serverSettings) =>
        new()
        {
            AccessToken = serverSettings.AccessToken,
            RefreshToken = serverSettings.RefreshToken,
            ExpireTime = serverSettings.ExpireTime,
            UserId = serverSettings.UserId,
            Login = serverSettings.Login
        };

    public static void CopyFromClientSettings(
        ClientSettings clientSettings,
        TaskSourceServerSettings serverSettings)
    {
        serverSettings.AccessToken = clientSettings.AccessToken;
        serverSettings.RefreshToken = clientSettings.RefreshToken;
        serverSettings.ExpireTime = clientSettings.ExpireTime;
        serverSettings.UserId = clientSettings.UserId;
        serverSettings.Login = clientSettings.Login;
    }

    private static TaskSourcesSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(TaskSourcesSettings.SectionName);
        var settings = new TaskSourcesSettings
        {
            ActiveSourceId = section.GetSection(nameof(TaskSourcesSettings.ActiveSourceId)).Get<string>()
                             ?? TaskSourceDescriptor.DefaultSourceId
        };

        var declaredSourceCount = section.GetSection("SourcesCount").Get<int?>();
        int? sourceCount = declaredSourceCount.HasValue
            ? Math.Max(
                declaredSourceCount.Value,
                GetPhysicalSlotCount(section, "SourceEntry", "SourceEntries"))
            : null;
        if (sourceCount.HasValue)
        {
            for (var i = 0; i < sourceCount.Value; i++)
            {
                var serialized = section.GetSection(
                        $"SourceEntry{i.ToString(CultureInfo.InvariantCulture)}")
                    .Get<string>();
                var source = DeserializeSlot<TaskSourceDescriptor>(serialized);
                if (source == null)
                {
                    source = ReadLegacySourceSlot(section, i);
                }

                if (source != null && !string.IsNullOrWhiteSpace(source.Id))
                {
                    settings.Sources.Add(source);
                }
            }
        }
        else
        {
            var sourceSections = ReadEntrySections(
                section,
                count: null,
                "SourceEntries",
                "Source",
                "SourceKey",
                nameof(TaskSourcesSettings.Sources));
            foreach (var sourceSection in sourceSections)
            {
                var source = ReadLegacySourceSection(sourceSection);
                if (source != null)
                {
                    settings.Sources.Add(source);
                }
            }
        }

        var declaredServerSettingsCount = section.GetSection("ServerSettingsCount").Get<int?>();
        int? serverSettingsCount = declaredServerSettingsCount.HasValue
            ? Math.Max(
                declaredServerSettingsCount.Value,
                GetPhysicalSlotCount(section, "ServerSettingEntry", "ServerSettingEntries"))
            : null;
        if (serverSettingsCount.HasValue)
        {
            for (var i = 0; i < serverSettingsCount.Value; i++)
            {
                var serialized = section.GetSection(
                        $"ServerSettingEntry{i.ToString(CultureInfo.InvariantCulture)}")
                    .Get<string>();
                var server = DeserializeSlot<TaskSourceServerSettings>(serialized);
                if (server == null)
                {
                    server = ReadLegacyServerSlot(section, i);
                }

                if (server != null && !string.IsNullOrWhiteSpace(server.SourceId))
                {
                    settings.ServerSettings.Add(server);
                }
            }
        }
        else
        {
            var serverSections = ReadEntrySections(
                section,
                count: null,
                "ServerSettingEntries",
                "Server",
                "ServerSettingsKey",
                nameof(TaskSourcesSettings.ServerSettings));
            foreach (var serverSection in serverSections)
            {
                var server = ReadLegacyServerSection(serverSection);
                if (server != null)
                {
                    settings.ServerSettings.Add(server);
                }
            }
        }

        var serializedProfiles = configuration.Get<string>(SyncProfilesSectionName);
        if (!string.IsNullOrWhiteSpace(serializedProfiles))
        {
            try
            {
                var profiles = JsonSerializer.Deserialize<Dictionary<string, GitSettings>>(serializedProfiles);
                if (profiles != null)
                {
                    settings.SyncSettings.AddRange(profiles
                        .Where(profile => !string.IsNullOrWhiteSpace(profile.Key))
                        .Select(profile => new TaskSourceSyncSettings
                        {
                            SourceId = profile.Key,
                            Git = CloneGitSettings(profile.Value)
                        }));
                    settings.LegacyProjection = ReadProjection(configuration);
                    return settings;
                }
            }
            catch (JsonException error)
            {
                throw new TaskSpaceCatalogException(
                    TaskSpaceCatalogIssue.InvalidSourceConfiguration,
                    [SyncProfilesSectionName],
                    "The persisted task-space synchronization profiles are corrupt.",
                    error);
            }
        }

        // Read the early map form as well, so preview builds that wrote it remain usable.
        var syncSection = configuration.GetSection(SyncProfilesSectionName);
        var syncCount = syncSection.GetSection("SyncSettingsCount").Get<int?>();
        var syncSections = ReadEntrySections(
            syncSection,
            syncCount,
            "SyncSettingEntries",
            "Sync",
            "SyncSettingsKey",
            nameof(TaskSourcesSettings.SyncSettings));
        foreach (var profileSection in syncSections)
        {
            var sourceId = profileSection.GetSection(nameof(TaskSourceSyncSettings.SourceId)).Get<string>();
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                continue;
            }

            settings.SyncSettings.Add(new TaskSourceSyncSettings
            {
                SourceId = sourceId,
                Git = profileSection.GetSection(nameof(TaskSourceSyncSettings.Git)).Get<GitSettings>() ?? new GitSettings()
            });
        }

        settings.LegacyProjection = ReadProjection(configuration);
        return settings;
    }

    private static T? DeserializeSlot<T>(string? serialized)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(serialized);
        }
        catch (JsonException ex)
        {
            throw new TaskSpaceCatalogException(
                TaskSpaceCatalogIssue.InvalidSourceConfiguration,
                [typeof(T).Name],
                $"The persisted task-space slot for '{typeof(T).Name}' is corrupt.",
                ex);
        }
    }

    private static TaskSourceDescriptor? ReadLegacySourceSlot(IConfiguration section, int index)
    {
        var key = section.GetSection($"SourceKey{index.ToString(CultureInfo.InvariantCulture)}").Get<string>();
        return string.IsNullOrWhiteSpace(key)
            ? null
            : ReadLegacySourceSection(
                section.GetSection("SourceEntries")
                    .GetSection($"Source{index.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static TaskSourceDescriptor? ReadLegacySourceSection(IConfiguration sourceSection)
    {
        var id = sourceSection.GetSection(nameof(TaskSourceDescriptor.Id)).Get<string>();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new TaskSourceDescriptor
        {
            Id = id,
            DisplayName = sourceSection.GetSection(nameof(TaskSourceDescriptor.DisplayName)).Get<string>() ?? string.Empty,
            Kind = Enum.TryParse<TaskSourceKind>(
                sourceSection.GetSection(nameof(TaskSourceDescriptor.Kind)).Get<string>(),
                ignoreCase: true,
                out var kind)
                ? kind
                : TaskSourceKind.File,
            Path = sourceSection.GetSection(nameof(TaskSourceDescriptor.Path)).Get<string>() ?? string.Empty,
            Url = sourceSection.GetSection(nameof(TaskSourceDescriptor.Url)).Get<string>() ?? string.Empty,
            IsEnabled = sourceSection.GetSection(nameof(TaskSourceDescriptor.IsEnabled)).Get<bool?>() ?? true
        };
    }

    private static TaskSourceServerSettings? ReadLegacyServerSlot(IConfiguration section, int index)
    {
        var key = section.GetSection($"ServerSettingsKey{index.ToString(CultureInfo.InvariantCulture)}").Get<string>();
        return string.IsNullOrWhiteSpace(key)
            ? null
            : ReadLegacyServerSection(
                section.GetSection("ServerSettingEntries")
                    .GetSection($"Server{index.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static TaskSourceServerSettings? ReadLegacyServerSection(IConfiguration serverSection)
    {
        var sourceId = serverSection.GetSection(nameof(TaskSourceServerSettings.SourceId)).Get<string>();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        return new TaskSourceServerSettings
        {
            SourceId = sourceId,
            Login = serverSection.GetSection(nameof(TaskSourceServerSettings.Login)).Get<string>() ?? string.Empty,
            Password = serverSection.GetSection(nameof(TaskSourceServerSettings.Password)).Get<string>() ?? string.Empty,
            AccessToken = serverSection.GetSection(nameof(TaskSourceServerSettings.AccessToken)).Get<string>() ?? string.Empty,
            RefreshToken = serverSection.GetSection(nameof(TaskSourceServerSettings.RefreshToken)).Get<string>() ?? string.Empty,
            ExpireTime = ReadDateTimeOffset(
                serverSection.GetSection(nameof(TaskSourceServerSettings.ExpireTime)).Get<string>()),
            UserId = serverSection.GetSection(nameof(TaskSourceServerSettings.UserId)).Get<string>() ?? string.Empty
        };
    }

    private static int ReadIndex(string key) =>
        int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;

    private static List<IConfigurationSection> ReadEntrySections(
        IConfiguration section,
        int? count,
        string entriesSectionName,
        string entryPrefix,
        string keyPrefix,
        string legacyArraySectionName)
    {
        if (count.HasValue)
        {
            return Enumerable
                .Range(0, count.Value)
                .Select(index =>
                {
                    var entryKey = $"{entryPrefix}{index.ToString(CultureInfo.InvariantCulture)}";
                    var configuredKey = section
                        .GetSection($"{keyPrefix}{index.ToString(CultureInfo.InvariantCulture)}")
                        .Get<string>();
                    return string.IsNullOrWhiteSpace(configuredKey)
                        ? null
                        : section.GetSection(entriesSectionName).GetSection(entryKey);
                })
                .Where(entry => entry != null)
                .Cast<IConfigurationSection>()
                .ToList();
        }

        var mapSections = section
            .GetSection(entriesSectionName)
            .GetChildren()
            .OrderBy(child => child.Key, StringComparer.Ordinal)
            .ToList();
        if (mapSections.Count > 0)
        {
            return mapSections;
        }

        return section
            .GetSection(legacyArraySectionName)
            .GetChildren()
            .OrderBy(child => ReadIndex(child.Key))
            .ToList();
    }

    private static ClientSettings ReadClientSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection("ClientSettings");
        return new ClientSettings
        {
            AccessToken = section.GetSection(nameof(ClientSettings.AccessToken)).Get<string>() ?? string.Empty,
            RefreshToken = section.GetSection(nameof(ClientSettings.RefreshToken)).Get<string>() ?? string.Empty,
            ExpireTime = ReadDateTimeOffset(section.GetSection(nameof(ClientSettings.ExpireTime)).Get<string>()),
            UserId = section.GetSection(nameof(ClientSettings.UserId)).Get<string>() ?? string.Empty,
            Login = section.GetSection(nameof(ClientSettings.Login)).Get<string>() ?? string.Empty
        };
    }

    private static void WriteClientSettings(IConfiguration configuration, ClientSettings clientSettings)
    {
        var section = configuration.GetSection("ClientSettings");
        section.GetSection(nameof(ClientSettings.AccessToken)).Set(clientSettings.AccessToken);
        section.GetSection(nameof(ClientSettings.RefreshToken)).Set(clientSettings.RefreshToken);
        section.GetSection(nameof(ClientSettings.ExpireTime)).Set(clientSettings.ExpireTime.ToString("O", CultureInfo.InvariantCulture));
        section.GetSection(nameof(ClientSettings.UserId)).Set(clientSettings.UserId);
        section.GetSection(nameof(ClientSettings.Login)).Set(clientSettings.Login);
    }

    private static DateTimeOffset ReadDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : default;
    }

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
}
