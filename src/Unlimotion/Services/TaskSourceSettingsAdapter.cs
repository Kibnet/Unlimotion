using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public static class TaskSourceSettingsAdapter
{
    public static TaskSourcesSettings LoadOrCreate(IConfiguration configuration, string? defaultStoragePath)
    {
        var settings = Read(configuration);
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

        var activeSource = settings.Sources.First(source =>
            string.Equals(source.Id, settings.ActiveSourceId, StringComparison.Ordinal));
        if (activeSource.Kind == TaskSourceKind.Server)
        {
            EnsureServerSettings(settings, activeSource.Id, legacyStorage, configuration);
        }

        Save(configuration, settings);
        SyncLegacy(configuration, settings, activeSource);
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

    public static void Save(IConfiguration configuration, TaskSourcesSettings settings)
    {
        var section = configuration.GetSection(TaskSourcesSettings.SectionName);
        section.GetSection(nameof(TaskSourcesSettings.ActiveSourceId)).Set(settings.ActiveSourceId);
        section.GetSection("SourcesCount").Set(settings.Sources.Count);
        section.GetSection("ServerSettingsCount").Set(settings.ServerSettings.Count);

        for (var i = 0; i < settings.Sources.Count; i++)
        {
            var source = settings.Sources[i];
            var entryKey = $"Source{i.ToString(CultureInfo.InvariantCulture)}";
            section.GetSection($"SourceKey{i.ToString(CultureInfo.InvariantCulture)}").Set(source.Id);
            var sourceSection = section.GetSection("SourceEntries").GetSection(entryKey);
            sourceSection.GetSection(nameof(TaskSourceDescriptor.Id)).Set(source.Id);
            sourceSection.GetSection(nameof(TaskSourceDescriptor.DisplayName)).Set(source.DisplayName);
            sourceSection.GetSection(nameof(TaskSourceDescriptor.Kind)).Set(source.Kind.ToString());
            sourceSection.GetSection(nameof(TaskSourceDescriptor.Path)).Set(source.Path);
            sourceSection.GetSection(nameof(TaskSourceDescriptor.Url)).Set(source.Url);
            sourceSection.GetSection(nameof(TaskSourceDescriptor.IsEnabled)).Set(source.IsEnabled);
        }

        for (var i = 0; i < settings.ServerSettings.Count; i++)
        {
            var server = settings.ServerSettings[i];
            var entryKey = $"Server{i.ToString(CultureInfo.InvariantCulture)}";
            section.GetSection($"ServerSettingsKey{i.ToString(CultureInfo.InvariantCulture)}").Set(server.SourceId);
            var serverSection = section.GetSection("ServerSettingEntries").GetSection(entryKey);
            serverSection.GetSection(nameof(TaskSourceServerSettings.SourceId)).Set(server.SourceId);
            serverSection.GetSection(nameof(TaskSourceServerSettings.Login)).Set(server.Login);
            serverSection.GetSection(nameof(TaskSourceServerSettings.Password)).Set(server.Password);
            serverSection.GetSection(nameof(TaskSourceServerSettings.AccessToken)).Set(server.AccessToken);
            serverSection.GetSection(nameof(TaskSourceServerSettings.RefreshToken)).Set(server.RefreshToken);
            serverSection.GetSection(nameof(TaskSourceServerSettings.ExpireTime)).Set(server.ExpireTime.ToString("O", CultureInfo.InvariantCulture));
            serverSection.GetSection(nameof(TaskSourceServerSettings.UserId)).Set(server.UserId);
        }
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
        if (string.Equals(serverSettings.SourceId, TaskSourceDescriptor.DefaultSourceId, StringComparison.Ordinal))
        {
            WriteClientSettings(configuration, ToClientSettings(serverSettings));
        }
    }

    public static void SyncLegacy(
        IConfiguration configuration,
        TaskSourcesSettings settings,
        TaskSourceDescriptor activeSource)
    {
        if (!string.Equals(activeSource.Id, TaskSourceDescriptor.DefaultSourceId, StringComparison.Ordinal))
        {
            return;
        }

        var legacyStorage = configuration.Get<TaskStorageSettings>("TaskStorage") ?? new TaskStorageSettings();
        legacyStorage.IsServerMode = activeSource.Kind == TaskSourceKind.Server;
        legacyStorage.Path = activeSource.Path;
        legacyStorage.URL = activeSource.Url;

        var serverSettings = settings.ServerSettings.FirstOrDefault(server =>
            string.Equals(server.SourceId, activeSource.Id, StringComparison.Ordinal));
        if (serverSettings != null)
        {
            legacyStorage.Login = serverSettings.Login;
            legacyStorage.Password = serverSettings.Password;
            WriteClientSettings(configuration, ToClientSettings(serverSettings));
        }

        configuration.Set("TaskStorage", legacyStorage);
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

        var sourceCount = section.GetSection("SourcesCount").Get<int?>();
        var sourceSections = ReadEntrySections(
            section,
            sourceCount,
            "SourceEntries",
            "Source",
            "SourceKey",
            nameof(TaskSourcesSettings.Sources));
        foreach (var sourceSection in sourceSections)
        {
            var id = sourceSection.GetSection(nameof(TaskSourceDescriptor.Id)).Get<string>();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            settings.Sources.Add(new TaskSourceDescriptor
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
            });
        }

        var serverSettingsCount = section.GetSection("ServerSettingsCount").Get<int?>();
        var serverSections = ReadEntrySections(
            section,
            serverSettingsCount,
            "ServerSettingEntries",
            "Server",
            "ServerSettingsKey",
            nameof(TaskSourcesSettings.ServerSettings));
        foreach (var serverSection in serverSections)
        {
            var sourceId = serverSection.GetSection(nameof(TaskSourceServerSettings.SourceId)).Get<string>();
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                continue;
            }

            settings.ServerSettings.Add(new TaskSourceServerSettings
            {
                SourceId = sourceId,
                Login = serverSection.GetSection(nameof(TaskSourceServerSettings.Login)).Get<string>() ?? string.Empty,
                Password = serverSection.GetSection(nameof(TaskSourceServerSettings.Password)).Get<string>() ?? string.Empty,
                AccessToken = serverSection.GetSection(nameof(TaskSourceServerSettings.AccessToken)).Get<string>() ?? string.Empty,
                RefreshToken = serverSection.GetSection(nameof(TaskSourceServerSettings.RefreshToken)).Get<string>() ?? string.Empty,
                ExpireTime = ReadDateTimeOffset(serverSection.GetSection(nameof(TaskSourceServerSettings.ExpireTime)).Get<string>()),
                UserId = serverSection.GetSection(nameof(TaskSourceServerSettings.UserId)).Get<string>() ?? string.Empty
            });
        }

        return settings;
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
}
