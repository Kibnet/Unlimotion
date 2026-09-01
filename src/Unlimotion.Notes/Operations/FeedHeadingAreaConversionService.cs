using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Operations;

public sealed record FeedHeadingAreaConversionRequest(
    string VaultId,
    string OperationId,
    string SourcePath,
    string ExpectedSourceRevision,
    MarkdownBlockSelection Selection,
    string SelectionPayloadHash,
    string AreaId,
    string AreaName,
    string? ParentAreaId,
    bool CreateArea);

public sealed record FeedHeadingAreaConversionResult(
    string AreaId,
    string SourceRevision,
    int OutputBlockIndex);

public sealed class FeedHeadingAreaConversionService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    MarkdownMutationService mutations,
    IFeedOperationJournal journal)
{
    public async Task<FeedHeadingAreaConversionResult> ConvertAsync(
        FeedHeadingAreaConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var source = await vault.ReadAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The heading source note does not exist.", request.SourcePath);
        if (!string.Equals(source.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(
                request.SourcePath,
                request.ExpectedSourceRevision,
                source.Revision);
        }

        var selected = request.Selection.Resolve(parser.Parse(source.Text));
        if (selected.Count != 1
            || selected[0].Kind != MarkdownBlockKind.Heading
            || !string.Equals(
                FeedOperationHash.Compute(selected[0].Raw),
                request.SelectionPayloadHash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The selected heading changed before conversion started.");
        }

        var canonical = CanonicalHeading(request.AreaName, request.AreaId);
        var record = new FeedOperationRecord(
            2,
            request.VaultId,
            request.OperationId,
            FeedOperationKind.HeadingAreaConversion,
            FeedOperationState.Pending,
            request.SourcePath,
            AreaCatalogStore.RelativePath,
            null,
            request.ExpectedSourceRevision,
            request.AreaId,
            DateTimeOffset.UtcNow,
            new FeedOperationRecoveryDescriptor(
                request.OperationId,
                request.ExpectedSourceRevision,
                request.Selection,
                request.SelectionPayloadHash,
                string.Empty,
                FeedOperationHash.Compute(canonical),
                AreaId: request.AreaId,
                AreaName: request.AreaName,
                ParentAreaId: request.ParentAreaId,
                CreateArea: request.CreateArea,
                CanonicalReplacement: canonical));
        await journal.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return await ResumeAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeedHeadingAreaConversionResult> ResumeAsync(
        FeedOperationRecord record,
        CancellationToken cancellationToken = default)
    {
        var descriptor = RequireDescriptor(record);
        await EnsureAreaAsync(descriptor, cancellationToken).ConfigureAwait(false);

        if (record.State == FeedOperationState.Pending)
        {
            record = record with
            {
                State = FeedOperationState.DestinationCreated,
                DestinationRevision = (await new AreaCatalogStore(vault).LoadAsync(cancellationToken)
                    .ConfigureAwait(false)).Revision,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await journal.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

        var source = await vault.ReadAsync(record.SourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The heading source note does not exist.", record.SourcePath);
        var document = parser.Parse(source.Text);
        var existing = document.Blocks.FirstOrDefault(block =>
            block.Kind == MarkdownBlockKind.AreaHeading
            && string.Equals(block.AreaId, descriptor.AreaId, StringComparison.Ordinal));
        int outputBlockIndex;
        string sourceRevision;
        if (existing is not null)
        {
            outputBlockIndex = existing.Index;
            sourceRevision = source.Revision;
        }
        else
        {
            var heading = ResolveOriginalHeading(document, descriptor);
            var updated = mutations.ReplaceSelection(
                source.Text,
                new MarkdownBlockSelection(heading.Index, 1),
                descriptor.CanonicalReplacement!);
            var write = await vault.WriteAsync(
                    record.SourcePath,
                    updated,
                    source.Revision,
                    source.HasUtf8Bom,
                    cancellationToken)
                .ConfigureAwait(false);
            outputBlockIndex = heading.Index;
            sourceRevision = write.Revision;
        }

        var completed = record with
        {
            State = FeedOperationState.Completed,
            SourceRevision = sourceRevision,
            ReviewApplied = true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await journal.SaveAsync(completed, cancellationToken).ConfigureAwait(false);
        return new FeedHeadingAreaConversionResult(descriptor.AreaId!, sourceRevision, outputBlockIndex);
    }

    private async Task EnsureAreaAsync(
        FeedOperationRecoveryDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var store = new AreaCatalogStore(vault);
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var existing = snapshot.Catalog.Areas.FirstOrDefault(area =>
            string.Equals(area.Id, descriptor.AreaId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(existing.Name, descriptor.AreaName, StringComparison.Ordinal)
                || !string.Equals(existing.ParentId, descriptor.ParentAreaId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Area '{descriptor.AreaId}' no longer matches the pending heading conversion.");
            }

            return;
        }

        if (descriptor.CreateArea != true)
        {
            throw new InvalidDataException(
                $"Area '{descriptor.AreaId}' selected by the pending heading conversion no longer exists.");
        }

        if (descriptor.ParentAreaId is not null
            && snapshot.Catalog.Areas.All(area =>
                !string.Equals(area.Id, descriptor.ParentAreaId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Parent area '{descriptor.ParentAreaId}' no longer exists.");
        }

        var siblings = snapshot.Catalog.Areas.Where(area =>
            string.Equals(area.ParentId, descriptor.ParentAreaId, StringComparison.Ordinal));
        snapshot.Catalog.Areas.Add(new AreaDefinition
        {
            Id = descriptor.AreaId!,
            Name = descriptor.AreaName!,
            ParentId = descriptor.ParentAreaId,
            SortOrder = siblings.Any() ? siblings.Max(static area => area.SortOrder) + 1 : 0
        });
        await store.SaveAsync(snapshot.Catalog, snapshot.Revision, cancellationToken).ConfigureAwait(false);
    }

    private static MarkdownBlock ResolveOriginalHeading(
        MarkdownDocument document,
        FeedOperationRecoveryDescriptor descriptor)
    {
        var candidates = document.Blocks.Where(block =>
                block.Kind == MarkdownBlockKind.Heading
                && string.Equals(
                    FeedOperationHash.Compute(block.Raw),
                    descriptor.SelectionPayloadHash,
                    StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidDataException(
                "The original heading cannot be resolved uniquely; explicit recovery is required.");
        }

        return candidates[0];
    }

    private static FeedOperationRecoveryDescriptor RequireDescriptor(FeedOperationRecord record)
    {
        if (record.SchemaVersion != 2
            || record.Kind != FeedOperationKind.HeadingAreaConversion
            || record.RecoveryDescriptor is not { } descriptor
            || string.IsNullOrWhiteSpace(descriptor.AreaId)
            || string.IsNullOrWhiteSpace(descriptor.AreaName)
            || string.IsNullOrWhiteSpace(descriptor.CanonicalReplacement))
        {
            throw new InvalidDataException("The heading-area conversion journal is incomplete or corrupt.");
        }

        FeedLinkSerializer.ValidateStableId(descriptor.AreaId, nameof(descriptor.AreaId));
        return descriptor;
    }

    private static void ValidateRequest(FeedHeadingAreaConversionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedSourceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SelectionPayloadHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AreaName);
        FeedLinkSerializer.ValidateStableId(request.AreaId, nameof(request.AreaId));
    }

    private static string CanonicalHeading(string areaName, string areaId)
    {
        var safeName = string.Concat(areaName.Where(static character => !char.IsControl(character))).Trim();
        if (safeName.Length == 0)
        {
            throw new ArgumentException("An area name cannot be empty.", nameof(areaName));
        }

        return $"## {safeName} <!-- unlimotion-area:{areaId} -->";
    }
}
