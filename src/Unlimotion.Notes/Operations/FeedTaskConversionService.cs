using System.Text.Json;
using System.Text.RegularExpressions;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record FeedTaskDraft(
    string TaskId,
    string OperationId,
    string Title,
    string Description,
    bool IsGoal,
    IReadOnlyList<string> AreaIds);

public sealed record FeedCreatedTask(string TaskId, string Title);

/// <summary>
/// Persists or resolves the task with the supplied stable ID. Implementations must be idempotent by
/// <see cref="FeedTaskDraft.TaskId"/> so a crash immediately after persistence cannot create a duplicate.
/// </summary>
public interface IFeedTaskCreationTarget
{
    bool SupportsClassification => true;

    Task<FeedCreatedTask> CreateOrGetAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default);
}

public enum FeedTaskConversionState
{
    Pending,
    TaskCreated,
    Completed
}

public sealed record FeedTaskConversionRecord(
    int SchemaVersion,
    string VaultId,
    string OperationId,
    FeedTaskConversionState State,
    string SourcePath,
    string ExpectedSourceRevision,
    string TaskId,
    string? SourceRevision,
    DateTimeOffset UpdatedAt,
    FeedTaskConversionRecoveryDescriptor? RecoveryDescriptor = null,
    string? RecoveryIssue = null,
    bool ReviewApplied = false,
    FeedOperationRecoveryResolution RecoveryResolution = FeedOperationRecoveryResolution.None);

public sealed record FeedTaskConversionRecoveryDescriptor(
    string OriginalOperationId,
    MarkdownBlockSelection Selection,
    string SelectionPayloadHash,
    string? SourceOutputHash,
    string Title,
    string Description,
    bool IsGoal,
    IReadOnlyList<string> AreaIds,
    string? ReviewSessionId = null,
    IReadOnlyList<BlockLocator>? InputLocators = null,
    IReadOnlyList<BlockLocator>? SourceOutputLocators = null);

public interface IFeedTaskConversionJournal
{
    Task<FeedTaskConversionRecord?> LoadAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(FeedTaskConversionRecord record, CancellationToken cancellationToken = default);

    async Task ResolveKeepBothAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(vaultId, operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The task conversion journal does not exist.");
        if (record.SchemaVersion < 2 || record.RecoveryDescriptor is null)
        {
            throw new InvalidOperationException("Legacy task conversions require manual recovery.");
        }

        if (record.State is not FeedTaskConversionState.TaskCreated and not FeedTaskConversionState.Completed)
        {
            throw new InvalidOperationException("The task must exist before keeping both copies.");
        }

        await SaveAsync(
                record with
                {
                    State = FeedTaskConversionState.Completed,
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
            ?? throw new InvalidDataException("The task conversion journal does not exist.");
        if (record.State != FeedTaskConversionState.Completed)
        {
            throw new InvalidOperationException("Review cannot be marked as applied before the task conversion completes.");
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

    Task<IReadOnlyList<FeedTaskConversionRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FeedTaskConversionRecord>>([]);
}

public sealed class FileFeedTaskConversionJournal(string appLocalRoot) : IFeedTaskConversionJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<FeedTaskConversionRecord?> LoadAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var path = Resolve(vaultId, operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<FeedTaskConversionRecord>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("The task conversion journal is empty.");
    }

    public async Task SaveAsync(FeedTaskConversionRecord record, CancellationToken cancellationToken = default)
    {
        var path = Resolve(record.VaultId, record.OperationId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<IReadOnlyList<FeedTaskConversionRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        FeedLinkSerializer.ValidateStableId(vaultId, nameof(vaultId));
        var directory = ResolveTransactionsDirectory(vaultId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var records = new List<FeedTaskConversionRecord>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.task.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var record = await JsonSerializer.DeserializeAsync<FeedTaskConversionRecord>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException($"The task conversion journal '{Path.GetFileName(path)}' is empty.");
            if (!string.Equals(record.VaultId, vaultId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The task conversion journal '{Path.GetFileName(path)}' belongs to another vault.");
            }

            if (record.State != FeedTaskConversionState.Completed
                || record.SchemaVersion >= 2 && !record.ReviewApplied)
            {
                records.Add(record);
            }
        }

        return records
            .OrderBy(static record => record.UpdatedAt)
            .ThenBy(static record => record.OperationId, StringComparer.Ordinal)
            .ToArray();
    }

    private string Resolve(string vaultId, string operationId)
    {
        FeedLinkSerializer.ValidateStableId(vaultId, nameof(vaultId));
        FeedLinkSerializer.ValidateStableId(operationId, nameof(operationId));
        return Path.Combine(ResolveTransactionsDirectory(vaultId), operationId + ".task.json");
    }

    private string ResolveTransactionsDirectory(string vaultId) =>
        Path.Combine(Path.GetFullPath(appLocalRoot), vaultId, "transactions");
}

public sealed class InMemoryFeedTaskConversionJournal : IFeedTaskConversionJournal
{
    private readonly Dictionary<string, FeedTaskConversionRecord> records = new(StringComparer.Ordinal);

    public Task<FeedTaskConversionRecord?> LoadAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records.TryGetValue(vaultId + ":" + operationId, out var record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(FeedTaskConversionRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records[record.VaultId + ":" + record.OperationId] = record;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FeedTaskConversionRecord>> ListPendingAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FeedTaskConversionRecord> result = records.Values
            .Where(record => string.Equals(record.VaultId, vaultId, StringComparison.Ordinal)
                && (record.State != FeedTaskConversionState.Completed
                    || record.SchemaVersion >= 2 && !record.ReviewApplied))
            .OrderBy(static record => record.UpdatedAt)
            .ThenBy(static record => record.OperationId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(result);
    }
}

public sealed record FeedTaskConversionRequest(
    string VaultId,
    string OperationId,
    string SourcePath,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    IReadOnlyList<string> AreaIds,
    bool IsGoal = false,
    string? ReviewSessionId = null);

public sealed record FeedTaskConversionResult(
    string TaskId,
    string Title,
    string SourceRevision,
    bool WasAlreadyCompleted);

public sealed partial class FeedTaskConversionService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations,
    IFeedTaskCreationTarget taskTarget,
    IFeedTaskConversionJournal journal,
    IRevisionStore? revisions = null)
{
    [GeneratedRegex(@"^\s*(?:[-*+]\s+)?(?:\[[ xX]\]\s*)?")]
    private static partial Regex LeadingTaskMarkerRegex();

    public async Task<FeedTaskConversionResult> ConvertAsync(
        FeedTaskConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var taskId = "feed-" + request.OperationId;
        FeedLinkSerializer.ValidateStableId(taskId, nameof(taskId));

        var operation = await journal.LoadAsync(request.VaultId, request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        ValidateExistingOperation(operation, request, taskId);
        if (operation?.State == FeedTaskConversionState.Completed)
        {
            var completedSource = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The converted task source is missing.");
            if (operation.RecoveryDescriptor?.SourceOutputHash is { Length: > 0 } completedSourceHash
                && !string.Equals(
                    FeedOperationHash.Compute(completedSource.Text),
                    completedSourceHash,
                    StringComparison.Ordinal))
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    request.SourcePath,
                    "The completed task conversion source drifted before its review decision was checkpointed.",
                    cancellationToken).ConfigureAwait(false);
            }

            EnsureSourceReferencesTask(completedSource.Text, taskId);
            return new FeedTaskConversionResult(taskId, ReadFallbackTitle(completedSource.Text, taskId), completedSource.Revision, true);
        }

        if (operation?.State == FeedTaskConversionState.TaskCreated)
        {
            var sourceAfterPossibleCommit = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
            if (operation.RecoveryDescriptor?.SourceOutputHash is { Length: > 0 } storedSourceOutputHash
                && string.Equals(FeedOperationHash.Compute(sourceAfterPossibleCommit.Text), storedSourceOutputHash, StringComparison.Ordinal))
            {
                var recovered = operation with
                {
                    State = FeedTaskConversionState.Completed,
                    SourceRevision = sourceAfterPossibleCommit.Revision,
                    ReviewApplied = ReviewDoesNotRequireDecision(request.ReviewSessionId),
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await journal.SaveAsync(recovered, cancellationToken).ConfigureAwait(false);
                return new FeedTaskConversionResult(
                    taskId,
                    ReadFallbackTitle(sourceAfterPossibleCommit.Text, taskId),
                    sourceAfterPossibleCommit.Revision,
                    true);
            }

            if (SourceReferencesTask(sourceAfterPossibleCommit.Text, taskId))
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    request.SourcePath,
                    "The task link exists, but the source changed after the partial conversion.",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var source = await RequireSourceAsync(request.SourcePath, request.ExpectedSourceRevision, cancellationToken)
            .ConfigureAwait(false);
        var document = parser.Parse(source.Text);
        var selected = request.Selection.Resolve(document);
        var (title, description) = ParseTaskContent(selected);
        var selectionPayloadHash = FeedOperationHash.Compute(string.Concat(selected.Select(static block => block.Raw)));
        var inputLocators = FeedOperationLocatorFactory.ForSelection(
            request.SourcePath,
            document,
            request.Selection);

        if (operation is null)
        {
            operation = new FeedTaskConversionRecord(
                2,
                request.VaultId,
                request.OperationId,
                FeedTaskConversionState.Pending,
                request.SourcePath,
                request.ExpectedSourceRevision,
                taskId,
                null,
                DateTimeOffset.UtcNow,
                new FeedTaskConversionRecoveryDescriptor(
                    request.OperationId,
                    request.Selection,
                    selectionPayloadHash,
                    null,
                    title,
                    description,
                    request.IsGoal,
                    request.AreaIds.ToArray(),
                    request.ReviewSessionId,
                    inputLocators));
        }
        else if (operation.RecoveryDescriptor is null)
        {
            operation = operation with
            {
                SchemaVersion = 2,
                RecoveryDescriptor = new FeedTaskConversionRecoveryDescriptor(
                    request.OperationId,
                    request.Selection,
                    selectionPayloadHash,
                    null,
                    title,
                    description,
                    request.IsGoal,
                    request.AreaIds.ToArray(),
                    request.ReviewSessionId,
                    inputLocators)
            };
        }
        else
        {
            ValidateRecoveryDescriptor(
                operation.RecoveryDescriptor,
                request,
                selectionPayloadHash,
                title,
                description,
                inputLocators);
        }

        await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

        var created = await taskTarget.CreateOrGetAsync(
                new FeedTaskDraft(taskId, request.OperationId, title, description, request.IsGoal, request.AreaIds),
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(created.TaskId, taskId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Task persistence returned an ID different from the stable conversion task ID.");
        }


        var taskLink = FeedLinkSerializer.Task(taskId, created.Title);
        var updatedSource = mutations.ReplaceSelection(
            source.Text,
            request.Selection,
            taskLink);
        var sourceOutputHash = FeedOperationHash.Compute(updatedSource);
        var sourceOutputLocators = FeedOperationLocatorFactory.ForMatchingBlock(
            request.SourcePath,
            parser.Parse(updatedSource),
            block => block.Raw.Contains($"(unlimotion://task/{taskId})", StringComparison.Ordinal));

        if (operation.State == FeedTaskConversionState.Pending
            || string.IsNullOrWhiteSpace(operation.RecoveryDescriptor?.SourceOutputHash))
        {
            operation = operation with
            {
                State = FeedTaskConversionState.TaskCreated,
                RecoveryDescriptor = operation.RecoveryDescriptor! with
                {
                    SourceOutputHash = sourceOutputHash,
                    SourceOutputLocators = sourceOutputLocators
                },
                RecoveryIssue = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(operation.RecoveryDescriptor?.SourceOutputHash, sourceOutputHash, StringComparison.Ordinal))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                request.SourcePath,
                "The stored task conversion payload no longer matches the requested source replacement.",
                cancellationToken).ConfigureAwait(false);
        }
        else if (!FeedOperationLocatorFactory.SequenceEqual(
                     operation.RecoveryDescriptor?.SourceOutputLocators,
                     sourceOutputLocators))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                request.SourcePath,
                "The stored task output locator no longer matches the journaled replacement.",
                cancellationToken).ConfigureAwait(false);
        }

        var sourceAfterTaskPersistence = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
        if (string.Equals(
                FeedOperationHash.Compute(sourceAfterTaskPersistence.Text),
                operation.RecoveryDescriptor!.SourceOutputHash,
                StringComparison.Ordinal))
        {
            var recovered = operation with
            {
                State = FeedTaskConversionState.Completed,
                SourceRevision = sourceAfterTaskPersistence.Revision,
                ReviewApplied = ReviewDoesNotRequireDecision(request.ReviewSessionId),
                RecoveryIssue = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(recovered, cancellationToken).ConfigureAwait(false);
            return new FeedTaskConversionResult(taskId, created.Title, sourceAfterTaskPersistence.Revision, true);
        }

        if (SourceReferencesTask(sourceAfterTaskPersistence.Text, taskId))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                request.SourcePath,
                "The task link exists, but the source differs from the journaled replacement.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(sourceAfterTaskPersistence.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(
                request.SourcePath,
                request.ExpectedSourceRevision,
                sourceAfterTaskPersistence.Revision);
        }

        if (revisions is not null)
        {
            await revisions.SaveAsync(request.VaultId, sourceAfterTaskPersistence, cancellationToken).ConfigureAwait(false);
        }

        var sourceWrite = await vault.WriteAsync(
                request.SourcePath,
                updatedSource,
                request.ExpectedSourceRevision,
                sourceAfterTaskPersistence.HasUtf8Bom,
                cancellationToken)
            .ConfigureAwait(false);
        var complete = operation with
        {
            State = FeedTaskConversionState.Completed,
            SourceRevision = sourceWrite.Revision,
            ReviewApplied = ReviewDoesNotRequireDecision(request.ReviewSessionId),
            RecoveryIssue = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await journal.SaveAsync(complete, cancellationToken).ConfigureAwait(false);
        return new FeedTaskConversionResult(taskId, created.Title, sourceWrite.Revision, false);
    }

    public Task<FeedTaskConversionResult> ResumeAsync(
        FeedTaskConversionRecord operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var descriptor = operation.RecoveryDescriptor
            ?? throw new InvalidDataException("The task conversion journal does not contain a durable recovery descriptor.");
        if (operation.SchemaVersion != 2
            || !string.Equals(descriptor.OriginalOperationId, operation.OperationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The task conversion recovery descriptor is incompatible with this operation.");
        }

        return ConvertAsync(
            new FeedTaskConversionRequest(
                operation.VaultId,
                descriptor.OriginalOperationId,
                operation.SourcePath,
                operation.ExpectedSourceRevision,
                descriptor.Selection,
                descriptor.AreaIds,
                descriptor.IsGoal,
                descriptor.ReviewSessionId),
            cancellationToken);
    }

    public Task MarkReviewAppliedAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        journal.MarkReviewAppliedAsync(vaultId, operationId, cancellationToken);

    internal static (string Title, string Description) ParseTaskContent(IReadOnlyList<MarkdownBlock> selection)
    {
        if (selection.Count == 0)
        {
            throw new InvalidOperationException("Task conversion requires at least one Markdown block.");
        }

        var raw = string.Concat(selection.Select(static block => block.Raw))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
        var lines = raw.Split('\n');
        var titleLineIndex = Array.FindIndex(lines, static line => !string.IsNullOrWhiteSpace(line));
        if (titleLineIndex < 0)
        {
            throw new InvalidOperationException("Task conversion requires a non-empty content line.");
        }

        var title = LeadingTaskMarkerRegex().Replace(lines[titleLineIndex], string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new InvalidOperationException("The selected Markdown does not contain a task title.");
        }

        var descriptionLines = lines
            .Where((_, index) => index != titleLineIndex)
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse()
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
            .Reverse();
        return (title, string.Join("\n", descriptionLines));
    }

    private async Task<VaultDocument> RequireSourceAsync(
        string path,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        var source = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", path);
        if (!string.Equals(source.Revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(path, expectedRevision, source.Revision);
        }

        return source;
    }

    private static void ValidateRequest(FeedTaskConversionRequest request)
    {
        FeedLinkSerializer.ValidateStableId(request.VaultId, nameof(request.VaultId));
        FeedLinkSerializer.ValidateStableId(request.OperationId, nameof(request.OperationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedSourceRevision);
        foreach (var areaId in request.AreaIds.Distinct(StringComparer.Ordinal))
        {
            FeedLinkSerializer.ValidateStableId(areaId, nameof(request.AreaIds));
        }
    }

    private static void ValidateExistingOperation(
        FeedTaskConversionRecord? operation,
        FeedTaskConversionRequest request,
        string taskId)
    {
        if (operation is null)
        {
            return;
        }

        if (operation.SchemaVersion is not 1 and not 2
            || !string.Equals(operation.VaultId, request.VaultId, StringComparison.Ordinal)
            || !string.Equals(operation.OperationId, request.OperationId, StringComparison.Ordinal)
            || !string.Equals(operation.SourcePath.Replace('\\', '/'), request.SourcePath.Replace('\\', '/'), StringComparison.Ordinal)
            || !string.Equals(operation.ExpectedSourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            || !string.Equals(operation.TaskId, taskId, StringComparison.Ordinal)
            || operation.State == FeedTaskConversionState.Completed && string.IsNullOrWhiteSpace(operation.SourceRevision))
        {
            throw new InvalidDataException("The task conversion journal does not match the requested operation.");
        }
    }

    private static void ValidateRecoveryDescriptor(
        FeedTaskConversionRecoveryDescriptor descriptor,
        FeedTaskConversionRequest request,
        string selectionPayloadHash,
        string title,
        string description,
        IReadOnlyList<BlockLocator> inputLocators)
    {
        if (!string.Equals(descriptor.OriginalOperationId, request.OperationId, StringComparison.Ordinal)
            || descriptor.Selection != request.Selection
            || !string.Equals(descriptor.SelectionPayloadHash, selectionPayloadHash, StringComparison.Ordinal)
            || !string.Equals(descriptor.Title, title, StringComparison.Ordinal)
            || !string.Equals(descriptor.Description, description, StringComparison.Ordinal)
            || descriptor.IsGoal != request.IsGoal
            || !descriptor.AreaIds.SequenceEqual(request.AreaIds, StringComparer.Ordinal)
            || !string.Equals(descriptor.ReviewSessionId, request.ReviewSessionId, StringComparison.Ordinal)
            || !FeedOperationLocatorFactory.SequenceEqual(descriptor.InputLocators, inputLocators))
        {
            throw new InvalidDataException("The task conversion recovery descriptor does not match the requested operation.");
        }
    }

    private async Task ThrowRecoveryConflictAsync(
        FeedTaskConversionRecord operation,
        string relativePath,
        string message,
        CancellationToken cancellationToken)
    {
        await journal.SaveAsync(
            operation with { RecoveryIssue = message, UpdatedAt = DateTimeOffset.UtcNow },
            cancellationToken).ConfigureAwait(false);
        throw new FeedOperationRecoveryConflictException(
            operation.VaultId,
            operation.OperationId,
            relativePath,
            message);
    }

    private static bool SourceReferencesTask(string source, string taskId) =>
        source.Contains($"(unlimotion://task/{taskId})", StringComparison.Ordinal);

    private static bool ReviewDoesNotRequireDecision(string? reviewSessionId) =>
        string.IsNullOrWhiteSpace(reviewSessionId);

    private static void EnsureSourceReferencesTask(string source, string taskId)
    {
        if (!SourceReferencesTask(source, taskId))
        {
            throw new InvalidDataException("The completed task conversion source no longer contains its task link.");
        }
    }

    private static string ReadFallbackTitle(string source, string taskId)
    {
        var suffix = $"](unlimotion://task/{taskId})";
        var suffixIndex = source.IndexOf(suffix, StringComparison.Ordinal);
        if (suffixIndex < 0)
        {
            return taskId;
        }

        var prefixIndex = source.LastIndexOf('[', suffixIndex);
        return prefixIndex < 0 ? taskId : source[(prefixIndex + 1)..suffixIndex];
    }
}
