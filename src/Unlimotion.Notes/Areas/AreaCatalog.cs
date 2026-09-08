using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Areas;

public sealed class AreaDefinition
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public string? ParentId { get; set; }

    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }

    public string? DefaultNoteFolder { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class AreaCatalog
{
    public int SchemaVersion { get; set; } = 1;

    public List<AreaDefinition> Areas { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported area catalog schema version '{SchemaVersion}'.");
        }

        var byId = new Dictionary<string, AreaDefinition>(StringComparer.Ordinal);
        foreach (var area in Areas)
        {
            if (string.IsNullOrWhiteSpace(area.Id) || string.IsNullOrWhiteSpace(area.Name))
            {
                throw new InvalidDataException("Every area requires a stable ID and a non-empty name.");
            }

            if (area.Id.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            {
                throw new InvalidDataException($"Area ID '{area.Id}' contains unsafe characters.");
            }

            if (area.Name.Any(char.IsControl))
            {
                throw new InvalidDataException($"Area '{area.Id}' name contains control characters.");
            }

            if (!byId.TryAdd(area.Id, area))
            {
                throw new InvalidDataException($"Duplicate area ID '{area.Id}'.");
            }

            if (string.Equals(area.Id, area.ParentId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Area '{area.Id}' cannot be its own parent.");
            }

            ValidateFolder(area.DefaultNoteFolder);
        }

        foreach (var area in Areas)
        {
            if (area.ParentId is not null && !byId.ContainsKey(area.ParentId))
            {
                throw new InvalidDataException($"Area '{area.Id}' references missing parent '{area.ParentId}'.");
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in Areas)
        {
            Visit(area);
        }

        void Visit(AreaDefinition area)
        {
            if (visited.Contains(area.Id))
            {
                return;
            }

            if (!visiting.Add(area.Id))
            {
                throw new InvalidDataException($"Area hierarchy contains a cycle at '{area.Id}'.");
            }

            if (area.ParentId is not null)
            {
                Visit(byId[area.ParentId]);
            }

            visiting.Remove(area.Id);
            visited.Add(area.Id);
        }
    }

    private static void ValidateFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (Path.IsPathRooted(folder)
            || folder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(static part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException("Default note folders must stay inside the vault.");
        }
    }
}

public sealed record AreaCatalogSnapshot(AreaCatalog Catalog, string? Revision);

public sealed class AreaCatalogStore(INoteVault vault)
{
    public const string RelativePath = ".unlimotion/areas.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AreaCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await vault.ReadAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return new AreaCatalogSnapshot(new AreaCatalog(), null);
        }

        var catalog = JsonSerializer.Deserialize<AreaCatalog>(document.Text, JsonOptions)
            ?? throw new InvalidDataException("The area catalog is empty.");
        catalog.Validate();
        return new AreaCatalogSnapshot(catalog, document.Revision);
    }

    public async Task<AreaCatalogSnapshot> SaveAsync(
        AreaCatalog catalog,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        var json = JsonSerializer.Serialize(catalog, JsonOptions) + "\n";
        var result = await vault.WriteAsync(RelativePath, json, expectedRevision, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new AreaCatalogSnapshot(catalog, result.Revision);
    }
}
