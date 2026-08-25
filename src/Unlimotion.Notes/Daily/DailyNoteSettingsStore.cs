using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Daily;

/// <summary>
/// Portable settings that describe the active daily filename convention of one vault.
/// </summary>
public sealed record DailyNoteSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string DailyFileNameFormat { get; init; } = DailyNoteNaming.DefaultFileNameFormat;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    public DailyNoteNaming CreateNaming()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported daily note settings schema version '{SchemaVersion}'.");
        }

        if (!DailyNoteNaming.TryCreate(DailyFileNameFormat, out var naming, out var validationError))
        {
            throw new InvalidDataException($"The daily note filename format is invalid: {validationError}");
        }

        return naming;
    }

    public void Validate() => _ = CreateNaming();
}

public sealed record DailyNoteSettingsSnapshot(DailyNoteSettings Settings, string? Revision)
{
    public DailyNoteNaming Naming => Settings.CreateNaming();
}

/// <summary>
/// Reads and writes the optional, vault-local daily filename sidecar.
/// </summary>
public sealed class DailyNoteSettingsStore(INoteVault vault)
{
    public const string RelativePath = ".unlimotion/daily-note-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<DailyNoteSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await vault.ReadAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return new DailyNoteSettingsSnapshot(new DailyNoteSettings(), null);
        }

        return new DailyNoteSettingsSnapshot(Parse(document.Text), document.Revision);
    }

    public async Task<DailyNoteSettingsSnapshot> SaveAsync(
        DailyNoteSettings settings,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var text = JsonSerializer.Serialize(settings, JsonOptions) + "\n";
        var write = await vault.WriteAsync(RelativePath, text, expectedRevision, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new DailyNoteSettingsSnapshot(settings, write.Revision);
    }

    private static DailyNoteSettings Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The daily note settings sidecar must contain a JSON object.");
            }

            RequireProperty(document.RootElement, "schemaVersion");
            RequireProperty(document.RootElement, "dailyFileNameFormat");
            var settings = JsonSerializer.Deserialize<DailyNoteSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("The daily note settings sidecar is empty.");
            settings.Validate();
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The daily note settings sidecar is not valid JSON.", exception);
        }
    }

    private static void RequireProperty(JsonElement element, string name)
    {
        if (!element.EnumerateObject().Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"The daily note settings sidecar is missing '{name}'.");
        }
    }
}
