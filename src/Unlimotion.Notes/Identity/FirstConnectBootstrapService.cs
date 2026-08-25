using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Identity;

public sealed record BootstrapFileEntry(
    string RelativePath,
    string StartRevision,
    string BatchPath,
    string BatchHash,
    int BaselineCount,
    int PendingCheckboxCount);

public sealed record BootstrapStartDocument(
    string RelativePath,
    string StartRevision,
    string Text);

public sealed record BootstrapManifest(
    int SchemaVersion,
    string VaultId,
    string OperationId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<BootstrapFileEntry> Files,
    string? RecoveryOfOperationId = null,
    string? RecoveryMode = null);

public sealed record BootstrapFingerprint(
    string RelativePath,
    string? AreaIdentity,
    MarkdownBlockKind BlockKind,
    string ContentHash,
    int Occurrence,
    bool BaselineKept,
    string? PreviousContentHash = null,
    string? NextContentHash = null);

public sealed record BootstrapFileBatch(
    int SchemaVersion,
    string RelativePath,
    string StartRevision,
    IReadOnlyList<BootstrapFingerprint> Fingerprints);

public sealed record BootstrapResult(
    BootstrapManifest Manifest,
    IReadOnlyList<BootstrapFingerprint> Fingerprints,
    int IndexedFiles,
    int PendingCheckboxes,
    bool ReusedExisting);

public sealed class FirstConnectBootstrapService(
    INoteVault vault,
    IMarkdownDocumentParser parser,
    DailyNoteNaming? naming = null)
{
    private const string BootstrapRoot = ".unlimotion/review/bootstrap";
    // An empty first connection is still a durable baseline boundary: content written after it
    // must never be reclassified as pre-existing history on a later reconnect. This is kept
    // separate from operation manifests so we never write an empty bootstrap manifest.
    private const string EmptySnapshotMarkerRoot = BootstrapRoot + "/empty";
    private const string OrphanRecoveryMode = "orphan-safe-intersection";
    private const string FinalRescanRecoveryMode = "final-rescan-reconciliation";
    private readonly DailyNoteNaming dailyNaming = naming ?? DailyNoteNaming.Default;

    private sealed record CapturedBootstrapFile(
        BootstrapFileEntry Entry,
        IReadOnlyList<BootstrapFingerprint> Fingerprints);

    private sealed record PreparedBootstrapFile(
        string RelativePath,
        string StartRevision,
        IReadOnlyList<BootstrapFingerprint> Fingerprints,
        string BatchJson);

    private sealed record BootstrapOperationPlan(
        string OperationId,
        IReadOnlyList<PreparedBootstrapFile> Files,
        string? RecoveryOfOperationId = null,
        string? RecoveryMode = null);

    private sealed record EmptySnapshotMarker(
        int SchemaVersion,
        string VaultId,
        string DailyFileNameFormat,
        string OperationId,
        DateTimeOffset CompletedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<BootstrapResult> CreateOrResumeAsync(
        string vaultId,
        string operationId,
        IEnumerable<string> startDailyPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startDailyPaths);
        return await CreateOrResumeCoreAsync(
                vaultId,
                operationId,
                token => CaptureStartSnapshotAsync(startDailyPaths, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BootstrapResult> CreateOrResumeAsync(
        string vaultId,
        string operationId,
        IEnumerable<BootstrapStartDocument> startDocuments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startDocuments);
        var immutableSnapshot = startDocuments
            .Select(static value => new BootstrapStartDocument(
                value.RelativePath.Replace('\\', '/'),
                value.StartRevision,
                value.Text))
            .ToArray();
        return await CreateOrResumeCoreAsync(
                vaultId,
                operationId,
                token => CaptureStartSnapshotAsync(immutableSnapshot, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<BootstrapResult> CreateOrResumeCoreAsync(
        string vaultId,
        string operationId,
        Func<CancellationToken, Task<IReadOnlyList<PreparedBootstrapFile>>> captureStartSnapshot,
        CancellationToken cancellationToken)
    {
        ValidateId(vaultId, nameof(vaultId));
        ValidateId(operationId, nameof(operationId));
        var existingEmpty = await TryReadEmptySnapshotMarkerAsync(vaultId, cancellationToken)
            .ConfigureAwait(false);
        if (existingEmpty is not null)
        {
            return existingEmpty;
        }

        var operationRoot = OperationRoot(operationId);
        var completePath = $"{operationRoot}/manifest.json";
        BootstrapResult? existingComplete = null;
        var requiresRecovery = false;
        try
        {
            existingComplete = await TryReadValidCompleteAsync(completePath, vaultId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            requiresRecovery = true;
        }

        if (existingComplete is null
            && await vault.ReadAsync(completePath, cancellationToken).ConfigureAwait(false) is not null)
        {
            requiresRecovery = true;
        }

        if (existingComplete is not null)
        {
            return existingComplete with { ReusedExisting = true };
        }

        var startPath = $"{operationRoot}/start.json";
        var startDocument = await vault.ReadAsync(startPath, cancellationToken).ConfigureAwait(false);
        if (startDocument is not null && !requiresRecovery)
        {
            try
            {
                var started = DeserializeManifest(startDocument.Text);
                ValidateStartedManifest(started, startPath, vaultId, operationId);
                return await CompleteStartedOperationAsync(started, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                requiresRecovery = true;
            }
        }

        var prepared = await captureStartSnapshot(cancellationToken).ConfigureAwait(false);
        if (prepared.Count == 0)
        {
            return await PersistEmptySnapshotMarkerAsync(vaultId, operationId, cancellationToken)
                .ConfigureAwait(false);
        }

        var plan = await SelectOperationPlanAsync(
                vaultId,
                operationId,
                prepared,
                requiresRecovery,
                cancellationToken)
            .ConfigureAwait(false);
        while (true)
        {
            try
            {
                return await CompletePreparedOperationAsync(vaultId, plan, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException) when (plan.RecoveryOfOperationId is null)
            {
                plan = await SelectOperationPlanAsync(
                        vaultId,
                        operationId,
                        prepared,
                        forceRecovery: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException) when (plan.RecoveryOfOperationId is not null)
            {
                plan = await MoveRecoveryPlanAfterCollisionAsync(plan, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<BootstrapResult> CompletePreparedOperationAsync(
        string vaultId,
        BootstrapOperationPlan plan,
        CancellationToken cancellationToken)
    {
        var operationRoot = OperationRoot(plan.OperationId);
        var completePath = $"{operationRoot}/manifest.json";
        var existingComplete = await TryReadValidCompleteAsync(completePath, vaultId, cancellationToken).ConfigureAwait(false);
        if (existingComplete is not null)
        {
            return existingComplete with { ReusedExisting = true };
        }

        if (await vault.ReadAsync(completePath, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidDataException("Bootstrap completion manifest is invalid or belongs to another operation.");
        }

        var startPath = $"{operationRoot}/start.json";
        var startDocument = await vault.ReadAsync(startPath, cancellationToken).ConfigureAwait(false);
        if (startDocument is not null)
        {
            var resumed = DeserializeManifest(startDocument.Text);
            ValidateStartedManifest(resumed, startPath, vaultId, plan.OperationId);
            return await CompleteStartedOperationAsync(resumed, cancellationToken).ConfigureAwait(false);
        }

        if (plan.Files.Count == 0)
        {
            return await PersistEmptySnapshotMarkerAsync(vaultId, plan.OperationId, cancellationToken)
                .ConfigureAwait(false);
        }

        var batches = await StagePreparedFilesAsync(plan.OperationId, plan.Files, cancellationToken).ConfigureAwait(false);
        var started = new BootstrapManifest(
            1,
            vaultId,
            plan.OperationId,
            "started",
            DateTimeOffset.UtcNow,
            null,
            batches.Select(static value => value.Entry).ToArray(),
            plan.RecoveryOfOperationId,
            plan.RecoveryMode);
        try
        {
            await vault.CreateAsync(startPath, Serialize(started), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (VaultRevisionConflictException)
        {
            var concurrentStart = await vault.ReadAsync(startPath, cancellationToken).ConfigureAwait(false);
            if (concurrentStart is null)
            {
                throw;
            }

            started = DeserializeManifest(concurrentStart.Text);
            ValidateStartedManifest(started, startPath, vaultId, plan.OperationId);
        }

        return await CompleteStartedOperationAsync(started, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BootstrapResult> CompleteStartedOperationAsync(
        BootstrapManifest started,
        CancellationToken cancellationToken)
    {
        if (started.Files.Count == 0)
        {
            return await PersistEmptySnapshotMarkerAsync(
                    started.VaultId,
                    started.OperationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var batches = await ReadBatchesAsync(started, cancellationToken).ConfigureAwait(false);
        var reconciliation = await CreateFinalRescanPlanAsync(started, batches, cancellationToken)
            .ConfigureAwait(false);
        if (reconciliation is not null)
        {
            return await CompletePreparedOperationAsync(started.VaultId, reconciliation, cancellationToken)
                .ConfigureAwait(false);
        }

        var complete = started with { State = "complete", CompletedAt = DateTimeOffset.UtcNow };
        var completePath = $"{OperationRoot(complete.OperationId)}/manifest.json";
        try
        {
            await vault.CreateAsync(completePath, Serialize(complete), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (VaultRevisionConflictException)
        {
            var concurrent = await TryReadValidCompleteAsync(completePath, complete.VaultId, cancellationToken).ConfigureAwait(false);
            if (concurrent is not null)
            {
                return concurrent with { ReusedExisting = true };
            }

            throw new InvalidDataException("A conflicting bootstrap completion manifest is invalid.");
        }

        return CreateResult(complete, batches, reusedExisting: false);
    }

    private async Task<BootstrapOperationPlan?> CreateFinalRescanPlanAsync(
        BootstrapManifest started,
        IReadOnlyList<CapturedBootstrapFile> batches,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var reconciled = new List<PreparedBootstrapFile>(batches.Count);
        foreach (var captured in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await vault.ReadAsync(captured.Entry.RelativePath, cancellationToken)
                .ConfigureAwait(false);
            if (current is null)
            {
                changed = true;
                continue;
            }

            if (string.Equals(current.Revision, captured.Entry.StartRevision, StringComparison.Ordinal))
            {
                reconciled.Add(PrepareFile(
                    captured.Entry.RelativePath,
                    captured.Entry.StartRevision,
                    captured.Fingerprints));
                continue;
            }

            changed = true;
            var safeBaseline = captured.Fingerprints
                .Where(static value => value.BaselineKept)
                .Select(FingerprintKey)
                .ToHashSet(StringComparer.Ordinal);
            var currentFingerprints = CreateFingerprints(
                    captured.Entry.RelativePath,
                    parser.Parse(current.Text))
                .Select(value => value with
                {
                    BaselineKept = value.BaselineKept && safeBaseline.Contains(FingerprintKey(value))
                })
                .ToArray();
            reconciled.Add(PrepareFile(
                captured.Entry.RelativePath,
                current.Revision,
                currentFingerprints));
        }

        if (!changed)
        {
            return null;
        }

        var descriptor = string.Join(
            '\n',
            reconciled.OrderBy(static value => value.RelativePath, StringComparer.Ordinal)
                .Select(static value => $"{value.RelativePath}:{value.StartRevision}:{Hash(value.BatchJson)}"));
        var operationId = "reconcile-" + Hash(
            $"{started.VaultId}|{started.OperationId}|{descriptor}")[..40];
        return new BootstrapOperationPlan(
            operationId,
            reconciled,
            started.OperationId,
            FinalRescanRecoveryMode);
    }

    private async Task<BootstrapOperationPlan> SelectOperationPlanAsync(
        string vaultId,
        string operationId,
        IReadOnlyList<PreparedBootstrapFile> currentSnapshot,
        bool forceRecovery,
        CancellationToken cancellationToken)
    {
        var filesRoot = $"{OperationRoot(operationId)}/files";
        var orphanPaths = await vault.ListFilesAsync(filesRoot, "*.json", cancellationToken).ConfigureAwait(false);
        if (orphanPaths.Count == 0 && !forceRecovery)
        {
            return new BootstrapOperationPlan(operationId, currentSnapshot);
        }

        var expectedByPath = currentSnapshot.ToDictionary(
            value => BatchPath(operationId, value.RelativePath),
            StringComparer.Ordinal);
        var orphanByPath = new Dictionary<string, VaultDocument>(StringComparer.Ordinal);
        foreach (var orphanPath in orphanPaths.OrderBy(static value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orphan = await vault.ReadAsync(orphanPath, cancellationToken).ConfigureAwait(false);
            if (orphan is not null)
            {
                orphanByPath[orphanPath] = orphan;
            }
        }

        var exactCrashResume = orphanByPath.Count == expectedByPath.Count
            && expectedByPath.All(pair => orphanByPath.TryGetValue(pair.Key, out var orphan)
                && string.Equals(orphan.Text, pair.Value.BatchJson, StringComparison.Ordinal));
        if (exactCrashResume && !forceRecovery)
        {
            return new BootstrapOperationPlan(operationId, currentSnapshot);
        }

        var safeFiles = new List<PreparedBootstrapFile>(currentSnapshot.Count);
        foreach (var current in currentSnapshot)
        {
            var orphanPath = BatchPath(operationId, current.RelativePath);
            var orphanBaselineKeys = orphanByPath.TryGetValue(orphanPath, out var orphan)
                ? TryReadOrphanBaselineKeys(current.RelativePath, orphan.Text)
                : null;
            var safeFingerprints = current.Fingerprints
                .Select(value => value with
                {
                    BaselineKept = value.BaselineKept
                        && orphanBaselineKeys?.Contains(FingerprintKey(value)) == true
                })
                .ToArray();
            safeFiles.Add(PrepareFile(current.RelativePath, current.StartRevision, safeFingerprints));
        }

        var orphanDescriptor = string.Join(
            '\n',
            orphanByPath.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}:{Hash(pair.Value.Text)}"));
        var operationArtifactDescriptor = await DescribeOperationArtifactsAsync(operationId, cancellationToken)
            .ConfigureAwait(false);
        var recoveryDescriptor = string.Join(
            '\n',
            safeFiles.Select(static file => $"{file.RelativePath}:{file.StartRevision}:{Hash(file.BatchJson)}"));
        var recoveryId = "recovery-" + Hash(
            $"{vaultId}|{operationId}|{orphanDescriptor}|{operationArtifactDescriptor}|{recoveryDescriptor}")[..40];
        return new BootstrapOperationPlan(
            recoveryId,
            safeFiles,
            operationId,
            OrphanRecoveryMode);
    }

    private async Task<BootstrapOperationPlan> MoveRecoveryPlanAfterCollisionAsync(
        BootstrapOperationPlan plan,
        CancellationToken cancellationToken)
    {
        var collisionDescriptor = await DescribeOperationArtifactsAsync(plan.OperationId, cancellationToken)
            .ConfigureAwait(false);
        var filesDescriptor = string.Join(
            '\n',
            plan.Files.Select(static file => $"{file.RelativePath}:{file.StartRevision}:{Hash(file.BatchJson)}"));
        var operationId = "recovery-" + Hash(
            $"{plan.OperationId}|{collisionDescriptor}|{filesDescriptor}")[..40];
        return plan with { OperationId = operationId };
    }

    private async Task<string> DescribeOperationArtifactsAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var operationRoot = OperationRoot(operationId);
        var paths = await vault.ListFilesAsync(operationRoot, "*.json", cancellationToken).ConfigureAwait(false);
        var values = new List<string>(paths.Count);
        foreach (var path in paths.OrderBy(static value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            values.Add($"{path}:{(document is null ? "missing" : Hash(document.Text))}");
        }

        return string.Join('\n', values);
    }

    private HashSet<string>? TryReadOrphanBaselineKeys(string relativePath, string batchJson)
    {
        try
        {
            var batch = DeserializeBatch(batchJson, "orphan batch");
            ValidateBatch(batch, relativePath, expectedStartRevision: null, "orphan batch");
            return batch.Fingerprints
                .Where(static value => value.BaselineKept)
                .Select(FingerprintKey)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<CapturedBootstrapFile>> StagePreparedFilesAsync(
        string operationId,
        IReadOnlyList<PreparedBootstrapFile> files,
        CancellationToken cancellationToken)
    {
        var result = new List<CapturedBootstrapFile>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchPath = BatchPath(operationId, file.RelativePath);
            var existingBatch = await vault.ReadAsync(batchPath, cancellationToken).ConfigureAwait(false);
            if (existingBatch is null)
            {
                try
                {
                    await vault.CreateAsync(batchPath, file.BatchJson, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (VaultRevisionConflictException)
                {
                    existingBatch = await vault.ReadAsync(batchPath, cancellationToken).ConfigureAwait(false);
                }
            }

            if (existingBatch is not null
                && !string.Equals(existingBatch.Text, file.BatchJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Bootstrap batch '{batchPath}' was staged before a crash but does not match the immutable snapshot.");
            }

            var entry = new BootstrapFileEntry(
                file.RelativePath,
                file.StartRevision,
                batchPath,
                Hash(file.BatchJson),
                file.Fingerprints.Count(static value => value.BaselineKept),
                file.Fingerprints.Count(static value => value.BlockKind == MarkdownBlockKind.TaskListItem && !value.BaselineKept));
            result.Add(new CapturedBootstrapFile(entry, file.Fingerprints));
        }

        return result;
    }

    public async Task<BootstrapResult?> FindValidCompleteAsync(
        string vaultId,
        IEnumerable<string> operationIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var operationId in operationIds)
        {
            ValidateId(operationId, nameof(operationIds));
            var result = await TryReadValidCompleteAsync(
                $"{OperationRoot(operationId)}/manifest.json",
                vaultId,
                cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                return result with { ReusedExisting = true };
            }
        }

        return null;
    }

    public async Task<BootstrapResult?> FindSafeCompleteAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(vaultId, nameof(vaultId));
        var empty = await TryReadEmptySnapshotMarkerAsync(vaultId, cancellationToken).ConfigureAwait(false);
        if (empty is not null)
        {
            return empty;
        }

        var manifestPaths = await vault.ListFilesAsync(
                BootstrapRoot,
                "manifest.json",
                cancellationToken)
            .ConfigureAwait(false);
        var valid = new List<BootstrapResult>();
        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await TryReadValidCompleteAsync(manifestPath, vaultId, cancellationToken)
                    .ConfigureAwait(false);
                if (result is not null)
                {
                    valid.Add(result);
                }
            }
            catch (InvalidDataException)
            {
                // Partial, out-of-order, or corrupted synced operations are not allowed to hide
                // content. They stay ignored until every referenced batch validates.
            }
        }

        return valid.Count switch
        {
            0 => null,
            1 => valid[0] with { ReusedExisting = true },
            _ => IntersectConcurrent(valid)
        };
    }

    private async Task<BootstrapResult?> TryReadEmptySnapshotMarkerAsync(
        string vaultId,
        CancellationToken cancellationToken)
    {
        var document = await vault.ReadAsync(EmptySnapshotMarkerPath(vaultId), cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        try
        {
            var marker = DeserializeEmptySnapshotMarker(document.Text);
            if (marker.SchemaVersion != 1 ||
                !string.Equals(marker.VaultId, vaultId, StringComparison.Ordinal) ||
                !IsValidId(marker.OperationId) ||
                !DailyNoteNaming.TryCreate(marker.DailyFileNameFormat, out _, out _))
            {
                throw new InvalidDataException("The empty bootstrap marker is invalid.");
            }

            // An empty boundary belongs to the active filename layout. A vault can have had an
            // empty default-layout connection before the user selects a layout that already has
            // history, and that history must still receive its own first-connect baseline.
            if (!string.Equals(marker.DailyFileNameFormat, dailyNaming.FileNameFormat, StringComparison.Ordinal))
            {
                return null;
            }

            return CreateEmptySnapshotResult(
                vaultId,
                marker.OperationId,
                marker.CompletedAt,
                reusedExisting: true);
        }
        catch (InvalidDataException)
        {
            // The marker path is keyed by this vault identity and active filename layout. If it
            // was partially synced or damaged, treating it as a conservative empty boundary is
            // safe: old content may be shown for review, but no post-connect content can be
            // silently baselined.
            return CreateEmptySnapshotResult(
                vaultId,
                "empty-safe-" + Hash(vaultId)[..40],
                DateTimeOffset.UtcNow,
                reusedExisting: true);
        }
    }

    private async Task<BootstrapResult> PersistEmptySnapshotMarkerAsync(
        string vaultId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var marker = new EmptySnapshotMarker(
            1,
            vaultId,
            dailyNaming.FileNameFormat,
            operationId,
            completedAt);
        try
        {
            await vault.CreateAsync(
                    EmptySnapshotMarkerPath(vaultId),
                    Serialize(marker),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return CreateEmptySnapshotResult(vaultId, operationId, completedAt);
        }
        catch (VaultRevisionConflictException)
        {
            var concurrent = await TryReadEmptySnapshotMarkerAsync(vaultId, cancellationToken)
                .ConfigureAwait(false);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<PreparedBootstrapFile>> CaptureStartSnapshotAsync(
        IEnumerable<string> startDailyPaths,
        CancellationToken cancellationToken)
    {
        var documents = new List<BootstrapStartDocument>();
        foreach (var candidatePath in startDailyPaths)
        {
            var relativePath = NormalizeDailyRelativePath(candidatePath, nameof(startDailyPaths));
            if (documents.Any(value => string.Equals(value.RelativePath, relativePath, StringComparison.Ordinal)))
            {
                continue;
            }

            var document = await vault.ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }

            documents.Add(new BootstrapStartDocument(relativePath, document.Revision, document.Text));
        }

        return await CaptureStartSnapshotAsync(documents, cancellationToken).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<PreparedBootstrapFile>> CaptureStartSnapshotAsync(
        IEnumerable<BootstrapStartDocument> startDocuments,
        CancellationToken cancellationToken)
    {
        var result = new List<PreparedBootstrapFile>();
        foreach (var document in startDocuments
                     .Select(value => value with
                     {
                         RelativePath = NormalizeDailyRelativePath(value.RelativePath, nameof(startDocuments))
                     })
                     .DistinctBy(static value => value.RelativePath, StringComparer.Ordinal)
                     .OrderBy(static value => value.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprints = CreateFingerprints(document.RelativePath, parser.Parse(document.Text));
            result.Add(PrepareFile(document.RelativePath, document.StartRevision, fingerprints));
        }

        return Task.FromResult<IReadOnlyList<PreparedBootstrapFile>>(result);
    }

    private static PreparedBootstrapFile PrepareFile(
        string relativePath,
        string startRevision,
        IReadOnlyList<BootstrapFingerprint> fingerprints)
    {
        var batch = new BootstrapFileBatch(1, relativePath, startRevision, fingerprints);
        var batchJson = JsonSerializer.Serialize(batch, JsonOptions) + "\n";
        return new PreparedBootstrapFile(relativePath, startRevision, fingerprints, batchJson);
    }

    private static IReadOnlyList<BootstrapFingerprint> CreateFingerprints(string relativePath, MarkdownDocument document)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<BootstrapFingerprint>();
        var contentBlocks = document.Blocks.Where(static block => block.IsContent).ToArray();
        for (var blockIndex = 0; blockIndex < contentBlocks.Length; blockIndex++)
        {
            var block = contentBlocks[blockIndex];
            if (block.Kind == MarkdownBlockKind.TaskListItem && block.IsTaskCompleted == true)
            {
                continue;
            }

            var area = block.AreaId ?? block.AreaName;
            var occurrenceKey = string.Join('|', area, block.Kind, block.ContentHash);
            occurrences.TryGetValue(occurrenceKey, out var occurrence);
            occurrences[occurrenceKey] = occurrence + 1;
            result.Add(new BootstrapFingerprint(
                relativePath.Replace('\\', '/'),
                area,
                block.Kind,
                block.ContentHash,
                occurrence,
                block.Kind != MarkdownBlockKind.TaskListItem,
                blockIndex > 0 ? contentBlocks[blockIndex - 1].ContentHash : null,
                blockIndex + 1 < contentBlocks.Length ? contentBlocks[blockIndex + 1].ContentHash : null));
        }

        return result;
    }

    private async Task<BootstrapResult?> TryReadValidCompleteAsync(
        string manifestPath,
        string vaultId,
        CancellationToken cancellationToken)
    {
        var document = await vault.ReadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var manifest = DeserializeManifest(document.Text);
        if (manifest.SchemaVersion != 1
            || manifest.State != "complete"
            || !string.Equals(manifest.VaultId, vaultId, StringComparison.Ordinal))
        {
            return null;
        }

        ValidateCompleteManifest(manifest, manifestPath, vaultId);
        var batches = await ReadBatchesAsync(manifest, cancellationToken).ConfigureAwait(false);
        return CreateResult(manifest, batches, reusedExisting: true);
    }

    private async Task<IReadOnlyList<CapturedBootstrapFile>> ReadBatchesAsync(
        BootstrapManifest manifest,
        CancellationToken cancellationToken)
    {
        var batches = new List<CapturedBootstrapFile>();
        foreach (var entry in manifest.Files)
        {
            var batch = await vault.ReadAsync(entry.BatchPath, cancellationToken).ConfigureAwait(false);
            if (batch is null || !string.Equals(Hash(batch.Text), entry.BatchHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Bootstrap batch '{entry.BatchPath}' is missing or has an invalid hash.");
            }

            var batchPayload = DeserializeBatch(batch.Text, entry.BatchPath);
            ValidateBatch(batchPayload, entry.RelativePath, entry.StartRevision, entry.BatchPath);
            var values = batchPayload.Fingerprints;
            var baselineCount = values.Count(static value => value.BaselineKept);
            var pendingCheckboxCount = values.Count(static value =>
                value.BlockKind == MarkdownBlockKind.TaskListItem && !value.BaselineKept);
            if (baselineCount != entry.BaselineCount
                || pendingCheckboxCount != entry.PendingCheckboxCount)
            {
                throw new InvalidDataException(
                    $"Bootstrap batch '{entry.BatchPath}' counts do not match its manifest entry.");
            }

            batches.Add(new CapturedBootstrapFile(entry, values));
        }

        return batches;
    }

    private static BootstrapResult IntersectConcurrent(IReadOnlyList<BootstrapResult> manifests)
    {
        var ordered = manifests.OrderBy(static value => value.Manifest.OperationId, StringComparer.Ordinal).ToArray();
        var commonBaselineKeys = ordered[0].Fingerprints
            .Where(static value => value.BaselineKept)
            .Select(FingerprintKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var manifest in ordered.Skip(1))
        {
            commonBaselineKeys.IntersectWith(manifest.Fingerprints
                .Where(static value => value.BaselineKept)
                .Select(FingerprintKey));
        }

        var fingerprints = ordered.SelectMany(static value => value.Fingerprints)
            .DistinctBy(FingerprintKey, StringComparer.Ordinal)
            .Select(value => value with
            {
                BaselineKept = value.BaselineKept && commonBaselineKeys.Contains(FingerprintKey(value))
            })
            .ToArray();
        var fileKeysInAll = ordered[0].Manifest.Files
            .Select(FileSnapshotKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var manifest in ordered.Skip(1))
        {
            fileKeysInAll.IntersectWith(manifest.Manifest.Files.Select(FileSnapshotKey));
        }

        var commonFiles = ordered[0].Manifest.Files
            .Where(value => fileKeysInAll.Contains(FileSnapshotKey(value)))
            .ToArray();
        var operationIds = string.Join("|", ordered.Select(static value => value.Manifest.OperationId));
        var synthetic = ordered[0].Manifest with
        {
            OperationId = "intersection-" + Hash(operationIds)[..24],
            Files = commonFiles,
            StartedAt = ordered.Min(static value => value.Manifest.StartedAt),
            CompletedAt = ordered.Max(static value => value.Manifest.CompletedAt),
            RecoveryOfOperationId = null,
            RecoveryMode = null
        };
        return new BootstrapResult(
            synthetic,
            fingerprints,
            commonFiles.Length,
            ordered.Max(static value => value.PendingCheckboxes),
            true);
    }

    private static string FingerprintKey(BootstrapFingerprint value) => string.Join(
        '|',
        value.RelativePath.Replace('\\', '/'),
        value.AreaIdentity,
        value.BlockKind,
        value.ContentHash,
        value.Occurrence,
        value.PreviousContentHash,
        value.NextContentHash);

    private static string FileSnapshotKey(BootstrapFileEntry value) =>
        string.Join('|', value.RelativePath.Replace('\\', '/'), value.StartRevision);

    private static BootstrapResult CreateResult(
        BootstrapManifest manifest,
        IReadOnlyList<CapturedBootstrapFile> batches,
        bool reusedExisting)
    {
        var fingerprints = batches.SelectMany(static value => value.Fingerprints).ToArray();
        return new BootstrapResult(
            manifest,
            fingerprints,
            manifest.Files.Count,
            manifest.Files.Sum(static value => value.PendingCheckboxCount),
            reusedExisting);
    }

    private static BootstrapResult CreateEmptySnapshotResult(
        string vaultId,
        string operationId,
        DateTimeOffset? completedAt = null,
        bool reusedExisting = false)
    {
        var completion = completedAt ?? DateTimeOffset.UtcNow;
        return new BootstrapResult(
            new BootstrapManifest(
                1,
                vaultId,
                operationId,
                "complete",
                completion,
                completion,
                []),
            [],
            0,
            0,
            reusedExisting);
    }

    private static BootstrapManifest DeserializeManifest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BootstrapManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("Bootstrap manifest is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Bootstrap manifest is not valid JSON.", exception);
        }
    }

    private static BootstrapFileBatch DeserializeBatch(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<BootstrapFileBatch>(json, JsonOptions)
                ?? throw new InvalidDataException($"Bootstrap batch '{source}' is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"Bootstrap batch '{source}' is not valid JSON.", exception);
        }
    }

    private static void ValidateBatch(
        BootstrapFileBatch batch,
        string expectedRelativePath,
        string? expectedStartRevision,
        string source)
    {
        if (batch.SchemaVersion != 1
            || !string.Equals(batch.RelativePath, expectedRelativePath, StringComparison.Ordinal)
            || expectedStartRevision is not null
                && !string.Equals(batch.StartRevision, expectedStartRevision, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(batch.StartRevision)
            || batch.Fingerprints is null)
        {
            throw new InvalidDataException($"Bootstrap batch '{source}' metadata does not match its file entry.");
        }

        ValidateFingerprints(expectedRelativePath, batch.Fingerprints, source);
    }

    private static string Serialize(BootstrapManifest manifest) => JsonSerializer.Serialize(manifest, JsonOptions) + "\n";

    private static string Serialize(EmptySnapshotMarker marker) =>
        JsonSerializer.Serialize(marker, JsonOptions) + "\n";

    private static EmptySnapshotMarker DeserializeEmptySnapshotMarker(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EmptySnapshotMarker>(json, JsonOptions)
                ?? throw new InvalidDataException("The empty bootstrap marker is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The empty bootstrap marker is not valid JSON.", exception);
        }
    }

    private void ValidateCompleteManifest(
        BootstrapManifest manifest,
        string manifestPath,
        string vaultId)
    {
        ValidateManifestCore(manifest, manifestPath, vaultId, manifest.OperationId, "complete");
        if (manifest.CompletedAt is null || manifest.CompletedAt < manifest.StartedAt)
        {
            throw new InvalidDataException("A complete bootstrap manifest must have a valid completion time.");
        }
    }

    private void ValidateStartedManifest(
        BootstrapManifest manifest,
        string manifestPath,
        string vaultId,
        string operationId)
    {
        ValidateManifestCore(manifest, manifestPath, vaultId, operationId, "started");
        if (manifest.CompletedAt is not null)
        {
            throw new InvalidDataException("A started bootstrap manifest cannot have a completion time.");
        }
    }

    private void ValidateManifestCore(
        BootstrapManifest manifest,
        string manifestPath,
        string vaultId,
        string operationId,
        string expectedState)
    {
        if (manifest.SchemaVersion != 1
            || !string.Equals(manifest.VaultId, vaultId, StringComparison.Ordinal)
            || !string.Equals(manifest.OperationId, operationId, StringComparison.Ordinal)
            || !string.Equals(manifest.State, expectedState, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bootstrap operation does not belong to the connected vault or expected state.");
        }

        if (!IsValidId(manifest.VaultId) || !IsValidId(manifest.OperationId))
        {
            throw new InvalidDataException("Bootstrap manifest contains an invalid vault or operation identifier.");
        }

        var expectedManifestPath = $"{OperationRoot(manifest.OperationId)}/{(expectedState == "complete" ? "manifest.json" : "start.json")}";
        if (!string.Equals(manifestPath, expectedManifestPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bootstrap manifest is stored outside its canonical operation directory.");
        }

        if (manifest.StartedAt == default)
        {
            throw new InvalidDataException("Bootstrap manifest does not have a valid start time.");
        }

        if ((manifest.RecoveryOfOperationId is null) != (manifest.RecoveryMode is null))
        {
            throw new InvalidDataException("Bootstrap recovery metadata is incomplete.");
        }

        if (manifest.RecoveryOfOperationId is not null
            && (!IsValidId(manifest.RecoveryOfOperationId)
                || string.Equals(manifest.RecoveryOfOperationId, manifest.OperationId, StringComparison.Ordinal)
                || !string.Equals(manifest.RecoveryMode, OrphanRecoveryMode, StringComparison.Ordinal)
                    && !string.Equals(manifest.RecoveryMode, FinalRescanRecoveryMode, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Bootstrap recovery metadata is invalid.");
        }

        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("Bootstrap manifest must contain at least one daily file.");
        }

        var relativePaths = new HashSet<string>(StringComparer.Ordinal);
        var batchPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (!IsCanonicalDailyRelativePath(entry.RelativePath))
            {
                throw new InvalidDataException($"Bootstrap file path '{entry.RelativePath}' is not a canonical daily note path.");
            }

            var expectedBatchPath = BatchPath(manifest.OperationId, entry.RelativePath);
            if (!string.Equals(entry.BatchPath, expectedBatchPath, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Bootstrap batch path '{entry.BatchPath}' does not match its operation and daily path hash.");
            }

            if (!relativePaths.Add(entry.RelativePath) || !batchPaths.Add(entry.BatchPath))
            {
                throw new InvalidDataException("Bootstrap manifest contains duplicate file or batch entries.");
            }

            if (string.IsNullOrWhiteSpace(entry.StartRevision)
                || !IsSha256(entry.BatchHash)
                || entry.BaselineCount < 0
                || entry.PendingCheckboxCount < 0)
            {
                throw new InvalidDataException($"Bootstrap file entry '{entry.RelativePath}' contains invalid metadata.");
            }
        }
    }

    private static void ValidateFingerprints(
        string relativePath,
        IReadOnlyList<BootstrapFingerprint> fingerprints,
        string source)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fingerprint in fingerprints)
        {
            if (!string.Equals(fingerprint.RelativePath, relativePath, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Bootstrap batch '{source}' contains a fingerprint for another file.");
            }

            if (!IsContentKind(fingerprint.BlockKind)
                || !IsSha256(fingerprint.ContentHash)
                || fingerprint.Occurrence < 0
                || fingerprint.PreviousContentHash is not null && !IsSha256(fingerprint.PreviousContentHash)
                || fingerprint.NextContentHash is not null && !IsSha256(fingerprint.NextContentHash)
                || fingerprint.BlockKind == MarkdownBlockKind.TaskListItem && fingerprint.BaselineKept)
            {
                throw new InvalidDataException($"Bootstrap batch '{source}' contains an invalid fingerprint.");
            }

            if (!keys.Add(FingerprintKey(fingerprint)))
            {
                throw new InvalidDataException($"Bootstrap batch '{source}' contains duplicate fingerprints.");
            }
        }

        foreach (var group in fingerprints.GroupBy(static value =>
                     string.Join('|', value.AreaIdentity, value.BlockKind, value.ContentHash)))
        {
            var occurrences = group.Select(static value => value.Occurrence).Order().ToArray();
            if (occurrences.Where((value, index) => value != index).Any())
            {
                throw new InvalidDataException($"Bootstrap batch '{source}' contains non-canonical fingerprint occurrences.");
            }
        }
    }

    private string NormalizeDailyRelativePath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Replace('\\', '/');
        if (!IsCanonicalDailyRelativePath(normalized))
        {
            throw new ArgumentException("A bootstrap source must be a canonical daily note path.", parameterName);
        }

        return normalized;
    }

    private bool IsCanonicalDailyRelativePath(string? value) =>
        value is not null
        && !value.Contains('\\', StringComparison.Ordinal)
        && dailyNaming.TryParseRelativePath(value, out _);

    private static bool IsContentKind(MarkdownBlockKind value) => value is MarkdownBlockKind.Heading
        or MarkdownBlockKind.Paragraph
        or MarkdownBlockKind.ListItem
        or MarkdownBlockKind.TaskListItem
        or MarkdownBlockKind.BlockQuote
        or MarkdownBlockKind.FencedCode
        or MarkdownBlockKind.HorizontalRule
        or MarkdownBlockKind.Raw;

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string OperationRoot(string operationId) => $"{BootstrapRoot}/{operationId}";

    private string EmptySnapshotMarkerPath(string vaultId) =>
        $"{EmptySnapshotMarkerRoot}/{vaultId}-{Hash(dailyNaming.FileNameFormat)[..40]}.json";

    private static string BatchPath(string operationId, string relativePath) =>
        $"{OperationRoot(operationId)}/files/{Hash(relativePath)}.json";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ValidateId(string value, string parameterName)
    {
        if (!IsValidId(value))
        {
            throw new ArgumentException("A bootstrap identifier must contain only letters, digits, underscore or dash.", parameterName);
        }
    }

    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
