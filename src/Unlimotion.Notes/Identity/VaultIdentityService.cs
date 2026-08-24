using System.Text.Json;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Identity;

public sealed record VaultIdentityManifest(int SchemaVersion, string VaultId);

public sealed class VaultIdentityService(INoteVault vault)
{
    public const string ManifestPath = ".unlimotion/vault.json";

    public async Task<VaultIdentityManifest> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await vault.ReadAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Parse(existing.Text);
        }

        var created = new VaultIdentityManifest(1, Guid.NewGuid().ToString("N"));
        var json = Serialize(created);
        try
        {
            await vault.CreateAsync(ManifestPath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (VaultRevisionConflictException)
        {
            var winner = await vault.ReadAsync(ManifestPath, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The vault identity appeared concurrently but could not be read.");
            return Parse(winner.Text);
        }
    }

    public static VaultIdentityManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<VaultIdentityManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The vault identity manifest is empty.");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.VaultId))
        {
            throw new InvalidDataException("Unsupported or invalid vault identity manifest.");
        }

        return manifest;
    }

    private static string Serialize(VaultIdentityManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions) + "\n";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class VaultRootRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<string, (string RootPath, int Attachments)> rootsByVaultId = new(StringComparer.Ordinal);

    public void Attach(string vaultId, string rootPath)
    {
        var canonical = Path.GetFullPath(rootPath);
        lock (sync)
        {
            if (rootsByVaultId.TryGetValue(vaultId, out var existing)
                && !string.Equals(existing.RootPath, canonical, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Vault '{vaultId}' is already attached at another local root.");
            }

            rootsByVaultId[vaultId] = existing == default
                ? (canonical, 1)
                : (existing.RootPath, existing.Attachments + 1);
        }
    }

    public void Detach(string vaultId, string rootPath)
    {
        lock (sync)
        {
            if (rootsByVaultId.TryGetValue(vaultId, out var existing)
                && string.Equals(existing.RootPath, Path.GetFullPath(rootPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                if (existing.Attachments <= 1)
                {
                    rootsByVaultId.Remove(vaultId);
                }
                else
                {
                    rootsByVaultId[vaultId] = (existing.RootPath, existing.Attachments - 1);
                }
            }
        }
    }
}
