using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

/// <summary>
/// Separate from the task-source catalog so its cached legacy projection cannot overwrite area defaults.
/// Identifiers are captured by the caller; a space switch never redirects an in-flight save.
/// </summary>
public sealed class AreaRootTaskSettingsStore(IConfiguration configuration)
{
    public const string SectionName = "TaskSourceAreaDefaults";
    private static readonly ConditionalWeakTable<IConfiguration, object> ConfigurationLocks = new();
    private readonly object _sync = ConfigurationLocks.GetValue(configuration, _ => new object());

    public string? GetRootTaskId(string sourceId, string vaultId, string areaId)
    {
        ValidateIdentity(sourceId, vaultId, areaId);
        lock (_sync)
        {
            var root = Read();
            var source = GetObject(root, sourceId, create: false);
            var vault = source is null ? null : GetObject(source, vaultId, create: false);
            var area = vault is null ? null : GetObject(vault, areaId, create: false);
            if (area?["RootTaskId"] is not { } taskId) return null;
            if (taskId is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id))
                return id;
            throw new InvalidDataException(L10n.Get("AreaRootTaskSettingsInvalid"));
        }
    }

    public void SetRootTaskId(string sourceId, string vaultId, string areaId, string? rootTaskId)
    {
        ValidateIdentity(sourceId, vaultId, areaId);
        lock (_sync)
        {
            var root = Read();
            var source = GetObject(root, sourceId, create: true)!;
            var vault = GetObject(source, vaultId, create: true)!;
            var area = GetObject(vault, areaId, create: true)!;
            if (string.IsNullOrWhiteSpace(rootTaskId)) area.Remove("RootTaskId");
            else area["RootTaskId"] = rootTaskId;
            configuration.Set(SectionName, root.ToJsonString());
        }
    }

    private JsonObject Read()
    {
        var serialized = configuration.Get<string>(SectionName);
        if (string.IsNullOrWhiteSpace(serialized)) return new JsonObject();
        try
        {
            return JsonNode.Parse(serialized) as JsonObject
                ?? throw new InvalidDataException(L10n.Get("AreaRootTaskSettingsInvalid"));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(L10n.Get("AreaRootTaskSettingsInvalid"), error);
        }
    }

    private static JsonObject? GetObject(JsonObject parent, string key, bool create)
    {
        if (parent.TryGetPropertyValue(key, out var node))
            return node as JsonObject ?? throw new InvalidDataException(L10n.Get("AreaRootTaskSettingsInvalid"));
        if (!create) return null;
        var child = new JsonObject();
        parent[key] = child;
        return child;
    }

    private static void ValidateIdentity(string sourceId, string vaultId, string areaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
    }
}
