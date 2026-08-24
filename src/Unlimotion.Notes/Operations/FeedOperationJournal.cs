using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;

namespace Unlimotion.Notes.Operations;

public enum FeedOperationKind
{
    NoteExtraction,
    MoveToToday
}

public enum FeedOperationState
{
    Pending,
    DestinationCreated,
    Completed
}

public enum FeedOperationRecoveryResolution
{
    None,
    CompletedReplacement,
    KeptBoth
}

public sealed record FeedOperationRecord(
    int SchemaVersion,
    string VaultId,
    string OperationId,
    FeedOperationKind Kind,
    FeedOperationState State,
    string SourcePath,
    string DestinationPath,
    string? DestinationRevision,
    string? SourceRevision,
    string? ResultId,
    DateTimeOffset UpdatedAt,
    FeedOperationRecoveryDescriptor? RecoveryDescriptor = null,
    string? RecoveryIssue = null,
    bool ReviewApplied = false,
    FeedOperationRecoveryResolution RecoveryResolution = FeedOperationRecoveryResolution.None);

public sealed record FeedOperationRecoveryDescriptor(
    string OriginalOperationId,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    string SelectionPayloadHash,
    string DestinationPayloadHash,
    string SourceOutputHash,
    string? ExpectedDestinationRevision = null,
    string? Folder = null,
    string? Title = null,
    IReadOnlyList<string>? AreaIds = null,
    DateOnly? DestinationDate = null,
    AreaReference? DestinationArea = null,
    string? ReviewSessionId = null,
    IReadOnlyList<BlockLocator>? InputLocators = null,
    IReadOnlyList<BlockLocator>? SourceOutputLocators = null,
    IReadOnlyList<BlockLocator>? DestinationOutputLocators = null);

public sealed class FeedOperationRecoveryConflictException(
    string vaultId,
    string operationId,
    string relativePath,
    string message) : InvalidOperationException(message)
{
    public string VaultId { get; } = vaultId;

    public string OperationId { get; } = operationId;

    public string RelativePath { get; } = relativePath;
}

public static class FeedOperationHash
{
    public static string Compute(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public interface IFeedOperationJournal
{
    Task<FeedOperationRecord?> LoadAsync(string vaultId, string operationId, CancellationToken cancellationToken = default);

    Task SaveAsync(FeedOperationRecord record, CancellationToken cancellationToken = default);

    async Task ResolveKeepBothAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(vaultId, operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The feed operation journal does not exist.");
        if (record.SchemaVersion < 2 || record.RecoveryDescriptor is null)
        {
            throw new InvalidOperationException("Legacy operations require manual recovery.");
        }

        if (record.State is not FeedOperationState.DestinationCreated and not FeedOperationState.Completed)
        {
            throw new InvalidOperationException("Both source and destination must exist before keeping both copies.");
        }

        await SaveAsync(
                record with
                {
                    State = FeedOperationState.Completed,
                    RecoveryIssue = null,
                    ReviewApplied = false,
                    RecoveryResolution = FeedOperationRecoveryResolution.KeptBoth,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    async Task MarkReviewAppliedAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(vaultId, operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The feed operation journal does not exist.");
        if (record.State != FeedOperationState.Completed)
        {
            throw new InvalidOperationException("Review cannot be marked as applied before the Markdown operation completes.");
        }

        if (record.ReviewApplied)
        {
            return;
        }

        await SaveAsync(
                record with
                {
                    ReviewApplied = true,
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    Task<IReadOnlyList<FeedOperationRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FeedOperationRecord>>([]);
}

public sealed class FileFeedOperationJournal(string appLocalRoot) : IFeedOperationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<FeedOperationRecord?> LoadAsync(string vaultId, string operationId, CancellationToken cancellationToken = default)
    {
        var path = Resolve(vaultId, operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<FeedOperationRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The feed operation journal is empty.");
    }

    public async Task<IReadOnlyList<FeedOperationRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(vaultId, nameof(vaultId));
        var directory = ResolveTransactionsDirectory(vaultId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var records = new List<FeedOperationRecord>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(static path => !path.EndsWith(".task.json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var record = await JsonSerializer.DeserializeAsync<FeedOperationRecord>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException($"The feed operation journal '{Path.GetFileName(path)}' is empty.");
            if (!string.Equals(record.VaultId, vaultId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The feed operation journal '{Path.GetFileName(path)}' belongs to another vault.");
            }

            if (IsPending(record))
            {
                records.Add(record);
            }
        }

        return records
            .OrderBy(static record => record.UpdatedAt)
            .ThenBy(static record => record.OperationId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task SaveAsync(FeedOperationRecord record, CancellationToken cancellationToken = default)
    {
        var path = Resolve(record.VaultId, record.OperationId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private string Resolve(string vaultId, string operationId)
    {
        ValidateId(vaultId, nameof(vaultId));
        ValidateId(operationId, nameof(operationId));
        return Path.Combine(ResolveTransactionsDirectory(vaultId), operationId + ".json");
    }

    private string ResolveTransactionsDirectory(string vaultId) =>
        Path.Combine(Path.GetFullPath(appLocalRoot), vaultId, "transactions");

    private static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Journal IDs must be safe single path segments.", parameterName);
        }
    }

    private static bool IsPending(FeedOperationRecord record) =>
        record.State != FeedOperationState.Completed
        || record.SchemaVersion >= 2 && !record.ReviewApplied;
}

public sealed class InMemoryFeedOperationJournal : IFeedOperationJournal
{
    private readonly Dictionary<string, FeedOperationRecord> records = new(StringComparer.Ordinal);

    public Task<FeedOperationRecord?> LoadAsync(string vaultId, string operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records.TryGetValue(vaultId + ":" + operationId, out var record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(FeedOperationRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records[record.VaultId + ":" + record.OperationId] = record;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FeedOperationRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FeedOperationRecord> result = records.Values
            .Where(record => string.Equals(record.VaultId, vaultId, StringComparison.Ordinal)
                && (record.State != FeedOperationState.Completed
                    || record.SchemaVersion >= 2 && !record.ReviewApplied))
            .OrderBy(static record => record.UpdatedAt)
            .ThenBy(static record => record.OperationId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(result);
    }
}
