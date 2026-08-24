using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record MoveToTodayRequest(
    string VaultId,
    string OperationId,
    string SourcePath,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    DateOnly DestinationDate,
    AreaReference? DestinationArea,
    string? ExpectedDestinationRevision,
    string ReviewSessionId);

public sealed record MoveToTodayResult(
    string Anchor,
    string DestinationPath,
    string SourceRevision,
    string DestinationRevision,
    string DeferredFromSessionId,
    bool WasAlreadyCompleted);

public sealed class FeedMoveToTodayService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations,
    IFeedOperationJournal journal,
    IRevisionStore? revisions = null)
{
    public async Task<MoveToTodayResult> MoveAsync(MoveToTodayRequest request, CancellationToken cancellationToken = default)
    {
        FeedLinkSerializer.ValidateStableId(request.VaultId, nameof(request.VaultId));
        FeedLinkSerializer.ValidateStableId(request.OperationId, nameof(request.OperationId));
        var destinationPath = $"Ежедневные/{request.DestinationDate:yyyy-MM-dd}.md";
        if (string.Equals(request.SourcePath.Replace('\\', '/'), destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Move to today is only available for another daily file.");
        }

        var anchor = "unlimotion-move-" + request.OperationId;
        var operation = await journal.LoadAsync(request.VaultId, request.OperationId, cancellationToken).ConfigureAwait(false);
        ValidateExistingOperation(operation, request, destinationPath, anchor);
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
                        "The completed move source drifted before its review decisions were checkpointed.",
                        cancellationToken).ConfigureAwait(false);
                }

                var completedDestination = await vault.ReadAsync(operation.DestinationPath, cancellationToken)
                    .ConfigureAwait(false);
                if (completedDestination is null)
                {
                    await ThrowRecoveryConflictAsync(
                        operation,
                        operation.DestinationPath,
                        "The completed move destination is missing before its review decisions were checkpointed.",
                        cancellationToken).ConfigureAwait(false);
                }

                await EnsureExactDestinationAsync(
                        operation,
                        completedDestination!,
                        completedDescriptor,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new MoveToTodayResult(
                    anchor,
                    operation.DestinationPath,
                    completedSource!.Revision,
                    completedDestination!.Revision,
                    request.ReviewSessionId,
                    true);
            }

            return new MoveToTodayResult(
                anchor,
                operation.DestinationPath,
                operation.SourceRevision!,
                operation.DestinationRevision!,
                request.ReviewSessionId,
                true);
        }

        var source = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
        var outputLink = FeedLinkSerializer.MovedBlock(request.DestinationDate, anchor);
        VaultDocument? destination = null;
        if (operation?.RecoveryDescriptor is { } storedDescriptor)
        {
            ValidateStoredRequest(storedDescriptor, request);
            destination = await ReadAndValidateStoredDestinationAsync(operation, storedDescriptor, cancellationToken)
                .ConfigureAwait(false);
            if (destination is not null
                && string.Equals(FeedOperationHash.Compute(destination.Text), storedDescriptor.DestinationPayloadHash, StringComparison.Ordinal)
                && operation.State == FeedOperationState.Pending)
            {
                operation = operation with
                {
                    State = FeedOperationState.DestinationCreated,
                    DestinationRevision = destination.Revision,
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(FeedOperationHash.Compute(source.Text), storedDescriptor.SourceOutputHash, StringComparison.Ordinal))
            {
                if (destination is null
                    || !string.Equals(FeedOperationHash.Compute(destination.Text), storedDescriptor.DestinationPayloadHash, StringComparison.Ordinal))
                {
                    await ThrowRecoveryConflictAsync(
                        operation,
                        destinationPath,
                        "The move source was replaced, but its exact destination payload is unavailable.",
                        cancellationToken).ConfigureAwait(false);
                }

                var recoveredComplete = operation with
                {
                    State = FeedOperationState.Completed,
                    SourceRevision = source.Revision,
                    DestinationRevision = destination!.Revision,
                    ReviewApplied = false,
                    RecoveryIssue = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await journal.SaveAsync(recoveredComplete, cancellationToken).ConfigureAwait(false);
                return new MoveToTodayResult(
                    anchor,
                    destinationPath,
                    source.Revision,
                    destination.Revision,
                    request.ReviewSessionId,
                    true);
            }

            if (source.Text.Contains(outputLink, StringComparison.Ordinal))
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    request.SourcePath,
                    "The move link exists, but the source changed after the partial operation.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (operation?.State == FeedOperationState.DestinationCreated
                 && source.Text.Contains(outputLink, StringComparison.Ordinal))
        {
            var legacyDestination = await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The move source was replaced but the destination daily note is missing.");
            if (!legacyDestination.Text.Contains('^' + anchor, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The move source points to a destination without the operation anchor.");
            }

            var recoveredComplete = operation with
            {
                State = FeedOperationState.Completed,
                SourceRevision = source.Revision,
                DestinationRevision = legacyDestination.Revision,
                ReviewApplied = operation.SchemaVersion < 2,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(recoveredComplete, cancellationToken).ConfigureAwait(false);
            return new MoveToTodayResult(
                anchor,
                destinationPath,
                source.Revision,
                legacyDestination.Revision,
                request.ReviewSessionId,
                true);
        }

        if (!string.Equals(source.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            if (operation is not null)
            {
                await SaveRecoveryIssueAsync(operation, "The move source changed before recovery could replace it.", cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new VaultRevisionConflictException(request.SourcePath, request.ExpectedSourceRevision, source.Revision);
        }

        var parsed = parser.Parse(source.Text);
        var selectedRaw = string.Concat(request.Selection.Resolve(parsed).Select(static block => block.Raw)).TrimEnd('\r', '\n');
        var inputLocators = FeedOperationLocatorFactory.ForSelection(
            request.SourcePath,
            parsed,
            request.Selection);
        destination ??= await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        var expectedDestinationRevision = operation?.RecoveryDescriptor?.ExpectedDestinationRevision
            ?? request.ExpectedDestinationRevision;
        var payload = selectedRaw + parsed.NewLine + '^' + anchor;
        var legacyDestinationAlreadyContainsOutput = operation is { RecoveryDescriptor: null }
            && expectedDestinationRevision is null
            && destination is not null
            && string.Equals(
                NormalizeLineEndings(destination.Text),
                NormalizeLineEndings(mutations.AppendQuickCapture(string.Empty, payload, request.DestinationArea)),
                StringComparison.Ordinal);
        var destinationAlreadyContainsOutput = operation?.RecoveryDescriptor is { } descriptorForDestination
            && destination is not null
            && string.Equals(
                FeedOperationHash.Compute(destination.Text),
                descriptorForDestination.DestinationPayloadHash,
                StringComparison.Ordinal)
            || legacyDestinationAlreadyContainsOutput;
        if (!destinationAlreadyContainsOutput
            && !string.Equals(destination?.Revision, expectedDestinationRevision, StringComparison.Ordinal))
        {
            if (operation is not null)
            {
                await SaveRecoveryIssueAsync(operation, "The move destination changed before the payload could be written.", cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new VaultRevisionConflictException(destinationPath, expectedDestinationRevision, destination?.Revision);
        }

        if (destination is not null && revisions is not null && !destinationAlreadyContainsOutput)
        {
            await revisions.SaveAsync(request.VaultId, destination, cancellationToken).ConfigureAwait(false);
        }

        var destinationRaw = destinationAlreadyContainsOutput ? destination!.Text : destination?.Text ?? string.Empty;
        var updatedDestination = destinationAlreadyContainsOutput
            ? destinationRaw
            : mutations.AppendQuickCapture(destinationRaw, payload, request.DestinationArea);
        var updatedSource = mutations.ReplaceSelection(source.Text, request.Selection, outputLink);
        var sourceOutputLocators = FeedOperationLocatorFactory.ForMatchingBlock(
            request.SourcePath,
            parser.Parse(updatedSource),
            block => block.Raw.Contains($"#^{anchor}", StringComparison.Ordinal));
        var destinationDocument = parser.Parse(updatedDestination);
        var normalizedPayload = FeedOperationLocatorFactory.NormalizeForDocument(payload, destinationDocument.NewLine);
        var destinationPayloadStart = updatedDestination.IndexOf(normalizedPayload, StringComparison.Ordinal);
        if (destinationPayloadStart < 0)
        {
            throw new InvalidDataException("The move destination payload could not be resolved for recovery.");
        }

        var destinationOutputLocators = FeedOperationLocatorFactory.ForRawRange(
            destinationPath,
            destinationDocument,
            destinationPayloadStart,
            normalizedPayload.Length);
        var descriptor = new FeedOperationRecoveryDescriptor(
            request.OperationId,
            request.ExpectedSourceRevision,
            request.Selection,
            FeedOperationHash.Compute(selectedRaw),
            FeedOperationHash.Compute(updatedDestination),
            FeedOperationHash.Compute(updatedSource),
            expectedDestinationRevision,
            DestinationDate: request.DestinationDate,
            DestinationArea: request.DestinationArea,
            ReviewSessionId: request.ReviewSessionId,
            InputLocators: inputLocators,
            SourceOutputLocators: sourceOutputLocators,
            DestinationOutputLocators: destinationOutputLocators);

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
            FeedOperationKind.MoveToToday,
            FeedOperationState.Pending,
            request.SourcePath,
            destinationPath,
            null,
            request.ExpectedSourceRevision,
            anchor,
            DateTimeOffset.UtcNow,
            descriptor);
        await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

        if (!destinationAlreadyContainsOutput)
        {
            _ = destination is null
                ? await vault.CreateAsync(destinationPath, updatedDestination, source.HasUtf8Bom, cancellationToken).ConfigureAwait(false)
                : await vault.WriteAsync(destinationPath, updatedDestination, destination.Revision, destination.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
            destination = (await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false))!;
        }

        await EnsureExactDestinationAsync(operation, destination!, descriptor, cancellationToken).ConfigureAwait(false);
        operation = operation with
        {
            State = FeedOperationState.DestinationCreated,
            DestinationRevision = destination!.Revision,
            RecoveryIssue = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

        if (revisions is not null)
        {
            await revisions.SaveAsync(request.VaultId, source, cancellationToken).ConfigureAwait(false);
        }

        var destinationBeforeSourceReplacement = await vault.ReadAsync(destinationPath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The move destination disappeared before source replacement.");
        await EnsureExactDestinationAsync(operation, destinationBeforeSourceReplacement, descriptor, cancellationToken)
            .ConfigureAwait(false);
        var sourceBeforeReplacement = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The source daily note no longer exists.", request.SourcePath);
        if (!string.Equals(sourceBeforeReplacement.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            await SaveRecoveryIssueAsync(operation, "The move source changed after its destination was created.", cancellationToken)
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
            ReviewApplied = false,
            RecoveryIssue = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await journal.SaveAsync(complete, cancellationToken).ConfigureAwait(false);
        return new MoveToTodayResult(anchor, destinationPath, sourceWrite.Revision, destinationBeforeSourceReplacement.Revision, request.ReviewSessionId, false);
    }

    public Task<MoveToTodayResult> ResumeAsync(
        FeedOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var descriptor = operation.RecoveryDescriptor
            ?? throw new InvalidDataException("The move journal does not contain a durable recovery descriptor.");
        if (operation.SchemaVersion != 2
            || operation.Kind != FeedOperationKind.MoveToToday
            || !string.Equals(descriptor.OriginalOperationId, operation.OperationId, StringComparison.Ordinal)
            || descriptor.DestinationDate is null
            || string.IsNullOrWhiteSpace(descriptor.ReviewSessionId))
        {
            throw new InvalidDataException("The move recovery descriptor is incompatible with this operation.");
        }

        return MoveAsync(
            new MoveToTodayRequest(
                operation.VaultId,
                descriptor.OriginalOperationId,
                operation.SourcePath,
                descriptor.ExpectedSourceRevision,
                descriptor.Selection,
                descriptor.DestinationDate.Value,
                descriptor.DestinationArea,
                descriptor.ExpectedDestinationRevision,
                descriptor.ReviewSessionId),
            cancellationToken);
    }

    public Task MarkReviewAppliedAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        journal.MarkReviewAppliedAsync(vaultId, operationId, cancellationToken);

    private async Task<VaultDocument?> ReadAndValidateStoredDestinationAsync(
        FeedOperationRecord operation,
        FeedOperationRecoveryDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var destination = await vault.ReadAsync(operation.DestinationPath, cancellationToken).ConfigureAwait(false);
        if (destination is null)
        {
            if (operation.State == FeedOperationState.DestinationCreated
                || descriptor.ExpectedDestinationRevision is not null)
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    operation.DestinationPath,
                    "The move destination disappeared after the operation was journaled.",
                    cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        var containsExactOutput = string.Equals(
            FeedOperationHash.Compute(destination.Text),
            descriptor.DestinationPayloadHash,
            StringComparison.Ordinal);
        if (containsExactOutput)
        {
            if (operation.State == FeedOperationState.DestinationCreated
                && !string.Equals(destination.Revision, operation.DestinationRevision, StringComparison.Ordinal))
            {
                await ThrowRecoveryConflictAsync(
                    operation,
                    operation.DestinationPath,
                    "The move destination revision changed after its checkpoint.",
                    cancellationToken).ConfigureAwait(false);
            }

            return destination;
        }

        if (operation.State == FeedOperationState.DestinationCreated
            || !string.Equals(destination.Revision, descriptor.ExpectedDestinationRevision, StringComparison.Ordinal))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                operation.DestinationPath,
                "The move destination changed after the partial operation.",
                cancellationToken).ConfigureAwait(false);
        }

        return destination;
    }

    private async Task EnsureExactDestinationAsync(
        FeedOperationRecord operation,
        VaultDocument destination,
        FeedOperationRecoveryDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(FeedOperationHash.Compute(destination.Text), descriptor.DestinationPayloadHash, StringComparison.Ordinal)
            || operation.State == FeedOperationState.DestinationCreated
            && !string.Equals(destination.Revision, operation.DestinationRevision, StringComparison.Ordinal))
        {
            await ThrowRecoveryConflictAsync(
                operation,
                operation.DestinationPath,
                "The move destination no longer matches the journaled payload.",
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

    private static void ValidateExistingOperation(
        FeedOperationRecord? operation,
        MoveToTodayRequest request,
        string destinationPath,
        string anchor)
    {
        if (operation is null)
        {
            return;
        }

        if (operation.SchemaVersion is not 1 and not 2
            || operation.Kind != FeedOperationKind.MoveToToday
            || !string.Equals(operation.VaultId, request.VaultId, StringComparison.Ordinal)
            || !string.Equals(operation.OperationId, request.OperationId, StringComparison.Ordinal)
            || !string.Equals(operation.SourcePath.Replace('\\', '/'), request.SourcePath.Replace('\\', '/'), StringComparison.Ordinal)
            || !string.Equals(operation.DestinationPath.Replace('\\', '/'), destinationPath, StringComparison.Ordinal)
            || !string.Equals(operation.ResultId, anchor, StringComparison.Ordinal)
            || operation.State == FeedOperationState.Completed
                && (string.IsNullOrWhiteSpace(operation.SourceRevision) || string.IsNullOrWhiteSpace(operation.DestinationRevision)))
        {
            throw new InvalidDataException("The move journal does not match the requested operation.");
        }
    }

    private static void ValidateStoredRequest(
        FeedOperationRecoveryDescriptor descriptor,
        MoveToTodayRequest request)
    {
        if (!string.Equals(descriptor.OriginalOperationId, request.OperationId, StringComparison.Ordinal)
            || !string.Equals(descriptor.ExpectedSourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            || descriptor.Selection != request.Selection
            || descriptor.DestinationDate != request.DestinationDate
            || descriptor.DestinationArea != request.DestinationArea
            || !string.Equals(descriptor.ExpectedDestinationRevision, request.ExpectedDestinationRevision, StringComparison.Ordinal)
            || !string.Equals(descriptor.ReviewSessionId, request.ReviewSessionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The move recovery descriptor does not match the requested operation.");
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
            || !string.Equals(stored.ExpectedDestinationRevision, expected.ExpectedDestinationRevision, StringComparison.Ordinal)
            || stored.DestinationDate != expected.DestinationDate
            || stored.DestinationArea != expected.DestinationArea
            || !string.Equals(stored.ReviewSessionId, expected.ReviewSessionId, StringComparison.Ordinal)
            || !FeedOperationLocatorFactory.SequenceEqual(stored.InputLocators, expected.InputLocators)
            || !FeedOperationLocatorFactory.SequenceEqual(stored.SourceOutputLocators, expected.SourceOutputLocators)
            || !FeedOperationLocatorFactory.SequenceEqual(stored.DestinationOutputLocators, expected.DestinationOutputLocators))
        {
            throw new InvalidDataException("The move payload no longer matches its recovery descriptor.");
        }
    }

    private static string NormalizeLineEndings(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
}
