using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Unlimotion.Notes.Recovery;

public sealed record FeedDraft(
    int SchemaVersion,
    string VaultId,
    string RelativePath,
    int BlockIndex,
    string BaseRevision,
    string RawMarkdown,
    DateTimeOffset UpdatedAt,
    string? EditorDocumentText = null,
    bool HasUtf8Bom = false);

public interface IFeedDraftStore
{
    Task SaveAsync(FeedDraft draft, CancellationToken cancellationToken = default);

    Task<FeedDraft?> LoadAsync(string vaultId, string relativePath, int blockIndex, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeedDraft>> ListAsync(string vaultId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string vaultId, string relativePath, int blockIndex, CancellationToken cancellationToken = default);
}

public sealed class FileFeedDraftStore(string appLocalRoot) : IFeedDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Recovery artifacts are deliberately human-inspectable. Allow every
        // Unicode range while retaining the encoder's JSON/HTML-sensitive
        // character escaping instead of using UnsafeRelaxedJsonEscaping.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    internal static JsonSerializerOptions JsonOptionsForRecovery => JsonOptions;

    public async Task SaveAsync(FeedDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported feed draft schema.", nameof(draft));
        }

        var path = Resolve(draft.VaultId, draft.RelativePath, draft.BlockIndex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicJsonWriteAsync(path, draft, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeedDraft?> LoadAsync(
        string vaultId,
        string relativePath,
        int blockIndex,
        CancellationToken cancellationToken = default)
    {
        var path = Resolve(vaultId, relativePath, blockIndex);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var draft = await JsonSerializer.DeserializeAsync<FeedDraft>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The recovery draft is empty.");
        if (draft.SchemaVersion != 1
            || !string.Equals(draft.VaultId, vaultId, StringComparison.Ordinal)
            || !string.Equals(NormalizePath(draft.RelativePath), NormalizePath(relativePath), StringComparison.Ordinal)
            || draft.BlockIndex != blockIndex)
        {
            throw new InvalidDataException("The recovery draft identity does not match its storage key.");
        }

        return draft;
    }

    public async Task<IReadOnlyList<FeedDraft>> ListAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveDraftDirectory(vaultId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<FeedDraft>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(static value => !value.EndsWith(".resolved.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var draft = await JsonSerializer.DeserializeAsync<FeedDraft>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException($"The recovery draft '{path}' is empty.");
            ValidateStoredDraft(draft, vaultId);
            if (!string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(Resolve(draft.VaultId, draft.RelativePath, draft.BlockIndex)),
                    PathComparison))
            {
                throw new InvalidDataException($"The recovery draft '{path}' does not match its storage key.");
            }

            result.Add(draft);
        }

        return result
            .OrderByDescending(static draft => draft.UpdatedAt)
            .ThenBy(static draft => draft.RelativePath, PathComparer)
            .ThenBy(static draft => draft.BlockIndex)
            .ToArray();
    }

    public Task DeleteAsync(string vaultId, string relativePath, int blockIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(vaultId, relativePath, blockIndex);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string vaultId, string relativePath, int blockIndex)
    {
        ValidateId(vaultId);
        if (blockIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockIndex));
        }

        var key = Hash(NormalizePath(relativePath) + ":" + blockIndex);
        return Path.Combine(ResolveDraftDirectory(vaultId), key + ".json");
    }

    internal static async Task AtomicJsonWriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken,
        bool overwrite = true)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private string ResolveDraftDirectory(string vaultId)
    {
        ValidateId(vaultId);
        return Path.Combine(Path.GetFullPath(appLocalRoot), vaultId, "drafts");
    }

    private static void ValidateStoredDraft(FeedDraft draft, string expectedVaultId)
    {
        if (draft.SchemaVersion != 1
            || !string.Equals(draft.VaultId, expectedVaultId, StringComparison.Ordinal)
            || draft.BlockIndex < 0)
        {
            throw new InvalidDataException("The recovery draft identity does not match its storage key.");
        }

        _ = NormalizePath(draft.RelativePath);
    }

    internal static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ArgumentException("Draft paths must be relative vault paths.", nameof(value));
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or ".."))
        {
            throw new ArgumentException("Draft paths cannot escape the vault.", nameof(value));
        }

        return normalized;
    }

    private static void ValidateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Vault IDs must be safe path segments.", nameof(value));
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public sealed record DocumentConflictBundle(
    int SchemaVersion,
    string VaultId,
    string ConflictId,
    string EditorRelativePath,
    string DiskRelativePath,
    int BlockIndex,
    string? BaseRevision,
    string EditorRevision,
    string EditorMarkdown,
    bool EditorHasUtf8Bom,
    string? DiskRevision,
    string? DiskMarkdown,
    bool DiskHasUtf8Bom,
    DateTimeOffset CreatedAt);

public interface IDocumentConflictStore
{
    Task<string> PreserveAsync(DocumentConflictBundle conflict, CancellationToken cancellationToken = default);

    Task<DocumentConflictBundle?> LoadAsync(string vaultId, string conflictId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentConflictBundle>> ListAsync(string vaultId, CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(
        string vaultId,
        string conflictId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<IReadOnlyList<DocumentConflictBundle>> ListUnresolvedAsync(
        string vaultId,
        CancellationToken cancellationToken = default) => ListAsync(vaultId, cancellationToken);
}

public sealed class FileDocumentConflictStore(string appLocalRoot) : IDocumentConflictStore
{
    public async Task<string> PreserveAsync(DocumentConflictBundle conflict, CancellationToken cancellationToken = default)
    {
        ValidateBundle(conflict);

        var directory = ResolveConflictDirectory(conflict.VaultId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SafeSegment(conflict.ConflictId) + ".json");
        if (File.Exists(path))
        {
            var existing = await LoadAsync(conflict.VaultId, conflict.ConflictId, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The existing immutable conflict bundle could not be read.");
            if (SamePreservedVersions(existing, conflict))
            {
                return path;
            }

            throw new IOException("Conflict bundles are immutable and cannot be overwritten with different data.");
        }

        try
        {
            await FileFeedDraftStore.AtomicJsonWriteAsync(path, conflict, cancellationToken, overwrite: false)
                .ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await LoadAsync(conflict.VaultId, conflict.ConflictId, cancellationToken).ConfigureAwait(false);
            if (existing is null || !SamePreservedVersions(existing, conflict))
            {
                throw new IOException("Conflict bundles are immutable and cannot be overwritten with different data.");
            }
        }

        return path;
    }

    public async Task<DocumentConflictBundle?> LoadAsync(
        string vaultId,
        string conflictId,
        CancellationToken cancellationToken = default)
    {
        var path = Resolve(vaultId, conflictId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var conflict = await JsonSerializer.DeserializeAsync<DocumentConflictBundle>(
                stream,
                FileFeedDraftStore.JsonOptionsForRecovery,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The document conflict bundle is empty.");
        ValidateStoredBundle(conflict, vaultId, conflictId);
        return conflict;
    }

    public async Task<IReadOnlyList<DocumentConflictBundle>> ListAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveConflictDirectory(vaultId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<DocumentConflictBundle>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(static path => !path.EndsWith(".resolved.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conflictId = Path.GetFileNameWithoutExtension(path);
            var conflict = await LoadAsync(vaultId, conflictId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"The document conflict bundle '{path}' disappeared while it was being listed.");
            result.Add(conflict);
        }

        return result
            .OrderByDescending(static conflict => conflict.CreatedAt)
            .ThenBy(static conflict => conflict.ConflictId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task AcknowledgeAsync(
        string vaultId,
        string conflictId,
        CancellationToken cancellationToken = default)
    {
        var conflictPath = Resolve(vaultId, conflictId);
        if (!File.Exists(conflictPath))
        {
            throw new FileNotFoundException("The conflict bundle cannot be acknowledged because it is missing.", conflictPath);
        }

        var markerPath = ResolveAcknowledgement(vaultId, conflictId);
        if (File.Exists(markerPath))
        {
            return;
        }

        await FileFeedDraftStore.AtomicJsonWriteAsync(
                markerPath,
                new DocumentConflictAcknowledgement(1, vaultId, conflictId, DateTimeOffset.UtcNow),
                cancellationToken,
                overwrite: false)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentConflictBundle>> ListUnresolvedAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(vaultId, cancellationToken).ConfigureAwait(false);
        return all.Where(value => !File.Exists(ResolveAcknowledgement(vaultId, value.ConflictId)))
            .ToArray();
    }

    private string Resolve(string vaultId, string conflictId) =>
        Path.Combine(ResolveConflictDirectory(vaultId), SafeSegment(conflictId) + ".json");

    private string ResolveAcknowledgement(string vaultId, string conflictId) =>
        Path.Combine(ResolveConflictDirectory(vaultId), SafeSegment(conflictId) + ".resolved.json");

    private string ResolveConflictDirectory(string vaultId) =>
        Path.Combine(Path.GetFullPath(appLocalRoot), SafeSegment(vaultId), "conflicts");

    private static void ValidateBundle(DocumentConflictBundle conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (conflict.SchemaVersion != 1
            || conflict.BlockIndex < 0
            || string.IsNullOrWhiteSpace(conflict.EditorRelativePath)
            || string.IsNullOrWhiteSpace(conflict.DiskRelativePath)
            || string.IsNullOrWhiteSpace(conflict.EditorRevision)
            || conflict.EditorMarkdown is null)
        {
            throw new ArgumentException("Invalid document conflict bundle.", nameof(conflict));
        }

        _ = SafeSegment(conflict.VaultId);
        _ = SafeSegment(conflict.ConflictId);
        _ = FileFeedDraftStore.NormalizePath(conflict.EditorRelativePath);
        _ = FileFeedDraftStore.NormalizePath(conflict.DiskRelativePath);
    }

    private sealed record DocumentConflictAcknowledgement(
        int SchemaVersion,
        string VaultId,
        string ConflictId,
        DateTimeOffset ResolvedAt);

    private static void ValidateStoredBundle(
        DocumentConflictBundle conflict,
        string expectedVaultId,
        string expectedConflictId)
    {
        try
        {
            ValidateBundle(conflict);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The stored document conflict bundle is invalid.", exception);
        }

        if (!string.Equals(conflict.VaultId, expectedVaultId, StringComparison.Ordinal)
            || !string.Equals(conflict.ConflictId, expectedConflictId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The document conflict bundle identity does not match its storage key.");
        }
    }

    private static bool SamePreservedVersions(DocumentConflictBundle left, DocumentConflictBundle right) =>
        left with { CreatedAt = default } == right with { CreatedAt = default };

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Conflict IDs must be safe path segments.", nameof(value));
        }

        return value;
    }
}
