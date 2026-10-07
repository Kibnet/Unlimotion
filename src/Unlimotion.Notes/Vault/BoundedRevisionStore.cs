using System.Security.Cryptography;
using System.Text;

namespace Unlimotion.Notes.Vault;

public interface IRevisionStore
{
    Task SaveAsync(string vaultId, VaultDocument document, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAsync(string vaultId, string relativePath, CancellationToken cancellationToken = default);
}

public sealed class BoundedRevisionStore(string appLocalRoot, int retention = 20) : IRevisionStore
{
    public async Task SaveAsync(string vaultId, VaultDocument document, CancellationToken cancellationToken = default)
    {
        ValidateSegment(vaultId);
        if (retention <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        var pathKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.RelativePath))).ToLowerInvariant();
        var directory = Path.Combine(appLocalRoot, vaultId, "revisions", pathKey);
        Directory.CreateDirectory(directory);
        var revisionPath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{document.Revision}.md");
        await File.WriteAllTextAsync(revisionPath, document.Text, new UTF8Encoding(document.HasUtf8Bom), cancellationToken).ConfigureAwait(false);

        var old = Directory.EnumerateFiles(directory, "*.md")
            .OrderByDescending(static value => value, StringComparer.Ordinal)
            .Skip(retention)
            .ToArray();
        foreach (var file in old)
        {
            File.Delete(file);
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(string vaultId, string relativePath, CancellationToken cancellationToken = default)
    {
        ValidateSegment(vaultId);
        cancellationToken.ThrowIfCancellationRequested();
        var pathKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath))).ToLowerInvariant();
        var directory = Path.Combine(appLocalRoot, vaultId, "revisions", pathKey);
        IReadOnlyList<string> result = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md").OrderByDescending(static value => value, StringComparer.Ordinal).ToArray()
            : [];
        return Task.FromResult(result);
    }

    private static void ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("A vault identity must be one safe path segment.", nameof(value));
        }
    }
}
