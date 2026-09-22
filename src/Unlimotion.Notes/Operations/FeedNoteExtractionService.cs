using System.Text;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record NoteExtractionRequest(
    string VaultId,
    string OperationId,
    string SourcePath,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    string Folder,
    string Title,
    string NoteId,
    IReadOnlyList<string> AreaIds,
    string? ReviewSessionId = null);

public sealed record NoteExtractionResult(
    string DestinationPath,
    string NoteId,
    string SourceRevision,
    string DestinationRevision,
    bool WasAlreadyCompleted);

public sealed class FeedNoteExtractionService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations,
    IFeedOperationJournal journal,
    IRevisionStore? revisions = null)
{
    public async Task<NoteExtractionResult> ExtractAsync(NoteExtractionRequest request, CancellationToken cancellationToken = default)
    {
        FeedLinkSerializer.ValidateStableId(request.VaultId, nameof(request.VaultId));
        FeedLinkSerializer.ValidateStableId(request.OperationId, nameof(request.OperationId));
        FeedLinkSerializer.ValidateStableId(request.NoteId, nameof(request.NoteId));
        foreach (var areaId in request.AreaIds)
        {
            FeedLinkSerializer.ValidateStableId(areaId, nameof(request.AreaIds));
        }

        var operation = await journal.LoadAsync(request.VaultId, request.OperationId, cancellationToken).ConfigureAwait(false);
        ValidateExistingOperation(operation, request);
        if (operation is not null)
        {
            vault.ResolveSafePath(operation.DestinationPath);
        }

        if (operation?.State == FeedOperationState.Completed)
        {
            if (operation.RecoveryDescriptor is { } completedDescriptor)
            {
                ValidateStoredRequest(completedDescriptor, request);
                var completedSource = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
                if (completedSource is null
                    || !string.Equals(
                        FeedOperationHash.Compute(completedSource.Text),
                        completedDescriptor.SourceOutputHash,
                        StringComparison.Ordinal))
                {
                    await ThrowRecoveryConflictAsync(
                        operation,
                        request.SourcePath,
                        "The completed extraction source drifted before its review decision was checkpointed.",
                        cancellationToken).ConfigureAwait(false);
                }

                var completedDestination = await vault.ReadAsync(operation.DestinationPath, cancellationToken)
                    .ConfigureAwait(false);
                if (completedDestination is null)
                {
                    await ThrowRecoveryConflictAsync(
                        operation,
                        operation.DestinationPath,
                        "The completed extraction destination is missing before its review decision was checkpointed.",
                        cancellationToken).ConfigureAwait(false);
                }

                await EnsureExactDestinationAsync(
                        operation,
                        completedDestination!,
                        completedDescriptor,
                        request.NoteId,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new NoteExtractionResult(
                    operation.DestinationPath,
                    operation.ResultId!,
                    completedSource!.Revision,
                    completedDestination!.Revision,
                    true);
            }

            return new NoteExtractionResult(
                operation.DestinationPath,
                operation.ResultId!,
                operation.SourceRevision!,
                operation.DestinationRevision!,
                true);
        }

        var source = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source note no longer exists.", request.SourcePath);
        var outputMarker = $"<!-- unlimotion-note:{request.NoteId} -->";
        if (operation?.RecoveryDescriptor is { } storedDescriptor)
        {
            ValidateStoredRequest(storedDescriptor, request);
            var storedDestination = await RequireExactDestinationAsync(operation, storedDescriptor, request.NoteId, cancellationToken)
                .ConfigureAwait(false);
            if (storedDestination is not null && operation.State == FeedOperationState.Pending)
            {
                operation = operation with
                {
                    State = FeedOperationState.DestinationCreated,
                    DestinationRevision = storedDestination.Revision,
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(FeedOperationHash.Compute(source.Text), storedDescriptor.SourceOutputHash, StringComparison.Ordinal))
            {
                if (storedDestination is null)
                {
                    await ThrowRecoveryConflictAsync(
                        operation,
                        operation.DestinationPath,
                        "The extraction source was replaced, but its destination is missing.",
                        cancellationToken).ConfigureAwait(false);
                }

                var recoveredComplete = operation with
                {
                    State = FeedOperationState.Completed,
                    SourceRevision = source.Revision,
                    DestinationRevision = storedDestination!.Revision,
                    ReviewApplied = ReviewDoesNotRequireDecision(request.ReviewSessionId),
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await journal.SaveAsync(recoveredComplete, cancellationToken).ConfigureAwait(false);
                return new NoteExtractionResult(
                    recoveredComplete.DestinationPath,
                    request.NoteId,
                    source.Revision,
                    storedDestination.Revision,
                    true);
            }

            if (source.Text.Contains(outputMarker, StringComparison.Ordinal))
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    request.SourcePath,
                    "The note link exists, but the source changed after the partial extraction.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (operation?.State == FeedOperationState.DestinationCreated
                 && source.Text.Contains(outputMarker, StringComparison.Ordinal))
        {
            var legacyDestination = await vault.ReadAsync(operation.DestinationPath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The extraction source was replaced but the destination note is missing.");
            EnsureDestinationBelongsToOperation(legacyDestination.Text, request.NoteId);
            var recoveredComplete = operation with
            {
                State = FeedOperationState.Completed,
                SourceRevision = source.Revision,
                DestinationRevision = legacyDestination.Revision,
                ReviewApplied = operation.SchemaVersion < 2
                    || ReviewDoesNotRequireDecision(request.ReviewSessionId),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(recoveredComplete, cancellationToken).ConfigureAwait(false);
            return new NoteExtractionResult(
                recoveredComplete.DestinationPath,
                request.NoteId,
                source.Revision,
                legacyDestination.Revision,
                true);
        }

        if (!string.Equals(source.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            if (operation is not null)
            {
                await SaveRecoveryIssueAsync(operation, "The extraction source changed before recovery could replace it.", cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new VaultRevisionConflictException(request.SourcePath, request.ExpectedSourceRevision, source.Revision);
        }

        var parsed = parser.Parse(source.Text);
        var selected = request.Selection.Resolve(parsed);
        var selectedRaw = string.Concat(selected.Select(static block => block.Raw)).TrimEnd('\r', '\n');
        var inputLocators = FeedOperationLocatorFactory.ForSelection(
            request.SourcePath,
            parsed,
            request.Selection);
        var destinationPath = operation?.DestinationPath
            ?? await ChooseDestinationAsync(request.Folder, request.Title, cancellationToken).ConfigureAwait(false);
        var destinationText = BuildDestination(request.Title, request.NoteId, request.AreaIds, selectedRaw, parsed.NewLine);
        var link = FeedLinkSerializer.Note(destinationPath, request.Title, request.NoteId);
        var updatedSource = mutations.ReplaceSelection(source.Text, request.Selection, link);
        var sourceOutputLocators = FeedOperationLocatorFactory.ForMatchingBlock(
            request.SourcePath,
            parser.Parse(updatedSource),
            block => block.Raw.Contains($"<!-- unlimotion-note:{request.NoteId} -->", StringComparison.Ordinal));
        var descriptor = new FeedOperationRecoveryDescriptor(
            request.OperationId,
            request.ExpectedSourceRevision,
            request.Selection,
            FeedOperationHash.Compute(selectedRaw),
            FeedOperationHash.Compute(destinationText),
            FeedOperationHash.Compute(updatedSource),
            Folder: request.Folder,
            Title: request.Title,
            AreaIds: request.AreaIds.ToArray(),
            ReviewSessionId: request.ReviewSessionId,
            InputLocators: inputLocators,
            SourceOutputLocators: sourceOutputLocators);

        if (operation?.RecoveryDescriptor is { } existingDescriptor)
        {
            ValidateExactDescriptor(existingDescriptor, descriptor);
        }
        else if (operation is not null)
        {
            operation = operation with { SchemaVersion = 2, RecoveryDescriptor = descriptor, RecoveryIssue = null };
        }

        operation ??= new FeedOperationRecord(
            2,
            request.VaultId,
            request.OperationId,
            FeedOperationKind.NoteExtraction,
            FeedOperationState.Pending,
            request.SourcePath,
            destinationPath,
            null,
            request.ExpectedSourceRevision,
            request.NoteId,
            DateTimeOffset.UtcNow,
            descriptor);
        await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

        VaultDocument destination;
        if (operation.State == FeedOperationState.DestinationCreated)
        {
            destination = await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The journal references a missing extracted note.");
            await EnsureExactDestinationAsync(operation, destination, descriptor, request.NoteId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var recoveredDestination = await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            if (recoveredDestination is null)
            {
                _ = await vault.CreateAsync(destinationPath, destinationText, cancellationToken: cancellationToken).ConfigureAwait(false);
                recoveredDestination = (await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false))!;
            }

            await EnsureExactDestinationAsync(operation, recoveredDestination, descriptor, request.NoteId, cancellationToken)
                .ConfigureAwait(false);
            destination = recoveredDestination;
            operation = operation with
            {
                State = FeedOperationState.DestinationCreated,
                DestinationRevision = destination.Revision,
                RecoveryIssue = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        if (revisions is not null)
        {
            await revisions.SaveAsync(request.VaultId, source, cancellationToken).ConfigureAwait(false);
        }

        var destinationBeforeSourceReplacement = await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The extraction destination disappeared before source replacement.");
        await EnsureExactDestinationAsync(
                operation,
                destinationBeforeSourceReplacement,
                descriptor,
                request.NoteId,
                cancellationToken)
            .ConfigureAwait(false);
        var sourceBeforeReplacement = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source note no longer exists.", request.SourcePath);
        if (!string.Equals(sourceBeforeReplacement.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            await SaveRecoveryIssueAsync(operation, "The extraction source changed after its destination was created.", cancellationToken)
                .ConfigureAwait(false);
            throw new VaultRevisionConflictException(
                request.SourcePath,
                request.ExpectedSourceRevision,
                sourceBeforeReplacement.Revision);
        }

        var sourceWrite = await vault.WriteAsync(
            request.SourcePath,
            updatedSource,
            request.ExpectedSourceRevision,
            sourceBeforeReplacement.HasUtf8Bom,
            cancellationToken).ConfigureAwait(false);
        var complete = operation with
        {
            State = FeedOperationState.Completed,
            SourceRevision = sourceWrite.Revision,
            DestinationRevision = destinationBeforeSourceReplacement.Revision,
            ReviewApplied = ReviewDoesNotRequireDecision(request.ReviewSessionId),
            RecoveryIssue = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await journal.SaveAsync(complete, cancellationToken).ConfigureAwait(false);
        return new NoteExtractionResult(
            destinationPath,
            request.NoteId,
            sourceWrite.Revision,
            destinationBeforeSourceReplacement.Revision,
            false);
    }

    public Task<NoteExtractionResult> ResumeAsync(
        FeedOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var descriptor = operation.RecoveryDescriptor
            ?? throw new InvalidDataException("The note extraction journal does not contain a durable recovery descriptor.");
        if (operation.SchemaVersion != 2
            || operation.Kind != FeedOperationKind.NoteExtraction
            || !string.Equals(descriptor.OriginalOperationId, operation.OperationId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptor.Title)
            || string.IsNullOrWhiteSpace(operation.ResultId))
        {
            throw new InvalidDataException("The note extraction recovery descriptor is incompatible with this operation.");
        }

        return ExtractAsync(
            new NoteExtractionRequest(
                operation.VaultId,
                descriptor.OriginalOperationId,
                operation.SourcePath,
                descriptor.ExpectedSourceRevision,
                descriptor.Selection,
                descriptor.Folder ?? string.Empty,
                descriptor.Title,
                operation.ResultId,
                descriptor.AreaIds ?? [],
                descriptor.ReviewSessionId),
            cancellationToken);
    }

    public Task MarkReviewAppliedAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        journal.MarkReviewAppliedAsync(vaultId, operationId, cancellationToken);

    private async Task<string> ChooseDestinationAsync(string folder, string title, CancellationToken cancellationToken)
    {
        var safeName = FeedLinkSerializer.MakeSafeFileName(title);
        for (var suffix = 1; ; suffix++)
        {
            var fileName = suffix == 1 ? safeName + ".md" : $"{safeName} {suffix}.md";
            var relative = string.IsNullOrWhiteSpace(folder)
                ? fileName
                : folder.Replace('\\', '/').Trim('/') + "/" + fileName;
            vault.ResolveSafePath(relative);
            if (await vault.ReadAsync(relative, cancellationToken).ConfigureAwait(false) is null)
            {
                return relative;
            }
        }
    }

    private async Task<VaultDocument?> RequireExactDestinationAsync(
        FeedOperationRecord operation,
        FeedOperationRecoveryDescriptor descriptor,
        string noteId,
        CancellationToken cancellationToken)
    {
        var destination = await vault.ReadAsync(operation.DestinationPath, cancellationToken).ConfigureAwait(false);
        if (destination is null)
        {
            if (operation.State == FeedOperationState.DestinationCreated)
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    operation.DestinationPath,
                    "The journaled extraction destination is missing.",
                    cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        await EnsureExactDestinationAsync(operation, destination, descriptor, noteId, cancellationToken)
            .ConfigureAwait(false);
        return destination;
    }

    private async Task EnsureExactDestinationAsync(
        FeedOperationRecord operation,
        VaultDocument destination,
        FeedOperationRecoveryDescriptor descriptor,
        string noteId,
        CancellationToken cancellationToken)
    {
        if (!destination.Text.Contains("unlimotion-id: " + noteId, StringComparison.Ordinal)
            || !string.Equals(FeedOperationHash.Compute(destination.Text), descriptor.DestinationPayloadHash, StringComparison.Ordinal)
            || operation.State == FeedOperationState.DestinationCreated
            && !string.Equals(destination.Revision, operation.DestinationRevision, StringComparison.Ordinal))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                operation.DestinationPath,
                "The extraction destination changed after the partial operation.",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveRecoveryIssueAsync(
        FeedOperationRecord operation,
        string message,
        CancellationToken cancellationToken)
    {
        await journal.SaveAsync(
            operation with { RecoveryIssue = message, UpdatedAt = DateTimeOffset.UtcNow },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ThrowRecoveryConflictAsync(
        FeedOperationRecord operation,
        string relativePath,
        string message,
        CancellationToken cancellationToken)
    {
        await SaveRecoveryIssueAsync(operation, message, cancellationToken).ConfigureAwait(false);
        throw new FeedOperationRecoveryConflictException(
            operation.VaultId,
            operation.OperationId,
            relativePath,
            message);
    }

    private static string BuildDestination(
        string title,
        string noteId,
        IEnumerable<string> areaIds,
        string selectedRaw,
        string newLine)
    {
        var builder = new StringBuilder();
        var areas = areaIds.ToArray();
        builder.Append("---").Append(newLine)
            .Append("unlimotion-id: ").Append(noteId).Append(newLine);
        if (areas.Length == 0)
        {
            builder.Append("unlimotion-areas: []").Append(newLine);
        }
        else
        {
            builder.Append("unlimotion-areas:").Append(newLine);
            foreach (var areaId in areas)
            {
                builder.Append("  - ").Append(areaId).Append(newLine);
            }
        }

        builder.Append("---").Append(newLine).Append(newLine)
            .Append("# ").Append(title.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)).Append(newLine).Append(newLine)
            .Append(selectedRaw).Append(newLine);
        return builder.ToString();
    }

    private static void EnsureDestinationBelongsToOperation(string text, string noteId)
    {
        if (!text.Contains("unlimotion-id: " + noteId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The existing destination belongs to another extraction operation.");
        }
    }

    private static void ValidateExistingOperation(FeedOperationRecord? operation, NoteExtractionRequest request)
    {
        if (operation is null)
        {
            return;
        }

        if (operation.SchemaVersion is not 1 and not 2
            || operation.Kind != FeedOperationKind.NoteExtraction
            || !string.Equals(operation.VaultId, request.VaultId, StringComparison.Ordinal)
            || !string.Equals(operation.OperationId, request.OperationId, StringComparison.Ordinal)
            || !string.Equals(operation.SourcePath.Replace('\\', '/'), request.SourcePath.Replace('\\', '/'), StringComparison.Ordinal)
            || !string.Equals(operation.ResultId, request.NoteId, StringComparison.Ordinal)
            || operation.State == FeedOperationState.Completed
                && (string.IsNullOrWhiteSpace(operation.SourceRevision) || string.IsNullOrWhiteSpace(operation.DestinationRevision)))
        {
            throw new InvalidDataException("The extraction journal does not match the requested operation.");
        }
    }

    private static void ValidateStoredRequest(
        FeedOperationRecoveryDescriptor descriptor,
        NoteExtractionRequest request)
    {
        if (!string.Equals(descriptor.OriginalOperationId, request.OperationId, StringComparison.Ordinal)
            || !string.Equals(descriptor.ExpectedSourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            || descriptor.Selection != request.Selection
            || !string.Equals(descriptor.Folder ?? string.Empty, request.Folder, StringComparison.Ordinal)
            || !string.Equals(descriptor.Title, request.Title, StringComparison.Ordinal)
            || !(descriptor.AreaIds ?? []).SequenceEqual(request.AreaIds, StringComparer.Ordinal)
            || !string.Equals(descriptor.ReviewSessionId, request.ReviewSessionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The extraction recovery descriptor does not match the requested operation.");
        }
    }

    private static void ValidateExactDescriptor(
        FeedOperationRecoveryDescriptor stored,
        FeedOperationRecoveryDescriptor expected)
    {
        if (!string.Equals(stored.OriginalOperationId, expected.OriginalOperationId, StringComparison.Ordinal)
            || !string.Equals(stored.ExpectedSourceRevision, expected.ExpectedSourceRevision, StringComparison.Ordinal)
            || stored.Selection != expected.Selection
            || !string.Equals(stored.SelectionPayloadHash, expected.SelectionPayloadHash, StringComparison.Ordinal)
            || !string.Equals(stored.DestinationPayloadHash, expected.DestinationPayloadHash, StringComparison.Ordinal)
            || !string.Equals(stored.SourceOutputHash, expected.SourceOutputHash, StringComparison.Ordinal)
            || !string.Equals(stored.Folder, expected.Folder, StringComparison.Ordinal)
            || !string.Equals(stored.Title, expected.Title, StringComparison.Ordinal)
            || !(stored.AreaIds ?? []).SequenceEqual(expected.AreaIds ?? [], StringComparer.Ordinal)
            || !string.Equals(stored.ReviewSessionId, expected.ReviewSessionId, StringComparison.Ordinal)
            || !FeedOperationLocatorFactory.SequenceEqual(stored.InputLocators, expected.InputLocators)
            || !FeedOperationLocatorFactory.SequenceEqual(stored.SourceOutputLocators, expected.SourceOutputLocators))
        {
            throw new InvalidDataException("The extraction payload no longer matches its recovery descriptor.");
        }
    }

    private static bool ReviewDoesNotRequireDecision(string? reviewSessionId) =>
        string.IsNullOrWhiteSpace(reviewSessionId);
}
